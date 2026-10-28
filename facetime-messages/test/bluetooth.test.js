const test = require('node:test');
const assert = require('node:assert');
const path = require('path');
const { BluetoothBackend } = require('../src/backends/bluetooth');

test('bluetooth backend groups MAP messages into chats and relays new ones', async (t) => {
  const b = new BluetoothBackend({ command: [process.execPath, path.join(__dirname, 'fake-mapbridge.js')] });
  t.after(() => b.disconnect());
  const status = new Promise((r) => b.once('status', r));
  await b.connect();
  assert.deepEqual(await status, { connected: true, device: 'iPhone' });

  const [chat] = await b.listChats();
  assert.equal(chat.id, '+905550000001');
  assert.equal(chat.name, 'Ayşe');
  assert.deepEqual((await b.listMessages(chat.id)).map((m) => m.text), ['Selam', 'Merhaba']);

  const incoming = new Promise((r) => b.once('message', r));
  const mine = await b.sendText(chat.id, 'Nasılsın?');
  assert.equal(mine.fromMe, true);
  assert.equal((await incoming).text, 'Cevap');
  assert.equal((await b.listChats())[0].lastMessage.text, 'Cevap');
  await assert.rejects(b.call('nope'), /Bilinmeyen komut/);
});
