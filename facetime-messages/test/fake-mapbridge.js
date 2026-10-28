// MapBridge.exe'nin JSON satır protokolünü taklit eden sahte yardımcı.
const rl = require('readline').createInterface({ input: process.stdin });
const out = (o) => process.stdout.write(JSON.stringify(o) + '\n');
const inbox = [{ id: '1', chatId: '+905550000001', text: 'Selam', date: 1000, fromMe: false, sender: 'Ayşe', read: true }];
const sent = [{ id: '2', chatId: '+905550000001', text: 'Merhaba', date: 2000, fromMe: true, sender: null, read: true }];
rl.on('line', (line) => {
  const req = JSON.parse(line);
  if (req.cmd === 'devices') return out({ id: req.id, ok: true, result: [{ id: 'bt-1', name: 'iPhone' }] });
  if (req.cmd === 'connect') { out({ event: 'status', connected: true, device: 'iPhone' }); return out({ id: req.id, ok: true, result: { device: 'iPhone' } }); }
  if (req.cmd === 'list') return out({ id: req.id, ok: true, result: req.folder === 'inbox' ? inbox : sent });
  if (req.cmd === 'send') {
    out({ id: req.id, ok: true, result: { sent: true } });
    return setTimeout(() => out({ event: 'message', message: { id: '3', chatId: req.to, text: 'Cevap', date: Date.now() + 1000, fromMe: false, sender: 'Ayşe', read: false } }), 20);
  }
  out({ id: req.id, ok: false, error: 'Bilinmeyen komut' });
});
