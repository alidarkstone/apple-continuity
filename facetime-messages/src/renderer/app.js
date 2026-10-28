const api = window.continuity;
const $ = (id) => document.getElementById(id);
const el = (tag, cls, text) => { const n = document.createElement(tag); if (cls) n.className = cls; if (text != null) n.textContent = text; return n; };

/* Gezinme */
function show(view) {
  document.querySelectorAll('.view').forEach((v) => v.classList.toggle('active', v.id === 'view-' + view));
  document.querySelectorAll('.rail button').forEach((b) => b.classList.toggle('active', b.dataset.view === view));
  if (view === 'camera' || view === 'settings' || view === 'facetime') refreshBridge();
}
document.querySelectorAll('.rail button').forEach((b) => (b.onclick = () => show(b.dataset.view)));

/* Zaman biçimleri */
const fmtTime = (d) => new Date(d).toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit' });
function fmtListDate(d) {
  const date = new Date(d), today = new Date();
  if (date.toDateString() === today.toDateString()) return fmtTime(d);
  const diff = (today - date) / 86400000;
  if (diff < 7) return date.toLocaleDateString('tr-TR', { weekday: 'long' });
  return date.toLocaleDateString('tr-TR');
}
const initials = (name) => name.replace(/[^\p{L}\p{N} ]/gu, '').split(/\s+/).filter(Boolean).slice(0, 2).map((w) => w[0]).join('').toUpperCase() || '#';

/* Mesajlar */
let chats = [];
let activeChat = null;
let messages = [];

async function loadChats() {
  try { chats = await api.messages.chats(); } catch (e) { chats = []; setStatus({ connected: false, error: e.message }); }
  renderChats();
}

function renderChats() {
  const q = $('chatSearch').value.trim().toLocaleLowerCase('tr');
  const list = $('chatList');
  list.replaceChildren(...chats
    .filter((c) => !q || c.name.toLocaleLowerCase('tr').includes(q))
    .map((c) => {
      const li = el('li', c.id === activeChat?.id ? 'active' : '');
      li.append(el('div', 'avatar', initials(c.name)), el('div', 'name', c.name),
        el('time', '', c.lastMessage ? fmtListDate(c.lastMessage.date) : ''),
        el('div', 'preview', c.lastMessage ? (c.lastMessage.text || 'Ek') : ''));
      li.onclick = () => openChat(c);
      return li;
    }));
}
$('chatSearch').oninput = renderChats;

async function openChat(chat) {
  activeChat = chat;
  $('convoName').textContent = chat.name;
  $('convoSub').textContent = chat.group ? chat.participants.join(', ') : (chat.participants[0] || '');
  $('composer').hidden = false;
  $('typing').hidden = true;
  renderChats();
  messages = await api.messages.list(chat.id);
  renderMessages();
  $('composerInput').focus();
}

async function renderMessages() {
  const box = $('bubbles');
  const nodes = [];
  let prev = null;
  for (const m of messages) {
    if (!prev || m.date - prev.date > 15 * 60_000) nodes.push(el('div', 'stamp', fmtListDate(m.date) + (fmtListDate(m.date) === fmtTime(m.date) ? '' : ' ' + fmtTime(m.date))));
    if (activeChat.group && !m.fromMe && m.sender && m.sender !== prev?.sender) nodes.push(el('div', 'sender', m.sender));
    const b = el('div', 'bubble ' + (m.fromMe ? 'out' : 'in'), m.text);
    for (const a of m.attachments || []) {
      if (a.mime?.startsWith('image/')) {
        const img = el('img');
        api.messages.attachmentUrl(a.id).then((u) => u && (img.src = u));
        b.append(img);
      } else {
        b.append(el('div', 'hint', '📎 ' + (a.name || 'Ek')));
      }
    }
    b.title = fmtTime(m.date);
    nodes.push(b);
    prev = m;
  }
  const last = messages.at(-1);
  if (last?.fromMe) nodes.push(el('div', 'receipt', last.read ? 'Okundu' : last.delivered ? 'İletildi' : 'Gönderiliyor…'));
  box.replaceChildren(...nodes);
  box.scrollTop = box.scrollHeight;
}

$('composer').onsubmit = async (e) => {
  e.preventDefault();
  const input = $('composerInput');
  const text = input.value.trim();
  if (!text || !activeChat) return;
  input.value = '';
  const sent = await api.messages.send(activeChat.id, text).catch((err) => { alert('Gönderilemedi: ' + err.message); return null; });
  if (sent && !messages.some((m) => m.id === sent.id)) messages.push(sent);
  renderMessages();
  loadChats();
};

