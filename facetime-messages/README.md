# Süreklilik — Windows 11 için FaceTime ve Mesajlar

Windows 11'de macOS'taki **Mesajlar**, **FaceTime** ve **Continuity Camera** deneyimine en yakın şeyi sunan bir Electron uygulaması.

| Özellik | Durum | Nasıl |
|---|---|---|
| Mesajlar (iMessage) | ✅ çalışır | Mac üzerinde çalışan [BlueBubbles Server](https://bluebubbles.app)'a bağlanır: gruplar, fotoğraflar, geçmiş, yazıyor göstergesi, okundu bilgisi |
| Mesajlar demo modu | ✅ çalışır | Sunucu olmadan arayüzü denemek için |
| FaceTime'a katılma | ✅ çalışır | `facetime.apple.com` web istemcisini Chrome olarak açar |
| "Windows'ta Devam Et" | ✅ çalışır | iPhone'daki görüşmenin bağlantısı bir iOS Kestirmesi ile Windows'a gelir, bildirime tıklayınca görüşme açılır ([kılavuz](docs/ios-kestirme.md)) |
| Continuity Camera | ✅ uygulama içinde | iPhone Safari'de QR ile açılan sayfa kamerayı WebRTC ile Windows'a yollar; FaceTime penceresinde "iPhone Kamerası" olarak görünür |
| iPhone kamerası Zoom/Teams'te | 🔜 sonraki adım | Windows 11 `MFCreateVirtualCamera` ile sistem geneli sanal kamera (C++ media source) |
| Windows'tan yeni FaceTime başlatma | ❌ | Apple web istemcisi yalnızca katılmaya izin veriyor; görüşmeyi iPhone/Mac başlatır |

## Neden bu mimari?

Apple, iMessage ve FaceTime protokollerini Windows'a açmıyor; tersine mühendislik yapan projeler (Beeper Mini, pypush)
Apple tarafından kısa sürede engellendi. Geriye kalan sağlam yollar:

- **Mesajlar:** iMessage'ı gerçekten konuşan tek cihaz bir Apple cihazı. Sürekli açık bir Mac'teki BlueBubbles Server,
  Mesajlar veritabanını ve gönderimini REST + socket.io ile dışarı açıyor. Mac yoksa tek seçenek Microsoft Phone Link'in
  kullandığı Bluetooth MAP yolu; o da grup, medya ve geçmiş desteklemiyor.
- **FaceTime:** Apple'ın resmi web istemcisi Windows'ta Chrome/Edge ile çalışıyor. Uygulama onu kendi penceresinde açar,
  kamera akışını da kendisi sağlar.
- **Continuity Camera:** Gerçek Continuity Camera yalnızca Mac'te. Burada iPhone, Safari'de açılan bir sayfa üzerinden
  kamerasını yerel ağda WebRTC ile doğrudan Windows'a gönderir (sunucu yok, internet yok, gecikme düşük).

```
 iPhone ──(Kestirme: FaceTime bağlantısı, HTTP :51516)──▶ Süreklilik ──▶ Bildirim ──▶ FaceTime penceresi
 iPhone Safari ──(WebRTC video, sinyal WSS :51515)──────▶ Süreklilik ──▶ FaceTime'ın getUserMedia'sı
 Mac (BlueBubbles) ──(REST + socket.io)─────────────────▶ Süreklilik ──▶ Mesajlar
```

## Kurulum

```powershell
git clone <bu depo>
cd apple-continuity/facetime-messages
npm install
npm start            # geliştirme
npm run dist         # Windows kurulum dosyası (dist/*.exe)
```

### Mesajlar'ı gerçek iMessage'a bağlamak

1. Mac'e BlueBubbles Server'ı kur, Apple ID ile Mesajlar'a giriş yapılı olsun, Mac uykuya geçmesin.
2. Sunucu ayarlarında bir parola belirle; uzaktan erişim için Cloudflare/ngrok bağlantısını ya da yerel IP'yi kullan.
3. Süreklilik → **Ayarlar** → "BlueBubbles Server" → adres ve parola → **Kaydet**.
4. (İsteğe bağlı) BlueBubbles'ın Private API'si açılırsa gönderim daha hızlı olur ve yazıyor göstergesi gelir; kapalıysa uygulama AppleScript yöntemine düşer.

### iPhone kamerası

1. Süreklilik → **Kamera** → QR kodu iPhone kamerasıyla tara.
2. Safari'deki sertifika uyarısında *Ayrıntılar → Web sitesini ziyaret et* (sertifika bu bilgisayarda üretildi).
3. **Kamerayı Başlat**. FaceTime ayarlarında "iPhone kamerasını kullan" açıksa FaceTime penceresi doğrudan bu kamerayı kullanır.

## Güvenlik

- Kamera sayfası, sinyal sunucusu ve devir ucu, ilk açılışta üretilen rastgele bir eşleştirme anahtarı ister.
- Uygulama, kendinden imzalı sertifikaya yalnızca `localhost:51515` için ve yalnızca kendi ürettiği sertifikayla eşleşirse güvenir.
- Devir ucu yalnızca `https://facetime.apple.com/join#…` bağlantılarını kabul eder.
- FaceTime sayfasına enjekte edilen kod sayfanın kendi dünyasında çalışır; Node/IPC erişimi sayfaya açılmaz.

## Test

```bash
npm test                    # köprü: devir ucu, yetkilendirme, sinyal aktarımı
npx -y playwright@1 --version >/dev/null; node test/camera.e2e.js   # Playwright gerekir;: sahte iPhone kamerası → WebRTC → getUserMedia (uçtan uca)
```

## Yol haritası

1. Sistem geneli sanal kamera (`MFCreateVirtualCamera`, Windows 11 22H2+) — iPhone kamerası Zoom, Teams, Discord'da da görünsün.
2. Mesajlar'da ek gönderme, tapback'ler, yanıt dizileri.
3. Mac'siz kullanıcılar için Bluetooth MAP arka ucu (Phone Link düzeyinde, yalnızca birebir metin).
