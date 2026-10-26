param([switch]$Rebuilt)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$manifestPath = Join-Path $root 'manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$dll = if ($Rebuilt) {
    Join-Path $root 'source/bin/MastabaInspect.dll'
}
else {
    Join-Path $root 'plugin/MastabaInspect.dll'
}
$output = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$archivePath = Join-Path $output "Inspect-$($manifest.version_number).zip"
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $dll, 'BepInEx/plugins/MastabaInspect/MastabaInspect.dll') | Out-Null
    foreach ($name in 'README.md','README.en.md','manifest.json') {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $root $name), $name) | Out-Null
    }
}
finally {
    $archive.Dispose()
}
Write-Output $archivePath
