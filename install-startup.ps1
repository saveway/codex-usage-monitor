$ErrorActionPreference = 'Stop'

$target = Join-Path $PSScriptRoot 'run-codex-usage-tray.bat'
if (-not (Test-Path -LiteralPath $target)) {
    throw "Launcher not found: $target"
}

$startup = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startup 'Codex Usage Tray.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.Description = 'Start Codex Usage Monitor'
$shortcut.Save()

Write-Host "Installed startup shortcut: $shortcutPath"
