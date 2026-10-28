const { app, BrowserWindow, ipcMain, Notification, session, shell } = require('electron');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const QRCode = require('qrcode');
const { Bridge } = require('./bridge');
const { BlueBubblesBackend } = require('./backends/bluebubbles');
const { DemoBackend } = require('./backends/demo');

// FaceTime web yalnızca Chrome/Edge kabul ediyor; düz bir Chrome kullanıcı ajanı ile görün.
app.userAgentFallback = `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/${process.versions.chrome} Safari/537.36`;
// iPhone ile LAN üzerinden doğrudan WebRTC: mDNS gizlemesi Safari tarafında çözümlenmeyebiliyor.
app.commandLine.appendSwitch('disable-features', 'WebRtcHideLocalIpsWithMdns');
app.setAppUserModelId('dev.darkstone.continuity');

const settingsPath = () => path.join(app.getPath('userData'), 'settings.json');
const defaults = { backend: 'demo', bbUrl: '', bbPassword: '', token: crypto.randomBytes(12).toString('base64url'), preferIphoneCamera: true };
let settings = defaults;
let mainWindow, bridge, backend;
let backendStatus = { connected: false, kind: 'demo' };
const handoffs = [];

function loadSettings() {
  try { settings = { ...defaults, ...JSON.parse(fs.readFileSync(settingsPath(), 'utf8')) }; } catch { settings = { ...defaults }; }
  saveSettings();
}
function saveSettings() {
  fs.mkdirSync(path.dirname(settingsPath()), { recursive: true });
  fs.writeFileSync(settingsPath(), JSON.stringify(settings, null, 2));
}
function send(channel, payload) { mainWindow?.webContents.send(channel, payload); }

async function startBackend() {
  backend?.disconnect();
  backend = settings.backend === 'bluebubbles' && settings.bbUrl
    ? new BlueBubblesBackend({ url: settings.bbUrl, password: settings.bbPassword })
    : new DemoBackend();
  backend.on('message', (m) => {
    send('messages:new', m);
    if (!m.fromMe && !mainWindow?.isFocused()) {
      new Notification({ title: m.sender || 'Mesaj', body: m.text || 'Ek', silent: false }).show();
    }
  });
  backend.on('typing', (t) => send('messages:typing', t));
  backend.on('status', (s) => send('messages:status', (backendStatus = { ...s, kind: backend.kind })));
  try {
    await backend.connect();
  } catch (err) {
    send('messages:status', (backendStatus = { connected: false, kind: backend.kind, error: err.message }));
  }
}

function openFaceTime(url) {
  const win = new BrowserWindow({
    width: 1100, height: 760, backgroundColor: '#000', title: 'FaceTime',
    autoHideMenuBar: true,
    webPreferences: {
      partition: 'persist:facetime',
      preload: path.join(__dirname, 'facetime', 'preload.js'),
      contextIsolation: true,
    },
  });
  win.loadURL(url);
  win.webContents.setWindowOpenHandler(({ url: u }) => { shell.openExternal(u); return { action: 'deny' }; });
  return win;
}

function onHandoff(h) {
  handoffs.unshift(h);
  handoffs.splice(20);
  send('facetime:handoff', h);
  const n = new Notification({
    title: 'FaceTime',
    body: `${h.from} üzerindeki görüşmeyi Windows'ta devam ettir`,
    silent: false,
  });
  n.on('click', () => openFaceTime(h.url));
  n.show();
}

function createMainWindow() {
  mainWindow = new BrowserWindow({
    width: 1080, height: 720, minWidth: 760, minHeight: 520,
    title: 'Süreklilik',
    backgroundMaterial: 'mica',
    titleBarStyle: 'hidden',
    titleBarOverlay: { color: '#00000000', symbolColor: '#888', height: 40 },
    webPreferences: { preload: path.join(__dirname, 'preload.js'), contextIsolation: true },
  });
  mainWindow.loadFile(path.join(__dirname, 'renderer', 'index.html'), {
    query: { view: process.argv.find((a) => a.startsWith('--view='))?.slice(7) || 'messages' },
  });
}

function registerIpc() {
  ipcMain.handle('settings:get', () => ({ ...settings, bbPassword: settings.bbPassword ? '••••••••' : '' }));
  ipcMain.handle('settings:set', async (_e, patch) => {
    if (patch.bbPassword === '••••••••') delete patch.bbPassword;
    const backendChanged = ['backend', 'bbUrl', 'bbPassword'].some((k) => k in patch && patch[k] !== settings[k]);
    settings = { ...settings, ...patch };
    saveSettings();
    if (backendChanged) await startBackend();
    return true;
  });
  ipcMain.handle('messages:status', () => backendStatus);
  ipcMain.handle('messages:chats', () => backend.listChats());
  ipcMain.handle('messages:list', (_e, chatId) => backend.listMessages(chatId));
  ipcMain.handle('messages:send', (_e, chatId, text) => backend.sendText(chatId, text));
  ipcMain.handle('messages:attachment', (_e, id) => backend.attachmentUrl(id));
  ipcMain.handle('bridge:info', async () => {
    const urls = bridge.urls();
    return {
      ...urls,
      token: bridge.token,
      cameraQr: await QRCode.toDataURL(urls.camera, { margin: 1, width: 240 }),
      phoneConnected: !!bridge.phone,
      handoffs,
    };
  });
  ipcMain.handle('facetime:open', (_e, url) => {
    if (!/^https:\/\/facetime\.apple\.com\//.test(url)) throw new Error('Geçerli bir FaceTime bağlantısı değil');
    openFaceTime(url);
  });
  // FaceTime penceresinin preload'u, ana dünyaya enjekte edeceği kamera kodunu buradan alır.
  ipcMain.on('facetime:config', (e) => {
    e.returnValue = {
      source: fs.readFileSync(path.join(__dirname, 'camera', 'viewer.js'), 'utf8'),
      signalUrl: bridge.urls().signal,
      preferIphone: !!settings.preferIphoneCamera && !!bridge.phone,
    };
  });
}

// Köprünün kendinden imzalı sertifikasına yalnızca localhost:51515 için güven.
app.on('certificate-error', (event, _wc, url, _err, cert, callback) => {
  const u = new URL(url);
  const ours = fs.readFileSync(path.join(app.getPath('userData'), 'bridge-cert.pem'), 'utf8').replace(/\r/g, '');
  if (u.hostname === 'localhost' && u.port === String(bridge.httpsPort) && cert.data.replace(/\r/g, '').trim() === ours.trim()) {
    event.preventDefault();
    return callback(true);
  }
  callback(false);
});

app.whenReady().then(async () => {
  loadSettings();
  for (const part of [session.defaultSession, session.fromPartition('persist:facetime')]) {
    part.setPermissionRequestHandler((wc, permission, cb) => {
      const origin = new URL(wc.getURL()).origin;
      cb(['media', 'notifications', 'fullscreen'].includes(permission) && (origin === 'https://facetime.apple.com' || origin === 'file://'));
    });
  }
  bridge = await new Bridge({ dataDir: app.getPath('userData'), token: settings.token }).start();
  bridge.on('handoff', onHandoff);
  bridge.on('camera', (c) => send('camera:status', c));
  registerIpc();
  createMainWindow();
  await startBackend();
});

app.on('window-all-closed', () => app.quit());
