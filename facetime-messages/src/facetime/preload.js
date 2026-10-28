// FaceTime penceresi: iPhone kamerasını sayfanın kendi getUserMedia'sına bağlar.
// Kod ana dünyaya enjekte edilir; preload'un Node/IPC erişimi sayfaya açılmaz.
const { ipcRenderer, webFrame } = require('electron');

const cfg = ipcRenderer.sendSync('facetime:config');
webFrame.executeJavaScript(`
  ${cfg.source}
  window.__continuity = installContinuityCamera(${JSON.stringify(cfg.signalUrl)});
  window.__continuity.patchMediaDevices(() => ${cfg.preferIphone ? 'true' : 'false'});
`);
