"""UniClip: Apple cihazlardan Windows 11 panosuna kopyala/yapistir koprusu.

Yalnizca Python standart kutuphanesi kullanir. Yerel agda (LAN) bir HTTP
sunucusu acar; iPhone/iPad Kisayollari ve Mac betigi buraya icerik gonderir.

  POST /clip   govde = metin (text/plain) veya gorsel (image/png, image/jpeg)
  GET  /clip   Windows panosundaki metni dondurur (Windows -> Apple yonu)
  GET  /health durum kontrolu

Her istekte "X-Token: <token>" basligi (veya ?token=...) gerekir.
"""
import ctypes
import json
import os
import secrets
import socket
import subprocess
import sys
import tempfile
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

HERE = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(HERE, "uniclip_config.json")
MAX_BODY = 25 * 1024 * 1024  # 25 MB
IS_WINDOWS = sys.platform == "win32"


def load_config():
    if os.path.exists(CONFIG_PATH):
        with open(CONFIG_PATH, encoding="utf-8") as f:
            cfg = json.load(f)
    else:
        cfg = {}
    changed = False
    if not cfg.get("token"):
        cfg["token"] = secrets.token_urlsafe(16)
        changed = True
    if not cfg.get("port"):
        cfg["port"] = 8765
        changed = True
    cfg.setdefault("notify", True)
    if changed:
        with open(CONFIG_PATH, "w", encoding="utf-8") as f:
            json.dump(cfg, f, indent=2)
    return cfg


# --------------------------------------------------------------------------
# Pano erisimi
# --------------------------------------------------------------------------
class WindowsClipboard:
    CF_UNICODETEXT = 13
    GMEM_MOVEABLE = 0x0002

    def __init__(self):
        from ctypes import wintypes

        self.user32 = ctypes.WinDLL("user32", use_last_error=True)
        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        u, k = self.user32, self.kernel32
        u.OpenClipboard.argtypes = [wintypes.HWND]
        u.OpenClipboard.restype = wintypes.BOOL
        u.GetClipboardData.argtypes = [wintypes.UINT]
        u.GetClipboardData.restype = wintypes.HANDLE
        u.SetClipboardData.argtypes = [wintypes.UINT, wintypes.HANDLE]
        u.SetClipboardData.restype = wintypes.HANDLE
        u.GetClipboardSequenceNumber.restype = wintypes.DWORD
        k.GlobalAlloc.argtypes = [wintypes.UINT, ctypes.c_size_t]
        k.GlobalAlloc.restype = wintypes.HGLOBAL
        k.GlobalLock.argtypes = [wintypes.HGLOBAL]
        k.GlobalLock.restype = wintypes.LPVOID
        k.GlobalUnlock.argtypes = [wintypes.HGLOBAL]
        k.GlobalFree.argtypes = [wintypes.HGLOBAL]
        self.lock = threading.Lock()

    def _open(self):
        for _ in range(20):  # baska bir uygulama panoyu tutuyor olabilir
            if self.user32.OpenClipboard(None):
                return
            time.sleep(0.05)
        raise OSError("Pano acilamadi (baska bir uygulama kullaniyor)")

    def set_text(self, text):
        data = text.encode("utf-16-le") + b"\x00\x00"
        with self.lock:
            self._open()
            try:
                self.user32.EmptyClipboard()
                h = self.kernel32.GlobalAlloc(self.GMEM_MOVEABLE, len(data))
                p = self.kernel32.GlobalLock(h)
                ctypes.memmove(p, data, len(data))
                self.kernel32.GlobalUnlock(h)
                if not self.user32.SetClipboardData(self.CF_UNICODETEXT, h):
                    self.kernel32.GlobalFree(h)
                    raise OSError("SetClipboardData basarisiz")
            finally:
                self.user32.CloseClipboard()

    def get_text(self):
        with self.lock:
            self._open()
            try:
                h = self.user32.GetClipboardData(self.CF_UNICODETEXT)
                if not h:
                    return ""
                p = self.kernel32.GlobalLock(h)
                try:
                    return ctypes.wstring_at(p)
                finally:
                    self.kernel32.GlobalUnlock(h)
            finally:
                self.user32.CloseClipboard()

    def set_image_file(self, path):
        # Gorseli .NET uzerinden (PNG, JPEG, BMP, GIF) panoya koyar.
        safe = path.replace("'", "''")
        script = (
            "Add-Type -AssemblyName System.Windows.Forms,System.Drawing;"
            f"$img=[System.Drawing.Image]::FromFile('{safe}');"
            "[System.Windows.Forms.Clipboard]::SetImage($img);$img.Dispose()"
        )
        with self.lock:
            subprocess.run(
                ["powershell", "-NoProfile", "-STA", "-Command", script],
                check=True,
                creationflags=0x08000000,  # CREATE_NO_WINDOW
                timeout=30,
            )