api.messages.onNew((m) => {
  if (activeChat && m.chatId === activeChat.id) {
    const i = messages.findIndex((x) => x.id === m.id);
    if (i >= 0) messages[i] = m; else messages.push(m);
    renderMessages();
  }
  loadChats();
});
api.messages.onTyping((t) => { if (activeChat && t.chatId === activeChat.id) $('typing').hidden = !t.typing; });

function setStatus(s) {
  const pill = $('msgStatus');
  pill.textContent = s.error ? 'Hata' : s.kind === 'demo' ? 'Demo' : s.connected ? 'Bağlı' : 'Bağlantı yok';
  pill.title = s.error || '';
  pill.classList.toggle('ok', !!s.connected && s.kind !== 'demo');
}
api.messages.onStatus((s) => { setStatus(s); if (s.connected) loadChats(); });

/* FaceTime */
let lastHandoff = null;
api.facetime.onHandoff((h) => {
  lastHandoff = h;
  $('handoffBanner').hidden = false;
  show('facetime');
  renderHandoffs();
});
$('handoffGo').onclick = () => { if (lastHandoff) api.facetime.open(lastHandoff.url); $('handoffBanner').hidden = true; };
$('joinForm').onsubmit = (e) => {
  e.preventDefault();
  api.facetime.open($('joinUrl').value.trim()).catch((err) => alert(err.message));
};
$('preferIphone').onchange = (e) => api.settings.set({ preferIphoneCamera: e.target.checked });

let bridgeInfo = null;
function renderHandoffs() {
  const items = bridgeInfo?.handoffs || [];
  if (lastHandoff && !items.some((h) => h.at === lastHandoff.at)) items.unshift(lastHandoff);
  if (!items.length) return;
  $('handoffList').replaceChildren(...items.map((h) => {
    const li = el('li');
    li.append(el('span', '', `${h.from} · ${fmtListDate(h.at)} ${fmtTime(h.at)}`));
    const btn = el('button', 'primary', 'Katıl');
    btn.onclick = () => api.facetime.open(h.url);
    li.append(btn);
    return li;
  }));
}

/* iPhone kamerası */
async function refreshBridge() {
  bridgeInfo = await api.bridge.info();
  $('camQr').src = bridgeInfo.cameraQr;
  $('camUrl').textContent = bridgeInfo.camera;
  $('handoffUrl').textContent = bridgeInfo.handoff;
  $('handoffToken').textContent = bridgeInfo.token;
  setCamStatus(bridgeInfo.phoneConnected);
  renderHandoffs();
}
function setCamStatus(connected) {
  $('camStatus').textContent = connected ? 'iPhone bağlı' : 'Bağlı değil';
  $('camStatus').classList.toggle('ok', connected);
}
api.bridge.onCamera((c) => setCamStatus(c.connected));

let camera = null;
$('camConnect').onclick = async () => {
  if (!bridgeInfo) await refreshBridge();
  camera = camera || installContinuityCamera(bridgeInfo.signal, { onDisconnect: () => ($('camPreview').srcObject = null) });
  try {
    $('camPreview').srcObject = await camera.connect();
  } catch (e) {
    alert(e.message + '. Önce iPhone\'da QR kodu açıp “Kamerayı Başlat”a dokun.');
  }
};

/* Ayarlar */
$('btScan').onclick = async () => {
  const sel = $('btDevice');
  try {
    const list = await api.bluetooth.devices();
    sel.replaceChildren(new Option('İlk eşleştirilmiş iPhone', ''), ...list.map((d) => new Option(d.name, d.id)));
    $('settingsMsg').textContent = list.length ? `${list.length} cihaz bulundu` : 'Eşleştirilmiş iPhone yok';
  } catch (e) {
    $('settingsMsg').textContent = e.message;
  }
};

async function loadSettings() {
  const s = await api.settings.get();
  const f = $('settingsForm');
  f.backend.value = s.backend;
  if (s.btDevice) $('btDevice').append(new Option(s.btDevice, s.btDevice, true, true));
  f.bbUrl.value = s.bbUrl;
  f.bbPassword.value = s.bbPassword;
  $('preferIphone').checked = s.preferIphoneCamera;
}
$('settingsForm').onsubmit = async (e) => {
  e.preventDefault();
  const f = e.target;
  $('settingsMsg').textContent = 'Bağlanılıyor…';
  await api.settings.set({ backend: f.backend.value, btDevice: f.btDevice.value, bbUrl: f.bbUrl.value.trim(), bbPassword: f.bbPassword.value });
  $('settingsMsg').textContent = 'Kaydedildi';
  activeChat = null;
  loadChats();
};

/* Başlangıç */
show(new URLSearchParams(location.search).get('view') || 'messages');
loadSettings();
loadChats();
api.messages.status().then(setStatus);
