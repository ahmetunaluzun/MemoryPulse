# Builds the Microsoft Store package (MSIX) of MemoryPulse.
#   powershell -File tools/make-msix.ps1             -> artifacts\msix\MemoryPulse_<ver>_neutral.msix (unsigned; the Store signs it)
#   powershell -File tools/make-msix.ps1 -Register   -> also installs the layout on this PC for testing (needs Developer Mode)
# Identity values come from packaging\store-identity.json (Partner Center > Product identity).
# Needs the Windows 10/11 SDK (makeappx.exe, makepri.exe).
param([switch]$Register)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $PSScriptRoot 'icon-draw.ps1')

function Find-SdkTool([string]$name) {
    $bin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $hit = Get-ChildItem $bin -Directory -Filter '10.*' | Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\$name" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $hit) { throw "$name not found under $bin. Install the Windows SDK." }
    return $hit
}

# 1. Version from the csproj (the single source), as X.Y.Z.0: the Store requires revision 0.
$csproj = Join-Path $root 'src\MemoryPulse\MemoryPulse.csproj'
$ver = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($ver -notmatch '^\d+\.\d+\.\d+$') { throw "Unexpected csproj Version '$ver'" }
$pkgVersion = "$ver.0"

# 2. Build.
& dotnet build $csproj -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$bin = Join-Path $root 'src\MemoryPulse\bin\Release\net48'

# 3. Layout.
$out = Join-Path $root 'artifacts\msix'
$layout = Join-Path $out 'layout'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $layout 'Assets') | Out-Null
Copy-Item (Join-Path $bin 'MemoryPulse.exe'), (Join-Path $bin 'MemoryPulse.exe.config') $layout

# 4. Tile images. Scale variants for tiles; target sizes (unplated) so the taskbar and Start show a crisp icon.
function Save-Png([int]$w, [int]$h, [double]$fill, [string]$file) {
    $bmp = New-IconBitmap $w $h $fill
    $bmp.Save((Join-Path $layout "Assets\$file"), [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
foreach ($scale in 100, 125, 150, 200, 400) {
    $k = $scale / 100
    Save-Png (50 * $k) (50 * $k) 1.0 "StoreLogo.scale-$scale.png"
    Save-Png (44 * $k) (44 * $k) 1.0 "Square44x44Logo.scale-$scale.png"
    Save-Png (71 * $k) (71 * $k) 0.6 "SmallTile.scale-$scale.png"
    Save-Png (150 * $k) (150 * $k) 0.6 "Square150x150Logo.scale-$scale.png"
    Save-Png (310 * $k) (150 * $k) 0.6 "Wide310x150Logo.scale-$scale.png"
    Save-Png (310 * $k) (310 * $k) 0.6 "LargeTile.scale-$scale.png"
}
foreach ($t in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256) {
    Save-Png $t $t 1.0 "Square44x44Logo.targetsize-$t.png"
    Save-Png $t $t 1.0 "Square44x44Logo.targetsize-${t}_altform-unplated.png"
    Save-Png $t $t 1.0 "Square44x44Logo.targetsize-${t}_altform-lightunplated.png"
}

# 5. Manifest.
$id = Get-Content (Join-Path $root 'packaging\store-identity.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($id.Name -like '*LocalTest*') { Write-Warning 'packaging\store-identity.json still has local test values; copy them from Partner Center before uploading.' }
$esc = { param($v) [Security.SecurityElement]::Escape($v) }
$manifest = (Get-Content (Join-Path $root 'packaging\Package.appxmanifest') -Raw -Encoding UTF8).
    Replace('{{Name}}', (& $esc $id.Name)).Replace('{{Publisher}}', (& $esc $id.Publisher)).
    Replace('{{PublisherDisplayName}}', (& $esc $id.PublisherDisplayName)).Replace('{{Version}}', $pkgVersion)
if ($manifest -match '\{\{\w+\}\}') { throw 'Unfilled token left in the manifest' }
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

# 6. Resource index (lets Windows pick the right scale/target size).
$makepri = Find-SdkTool 'makepri.exe'
$priConfig = Join-Path $out 'priconfig.xml'
& $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
& $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makepri failed' }
Remove-Item $priConfig

# 7. Pack.
$msix = Join-Path $out "MemoryPulse_${pkgVersion}_neutral.msix"
& (Find-SdkTool 'makeappx.exe') pack /d $layout /p $msix /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }
Write-Host "Written $msix"

# 8. Optional: install the layout on this PC (Developer Mode, no signing needed).
if ($Register) {
    Get-AppxPackage -Name $id.Name | Remove-AppxPackage
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    Write-Host "Registered $($id.Name) $pkgVersion (Start menu: MemoryPulse)"
}
