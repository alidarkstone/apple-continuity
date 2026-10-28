# UniClip kurulumu: guvenlik duvari kurali + oturum acilista otomatik baslatma.
# Yonetici PowerShell'de calistirin:  powershell -ExecutionPolicy Bypass -File install.ps1
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$script = Join-Path $here "uniclip_server.py"

$pythonw = (Get-Command pythonw.exe -ErrorAction SilentlyContinue).Source
if (-not $pythonw) { throw "Python bulunamadi. https://www.python.org/downloads/ adresinden kurun ('Add to PATH' secili olsun)." }

# Yalnizca yerel aga (Private profil) 8765 portunu ac
if (-not (Get-NetFirewallRule -DisplayName "UniClip" -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName "UniClip" -Direction Inbound -Protocol TCP -LocalPort 8765 -Profile Private -Action Allow | Out-Null
    Write-Host "Guvenlik duvari kurali eklendi (TCP 8765, Ozel ag)."
}

# Baslangic klasorune kisayol
$startup = [Environment]::GetFolderPath("Startup")
$lnk = Join-Path $startup "UniClip.lnk"
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = $pythonw
$sc.Arguments = "`"$script`""
$sc.WorkingDirectory = $here
$sc.Save()
Write-Host "Oturum acilista otomatik baslayacak: $lnk"

Start-Process $pythonw -ArgumentList "`"$script`"" -WorkingDirectory $here
Start-Sleep -Seconds 2
$cfg = Get-Content (Join-Path $here "uniclip_config.json") | ConvertFrom-Json
$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.PrefixOrigin -in "Dhcp","Manual" -and $_.IPAddress -notlike "169.*" } | Select-Object -First 1).IPAddress
Write-Host ""
Write-Host "UniClip calisiyor. iPhone Kisayolu ve Mac betigi icin:"
Write-Host "  Adres : http://${ip}:8765/clip"
Write-Host "  Token : $($cfg.token)"
