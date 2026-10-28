# Mesajlar için macOS sanal makinesi

Bu bilgisayarda arka planda çalışan küçük bir macOS sanal makinesi, Apple ID'nle Mac gibi iMessage alır ve gönderir.
Süreklilik onu pencere açmadan başlatır, içindeki BlueBubbles'a bağlanır, kapanırken duraklatır.

> **Bilmen gerekenler.** Apple'ın lisansı macOS'u yalnızca Apple donanımında çalıştırmaya izin verir; bu kurulum
> o şartlara aykırıdır. iMessage'ın sanal makinede açılması için makineye geçerli bir Mac seri numarası tanımlanması
> gerekir; Apple bazen etkinleştirmeyi reddeder ya da hesabın iMessage'ını geçici kilitler (Apple desteği açar).
> Başka bir Mac'in seri numarasını kopyalama. İlk denemeyi istersen yedek bir Apple ID ile yap.

## Gerekenler

| | Önerilen |
|---|---|
| İşlemci | Intel (VT-x) ya da AMD (SVM); BIOS'ta sanallaştırma açık. AMD için aşağıdaki bölüme bak |
| Bilgisayarın RAM'i | 16 GB (VM 4 GB alır) |
| Disk | 60 GB boş alan, SSD |
| Sanallaştırma | VMware Workstation Pro (kişisel kullanım için ücretsiz) |

## AMD işlemciler (ör. Ryzen 9 9950X3D)

macOS yalnızca Intel işlemcileri tanır. Süreklilik'teki **Hafif profil** düğmesi AMD işlemciyi kendiliğinden algılar
ve `.vmx` dosyasına işlemciyi sanal makineye Intel olarak tanıtan CPUID maskelerini ekler (`cpuid.0.*`, `cpuid.1.*`,
`smc.version = "0"`). Ek olarak:

- **macOS sürümü:** AMD'de Ventura (13) en sorunsuz sürüm. Sonoma ve sonrası bazı AMD kurulumlarında açılmıyor.
- **Hyper-V / Bellek bütünlüğü:** Windows 11'de "Çekirdek yalıtımı → Bellek bütünlüğü", Hyper-V, "Sanal Makine
  Platformu" ya da "Windows Hiper Yönetici Platformu" açıksa VMware Hyper-V'nin üstünde çalışır ve macOS bu modda
  açılmaz ya da çok yavaş çalışır. Bunları kapatmak gerekir. Bedeli: Bellek bütünlüğü bir güvenlik katmanıdır;
  WSL2 ve Docker Desktop da Sanal Makine Platformu'na ihtiyaç duyar ve çalışmaz hale gelir.
- **Çekirdekler:** VM yalnızca 2 çekirdek kullanır; oyun ve iş yükleri etkilenmez.

## Kurulum

1. **VMware Workstation Pro**'yu kur. VMware, Apple dışı donanımda macOS'u varsayılan olarak listelemez;
   topluluğun "unlocker" araçları bu seçeneği açar.
2. **macOS'u Apple'dan indir** ve yeni bir sanal makineye kur: 2 çekirdek, 4 GB RAM, 60 GB ince (thin) disk.
   Ventura ya da Sonoma yeterli; en yeni sürüm şart değil, daha eski sürüm daha hafif çalışır.
3. **VMware Tools**'u kur (ekran ve saat senkronu için).
4. **Apple ID ile giriş yap**, Mesajlar'ı aç ve bir mesajın geldiğini gör.
5. **BlueBubbles Server**'ı kur ([indir](https://bluebubbles.app/downloads/)): Tam Disk Erişimi ve Erişilebilirlik
   izinlerini ver, bir parola belirle, port 1234 kalsın, "Oturum açılışında başlat"ı aç.
6. **Hafifletme betiğini** VM içinde çalıştır: `scripts/macos-hafif.sh` dosyasını VM'e kopyala, Terminal'de
   `bash macos-hafif.sh`. Spotlight, Time Machine, otomatik güncelleme, uyku, saydamlık/hareket ve App Nap kapanır.
7. **Otomatik oturum açmayı** aç (Sistem Ayarları → Kullanıcılar ve Gruplar), VM'i kapat.
8. **Süreklilik → Ayarlar → macOS sanal makinesi**: `.vmx` dosyasını seç → **Hafif profil** (2 çekirdek, 4 GB,
   3D/ses/yazıcı kapalı, bellek diske yazılmaz, AMD'de CPUID maskeleri; eski hali `.vmx.bak`) → BlueBubbles parolasını yaz → **Kaydet**.

Bundan sonra Süreklilik her açılışta VM'i arka planda başlatır (duraklatılmışsa birkaç saniyede devam eder),
BlueBubbles hazır olunca Mesajlar'ı bağlar. Kapatınca VM duraklatılır ve hiç kaynak kullanmaz.

## Ne kadar kaynak kullanır?

- **Uygulama kapalıyken:** hiç (VM duraklatılmış, yalnızca diskte durur).
- **Açıkken, boşta:** 4 GB RAM; hafifletmeden sonra işlemci genelde %1–3 civarında.
- **İlk açılış:** soğuk başlatma 1–2 dakika; duraklatılmıştan devam 5–10 saniye.

## Geri alma (VM içinde)

```bash
sudo mdutil -a -i on
sudo tmutil enable
sudo softwareupdate --schedule on
sudo defaults write /Library/Preferences/com.apple.SoftwareUpdate AutomaticDownload -bool true
defaults write com.apple.universalaccess reduceTransparency -bool false
defaults write com.apple.universalaccess reduceMotion -bool false
defaults delete NSGlobalDomain NSAppSleepDisabled
```

`.vmx` ayarlarını geri almak için VM kapalıyken `.vmx.bak` dosyasını `.vmx` adıyla geri koy.
