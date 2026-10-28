# Evdeki Mac'i Mesajlar köprüsü yapmak

Amaç: Windows'taki Mesajlar senin Apple ID'nle, Mac'teki gibi çalışsın (gruplar, fotoğraflar, tüm geçmiş).
Mac başka biri tarafından kullanılırken bile arka planda çalışmaya devam eder.

## 1. Sana ait bir kullanıcı

1. Sistem Ayarları → **Kullanıcılar ve Gruplar** → **Kullanıcı Ekle** (ör. "Köprü" ya da kendi adın).
2. Bu kullanıcıyla oturum aç, **Mesajlar**'da Apple ID'nle giriş yap, birkaç mesajın geldiğini gör.
3. Sistem Ayarları → **Kilit Ekranı / Denetim Merkezi** → **Hızlı Kullanıcı Geçişi**'ni menü çubuğunda göster.

## 2. Uyku ve oturum

- Sistem Ayarları → **Enerji** (dizüstüde **Pil → Seçenekler**): "Ekran kapalıyken otomatik uykuyu engelle" açık.
  Dizüstüyse adaptöre takılı kalmalı.
- "Ağ erişimi için uyandır" açık.
- Kızın kendi profiline **menü çubuğundaki kullanıcı simgesinden** geçsin, **Çıkış Yap** demesin.
  Çıkış yapılmadığı sürece senin oturumun ve Mesajlar arka planda çalışmaya devam eder.

## 3. BlueBubbles Server

1. Kendi kullanıcında [BlueBubbles Server](https://bluebubbles.app/downloads/)'ı kur, istenen izinleri ver
   (Tam Disk Erişimi, Erişilebilirlik).
2. Bir sunucu parolası belirle.
3. Bağlantı yöntemi: evde aynı ağdaysan yerel IP yeterli (ör. `http://192.168.1.20:1234`).
   Dışarıdan da çalışsın istersen BlueBubbles'ın Cloudflare bağlantısını aç.
4. BlueBubbles'ı **Oturum açılışında başlat** olarak ayarla.

## 4. Windows

Süreklilik → **Ayarlar** → **BlueBubbles Server** → adres ve parola → **Kaydet**.
Mesajlar sekmesinde sohbetlerin gelmesi gerekir.

## Bilinmesi gerekenler

- Mac yeniden başlarsa senin kullanıcın otomatik açılmaz. Bir kez oturum açıp kızının profiline hızlı geçişle dönmek gerekir.
- Mac kapalıysa ya da senin oturumun kapandıysa Windows'ta yeni mesaj gelmez. Bluetooth arka ucu bu durumda yedek olarak kullanılabilir.
