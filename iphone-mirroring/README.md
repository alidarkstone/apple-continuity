# iPhone Yansıtma (Windows 11)

macOS'taki "iPhone Yansıtma"nın Windows karşılığı: iPhone'un ekranı bir Windows
penceresinde görünür, fareyle tıklayıp sürükler, klavyeyle yazarsınız.

```
iPhone ──AirPlay (Wi-Fi)──▶ UxPlay ──▶ iPhoneYansitma.exe penceresi
   ▲                                          │ tıklama / tuş
   └──Bluetooth fare+klavye── ESP32 ◀──USB────┘
```

- **Görüntü:** iPhone'un kendi "Ekran Yansıtma" özelliği (AirPlay). Windows tarafında
  açık kaynak [UxPlay](https://github.com/FDH2/UxPlay) alıcısı çalışır; görüntü
  ekran kartında çözülür (d3d11), işlemci yükü düşüktür.
- **Kontrol:** iOS, Bluetooth fare ve klavyeyi AssistiveTouch üzerinden destekler.
  Windows bir PC'yi Bluetooth fareye dönüştüremediği için bu işi ~150–250 TL'lik bir
  **ESP32 kartı** yapar: USB'den bilgisayara takılır, iPhone'a "iPhone Kontrol"
  adlı bir fare+klavye olarak bağlanır.
- ESP32 olmadan da çalışır; o zaman yalnızca izleme olur.

## Mac'teki iPhone Yansıtma ile farklar (dürüst liste)

| | Mac | Bu çözüm |
| --- | --- | --- |
| iPhone kilitliyken kullanma | ✅ | ❌ iPhone açık ve kilitsiz olmalı; ekranı da yanar |
| Yansıtmayı bilgisayardan başlatma | ✅ | ❌ Her seferinde iPhone'da Denetim Merkezi → Ekran Yansıtma |
| Dokunma hissi | Doğrudan | iPhone'da gri bir imleç gezer; dokunma ~0,3–1 sn gecikmeli |
| Ek donanım | Yok | ESP32 kartı (kontrol için) |
| Pil | Az etkiler | Ekran açık kaldığı için belirgin tüketir; şarjda kullanın |
| Kaynak (PC) | – | UxPlay ~100–150 MB RAM, birkaç % CPU (macOS sanal makinesi 4–8 GB RAM ister) |

Neden imleç ve gecikme: iOS dışarıdan "şu noktaya dokun" komutunu kabul etmez,
sadece fare hareketi kabul eder. Uygulama imleci önce köşeye dayar, sonra hedefe
sabit adımlarla götürür ve tıklar. Kayma olursa araç çubuğundaki **hizala**
düğmesi ya da bir kez **kalibre et** düzeltir.

## Gerekenler

- Windows 10/11 (64 bit), iPhone ile **aynı Wi-Fi ağı**
- Kontrol için bir ESP32 kartı (biri yeterli):
  - **ESP32 DevKit / ESP32-WROOM-32** (en yaygını) → `iphone-kontrol-esp32.bin`
  - **ESP32-S3 DevKitC** → `iphone-kontrol-esp32s3.bin`
  - **ESP32-C3 Super Mini** (en küçüğü, USB-C) → `iphone-kontrol-esp32c3.bin`
  - Veri taşıyan bir USB kablosu (sadece şarj kablosu olmaz)

## Kurulum

1. **Paketi indirin.** GitHub'da depo → Actions → *iphone-mirroring* → son başarılı
   çalışma → `iPhoneYansitma-win-x64`. Zip'i bir klasöre açın (ör. `C:\iPhoneYansitma`).
2. **ESP32'ye yazılımı yükleyin (bir kerelik).** Kartı takın, Chrome veya Edge ile
   <https://espressif.github.io/esptool-js/> sayfasını açın → *Connect* → kartın COM
   portunu seçin → adres **0x0**, dosya `kontrolcu\iphone-kontrol-<kart>.bin` →
   *Program*. Bitince kartı çıkarıp takın.
3. **iPhone ayarları (bir kerelik):**
   - Ayarlar → Bluetooth → **iPhone Kontrol (Sureklilik)** → bağlan.
   - Ayarlar → Erişilebilirlik → Dokunma → **AssistiveTouch: açık** (imleç bununla çıkar).
     Aynı yerde *İzleme Hızı* orta civarında kalsın; değiştirirseniz yeniden kalibre edin.
   - Ayarlar → Genel → Klavye → Donanım Klavyesi → **Türkçe Q** (PC klavyenizle aynı).
4. **`iPhoneYansitma.exe`'yi çalıştırın.** Windows Güvenlik Duvarı sorarsa
   *Özel ağlar* için izin verin (yoksa iPhone bilgisayarı göremez).
5. iPhone'da **Denetim Merkezi → Ekran Yansıtma → Windows**.
6. İlk seferde araç çubuğunda **kalibre et**'e basın, iPhone'daki gri imleç
   durunca tam üstüne tıklayın. Ayar kaydedilir.

## Kullanım

| Bilgisayarda | iPhone'da |
| --- | --- |
| Tıklama | Dokunma |
| Basılı tutup sürükleme | Kaydırma / sürükleme |
| Fare tekerleği | Kaydırma |
| Klavye | Yazma (Ctrl = ⌘, Windows tuşu = Control) |
| Ctrl+Boşluk | Spotlight araması |
| Ctrl+C / Ctrl+V | Kopyala / yapıştır |
| Araç çubuğu | Ana ekran, uygulama değiştirici, arama, ses −/+, döndür, hizala, kalibre et |

Pencere iPhone'un oranına göre boyutlanır. iPhone'u yan çevirdiğinizde görüntü
yatay gelirse **döndür** düğmesine basın.

## Ayarlar

`%APPDATA%\iPhoneYansitma\settings.json`:

- `ReceiverName`: Ekran Yansıtma listesindeki ad (varsayılan `Windows`)
- `ComPort`: ESP32'nin portu (boşsa kendisi bulur)
- `CtrlAsCommand`: `false` yaparsanız Ctrl, iPhone'da da Control olur
- `Pointer.Step`, `Pointer.HomeEvery`: imleç adımı ve kaç dokunmada bir köşeye hizalanacağı

## Bilinen belirsizlikler (henüz gerçek cihazda denenmedi)

- **Ana ekran** düğmesi HID "AC Home" tuşunu yollar; **uygulama değiştirici** bunu iki
  kez yollar. iOS sürümüne göre çalışmazsa AssistiveTouch menüsünden yapılabilir.
- iPhone döndüğünde pencere oranı kendiliğinden değişmez (döndür düğmesi var).
- UxPlay'in Windows sürümü (1.74) yeni mDNS kodunu kullanıyor; iPhone listede
  bilgisayarı görmezse güvenlik duvarında UDP 5353 ve UxPlay'e izin verildiğini kontrol edin.

## Geliştirme

```
firmware/                 ESP32 yazılımı (PlatformIO, NimBLE): BLE HID fare+klavye+medya tuşları
src/IPhoneMirror.Core/    Platformdan bağımsız mantık: imleç planlama, görüntü eşleme, tuş çevirme
src/IPhoneMirror/         WPF uygulaması: UxPlay penceresini gömer, girdileri ESP32'ye yollar
tests/                    Core birim testleri
scripts/build-uxplay.sh   UxPlay'i MSYS2'de derleyip gereken GStreamer parçalarıyla paketler
```

ESP32 seri protokolü (115200 baud, satır başına bir komut) `firmware/src/main.cpp`
başında anlatılır; komutları elle denemek için Arduino seri monitörü yeterlidir
(`?`, `c 547` = ana ekran, `m 50 0`, `b 1` / `b 0`).

UxPlay GPL-3.0 lisanslıdır; pakette lisansı ve kaynak bağlantısı (`uxplay/UxPlay-*.txt`) bulunur.
