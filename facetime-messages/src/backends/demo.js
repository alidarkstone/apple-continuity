// Sunucu olmadan arayüzü denemek için sahte arka uç.
const { EventEmitter } = require('events');
const crypto = require('crypto');

const now = Date.now();
const min = 60_000;

function seed() {
  return [
    { id: 'demo-ayse', name: 'Ayşe', group: false, participants: ['+90 555 000 0001'], messages: [
      { text: 'Akşam FaceTime yapalım mı?', fromMe: false, date: now - 42 * min },
      { text: 'Olur, 9 gibi ararım', fromMe: true, date: now - 40 * min, read: true },
      { text: 'Süper 👍', fromMe: false, date: now - 39 * min },
    ] },
    { id: 'demo-aile', name: 'Aile 🏡', group: true, participants: ['Annem', 'Babam', 'Mert'], messages: [
      { text: 'Pazar kahvaltısı bizde', fromMe: false, sender: 'Annem', date: now - 3 * 60 * min },
      { text: 'Geliyoruz', fromMe: false, sender: 'Mert', date: now - 170 * min },
      { text: 'Ben de oradayım', fromMe: true, date: now - 160 * min, read: true },
    ] },
    { id: 'demo-can', name: 'Can', group: false, participants: ['can@icloud.com'], messages: [
      { text: 'Windows\'tan iMessage mı atıyorsun şu an? 😄', fromMe: false, date: now - 26 * 60 * min },
    ] },
  ].map((c) => ({
    ...c,
    messages: c.messages.map((m) => ({
      id: crypto.randomUUID(), chatId: c.id, sender: null, delivered: true, read: false, attachments: [], ...m,
    })),
  }));
}

class DemoBackend extends EventEmitter {
  constructor() {
    super();
    this.kind = 'demo';
    this.chats = seed();
  }

  async connect() { setTimeout(() => this.emit('status', { connected: true }), 0); return this; }
  disconnect() {}

  async listChats() {
    return this.chats
      .map(({ messages, ...c }) => ({ ...c, lastMessage: messages.at(-1) || null }))
      .sort((a, b) => (b.lastMessage?.date || 0) - (a.lastMessage?.date || 0));
  }

  async listMessages(chatId) {
    return this.chats.find((c) => c.id === chatId)?.messages || [];
  }

  async sendText(chatId, text) {
    const chat = this.chats.find((c) => c.id === chatId);
    const m = { id: crypto.randomUUID(), chatId, text, fromMe: true, date: Date.now(), delivered: true, read: false, sender: null, attachments: [] };
    chat.messages.push(m);
    // Karşı taraf yazıyor... ve cevap veriyor.
    setTimeout(() => this.emit('typing', { chatId, typing: true }), 700);
    setTimeout(() => {
      this.emit('typing', { chatId, typing: false });
      const reply = { id: crypto.randomUUID(), chatId, text: 'Demo modundasın — gerçek mesajlar için Ayarlar\'dan BlueBubbles\'ı bağla.', fromMe: false, sender: chat.group ? chat.participants[0] : null, date: Date.now(), delivered: true, read: false, attachments: [] };
      chat.messages.push(reply);
      this.emit('message', reply);
    }, 2200);
    return m;
  }

  attachmentUrl() { return null; }
}

module.exports = { DemoBackend };
