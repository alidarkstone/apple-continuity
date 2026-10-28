// macOS sanal makinesini (VMware Workstation) pencere açmadan yönetir.
// İçinde BlueBubbles Server çalışan VM'i uygulama açılınca başlatır, IP'sini bulur, çıkışta duraklatır.
const { EventEmitter } = require('events');
const { execFile } = require('child_process');
const fs = require('fs');
const path = require('path');

function findVmrun() {
  const roots = [process.env['ProgramFiles(x86)'], process.env.ProgramFiles].filter(Boolean);
  return roots.map((r) => path.join(r, 'VMware', 'VMware Workstation', 'vmrun.exe')).find((p) => fs.existsSync(p));
}

class VmManager extends EventEmitter {
  constructor({ vmx, vmrun, bbPort = 1234 } = {}) {
    super();
    this.vmx = vmx;
    this.vmrun = vmrun || findVmrun();
    this.bbPort = bbPort;
    this.state = 'stopped';
  }

  run(args, timeout = 120_000) {
    return new Promise((resolve, reject) => {
      if (!this.vmrun) return reject(new Error('VMware Workstation bulunamadı (vmrun.exe)'));
      execFile(this.vmrun, args, { timeout, windowsHide: true }, (err, stdout, stderr) => {
        // vmrun hataları stdout'a "Error: ..." olarak yazar.
        const out = String(stdout).trim();
        if (err || /^Error:/m.test(out)) return reject(new Error((out || String(stderr) || err.message).replace(/^Error:\s*/, '')));
        resolve(out);
      });
    });
  }

  setState(state, extra = {}) {
    this.state = state;
    this.emit('status', { state, ...extra });
  }

  async isRunning() {
    const out = await this.run(['list']);
    const norm = (p) => path.resolve(p).toLowerCase();
    return out.split(/\r?\n/).slice(1).some((l) => l.trim() && norm(l.trim()) === norm(this.vmx));
  }

  // VM'i başlatır (zaten çalışıyorsa dokunmaz) ve BlueBubbles adresini döner.
  async start() {
    if (!this.vmx || !fs.existsSync(this.vmx)) throw new Error('Sanal makine dosyası (.vmx) bulunamadı');
    this.setState('starting');
    try {
      if (!(await this.isRunning())) await this.run(['-T', 'ws', 'start', this.vmx, 'nogui']);
      const ip = await this.run(['-T', 'ws', 'getGuestIPAddress', this.vmx, '-wait'], 300_000);
      const url = `http://${ip}:${this.bbPort}`;
      this.setState('running', { ip, url });
      return url;
    } catch (err) {
      this.setState('error', { error: err.message });
      throw err;
    }
  }

  // Yalnızca VM kapalıyken: VMware çalışan bir VM'in .vmx dosyasını kendi kopyasıyla ezer.
  async tune(overrides) {
    if (await this.isRunning()) throw new Error('Hafif profil için önce sanal makineyi kapat');
    fs.copyFileSync(this.vmx, this.vmx + '.bak');
    fs.writeFileSync(this.vmx, applyLightProfile(fs.readFileSync(this.vmx, 'utf8'), overrides));
  }

  async suspend() {
    if (!(await this.isRunning().catch(() => false))) return;
    this.setState('suspending');
    await this.run(['-T', 'ws', 'suspend', this.vmx, 'soft']).catch(() => {});
    this.setState('stopped');
  }
}

// Mesajlar köprüsü için yeterli en hafif donanım: 3D, ses, yazıcı yok; bellek diske yansıtılmaz, paylaşım açık.
// 4 GB, güncel macOS'un takılmadan çalıştığı alt sınır; 2 çekirdek Mesajlar + BlueBubbles için yeterli.
const LIGHT_PROFILE = {
  numvcpus: '2',
  'cpuid.coresPerSocket': '2',
  memsize: '4096',
  'mks.enable3d': 'FALSE',
  'svga.vramSize': '8388608',
  'sound.present': 'FALSE',
  'serial0.present': 'FALSE',
  'printers.enabled': 'FALSE',
  'floppy0.present': 'FALSE',
  'isolation.tools.dnd.disable': 'TRUE',
  'isolation.tools.copy.disable': 'TRUE',
  'isolation.tools.paste.disable': 'TRUE',
  MemTrimRate: '30',
  'sched.mem.pshare.enable': 'TRUE',
  'mainMem.useNamedFile': 'FALSE',
  'tools.syncTime': 'TRUE',
};

// .vmx metnine hafif profili uygular; var olan anahtarları günceller, olmayanları ekler.
function applyLightProfile(text, overrides = {}) {
  const want = { ...LIGHT_PROFILE, ...overrides };
  const seen = new Set();
  const lines = text.split(/\r?\n/).map((line) => {
    const m = line.match(/^\s*([^=\s]+)\s*=/);
    const key = m && Object.keys(want).find((k) => k.toLowerCase() === m[1].toLowerCase());
    if (!key) return line;
    seen.add(key);
    return `${key} = "${want[key]}"`;
  });
  while (lines.length && lines.at(-1) === '') lines.pop();
  for (const [k, v] of Object.entries(want)) if (!seen.has(k)) lines.push(`${k} = "${v}"`);
  return lines.join('\r\n') + '\r\n';
}

// BlueBubbles VM açılır açılmaz hazır olmaz; sunucu yanıt verene kadar bekle.
async function waitForServer(url, password, { timeoutMs = 300_000, intervalMs = 5_000, fetchImpl = fetch } = {}) {
  const deadline = Date.now() + timeoutMs;
  const u = new URL('/api/v1/server/info', url);
  u.searchParams.set('password', password || '');
  while (Date.now() < deadline) {
    try {
      const res = await fetchImpl(u, { signal: AbortSignal.timeout(intervalMs) });
      if (res.ok) return true;
    } catch { /* henüz açılmadı */ }
    await new Promise((r) => setTimeout(r, intervalMs));
  }
  throw new Error('BlueBubbles sanal makinede yanıt vermedi');
}

module.exports = { VmManager, waitForServer, findVmrun, applyLightProfile, LIGHT_PROFILE };
