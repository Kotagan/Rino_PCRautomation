# Start MuMu device N + Taiwan Rino (--instance, dual-open)
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(1, 3)]
    [int]$Index
)

$ErrorActionPreference = "Stop"
$mm = "C:\Program Files\Netease\MuMu\nx_main\MuMuManager.exe"
$adb = "C:\Program Files\Netease\MuMu\nx_main\adb.exe"

# Prefer Release build (supports --instance); fallback to dual pack
$releaseRoot = "D:\workplace\bin\x64\Release"
$guiRoot = "D:\workplace\Rino\Rino-GUI-V1.8.0A\Rino-GUI-V1.8.0A"
$dualRoot = Join-Path $guiRoot "dual"
$releaseExe = Join-Path $releaseRoot "Rino_PCRautomation.exe"
$dualExe = Join-Path $dualRoot "Rino_PCRautomation.exe"

if (Test-Path $releaseExe) {
    $rinoRoot = $releaseRoot
    $rinoExe = $releaseExe
}
elseif (Test-Path $dualExe) {
    $rinoRoot = $dualRoot
    $rinoExe = $dualExe
}
else {
    throw "Rino_PCRautomation.exe not found in Release or dual"
}

$port = if ($Index -eq 1) { 16416 } else { 16480 }
$serial = "127.0.0.1:$port"

Write-Host "MuMu-$Index + Rino --instance $Index @ $serial"
Write-Host "Rino root: $rinoRoot"

# 1) Launch emulator
& $mm control -v $Index launch | Out-Null

# 2) Wait Android
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    $info = & $mm info -v $Index 2>&1 | Out-String
    if ($info -match '"player_state":\s*"start_finished"' -and $info -match '"is_android_started":\s*true') {
        break
    }
}
& $adb connect $serial 2>$null | Out-Null

# 3) Seed/patch config_instN
$seedShared = Join-Path $rinoRoot "config"
if (-not (Test-Path $seedShared)) {
    $seedShared = Join-Path $dualRoot "config"
}
if (-not (Test-Path $seedShared)) {
    $seedShared = Join-Path $guiRoot "config"
}

$rinoRootPy = ($rinoRoot -replace '\\', '/')
$seedSharedPy = ($seedShared -replace '\\', '/')

$py = @"
import json, shutil
from pathlib import Path
idx = $Index
serial = '$serial'
adb = r'C:/Program Files/Netease/MuMu/nx_main/adb.exe'
root = Path(r'$rinoRootPy')
shared = Path(r'$seedSharedPy')
inst = root / ('config_inst' + str(idx))
if shared.is_dir() and (not inst.exists() or not any(inst.iterdir())):
    if inst.exists():
        shutil.rmtree(inst)
    shutil.copytree(shared, inst)
    print('seeded', inst)
inst.mkdir(parents=True, exist_ok=True)
mumu_cfg = json.dumps({'extras': {'mumu': {'enable': True, 'index': idx, 'path': 'C:/Program Files/Netease/MuMu'}}}, separators=(',', ':'))
for p in inst.glob('*.json'):
    name = p.name.lower()
    if 'maa' in name or 'task_presets' in name:
        continue
    try:
        d = json.loads(p.read_text(encoding='utf-8'))
    except Exception as e:
        print('skip', p.name, e)
        continue
    if not isinstance(d, dict):
        continue
    d['AdbSerial'] = serial
    d['AdbPath'] = adb
    d['EmulatorConfig'] = str(idx)
    device = d.get('AdbDevice')
    if not isinstance(device, dict):
        device = {}
        d['AdbDevice'] = device
    device['AdbSerial'] = serial
    device['AdbPath'] = adb
    device['Name'] = 'MuMuPlayer12-' + str(idx)
    device['Config'] = mumu_cfg
    device.pop('InfoHandle', None)
    p.write_text(json.dumps(d, ensure_ascii=False, indent=4), encoding='utf-8')
    print('patched', p.name, '->', serial)
"@

python -u -c $py

# 4) Start Rino with --instance
Start-Process -FilePath $rinoExe -ArgumentList "--instance", "$Index" -WorkingDirectory $rinoRoot
Write-Host "Started Rino instance $Index ($serial)"