# Builds the Microsoft Store package (MSIX) from a published EmojiAnywhere.exe.
# The Store signs it after upload, so it is left unsigned here.
#   .\packaging\build-msix.ps1 -Version 0.1.0 -SourceExe out\standalone\EmojiAnywhere.exe -Out dist\EmojiAnywhere.msix
param(
  [Parameter(Mandatory)] [string] $Version,
  [Parameter(Mandatory)] [string] $SourceExe,
  [Parameter(Mandatory)] [string] $Out
)
$ErrorActionPreference = 'Stop'
$parts = $Version.Split('.')
while ($parts.Count -lt 3) { $parts += '0' }
$msixVersion = "$($parts[0]).$($parts[1]).$($parts[2]).0" # the Store needs the last number to be 0

$root = Split-Path $PSScriptRoot -Parent
$stage = Join-Path ([IO.Path]::GetTempPath()) ("ea-msix-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
  Copy-Item $SourceExe (Join-Path $stage 'EmojiAnywhere.exe')
  Copy-Item (Join-Path $PSScriptRoot 'msix\Images') (Join-Path $stage 'Images') -Recurse
  Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $stage
  (Get-Content (Join-Path $PSScriptRoot 'msix\AppxManifest.xml') -Raw -Encoding UTF8).Replace('__VERSION__', $msixVersion) |
    Set-Content (Join-Path $stage 'AppxManifest.xml') -Encoding UTF8

  $makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
    Sort-Object FullName -Descending | Select-Object -First 1
  if (-not $makeappx) { throw 'makeappx.exe not found. Install the Windows 10/11 SDK.' }
  New-Item -ItemType Directory -Path (Split-Path $Out -Parent) -Force | Out-Null
  & $makeappx.FullName pack /o /h SHA256 /d $stage /p $Out
  if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE" }
  Write-Host "Built $Out (version $msixVersion)"
} finally {
  Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
