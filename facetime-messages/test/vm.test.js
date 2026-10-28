const test = require('node:test');
const assert = require('node:assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { VmManager, waitForServer, applyLightProfile } = require('../src/vm');

const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'vm-'));
const vmx = path.join(dir, 'macOS.vmx');
process.env.FAKE_VMRUN_STATE = path.join(dir, 'state');

test('light profile rewrites existing keys and appends missing ones', () => {
  const out = applyLightProfile('displayName = "macOS"\nmemsize = "8192"\nMKS.enable3d = "TRUE"\n');
  assert.match(out, /^displayName = "macOS"\r\n/);
  assert.match(out, /memsize = "4096"/);
  assert.match(out, /mks\.enable3d = "FALSE"/);
  assert.doesNotMatch(out, /8192|"TRUE"\r\nMKS/);
  assert.match(out, /sound\.present = "FALSE"/);
  assert.equal(out.match(/memsize/g).length, 1);
  assert.match(applyLightProfile('', { memsize: '6144' }), /memsize = "6144"/);
});

test('vm manager starts headless, finds the guest, tunes only when off, suspends', async () => {
  fs.writeFileSync(vmx, 'memsize = "8192"\n');
  const vm = new VmManager({ vmx, vmrun: path.join(__dirname, 'fake-vmrun.js') });
  const states = [];
  vm.on('status', (s) => states.push(s.state));
  await vm.tune();
  assert.match(fs.readFileSync(vmx, 'utf8'), /memsize = "4096"/);
  assert.ok(fs.existsSync(vmx + '.bak'));
  assert.equal(await vm.start(), 'http://192.168.56.10:1234');
  assert.equal(await vm.isRunning(), true);
  await assert.rejects(vm.tune(), /kapat/);
  await vm.suspend();
  assert.equal(await vm.isRunning(), false);
  assert.deepEqual(states, ['starting', 'running', 'suspending', 'stopped']);
});

test('waitForServer polls until BlueBubbles answers', async () => {
  let calls = 0;
  const fetchImpl = async () => ({ ok: ++calls >= 3 });
  assert.equal(await waitForServer('http://x:1234', 'pw', { intervalMs: 5, fetchImpl }), true);
  assert.equal(calls, 3);
  await assert.rejects(waitForServer('http://x:1234', 'pw', { timeoutMs: 20, intervalMs: 5, fetchImpl: async () => ({ ok: false }) }), /yanıt vermedi/);
});
