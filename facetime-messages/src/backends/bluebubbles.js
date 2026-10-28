// BlueBubbles Server (Mac üzerinde çalışır) arka ucu.
// REST: https://docs.bluebubbles.app/server/developer-guides/rest-api-and-webhooks
// Gerçek zamanlı olaylar socket.io üzerinden gelir ("new-message", "updated-message", "typing-indicator").
const { EventEmitter } = require('events');
const crypto = require('crypto');
const { io } = require('socket.io-client');

// Apple tarih damgaları: BlueBubbles milisaniye Unix zamanı döner.
function mapMessage(m) {
  return {
    id: m.guid,
    chatId: m.chats?.[0]?.guid,
    text: m.text || '',
    date: m.dateCreated,
    fromMe: !!m.isFromMe,
    sender: m.handle?.address || null,
    delivered: !!m.dateDelivered,
    read: !!m.dateRead,
    attachments: (m.attachments || []).map((a) => ({
      id: a.guid, name: a.transferName, mime: a.mimeType,
    })),
  };
}

function chatName(c) {
  if (c.displayName) return c.displayName;
  const people = (c.participants || []).map((p) => p.address);
  return people.join(', ') || c.chatIdentifier;
}

class BlueBubblesBackend extends EventEmitter {
  constructor({ url, password }) {
    super();
    this.base = url.replace(/\/+$/, '');
    this.password = password;
    this.kind = 'bluebubbles';
  }

  async api(path, { method = 'GET', body, query = {} } = {}) {
    const u = new URL(this.base + '/api/v1' + path);
    u.searchParams.set('password', this.password);
    for (const [k, v] of Object.entries(query)) u.searchParams.set(k, v);
    const res = await fetch(u, {
      method,
      headers: body ? { 'content-type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    });
    const json = await res.json().catch(() => ({}));
    if (!res.ok || (json.status && json.status >= 400)) {
      throw new Error(json.message || json.error?.message || `BlueBubbles ${res.status}`);
    }
    return json.data;
  }

  async connect() {
    await this.api('/server/info');
    this.socket = io(this.base, { query: { password: this.password }, transports: ['websocket'] });
    this.socket.on('connect', () => this.emit('status', { connected: true }));
    this.socket.on('disconnect', () => this.emit('status', { connected: false }));
    this.socket.on('new-message', (m) => this.emit('message', mapMessage(m)));
    this.socket.on('updated-message', (m) => this.emit('message', mapMessage(m)));
    this.socket.on('typing-indicator', (t) => this.emit('typing', { chatId: t.guid, typing: !!t.display }));
    return this;
  }

  disconnect() { this.socket?.close(); }

  async listChats() {
    const chats = await this.api('/chat/query', {
      method: 'POST',
      body: { limit: 200, offset: 0, with: ['lastMessage', 'participants'], sort: 'lastmessage' },
    });
    return chats.map((c) => ({
      id: c.guid,
      name: chatName(c),
      group: (c.participants || []).length > 1,
      participants: (c.participants || []).map((p) => p.address),
      lastMessage: c.lastMessage ? mapMessage(c.lastMessage) : null,
    }));
  }

  async listMessages(chatId, { limit = 80 } = {}) {
    const msgs = await this.api(`/chat/${encodeURIComponent(chatId)}/message`, {
      query: { limit, sort: 'DESC', with: 'handle,attachment' },
    });
    return msgs.map(mapMessage).map((m) => ({ ...m, chatId })).reverse();
  }

  async sendText(chatId, text) {
    const m = await this.api('/message/text', {
      method: 'POST',
      body: { chatGuid: chatId, tempGuid: 'temp-' + crypto.randomUUID(), message: text, method: 'private-api' },
    }).catch(() => this.api('/message/text', {
      // Private API kapalıysa AppleScript yöntemine düş.
      method: 'POST',
      body: { chatGuid: chatId, tempGuid: 'temp-' + crypto.randomUUID(), message: text, method: 'apple-script' },
    }));
    return m ? { ...mapMessage(m), chatId } : null;
  }

  attachmentUrl(id) {
    return `${this.base}/api/v1/attachment/${encodeURIComponent(id)}/download?password=${encodeURIComponent(this.password)}`;
  }
}

module.exports = { BlueBubblesBackend, mapMessage };
