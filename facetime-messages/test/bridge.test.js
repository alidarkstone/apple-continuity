const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const WebSocket = require('ws');
const { Bridge } = require('../src/bridge');

const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'bridge-'));
const token = 'test-token-123';
let bridge;

test.before(async () => { bridge = await new Bridge({ dataDir, token, httpsPort: 52515, httpPort: 52516 }).start(); });
test.after(() => bridge.stop());

const post = (body) => fetch('http://127.0.0.1:52516/handoff', { method: 'POST', body: JSON.stringify(body) });

test('handoff accepts a FaceTime link with the right token', async () => {
  const got = new Promise((r) => bridge.once('handoff', r));
  const res = await post({ url: 'https://facetime.apple.com/join#v=1&p=abc&k=def', token, device: 'iPhone 16' });
  assert.equal(res.status, 200);
  const h = await got;
  assert.equal(h.from, 'iPhone 16');
  assert.match(h.url, /^https:\/\/facetime\.apple\.com\/join#/);
});

test('handoff rejects a wrong token and non-FaceTime urls', async () => {
  assert.equal((await post({ url: 'https://facetime.apple.com/join#v=1', token: 'nope' })).status, 401);
  assert.equal((await post({ url: 'https://evil.example/join#v=1', token })).status, 400);
});

test('signaling relays offer/answer between phone and viewer', async () => {
  const opts = { rejectUnauthorized: false };
  const phone = new WebSocket(`wss://127.0.0.1:52515/signal?t=${token}&role=phone`, opts);
  await new Promise((r) => phone.once('open', r));
  const want = new Promise((r) => phone.once('message', (d) => r(JSON.parse(d))));
  const viewer = new WebSocket(`wss://127.0.0.1:52515/signal?t=${token}&role=viewer`, opts);
  const w = await want;
  assert.equal(w.type, 'want');
  const offer = new Promise((r) => viewer.once('message', (d) => r(JSON.parse(d))));
  phone.send(JSON.stringify({ type: 'offer', viewer: w.viewer, sdp: 'v=0' }));
  assert.equal((await offer).sdp, 'v=0');
  const answer = new Promise((r) => phone.once('message', (d) => r(JSON.parse(d))));
  viewer.send(JSON.stringify({ type: 'answer', sdp: 'v=1' }));
  const a = await answer;
  assert.deepEqual([a.type, a.viewer, a.sdp], ['answer', w.viewer, 'v=1']);
  phone.close(); viewer.close();
});

test('signaling refuses connections without the token', async () => {
  const ws = new WebSocket('wss://127.0.0.1:52515/signal?t=bad&role=viewer', { rejectUnauthorized: false });
  await new Promise((r) => ws.once('error', r));
});

test('camera page is served only with the token', async () => {
  process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
  assert.equal((await fetch(`https://127.0.0.1:52515/camera?t=${token}`)).status, 200);
  assert.equal((await fetch('https://127.0.0.1:52515/camera')).status, 404);
});
