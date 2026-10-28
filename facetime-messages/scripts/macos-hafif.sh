#!/bin/bash
# macOS sanal makinesini Mesajlar köprüsü için hafifletir.
# Sanal makinenin İÇİNDE, Terminal'de çalıştır:  bash macos-hafif.sh
# Her adım geri alınabilir; geri alma komutları docs/macos-sanal-makine.md'de.
set -u
echo "Yönetici parolası istenecek."
sudo -v || exit 1

step() { printf '• %s\n' "$1"; shift; "$@" >/dev/null 2>&1 || printf '  (atlandı)\n'; }

# Disk ve CPU'yu en çok yoran arka plan işleri
step "Spotlight dizinlemesi kapatılıyor"          sudo mdutil -a -i off
step "Time Machine kapatılıyor"                    sudo tmutil disable
step "Otomatik güncelleme indirmesi kapatılıyor"   sudo defaults write /Library/Preferences/com.apple.SoftwareUpdate AutomaticDownload -bool false
step "Otomatik güncelleme denetimi kapatılıyor"    sudo softwareupdate --schedule off

# Uyku: VM uyursa mesaj gelmez; ekran kapansın, sistem uyumasın
step "Sistem uykusu kapatılıyor"                   sudo pmset -a sleep 0 disksleep 0 powernap 0 standby 0 hibernatemode 0
step "Ekran 1 dakikada kapanacak"                  sudo pmset -a displaysleep 1
step "Ekran koruyucu kapatılıyor"                  defaults -currentHost write com.apple.screensaver idleTime 0

# Grafik yükü (VM'de 3D hızlandırma yok)
step "Saydamlık azaltılıyor"                       defaults write com.apple.universalaccess reduceTransparency -bool true
step "Hareket azaltılıyor"                         defaults write com.apple.universalaccess reduceMotion -bool true

# Mesajlar ve BlueBubbles arka planda yavaşlatılmasın (App Nap)
step "App Nap kapatılıyor"                         defaults write NSGlobalDomain NSAppSleepDisabled -bool YES

# Gereksiz asistanlar
step "Siri kapatılıyor"                            defaults write com.apple.assistant.support "Assistant Enabled" -bool false

echo
echo "Bitti. Son iki adımı elle yap:"
echo "  1) Sistem Ayarları → Kullanıcılar ve Gruplar → Otomatik oturum açma: bu kullanıcı"
echo "  2) BlueBubbles Server → Ayarlar → 'Oturum açılışında başlat' açık"
echo "Sonra sanal makineyi yeniden başlat."
