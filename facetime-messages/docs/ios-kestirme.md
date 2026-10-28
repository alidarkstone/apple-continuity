# iOS Kestirmesi: "Windows'ta Devam Et"

Bu kestirme, iPhone'daki FaceTime görüşmesinin bağlantısını Windows'taki Süreklilik uygulamasına gönderir.
Bir kere kurulur, sonra görüşme sırasında paylaşım menüsünden tek dokunuşla çalışır.

## Kurulum (yaklaşık 2 dakika)

1. Windows'ta Süreklilik → **Ayarlar** sayfasındaki *adres* ve *eşleştirme anahtarını* not al.
2. iPhone'da **Kestirmeler** → sağ üst **+** → adını **Windows'ta Devam Et** yap.
3. Kestirme ayrıntılarında (ⓘ) **Paylaşım Sayfasında Göster**'i aç. Girdi türü olarak yalnızca **URL** ve **Metin** seçili kalsın.
4. Eylemleri sırayla ekle:
   1. **Girdiden URL'leri Al** ← girdi: *Kestirme Girdisi*
   2. **URL İçeriğini Al**
      - URL: Ayarlar'daki adres, ör. `http://192.168.1.25:51516/handoff`
      - Yöntem: **POST**, Gövde: **JSON**
      - `url` → (1. adımın sonucu: *URL'ler*)
      - `token` → eşleştirme anahtarı
      - `device` → **Aygıt Ayrıntılarını Al › Aygıt Adı**
   3. **Bildirim Göster**: "Windows'a gönderildi"
5. Kaydet.

## Kullanım

1. iPhone'da FaceTime görüşmesindeyken üstteki kişi kartına dokun → **Bağlantıyı Paylaş**.
2. Paylaşım menüsünden **Windows'ta Devam Et**'i seç.
3. Windows'ta bildirim çıkar ve uygulamada "Windows'ta Devam Et" kartı belirir. Tıkla.
4. FaceTime penceresinde adını yazıp **Katıl**'a bas. iPhone'da gelen katılım isteğini onayla.
5. Windows görüşmeye girince iPhone'da **Ayrıl**'a dokun. Görüşme Windows'ta devam eder.

## Notlar

- iPhone ile bilgisayar aynı Wi‑Fi'da olmalı. Dışarıdan da çalışsın istersen Tailscale gibi bir VPN ile
  bilgisayarın Tailscale adresini kullanabilirsin.
- Windows Güvenlik Duvarı ilk açılışta 51515 ve 51516 portları için izin isteyecek; **Özel ağlar**'a izin ver.
- Apple, devam eden bir FaceTime görüşmesini başka bir cihaza "aktarma" API'si sunmuyor. Bu akış aynı
  görüşmeye Windows'u yeni bir katılımcı olarak ekleyip iPhone'u çıkararak aynı sonucu verir.
