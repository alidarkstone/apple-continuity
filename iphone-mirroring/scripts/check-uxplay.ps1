# Checks that a bundled UxPlay folder runs without MSYS2: every GStreamer element its
# pipelines use must load with only the folder itself on PATH.
param([Parameter(Mandatory)] [string] $Dir)
$ErrorActionPreference = 'Stop'
$Dir = (Resolve-Path $Dir).Path

$env:PATH = "$Dir;$env:SystemRoot\System32;$env:SystemRoot"
$env:GST_PLUGIN_PATH = "$Dir\lib\gstreamer-1.0"
$env:GST_PLUGIN_SYSTEM_PATH = "$Dir\lib\gstreamer-1.0"
$env:GST_PLUGIN_SCANNER = "$Dir\libexec\gstreamer-1.0\gst-plugin-scanner.exe"
$env:GST_REGISTRY = "$env:RUNNER_TEMP\gst-registry.bin"

$required = 'appsrc', 'queue', 'h264parse', 'decodebin', 'avdec_h264', 'videoconvert', 'videoscale',
            'videoflip', 'avdec_aac', 'avdec_alac', 'audioconvert', 'audioresample', 'volume', 'level',
            'autoaudiosink', 'wasapisink', 'jpegdec', 'imagefreeze'
$missing = $required | Where-Object { & "$Dir\gst-inspect-1.0.exe" --exists $_; $LASTEXITCODE -ne 0 }
if ($missing) { throw "Missing GStreamer elements: $($missing -join ', ')" }

# d3d11 elements only register when a Direct3D 11 device exists (the build VM may have none).
& "$Dir\gst-inspect-1.0.exe" --exists d3d11videosink
if ($LASTEXITCODE -ne 0) { Write-Warning 'd3d11videosink not available on this machine (no D3D11 device?)' }

& "$Dir\uxplay.exe" -v
if ($LASTEXITCODE -ne 0) { throw "uxplay.exe -v failed ($LASTEXITCODE)" }
Write-Host 'UxPlay bundle OK'
