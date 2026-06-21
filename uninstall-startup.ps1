$ErrorActionPreference = 'Stop'

$startup = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startup 'Codex Usage Tray.lnk'
if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
    Write-Host "Removed startup shortcut: $shortcutPath"
} else {
    Write-Host 'Startup shortcut is not installed.'
}
