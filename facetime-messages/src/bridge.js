// iPhone <-> Windows köprüsü.
//  - HTTPS :51515  /camera      iPhone Safari'de açılan kamera sayfası (getUserMedia güvenli bağlam ister)
//  - WSS   :51515  /signal      WebRTC sinyalleşmesi (iPhone = yayıncı, Windows pencereleri = izleyici)
//  - HTTP  :51516  /handoff     iOS Kestirmesi'nin FaceTime bağlantısını gönderdiği uç
// Electron'dan bağımsızdır; testlerde doğrudan çalıştırılabilir.
const https = require('https');
const http = require('http');
const fs = require('fs');
const os = require('os');
const path = require('path');
const crypto = require('crypto');
const { EventEmitter } = require('events');
const { WebSocketServer } = require('ws');
const selfsigned = require('selfsigned');

const FACETIME_LINK = /^https:\/\/facetime\.apple\.com\/join#/;

function lanAddresses() {
  return Object.values(os.networkInterfaces())
    .flat()
    .filter((i) => i && i.family === 'IPv4' && !i.internal)
    .map((i) => i.address);
}

function loadOrCreateCert(dir) {
  const keyPath = path.join(dir, 'bridge-key.pem');
  const certPath = path.join(dir, 'bridge-cert.pem');
  if (fs.existsSync(keyPath) && fs.existsSync(certPath)) {
    return { key: fs.readFileSync(keyPath), cert: fs.readFileSync(certPath) };
  }
  const altNames = [{ type: 2, value: 'localhost' }, ...['127.0.0.1', ...lanAddresses()].map((ip) => ({ type: 7, ip }))];
  const pems = selfsigned.generate([{ name: 'commonName', value: 'Sureklilik Bridge' }], {
    days: 825, keySize: 2048, algorithm: 'sha256',
    extensions: [
      { name: 'basicConstraints', cA: false },
      { name: 'keyUsage', digitalSignature: true, keyEncipherment: true },
      { name: 'extKeyUsage', serverAuth: true },
      { name: 'subjectAltName', altNames },
    ],
  });
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(keyPath, pems.private);
  fs.writeFileSync(certPath, pems.cert);
  return { key: pems.private, cert: pems.cert };
}

class Bridge extends EventEmitter {
  constructor({ dataDir, token, httpsPort = 51515, httpPort = 51516 }) {
    super();
    this.dataDir = dataDir;
    this.token = token || crypto.randomBytes(12).toString('base64url');
    this.httpsPort = httpsPort;
    this.httpPort = httpPort;
    this.phone = null;            // iPhone kamera soketi
    this.viewers = new Map();     // viewerId -> soket
  }

  urls() {
    const ip = lanAddresses()[0] || '127.0.0.1';
    return {
      camera: `https://${ip}:${this.httpsPort}/camera?t=${this.token}`,
      handoff: `http://${ip}:${this.httpPort}/handoff`,
      signal: `wss://localhost:${this.httpsPort}/signal?t=${this.token}&role=viewer`,
    };
  }

  authorized(url) {
    const t = url.searchParams.get('t');
    return t && t.length === this.token.length && crypto.timingSafeEqual(Buffer.from(t), Buffer.from(this.token));
  }

  async start() {
    const tls = loadOrCreateCert(this.dataDir);
    this.httpsServer = https.createServer(tls, (req, res) => this.onHttps(req, res));
    this.httpServer = http.createServer((req, res) => this.onHandoff(req, res));
    this.wss = new WebSocketServer({ noServer: true });
    this.httpsServer.on('upgrade', (req, socket, head) => {
      const url = new URL(req.url, 'https://x');
      if (url.pathname !== '/signal' || !this.authorized(url)) return socket.destroy();
      this.wss.handleUpgrade(req, socket, head, (ws) => this.onSocket(ws, url.searchParams.get('role')));
    });
    await Promise.all([
      new Promise((r) => this.httpsServer.listen(this.httpsPort, '0.0.0.0', r)),
      new Promise((r) => this.httpServer.listen(this.httpPort, '0.0.0.0', r)),
    ]);
    return this;
  }

  stop() {
    for (const c of this.wss?.clients || []) c.terminate();
    return Promise.all([this.httpsServer, this.httpServer].map((s) => new Promise((r) => (s ? s.close(r) : r()))));
  }

  onHttps(req, res) {
    const url = new URL(req.url, 'https://x');
    if (url.pathname === '/camera' && this.authorized(url)) {
      res.writeHead(200, { 'content-type': 'text/html; charset=utf-8', 'cache-control': 'no-store' });
      return fs.createReadStream(path.join(__dirname, 'camera', 'phone.html')).pipe(res);
    }
    res.writeHead(404).end();
  }

  // POST /handoff  {"url":"https://facetime.apple.com/join#...","token":"..."}
  onHandoff(req, res) {
    const url = new URL(req.url, 'http://x');
    if (req.method !== 'POST' || url.pathname !== '/handoff') return res.writeHead(404).end();
    let body = '';
    req.on('data', (c) => { body += c; if (body.length > 8192) req.destroy(); });
    req.on('end', () => {
      let data;
      try { data = JSON.parse(body); } catch { data = Object.fromEntries(new URLSearchParams(body)); }
      url.searchParams.set('t', data.token || '');
      if (!this.authorized(url)) return res.writeHead(401).end('token');
      const link = String(data.url || '').trim();
      if (!FACETIME_LINK.test(link)) return res.writeHead(400).end('FaceTime bağlantısı değil');
      this.emit('handoff', { url: link, from: data.device || 'iPhone', at: Date.now() });
      res.writeHead(200, { 'content-type': 'application/json' }).end('{"ok":true}');
    });
  }

  onSocket(ws, role) {
    if (role === 'phone') {
      this.phone?.close();
      this.phone = ws;
      this.emit('camera', { connected: true });
      for (const id of this.viewers.keys()) ws.send(JSON.stringify({ type: 'want', viewer: id }));
      ws.on('message', (raw) => {
        const msg = safeJson(raw);
        if (msg?.viewer) this.viewers.get(msg.viewer)?.send(JSON.stringify(msg));
      });
      ws.on('close', () => {
        if (this.phone !== ws) return;
        this.phone = null;
        this.emit('camera', { connected: false });
        for (const v of this.viewers.values()) v.send(JSON.stringify({ type: 'bye' }));
      });
      return;
    }
    const id = crypto.randomUUID();
    this.viewers.set(id, ws);
    this.phone?.send(JSON.stringify({ type: 'want', viewer: id }));
    ws.on('message', (raw) => {
      const msg = safeJson(raw);
      if (msg) this.phone?.send(JSON.stringify({ ...msg, viewer: id }));
    });
    ws.on('close', () => {
      this.viewers.delete(id);
      this.phone?.send(JSON.stringify({ type: 'bye', viewer: id }));
    });
  }
}

function safeJson(raw) {
  try { return JSON.parse(raw); } catch { return null; }
}

module.exports = { Bridge, lanAddresses, FACETIME_LINK };
