// iPhone sayfası (sahte kamera) -> köprü -> izleyici: gerçek WebRTC uçtan uca.
const { chromium } = require('playwright');
const fs = require('fs'), os = require('os'), path = require('path');
const { Bridge } = require('../src/bridge');
(async () => {
  const bridge = await new Bridge({ dataDir: fs.mkdtempSync(path.join(os.tmpdir(), 'cam-')), token: 'e2e', httpsPort: 53515, httpPort: 53516 }).start();
  const browser = await chromium.launch({ args: ['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream'] });
  const ctx = await browser.newContext({ ignoreHTTPSErrors: true, permissions: ['camera'] });
  const phone = await ctx.newPage();
  await phone.goto('https://localhost:53515/camera?t=e2e');
  await phone.click('#start');
  const viewer = await ctx.newPage();
  await viewer.goto('https://localhost:53515/camera?t=e2e'); // güvenli bağlam için aynı köken
  await viewer.addScriptTag({ content: fs.readFileSync(path.join(__dirname, '../src/camera/viewer.js'), 'utf8') });
  const r = await viewer.evaluate(async () => {
    const cam = installContinuityCamera('wss://localhost:53515/signal?t=e2e&role=viewer');
    cam.patchMediaDevices(() => true);
    const devices = await navigator.mediaDevices.enumerateDevices();
    const s = await navigator.mediaDevices.getUserMedia({ video: true, audio: false });
    const v = document.createElement('video'); v.muted = true; v.srcObject = s; await v.play();
    await new Promise((res) => setTimeout(res, 1500));
    return { first: devices[0].label, tracks: s.getVideoTracks().length, w: v.videoWidth, h: v.videoHeight };
  });
  console.log(JSON.stringify(r), 'phone status:', await phone.textContent('#statusText'));
  await browser.close(); await bridge.stop();
  process.exit(r.tracks === 1 && r.w > 0 ? 0 : 1);
})();
