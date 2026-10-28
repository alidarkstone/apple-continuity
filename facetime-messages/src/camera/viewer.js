// iPhone kamerasını bir MediaStream olarak alan izleyici.
// Hem uygulama penceresinde hem de FaceTime sayfasının ana dünyasına enjekte edilerek çalışır,
// bu yüzden dış bağımlılığı yoktur ve kendi başına bir fonksiyon olarak tutulur.
function installContinuityCamera(signalUrl, opts) {
  const DEVICE_ID = 'continuity-iphone';
  const LABEL = 'iPhone Kamerası (Süreklilik)';
  let current = null;   // { stream, pc, ws }
  let pending = null;

  function connect() {
    if (current && current.stream.getVideoTracks().some((t) => t.readyState === 'live')) return Promise.resolve(current.stream);
    if (pending) return pending;
    pending = new Promise((resolve, reject) => {
      const ws = new WebSocket(signalUrl);
      const pc = new RTCPeerConnection({ iceServers: [] });
      const timer = setTimeout(() => { cleanup(); reject(new Error('iPhone kamerası bulunamadı')); }, opts?.timeoutMs || 15000);
      function cleanup() { clearTimeout(timer); pending = null; try { ws.close(); } catch {} try { pc.close(); } catch {} }
      pc.ontrack = (e) => {
        clearTimeout(timer);
        const stream = e.streams[0] || new MediaStream([e.track]);
        current = { stream, pc, ws };
        pending = null;
        resolve(stream);
      };
      pc.onicecandidate = (e) => e.candidate && ws.readyState === 1 && ws.send(JSON.stringify({ type: 'candidate', candidate: e.candidate }));
      ws.onmessage = async (e) => {
        const msg = JSON.parse(e.data);
        if (msg.type === 'offer') {
          await pc.setRemoteDescription({ type: 'offer', sdp: msg.sdp });
          await pc.setLocalDescription(await pc.createAnswer());
          ws.send(JSON.stringify({ type: 'answer', sdp: pc.localDescription.sdp }));
        } else if (msg.type === 'candidate' && msg.candidate) {
          await pc.addIceCandidate(msg.candidate).catch(() => {});
        } else if (msg.type === 'bye') {
          if (current?.pc === pc) { current.stream.getTracks().forEach((t) => t.stop()); current = null; }
          opts?.onDisconnect?.();
        }
      };
      ws.onerror = () => { if (!current) { cleanup(); reject(new Error('Köprüye bağlanılamadı')); } };
    });
    return pending;
  }

  // FaceTime gibi sayfalarda getUserMedia ve enumerateDevices'ı sarar.
  function patchMediaDevices(preferIphone) {
    const md = navigator.mediaDevices;
    const origGUM = md.getUserMedia.bind(md);
    const origEnum = md.enumerateDevices.bind(md);

    md.enumerateDevices = async () => {
      const list = await origEnum();
      const fake = { deviceId: DEVICE_ID, groupId: DEVICE_ID, kind: 'videoinput', label: LABEL, toJSON() { return this; } };
      return preferIphone() ? [fake, ...list] : [...list, fake];
    };

    md.getUserMedia = async (constraints = {}) => {
      const v = constraints.video;
      const requested = v && typeof v === 'object' ? [v.deviceId?.exact, v.deviceId?.ideal, v.deviceId].flat().find((x) => typeof x === 'string') : null;
      const useIphone = v && (requested === DEVICE_ID || (!requested && preferIphone()));
      if (!useIphone) return origGUM(constraints);
      const [cam, mic] = await Promise.all([
        connect(),
        constraints.audio ? origGUM({ audio: constraints.audio }) : Promise.resolve(null),
      ]);
      const tracks = cam.getVideoTracks().map((t) => t.clone());
      return new MediaStream([...tracks, ...(mic ? mic.getAudioTracks() : [])]);
    };
  }

  return { connect, patchMediaDevices, DEVICE_ID, LABEL };
}

if (typeof module !== 'undefined') module.exports = { installContinuityCamera };