class MemoryClipboard:
    """Windows disinda (test icin) kullanilan sahte pano."""

    def __init__(self):
        self.text = ""
        self.image_path = None

    def set_text(self, text):
        self.text = text

    def get_text(self):
        return self.text

    def set_image_file(self, path):
        self.image_path = path


def notify(title, message):
    if not IS_WINDOWS:
        print(f"[bildirim] {title}: {message}")
        return
    msg = message.replace("'", "''").replace("\n", " ")[:120]
    title = title.replace("'", "''")
    script = (
        "Add-Type -AssemblyName System.Windows.Forms;"
        "$n=New-Object System.Windows.Forms.NotifyIcon;"
        "$n.Icon=[System.Drawing.SystemIcons]::Information;$n.Visible=$true;"
        f"$n.ShowBalloonTip(2500,'{title}','{msg}','Info');"
        "Start-Sleep -Seconds 3;$n.Dispose()"
    )
    subprocess.Popen(
        ["powershell", "-NoProfile", "-WindowStyle", "Hidden", "-Command", script],
        creationflags=0x08000000,
    )


# --------------------------------------------------------------------------
# HTTP sunucusu
# --------------------------------------------------------------------------
IMAGE_EXT = {"image/png": ".png", "image/jpeg": ".jpg", "image/jpg": ".jpg",
             "image/gif": ".gif", "image/bmp": ".bmp"}


def make_handler(cfg, clipboard):
    class Handler(BaseHTTPRequestHandler):
        server_version = "UniClip/1.0"

        def log_message(self, fmt, *args):
            print(f"{self.address_string()} - {fmt % args}")

        def _authorized(self):
            token = self.headers.get("X-Token")
            if not token:
                token = parse_qs(urlparse(self.path).query).get("token", [None])[0]
            return token is not None and secrets.compare_digest(token, cfg["token"])

        def _send(self, code, body, ctype="text/plain; charset=utf-8"):
            data = body.encode("utf-8") if isinstance(body, str) else body
            self.send_response(code)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def do_GET(self):
            path = urlparse(self.path).path
            if path == "/health":
                return self._send(200, "ok")
            if not self._authorized():
                return self._send(401, "yetkisiz")
            if path == "/clip":
                return self._send(200, clipboard.get_text())
            self._send(404, "bulunamadi")

        def do_POST(self):
            if urlparse(self.path).path != "/clip":
                return self._send(404, "bulunamadi")
            if not self._authorized():
                return self._send(401, "yetkisiz")
            length = int(self.headers.get("Content-Length") or 0)
            if length <= 0:
                return self._send(400, "bos icerik")
            if length > MAX_BODY:
                return self._send(413, "icerik cok buyuk")
            body = self.rfile.read(length)
            ctype = (self.headers.get("Content-Type") or "text/plain").split(";")[0].strip().lower()
            device = self.headers.get("X-Device", "Apple cihaz")

            if ctype.startswith("image/"):
                if ctype not in IMAGE_EXT:
                    return self._send(415, f"desteklenmeyen gorsel: {ctype} (PNG/JPEG gonderin)")
                fd, path = tempfile.mkstemp(prefix="uniclip_", suffix=IMAGE_EXT[ctype])
                with os.fdopen(fd, "wb") as f:
                    f.write(body)
                clipboard.set_image_file(path)
                summary = "Gorsel kopyalandi"
            else:
                text = body.decode("utf-8", errors="replace")
                clipboard.set_text(text)
                summary = text if len(text) <= 60 else text[:57] + "..."
            if cfg.get("notify"):
                notify(f"{device} panosundan", summary)
            self._send(200, "ok")

    return Handler


def local_ips():
    ips = set()
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("10.255.255.255", 1))
        ips.add(s.getsockname()[0])
        s.close()
    except OSError:
        pass
    try:
        for info in socket.getaddrinfo(socket.gethostname(), None, socket.AF_INET):
            ips.add(info[4][0])
    except OSError:
        pass
    return sorted(ip for ip in ips if not ip.startswith("127."))


def main():
    cfg = load_config()
    clipboard = WindowsClipboard() if IS_WINDOWS else MemoryClipboard()
    server = ThreadingHTTPServer(("0.0.0.0", cfg["port"]), make_handler(cfg, clipboard))
    print("UniClip calisiyor.")
    for ip in local_ips() or ["<bu-bilgisayarin-ip-adresi>"]:
        print(f"  Adres : http://{ip}:{cfg['port']}/clip")
    print(f"  Token : {cfg['token']}")
    print("Durdurmak icin Ctrl+C.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
