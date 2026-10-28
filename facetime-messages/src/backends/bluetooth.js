// Mac'siz Mesajlar: iPhone'a Bluetooth MAP ile bağlanan MapBridge.exe yardımcı programını kullanır.
// Microsoft Phone Link ile aynı yol, aynı sınırlar: birebir metin; grup, medya ve eski geçmiş yok.
const { EventEmitter } = require('events');
const { spawn } = require('child_process');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');
const readline = require('readline');

function helperPath() {
  const candidates = [
    process.resourcesPath && path.join(process.resourcesPath, 'MapBridge.exe'),
    path.join(__dirname, '..', '..', 'native', 'out', 'MapBridge.exe'),
  ].filter(Boolean);
  return candidates.find((p) => fs.existsSync(p));
}

class BluetoothBackend extends EventEmitter {
  constructor({ device, command } = {}) {
    super();
    this.kind = 'bluetooth';
    this.device = device;
    this.command = command; // testlerde sahte yardımcı için [exe, ...args]
    this.pending = new Map();
    this.nextId = 1;
    this.messages = new Map(); // id -> mesaj
  }

  start() {
    if (this.proc) return;
    const [exe, ...args] = this.command || [helperPath()];
    if (!exe) throw new Error('MapBridge.exe bulunamadı. Bluetooth Mesajlar yalnızca Windows\'ta çalışır.');
    this.proc = spawn(exe, args, { stdio: ['pipe', 'pipe', 'inherit'], windowsHide: true });
    this.proc.on('exit', () => {
      this.proc = null;
      for (const { reject } of this.pending.values()) reject(new Error('Bluetooth köprüsü kapandı'));
      this.pending.clear();
      this.emit('status', { connected: false });
    });
    readline.createInterface({ input: this.proc.stdout }).on('line', (line) => this.onLine(line));
  }

  onLine(line) {
    let msg;
    try { msg = JSON.parse(line); } catch { return; }
    if (msg.event === 'status') return this.emit('status', { connected: msg.connected, device: msg.device });
    if (msg.event === 'message') return this.store(msg.message, true);
    if (msg.event === 'warning') return this.emit('warning', msg.message);
    const p = this.pending.get(msg.id);
    if (!p) return;
    this.pending.delete(msg.id);
    msg.ok ? p.resolve(msg.result) : p.reject(new Error(msg.error));
  }

  call(cmd, args = {}) {
    this.start();
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.proc.stdin.write(JSON.stringify({ id, cmd, ...args }) + '\n');
    });
  }

  store(m, notify) {
    const isNew = !this.messages.has(m.id);
    this.messages.set(m.id, { delivered: true, attachments: [], ...m });
    if (notify && isNew) this.emit('message', this.messages.get(m.id));
  }

  devices() { return this.call('devices'); }

  async connect() {
    let device = this.device;
    if (!device) {
      const list = await this.devices();
      if (!list.length) throw new Error('Eşleştirilmiş iPhone bulunamadı. Önce Windows Bluetooth ayarlarından eşleştir.');
      device = list[0].id;
    }
    await this.call('connect', { device });
    await this.refresh();
    return this;
  }

  async refresh() {
    for (const folder of ['inbox', 'sent']) {
      const list = await this.call('list', { folder, max: 200 }).catch(() => []);
      list.forEach((m) => this.store(m, false));
    }
  }

  disconnect() {
    const proc = this.proc;
    this.proc = null;
    if (!proc) return;
    proc.stdin.end();
    setTimeout(() => proc.exitCode === null && proc.kill(), 2000).unref();
  }

  async listChats() {
    const chats = new Map();
    for (const m of this.messages.values()) {
      const c = chats.get(m.chatId) || { id: m.chatId, name: m.chatId, group: false, participants: [m.chatId], lastMessage: null };
      if (!m.fromMe && m.sender) c.name = m.sender;
      if (!c.lastMessage || m.date > c.lastMessage.date) c.lastMessage = m;
      chats.set(m.chatId, c);
    }
    return [...chats.values()].sort((a, b) => b.lastMessage.date - a.lastMessage.date);
  }

  async listMessages(chatId) {
    return [...this.messages.values()].filter((m) => m.chatId === chatId).sort((a, b) => a.date - b.date);
  }

  async sendText(chatId, text) {
    await this.call('send', { to: chatId, text });
    // iPhone gönderilen mesajı "sent" klasörüne yazar; o gelene kadar yerel bir kopya göster.
    const m = { id: 'local-' + crypto.randomUUID(), chatId, text, date: Date.now(), fromMe: true, sender: null, read: false };
    this.store(m, false);
    return this.messages.get(m.id);
  }

  attachmentUrl() { return null; }
}

module.exports = { BluetoothBackend, helperPath };
