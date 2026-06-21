Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class NativeIconMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
'@

$ErrorActionPreference = 'Stop'

$AppDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RuntimeDir = Join-Path $env:LOCALAPPDATA 'CodexUsageMonitor'
if (-not (Test-Path -LiteralPath $RuntimeDir)) {
    New-Item -ItemType Directory -Path $RuntimeDir -Force | Out-Null
}
$DataPath = Join-Path $RuntimeDir 'codex-usage.json'
$LogPath = Join-Path $RuntimeDir 'codex-usage-tray.log'
$ScraperLogPath = Join-Path $RuntimeDir 'codex-usage-scraper.log'
$ScraperPidPath = Join-Path $RuntimeDir 'codex-usage-scraper.pid'
$ScraperLockPath = Join-Path $RuntimeDir 'codex-usage-scraper.lock'
$ScraperScriptPath = Join-Path $AppDir 'codex-usage-scraper.py'
$ScraperExePath = Join-Path $AppDir 'codex-usage-scraper.exe'
$ScraperIsExecutable = Test-Path -LiteralPath $ScraperExePath
if ($ScraperIsExecutable) {
    $ScraperPath = $ScraperExePath
    $ScraperHostPath = $ScraperExePath
    $ScraperBaseArguments = @()
} else {
    $ScraperPath = $ScraperScriptPath
    $ScraperHostPath = 'pythonw.exe'
    $ScraperBaseArguments = @($ScraperScriptPath)
}
$AckPath = Join-Path $RuntimeDir 'codex-usage-ack.json'
$SettingsPath = Join-Path $RuntimeDir 'codex-usage-settings.json'
$SettingsUrl = 'https://chatgpt.com/codex/cloud/settings/analytics#usage'
$script:WidgetForm = $null
$script:WidgetPanel = $null
$script:WidgetVisible = $false
$script:WidgetDrag = $false
$script:WidgetDragStart = $null
$script:WidgetTransparentColor = [System.Drawing.Color]::FromArgb(232, 250, 240)
$script:WidgetSize = 128
$script:GraphStyle = 'Rings'
$script:RefreshIntervalSeconds = 600
$script:SharedMenu = $null
$script:IntervalMenuItems = @{}
$script:IsExiting = $false
$script:NotifyIconDisposed = $false
$script:NotifyIcon = $null
$script:RefreshTimer = $null
$script:BlinkTimer = $null
$script:ManualFetchTimer = $null
$script:ManualFetchProcess = $null
$MaxLogBytes = 2MB
$script:DefaultColors = [ordered]@{
    FiveCritical = '#B91C1C'
    FiveDanger = '#DC2626'
    FiveLow = '#EA580C'
    FiveCaution = '#EAB308'
    FiveGood = '#84CC16'
    FiveNormal = '#20BF60'
    WeekCritical = '#701A75'
    WeekDanger = '#BE185D'
    WeekLow = '#DB2777'
    WeekCaution = '#A855F7'
    WeekGood = '#6366F1'
    WeekNormal = '#3A88FF'
    AlertFlash = '#FFFFFF'
    Track = '#CDE8DA'
    Text = '#1C533B'
    MutedText = '#417058'
    Close = '#377D58'
    CodexMark = '#2A8C5B'
    CenterFill = '#FFFFFF'
    TransparentEdge = '#E8FAF0'
    BatteryOutline = '#5B8D70'
}
$script:Colors = [ordered]@{}

function Rotate-LogFile {
    param([string]$Path, [long]$MaxBytes)
    try {
        if ((Test-Path -LiteralPath $Path) -and ((Get-Item -LiteralPath $Path).Length -gt $MaxBytes)) {
            $backupPath = "$Path.1"
            $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            try {
                [void]$stream.Seek(-$MaxBytes, [System.IO.SeekOrigin]::End)
                $buffer = New-Object byte[] $MaxBytes
                $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
            } finally {
                $stream.Dispose()
            }
            if ($bytesRead -gt 0) {
                $tail = New-Object byte[] $bytesRead
                [Array]::Copy($buffer, $tail, $bytesRead)
                [System.IO.File]::WriteAllBytes($backupPath, $tail)
            }
            Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
        }
    } catch {
    }
}

function Write-Log {
    param([string]$Message)
    try {
        Rotate-LogFile $LogPath $MaxLogBytes
        $line = '{0} {1}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'), $Message
        Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8 -ErrorAction SilentlyContinue
    } catch {
    }
}

function Get-AppSettings {
    if (-not (Test-Path -LiteralPath $SettingsPath)) {
        return [pscustomobject]@{
            widgetSize = 128
            graphStyle = 'Rings'
            refreshIntervalSeconds = 600
            colors = $null
        }
    }
    try {
        $raw = Get-Content -LiteralPath $SettingsPath -Raw -Encoding UTF8
        $settings = $raw | ConvertFrom-Json
        if (-not $settings.widgetSize) { $settings | Add-Member -NotePropertyName widgetSize -NotePropertyValue 128 }
        if (-not $settings.graphStyle) { $settings | Add-Member -NotePropertyName graphStyle -NotePropertyValue 'Rings' }
        if (-not $settings.refreshIntervalSeconds) { $settings | Add-Member -NotePropertyName refreshIntervalSeconds -NotePropertyValue 600 }
        if (-not $settings.PSObject.Properties['colors']) { $settings | Add-Member -NotePropertyName colors -NotePropertyValue $null }
        return $settings
    } catch {
        return [pscustomobject]@{
            widgetSize = 128
            graphStyle = 'Rings'
            refreshIntervalSeconds = 600
            colors = $null
        }
    }
}

function Save-AppSettings {
    [pscustomobject]@{
        widgetSize = $script:WidgetSize
        graphStyle = $script:GraphStyle
        refreshIntervalSeconds = $script:RefreshIntervalSeconds
        colors = [pscustomobject]$script:Colors
    } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $SettingsPath -Encoding UTF8
}

function Apply-AppSettings {
    $settings = Get-AppSettings
    $script:WidgetSize = [int]$settings.widgetSize
    if ($script:WidgetSize -ne 256) {
        $script:WidgetSize = 128
    }
    $allowed = @('Rings', 'Bars', 'Meters', 'Battery')
    if ($allowed -contains [string]$settings.graphStyle) {
        $script:GraphStyle = [string]$settings.graphStyle
    } else {
        $script:GraphStyle = 'Rings'
    }
    $allowedIntervals = @(600, 900, 1800, 3600)
    $interval = [int]$settings.refreshIntervalSeconds
    if ($allowedIntervals -contains $interval) {
        $script:RefreshIntervalSeconds = $interval
    } else {
        $script:RefreshIntervalSeconds = 600
    }
    $script:Colors = [ordered]@{}
    foreach ($key in $script:DefaultColors.Keys) {
        $value = $script:DefaultColors[$key]
        if ($settings.colors -and $settings.colors.PSObject.Properties[$key]) {
            $candidate = [string]$settings.colors.$key
            if ($candidate -match '^#[0-9A-Fa-f]{6}$') {
                $value = $candidate.ToUpperInvariant()
            }
        }
        $script:Colors[$key] = $value
    }
    $script:WidgetTransparentColor = Convert-HexToColor $script:Colors.TransparentEdge
    Save-AppSettings
}

function Convert-HexToColor {
    param([string]$Hex)
    return [System.Drawing.ColorTranslator]::FromHtml($Hex)
}

function Convert-ColorToHex {
    param([System.Drawing.Color]$Color)
    return '#{0:X2}{1:X2}{2:X2}' -f $Color.R, $Color.G, $Color.B
}

function Get-ThemeColor {
    param([string]$Name)
    if (-not $script:Colors.Contains($Name)) {
        return [System.Drawing.Color]::Black
    }
    return Convert-HexToColor $script:Colors[$Name]
}

function Select-ThemeColor {
    param([string]$Name)

    $dialog = New-Object System.Windows.Forms.ColorDialog
    $dialog.FullOpen = $true
    $dialog.Color = Get-ThemeColor $Name
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $script:Colors[$Name] = Convert-ColorToHex $dialog.Color
        if ($Name -eq 'TransparentEdge') {
            $script:WidgetTransparentColor = $dialog.Color
            if ($script:WidgetForm) {
                $script:WidgetForm.BackColor = $dialog.Color
                $script:WidgetForm.TransparencyKey = $dialog.Color
                $script:WidgetPanel.BackColor = $dialog.Color
            }
        }
        Save-AppSettings
        Update-Tray
    }
    $dialog.Dispose()
}

function Reset-ThemeColors {
    $script:Colors = [ordered]@{}
    foreach ($key in $script:DefaultColors.Keys) {
        $script:Colors[$key] = $script:DefaultColors[$key]
    }
    $script:WidgetTransparentColor = Get-ThemeColor 'TransparentEdge'
    if ($script:WidgetForm) {
        $script:WidgetForm.BackColor = $script:WidgetTransparentColor
        $script:WidgetForm.TransparencyKey = $script:WidgetTransparentColor
        $script:WidgetPanel.BackColor = $script:WidgetTransparentColor
    }
    Save-AppSettings
    Update-Tray
}

function Test-IsRelatedProcess {
    param($Process)

    if (-not $Process -or ([int]$Process.ProcessId -eq $PID)) {
        return $false
    }
    $name = [string]$Process.Name
    $commandLine = [string]$Process.CommandLine
    $executablePath = [string]$Process.ExecutablePath
    $allowedHost = $name -match '^(pythonw?|powershell|pwsh|codex-usage-scraper)(\.exe)?$'
    if (-not $allowedHost) {
        return $false
    }

    $expectedScraperExe = [System.IO.Path]::GetFullPath($ScraperExePath)
    if ($executablePath -and [string]::Equals($executablePath, $expectedScraperExe, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    if ($name -match '^codex-usage-scraper(?:\.exe)?$') {
        return $true
    }
    if ($name -match '^pythonw?(?:\.exe)?$' -and $commandLine -match 'codex-usage-scraper\.py') {
        return $true
    }
    if ($name -match '^(powershell|pwsh)(?:\.exe)?$' -and $commandLine -match '(?i)(?:^|\s)-File\s+"?[^"\r\n]*codex-usage-tray\.ps1(?:"|\s|$)') {
        return $true
    }

    return $false
}

function Test-IsScraperProcess {
    param($Process)

    if (-not $Process -or ([int]$Process.ProcessId -eq $PID)) {
        return $false
    }
    $name = [string]$Process.Name
    $commandLine = [string]$Process.CommandLine
    $executablePath = [string]$Process.ExecutablePath
    $expectedScraperExe = [System.IO.Path]::GetFullPath($ScraperExePath)
    if ($executablePath -and [string]::Equals($executablePath, $expectedScraperExe, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    if ($name -match '^codex-usage-scraper(?:\.exe)?$') {
        return $true
    }
    return ($name -match '^pythonw?(?:\.exe)?$' -and $commandLine -match 'codex-usage-scraper\.py')
}

function Stop-UsageScraper {
    param([switch]$CleanupRelated)

    $stoppedProcessIds = New-Object System.Collections.Generic.List[int]
    try {
        if (Test-Path -LiteralPath $ScraperPidPath) {
            $scraperProcessId = 0
            if ([int]::TryParse((Get-Content -LiteralPath $ScraperPidPath -Raw).Trim(), [ref]$scraperProcessId)) {
                $process = Get-CimInstance Win32_Process -Filter "ProcessId = $scraperProcessId" -ErrorAction SilentlyContinue
                if (Test-IsScraperProcess $process) {
                    Stop-Process -Id $scraperProcessId -Force -ErrorAction Stop
                    $stoppedProcessIds.Add($scraperProcessId)
                    Write-Log "Stopped scraper PID $scraperProcessId."
                }
            }
        }

        if ($CleanupRelated) {
            $relatedProcesses = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
                Test-IsRelatedProcess $_
            }
            foreach ($process in $relatedProcesses) {
                $processId = [int]$process.ProcessId
                if (-not $stoppedProcessIds.Contains($processId)) {
                    try {
                        Stop-Process -Id $processId -Force -ErrorAction Stop
                        $stoppedProcessIds.Add($processId)
                        Write-Log "Stopped related process PID $processId ($($process.Name))."
                    } catch {
                        Write-Log "Could not stop related PID $processId`: $($_.Exception.Message)"
                    }
                }
            }
        }

        foreach ($processId in $stoppedProcessIds) {
            try {
                Wait-Process -Id $processId -Timeout 5 -ErrorAction SilentlyContinue
            } catch {
            }
        }
    } catch {
        Write-Log ("Could not stop existing scraper: " + $_.Exception.Message)
    }

    try {
        $remaining = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
            Test-IsRelatedProcess $_
        })
        if (Test-Path -LiteralPath $ScraperPidPath) {
            $remainingPid = 0
            if ([int]::TryParse((Get-Content -LiteralPath $ScraperPidPath -Raw).Trim(), [ref]$remainingPid)) {
                $pidProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $remainingPid" -ErrorAction SilentlyContinue
                if ($pidProcess -and -not ($remaining.ProcessId -contains $remainingPid)) {
                    $remaining += $pidProcess
                }
            }
        }
        if ($remaining.Count -eq 0) {
            Remove-Item -LiteralPath $ScraperPidPath -Force -ErrorAction SilentlyContinue
            try {
                Remove-Item -LiteralPath $ScraperLockPath -Force -ErrorAction Stop
            } catch {
                if (Test-Path -LiteralPath $ScraperLockPath) {
                    Write-Log ("Could not remove scraper lock after process exit: " + $_.Exception.Message)
                }
            }
        } else {
            Write-Log "Skipped PID/lock cleanup because $($remaining.Count) related process(es) are still running."
        }
    } catch {
        Write-Log ("Could not finalize scraper PID/lock cleanup: " + $_.Exception.Message)
    }
}

function Invoke-AppCleanup {
    param([switch]$RequestApplicationExit)

    if ($script:IsExiting) {
        return
    }
    $script:IsExiting = $true
    Write-Log 'Application cleanup started.'

    foreach ($timerName in @('RefreshTimer', 'BlinkTimer', 'ManualFetchTimer')) {
        try {
            $timer = Get-Variable -Name $timerName -Scope Script -ValueOnly -ErrorAction SilentlyContinue
            if ($timer) {
                $timer.Stop()
                $timer.Dispose()
                Set-Variable -Name $timerName -Scope Script -Value $null
            }
        } catch {
            Write-Log "Could not dispose $timerName`: $($_.Exception.Message)"
        }
    }

    try {
        Stop-UsageScraper -CleanupRelated
    } catch {
        Write-Log ("Scraper cleanup failed: " + $_.Exception.Message)
    }

    try {
        if ($script:WidgetForm -and -not $script:WidgetForm.IsDisposed) {
            $script:WidgetForm.Close()
        }
    } catch {
        Write-Log ("Could not close widget: " + $_.Exception.Message)
    }

    try {
        if ($null -ne $script:NotifyIcon -and -not $script:NotifyIconDisposed) {
            try {
                $script:NotifyIcon.Visible = $false
            } catch {
                Write-Log ("Could not hide tray icon during cleanup: " + $_.Exception.Message)
            }
            try {
                if ($script:NotifyIcon.Icon) {
                    $script:NotifyIcon.Icon.Dispose()
                    $script:NotifyIcon.Icon = $null
                }
            } catch {
                Write-Log ("Could not dispose tray image: " + $_.Exception.Message)
            }
            try {
                $script:NotifyIcon.Dispose()
            } catch {
                Write-Log ("Could not dispose tray icon: " + $_.Exception.Message)
            }
            $script:NotifyIconDisposed = $true
            $script:NotifyIcon = $null
        }
    } catch {
        Write-Log ("Tray icon cleanup failed: " + $_.Exception.Message)
    }

    Write-Log 'Application cleanup finished.'
    if ($RequestApplicationExit) {
        try {
            [System.Windows.Forms.Application]::Exit()
        } catch {
            Write-Log ("Application exit request failed: " + $_.Exception.Message)
        }
    }
}

function Restart-UsageScraper {
    Stop-UsageScraper
    try {
        $process = Start-Process -FilePath $ScraperHostPath -ArgumentList $ScraperBaseArguments -WindowStyle Hidden -PassThru
        Set-Content -LiteralPath $ScraperPidPath -Value $process.Id -Encoding ASCII
        Write-Log "Scraper restarted with interval $script:RefreshIntervalSeconds seconds."
    } catch {
        Write-Log ("Could not restart scraper: " + $_.Exception.Message)
    }
}

function Start-VisibleFetch {
    Stop-UsageScraper
    try {
        $fetchArguments = @($ScraperBaseArguments) + @('--once', '--login')
        $script:ManualFetchProcess = Start-Process -FilePath $ScraperHostPath -ArgumentList $fetchArguments -WindowStyle Hidden -PassThru
        if ($script:ManualFetchTimer) {
            $script:ManualFetchTimer.Stop()
            $script:ManualFetchTimer.Dispose()
        }
        $script:ManualFetchTimer = New-Object System.Windows.Forms.Timer
        $script:ManualFetchTimer.Interval = 2000
        $script:ManualFetchTimer.Add_Tick({
            if ($script:ManualFetchProcess -and $script:ManualFetchProcess.HasExited) {
                $exitCode = $script:ManualFetchProcess.ExitCode
                $script:ManualFetchTimer.Stop()
                $script:ManualFetchTimer.Dispose()
                $script:ManualFetchTimer = $null
                $script:ManualFetchProcess = $null
                Update-Tray
                if ($exitCode -eq 0) {
                    $script:NotifyIcon.BalloonTipTitle = 'Codex usage updated'
                    $script:NotifyIcon.BalloonTipText = Format-UsageSummary $script:UsageData
                    $script:NotifyIcon.ShowBalloonTip(4000)
                    Write-Log 'Visible fetch completed successfully.'
                } else {
                    $failureMessage = "Fetch failed (exit code $exitCode). Check the log:`n$ScraperLogPath"
                    Write-Log $failureMessage
                    $script:NotifyIcon.BalloonTipTitle = 'Codex usage fetch failed'
                    $script:NotifyIcon.BalloonTipText = $failureMessage
                    $script:NotifyIcon.ShowBalloonTip(8000)
                    if ($script:WidgetVisible) {
                        [System.Windows.Forms.MessageBox]::Show(
                            $failureMessage,
                            'Codex Usage Monitor',
                            [System.Windows.Forms.MessageBoxButtons]::OK,
                            [System.Windows.Forms.MessageBoxIcon]::Warning
                        ) | Out-Null
                    }
                }
                Restart-UsageScraper
            }
        })
        $script:ManualFetchTimer.Start()
        return $true
    } catch {
        $failureMessage = "Could not start visible fetch: $($_.Exception.Message)`nLog: $ScraperLogPath"
        Write-Log $failureMessage
        $script:NotifyIcon.BalloonTipTitle = 'Codex usage fetch failed'
        $script:NotifyIcon.BalloonTipText = $failureMessage
        $script:NotifyIcon.ShowBalloonTip(8000)
        if ($script:WidgetVisible) {
            [System.Windows.Forms.MessageBox]::Show(
                $failureMessage,
                'Codex Usage Monitor',
                [System.Windows.Forms.MessageBoxButtons]::OK,
                [System.Windows.Forms.MessageBoxIcon]::Warning
            ) | Out-Null
        }
        Restart-UsageScraper
        return $false
    }
}

function Set-RefreshInterval {
    param([int]$Seconds)

    $script:RefreshIntervalSeconds = $Seconds
    Save-AppSettings
    foreach ($key in $script:IntervalMenuItems.Keys) {
        $script:IntervalMenuItems[$key].Checked = ([int]$key -eq $Seconds)
    }
    Restart-UsageScraper
}

function Draw-CodexMark {
    param(
        [System.Drawing.Graphics]$Graphics,
        [float]$CenterX,
        [float]$CenterY,
        [float]$FontSize
    )

    $font = New-Object System.Drawing.Font 'Consolas', $FontSize, ([System.Drawing.FontStyle]::Bold)
    $brush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'CodexMark')
    $text = 'C>'
    $size = $Graphics.MeasureString($text, $font)
    $Graphics.DrawString($text, $font, $brush, ($CenterX - $size.Width / 2), ($CenterY - $size.Height / 2))
    $font.Dispose()
    $brush.Dispose()
}

function Get-UsageColor {
    param(
        [int]$Remaining,
        [bool]$BlinkOn = $false
    )

    if ($Remaining -le 0 -and $BlinkOn) {
        return Get-ThemeColor 'AlertFlash'
    }
    if ($Remaining -le 5) {
        return Get-ThemeColor 'FiveCritical'
    }
    if ($Remaining -le 15) {
        return Get-ThemeColor 'FiveDanger'
    }
    if ($Remaining -le 25) {
        return Get-ThemeColor 'FiveLow'
    }
    if ($Remaining -le 50) {
        return Get-ThemeColor 'FiveCaution'
    }
    if ($Remaining -le 75) {
        return Get-ThemeColor 'FiveGood'
    }
    return Get-ThemeColor 'FiveNormal'
}

function Get-WeeklyColor {
    param(
        [int]$Remaining,
        [bool]$BlinkOn = $false
    )

    if ($Remaining -le 0 -and $BlinkOn) {
        return Get-ThemeColor 'AlertFlash'
    }
    if ($Remaining -le 5) {
        return Get-ThemeColor 'WeekCritical'
    }
    if ($Remaining -le 15) {
        return Get-ThemeColor 'WeekDanger'
    }
    if ($Remaining -le 25) {
        return Get-ThemeColor 'WeekLow'
    }
    if ($Remaining -le 50) {
        return Get-ThemeColor 'WeekCaution'
    }
    if ($Remaining -le 75) {
        return Get-ThemeColor 'WeekGood'
    }
    return Get-ThemeColor 'WeekNormal'
}

function ConvertTo-ResetDate {
    param(
        [string]$ResetAt,
        [string]$ResetText,
        [bool]$Weekly
    )

    $target = [datetime]::MinValue
    if ($ResetAt -and [datetime]::TryParse($ResetAt, [ref]$target)) {
        return $target
    }

    if (-not $ResetText) {
        return $null
    }

    $now = Get-Date
    $hour = $null
    $minute = $null
    if ($ResetText -match '(\d{1,2})\s*:\s*(\d{2})') {
        $hour = [int]$Matches[1]
        $minute = [int]$Matches[2]
    } else {
        return $null
    }

    if (($ResetText -match 'PM') -or ($ResetText -match ([string][char]0xC624 + [string][char]0xD6C4))) {
        if ($hour -lt 12) {
            $hour += 12
        }
    }
    if (($ResetText -match 'AM') -or ($ResetText -match ([string][char]0xC624 + [string][char]0xC804))) {
        if ($hour -eq 12) {
            $hour = 0
        }
    }

    if ($ResetText -match '(\d{4})\.\s*(\d{1,2})\.\s*(\d{1,2})\.') {
        return Get-Date -Year ([int]$Matches[1]) -Month ([int]$Matches[2]) -Day ([int]$Matches[3]) -Hour $hour -Minute $minute -Second 0
    }

    $target = Get-Date -Year $now.Year -Month $now.Month -Day $now.Day -Hour $hour -Minute $minute -Second 0
    if ($target -le $now) {
        if ($Weekly) {
            $target = $target.AddDays(7)
        } else {
            $target = $target.AddDays(1)
        }
    }
    return $target
}

function Format-ResetRemaining {
    param(
        [datetime]$Target,
        [bool]$Weekly
    )

    $hourUnit = [string][char]0xC2DC + [string][char]0xAC04
    $minuteUnit = [string][char]0xBD84
    $dayUnit = [string][char]0xC77C

    if (-not $Target) {
        return '?'
    }

    $span = $Target - (Get-Date)
    if ($span.TotalSeconds -lt 0) {
        $span = [timespan]::Zero
    }

    if ($Weekly) {
        $days = [int][Math]::Floor($span.TotalDays)
        $hours = $span.Hours
        return "$days$dayUnit$hours$hourUnit"
    }

    $totalHours = [int][Math]::Floor($span.TotalHours)
    $minutes = $span.Minutes
    return "$totalHours$hourUnit$minutes$minuteUnit"
}

function New-UsageIcon {
    param(
        [int]$FiveHourRemaining = 0,
        [int]$WeeklyRemaining = 0,
        [bool]$FiveHourBlinkOn = $false,
        [bool]$WeeklyBlinkOn = $false,
        [bool]$FiveHourAlertActive = $false,
        [bool]$WeeklyAlertActive = $false
    )

    $bitmap = New-Object System.Drawing.Bitmap 64, 64
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $fiveSweep = [Math]::Max(0, [Math]::Min(100, $FiveHourRemaining)) * 3.6
    $weekSweep = [Math]::Max(0, [Math]::Min(100, $WeeklyRemaining)) * 3.6
    if ($FiveHourAlertActive) {
        $fiveSweep = 360
    }
    if ($WeeklyAlertActive) {
        $weekSweep = 360
    }

    $backPen = New-Object System.Drawing.Pen (Get-ThemeColor 'Track'), 8
    $fivePen = New-Object System.Drawing.Pen (Get-UsageColor $FiveHourRemaining $FiveHourBlinkOn), 8
    $weekPen = New-Object System.Drawing.Pen (Get-WeeklyColor $WeeklyRemaining $WeeklyBlinkOn), 8
    $fivePen.StartCap = $fivePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $weekPen.StartCap = $weekPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $graphics.DrawArc($backPen, 7, 7, 50, 50, -90, 360)
    $graphics.DrawArc($fivePen, 7, 7, 50, 50, -90, $fiveSweep)
    $graphics.DrawArc($backPen, 18, 18, 28, 28, -90, 360)
    $graphics.DrawArc($weekPen, 18, 18, 28, 28, -90, $weekSweep)

    $centerBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'CenterFill')
    $graphics.FillEllipse($centerBrush, 22, 22, 20, 20)
    Draw-CodexMark $graphics 32 32 8

    $iconHandle = $bitmap.GetHicon()
    $icon = [System.Drawing.Icon]::FromHandle($iconHandle).Clone()
    [void][NativeIconMethods]::DestroyIcon($iconHandle)

    $graphics.Dispose()
    $bitmap.Dispose()
    $backPen.Dispose()
    $fivePen.Dispose()
    $weekPen.Dispose()
    $centerBrush.Dispose()

    return $icon
}

function Get-UsageData {
    if (-not (Test-Path -LiteralPath $DataPath)) {
        return [pscustomobject]@{
            hasData = $false
            fiveHourRemaining = 0
            fiveHourReset = ''
            fiveHourResetAt = ''
            weeklyRemaining = 0
            weeklyReset = ''
            weeklyResetAt = ''
            creditsRemaining = 0
            updatedAt = ''
            status = 'no_data'
            message = 'No data yet'
            action = 'Login or Fetch now required'
        }
    }

    try {
        $raw = Get-Content -LiteralPath $DataPath -Raw -Encoding UTF8
        $data = $raw | ConvertFrom-Json
        if (-not $data.PSObject.Properties['hasData']) {
            $data | Add-Member -NotePropertyName hasData -NotePropertyValue $true
        }
        return $data
    } catch {
        return [pscustomobject]@{
            hasData = $false
            fiveHourRemaining = 0
            fiveHourReset = ''
            fiveHourResetAt = ''
            weeklyRemaining = 0
            weeklyReset = ''
            weeklyResetAt = ''
            creditsRemaining = 0
            updatedAt = ''
            status = 'no_data'
            message = 'No data yet'
            action = 'Login or Fetch now required'
        }
    }
}

function Get-AckData {
    if (-not (Test-Path -LiteralPath $AckPath)) {
        return [pscustomobject]@{
            fiveHourZeroAck = $false
            weeklyZeroAck = $false
        }
    }

    try {
        $raw = Get-Content -LiteralPath $AckPath -Raw -Encoding UTF8
        return $raw | ConvertFrom-Json
    } catch {
        return [pscustomobject]@{
            fiveHourZeroAck = $false
            weeklyZeroAck = $false
        }
    }
}

function Save-AckData {
    param($AckData)

    $AckData | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $AckPath -Encoding UTF8
}

function Sync-AckState {
    param($Data)

    $ack = Get-AckData
    $changed = $false

    if ([int]$Data.fiveHourRemaining -gt 0 -and $ack.fiveHourZeroAck) {
        $ack.fiveHourZeroAck = $false
        $changed = $true
    }
    if ([int]$Data.weeklyRemaining -gt 0 -and $ack.weeklyZeroAck) {
        $ack.weeklyZeroAck = $false
        $changed = $true
    }

    if ($changed) {
        Save-AckData $ack
    }

    return $ack
}

function Test-ZeroAlertActive {
    param($Data, $AckData)

    $fiveActive = ([int]$Data.fiveHourRemaining -le 0) -and (-not $AckData.fiveHourZeroAck)
    $weeklyActive = ([int]$Data.weeklyRemaining -le 0) -and (-not $AckData.weeklyZeroAck)
    return $fiveActive -or $weeklyActive
}

function Format-UsageSummary {
    param($Data)

    if (-not $Data.hasData) {
        return @(
            'Codex usage'
            'No data yet'
            'Login or Fetch now required'
        ) -join ([Environment]::NewLine)
    }

    $fiveResetTarget = ConvertTo-ResetDate $Data.fiveHourResetAt $Data.fiveHourReset $false
    $weeklyResetTarget = ConvertTo-ResetDate $Data.weeklyResetAt $Data.weeklyReset $true
    $fiveRemaining = Format-ResetRemaining $fiveResetTarget $false
    $weeklyRemaining = Format-ResetRemaining $weeklyResetTarget $true

    $lines = @(
        'Codex usage'
        "5h limit: $($Data.fiveHourRemaining)% remaining"
        "Reset in: $fiveRemaining"
        "Weekly limit: $($Data.weeklyRemaining)% remaining"
        "Reset in: $weeklyRemaining"
        "Updated: $($Data.updatedAt)"
    )

    if ($Data.creditsRemaining -and ([double]$Data.creditsRemaining -ne 0)) {
        $lines += "Credits: $($Data.creditsRemaining)"
    }

    if ($Data.status -and $Data.status -ne 'ok') {
        $lines += "Status: $($Data.status)"
    }
    if ($Data.lastError) {
        $lines += "Last error: $($Data.lastError)"
    }

    return $lines -join ([Environment]::NewLine)
}

function Update-Tray {
    if ($script:IsExiting -or $null -eq $script:NotifyIcon -or $script:NotifyIconDisposed) {
        return
    }
    $script:UsageData = Get-UsageData
    $hasData = [bool]$script:UsageData.hasData
    if ($hasData) {
        $script:AckData = Sync-AckState $script:UsageData
    } else {
        $script:AckData = Get-AckData
    }
    $summary = Format-UsageSummary $script:UsageData
    $fiveResetTarget = ConvertTo-ResetDate $script:UsageData.fiveHourResetAt $script:UsageData.fiveHourReset $false
    $weeklyResetTarget = ConvertTo-ResetDate $script:UsageData.weeklyResetAt $script:UsageData.weeklyReset $true
    $fiveResetRemaining = Format-ResetRemaining $fiveResetTarget $false
    $weeklyResetRemaining = Format-ResetRemaining $weeklyResetTarget $true
    $fiveAlertActive = $hasData -and ([int]$script:UsageData.fiveHourRemaining -le 0) -and (-not $script:AckData.fiveHourZeroAck)
    $weeklyAlertActive = $hasData -and ([int]$script:UsageData.weeklyRemaining -le 0) -and (-not $script:AckData.weeklyZeroAck)
    $fiveBlink = $fiveAlertActive -and $script:BlinkOn
    $weeklyBlink = $weeklyAlertActive -and $script:BlinkOn
    $creditText = ''
    if ($hasData -and $script:UsageData.creditsRemaining -and ([double]$script:UsageData.creditsRemaining -ne 0)) {
        $creditText = " | C $($script:UsageData.creditsRemaining)"
    }

    if ($script:NotifyIcon.Visible) {
        $newIcon = New-UsageIcon `
            -FiveHourRemaining ([int]$script:UsageData.fiveHourRemaining) `
            -WeeklyRemaining ([int]$script:UsageData.weeklyRemaining) `
            -FiveHourBlinkOn $fiveBlink `
            -WeeklyBlinkOn $weeklyBlink `
            -FiveHourAlertActive $fiveAlertActive `
            -WeeklyAlertActive $weeklyAlertActive
        $oldIcon = $script:NotifyIcon.Icon
        $script:NotifyIcon.Icon = $newIcon
        if ($oldIcon) {
            $oldIcon.Dispose()
        }
    }
    if ($hasData) {
        $tooltip = "5h $($script:UsageData.fiveHourRemaining)% $fiveResetRemaining | W $($script:UsageData.weeklyRemaining)% $weeklyResetRemaining$creditText"
    } else {
        $tooltip = 'Codex: No data yet | Fetch now required'
    }
    if ($tooltip.Length -gt 63) {
        $tooltip = $tooltip.Substring(0, 63)
    }
    $script:NotifyIcon.Text = $tooltip

    if ($hasData) {
        $script:SummaryItem.Text = "5h $($script:UsageData.fiveHourRemaining)% / Weekly $($script:UsageData.weeklyRemaining)% remaining"
        $script:FiveHourItem.Text = "5h limit: $($script:UsageData.fiveHourRemaining)% remaining, reset in $fiveResetRemaining"
        $script:WeeklyItem.Text = "Weekly limit: $($script:UsageData.weeklyRemaining)% remaining, reset in $weeklyResetRemaining"
    } else {
        $script:SummaryItem.Text = 'No data yet'
        $script:FiveHourItem.Text = 'Login or Fetch now required'
        $script:WeeklyItem.Text = 'No usage data available'
    }
    if ($hasData -and $script:UsageData.creditsRemaining -and ([double]$script:UsageData.creditsRemaining -ne 0)) {
        $script:SummaryItem.Text = "$($script:SummaryItem.Text) / Credits $($script:UsageData.creditsRemaining)"
    }

    $alertActive = Test-ZeroAlertActive $script:UsageData $script:AckData
    $script:AckAlertItem.Enabled = $alertActive
    if ($alertActive) {
        $script:AckAlertItem.Text = 'Acknowledge zero alert'
    } else {
        $script:AckAlertItem.Text = 'Zero alert acknowledged'
    }

    if ($script:WidgetPanel) {
        $script:WidgetPanel.Invalidate()
    }
}

function Hide-UsageWidget {
    if ($script:IsExiting) {
        return
    }
    if ($script:WidgetForm) {
        $script:WidgetVisible = $false
        $script:WidgetForm.Hide()
    }
    if ($null -ne $script:NotifyIcon -and -not $script:NotifyIconDisposed) {
        $script:NotifyIcon.Visible = $true
    }
    Update-Tray
}

function Show-UsageWidget {
    if ($script:IsExiting) {
        return
    }
    if (-not $script:WidgetForm) {
        $form = New-Object System.Windows.Forms.Form
        $form.Text = 'Codex Usage'
        $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
        $form.ShowInTaskbar = $false
        $form.TopMost = $true
        $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
        $form.Size = New-Object System.Drawing.Size -ArgumentList @($script:WidgetSize, $script:WidgetSize)
        $form.BackColor = $script:WidgetTransparentColor
        $form.TransparencyKey = $script:WidgetTransparentColor

        $screen = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $form.Location = New-Object System.Drawing.Point -ArgumentList @(
            ($screen.Right - 144),
            ($screen.Top + 16)
        )

        $panel = New-Object System.Windows.Forms.Panel
        $panel.Dock = [System.Windows.Forms.DockStyle]::Fill
        $panel.BackColor = $script:WidgetTransparentColor
        $panel.Add_Paint({
            param($sender, $eventArgs)

            $g = $eventArgs.Graphics
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.Clear($script:WidgetTransparentColor)
            $scale = $sender.Width / 128.0
            $scaleState = $g.Save()
            $g.ScaleTransform($scale, $scale)

            if (-not $script:UsageData) {
                $g.Restore($scaleState)
                return
            }
            if (-not $script:UsageData.hasData) {
                $titleFont = New-Object System.Drawing.Font 'Segoe UI', 9, ([System.Drawing.FontStyle]::Bold)
                $bodyFont = New-Object System.Drawing.Font 'Segoe UI', 7
                $textBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'Text')
                $mutedBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'MutedText')
                $closeBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'Close')
                Draw-CodexMark $g 64 36 10
                $g.DrawString('No data yet', $titleFont, $textBrush, 28, 58)
                $g.DrawString('Login or Fetch now', $bodyFont, $mutedBrush, 22, 78)
                $g.DrawString('required', $bodyFont, $mutedBrush, 45, 92)
                $g.DrawString('x', $titleFont, $closeBrush, 113, 3)
                $titleFont.Dispose()
                $bodyFont.Dispose()
                $textBrush.Dispose()
                $mutedBrush.Dispose()
                $closeBrush.Dispose()
                $g.Restore($scaleState)
                return
            }

            $five = [int]$script:UsageData.fiveHourRemaining
            $week = [int]$script:UsageData.weeklyRemaining
            $fiveResetTarget = ConvertTo-ResetDate $script:UsageData.fiveHourResetAt $script:UsageData.fiveHourReset $false
            $weeklyResetTarget = ConvertTo-ResetDate $script:UsageData.weeklyResetAt $script:UsageData.weeklyReset $true
            $fiveResetRemaining = Format-ResetRemaining $fiveResetTarget $false
            $weeklyResetRemaining = Format-ResetRemaining $weeklyResetTarget $true
            $fiveAlert = ($five -le 0) -and (-not $script:AckData.fiveHourZeroAck)
            $weekAlert = ($week -le 0) -and (-not $script:AckData.weeklyZeroAck)
            $fiveSweep = [Math]::Max(0, [Math]::Min(100, $five)) * 3.6
            $weekSweep = [Math]::Max(0, [Math]::Min(100, $week)) * 3.6
            if ($fiveAlert) { $fiveSweep = 360 }
            if ($weekAlert) { $weekSweep = 360 }

            $backPen = New-Object System.Drawing.Pen (Get-ThemeColor 'Track'), 7
            $fivePen = New-Object System.Drawing.Pen (Get-UsageColor $five ($fiveAlert -and $script:BlinkOn)), 7
            $weekPen = New-Object System.Drawing.Pen (Get-WeeklyColor $week ($weekAlert -and $script:BlinkOn)), 7
            $fivePen.StartCap = $fivePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $weekPen.StartCap = $weekPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

            if ($script:GraphStyle -eq 'Bars') {
                $trackBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'Track')
                $fiveBrush = New-Object System.Drawing.SolidBrush (Get-UsageColor $five ($fiveAlert -and $script:BlinkOn))
                $weekBrush = New-Object System.Drawing.SolidBrush (Get-WeeklyColor $week ($weekAlert -and $script:BlinkOn))
                $g.FillRectangle($trackBrush, 14, 24, 100, 14)
                $g.FillRectangle($fiveBrush, 14, 24, [int](100 * $five / 100), 14)
                $g.FillRectangle($trackBrush, 14, 52, 100, 14)
                $g.FillRectangle($weekBrush, 14, 52, [int](100 * $week / 100), 14)
                $trackBrush.Dispose()
                $fiveBrush.Dispose()
                $weekBrush.Dispose()
            } elseif ($script:GraphStyle -eq 'Meters') {
                $trackBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'Track')
                $fiveBrush = New-Object System.Drawing.SolidBrush (Get-UsageColor $five ($fiveAlert -and $script:BlinkOn))
                $weekBrush = New-Object System.Drawing.SolidBrush (Get-WeeklyColor $week ($weekAlert -and $script:BlinkOn))
                $g.FillRectangle($trackBrush, 34, 16, 18, 62)
                $g.FillRectangle($trackBrush, 76, 16, 18, 62)
                $g.FillRectangle($fiveBrush, 34, (16 + [int](62 * (100 - $five) / 100)), 18, [int](62 * $five / 100))
                $g.FillRectangle($weekBrush, 76, (16 + [int](62 * (100 - $week) / 100)), 18, [int](62 * $week / 100))
                $trackBrush.Dispose()
                $fiveBrush.Dispose()
                $weekBrush.Dispose()
            } elseif ($script:GraphStyle -eq 'Battery') {
                $outlinePen = New-Object System.Drawing.Pen (Get-ThemeColor 'BatteryOutline'), 2
                $fiveBrush = New-Object System.Drawing.SolidBrush (Get-UsageColor $five ($fiveAlert -and $script:BlinkOn))
                $weekBrush = New-Object System.Drawing.SolidBrush (Get-WeeklyColor $week ($weekAlert -and $script:BlinkOn))
                $g.DrawRectangle($outlinePen, 16, 22, 86, 16)
                $g.FillRectangle($fiveBrush, 19, 25, [int](80 * $five / 100), 10)
                $g.FillRectangle($outlinePen.Brush, 104, 26, 5, 8)
                $g.DrawRectangle($outlinePen, 16, 52, 86, 16)
                $g.FillRectangle($weekBrush, 19, 55, [int](80 * $week / 100), 10)
                $g.FillRectangle($outlinePen.Brush, 104, 56, 5, 8)
                $outlinePen.Dispose()
                $fiveBrush.Dispose()
                $weekBrush.Dispose()
            } else {
                $g.DrawArc($backPen, 25, 4, 78, 78, -90, 360)
                $g.DrawArc($fivePen, 25, 4, 78, 78, -90, $fiveSweep)
                $g.DrawArc($backPen, 41, 20, 46, 46, -90, 360)
                $g.DrawArc($weekPen, 41, 20, 46, 46, -90, $weekSweep)

                $centerBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'CenterFill')
                $g.FillEllipse($centerBrush, 53, 33, 22, 22)
                Draw-CodexMark $g 64 44 8
                $centerBrush.Dispose()
            }

            if ($script:GraphStyle -ne 'Rings') {
                $centerBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'CenterFill')
                $g.FillEllipse($centerBrush, 53, 33, 22, 22)
                Draw-CodexMark $g 64 44 8
                $centerBrush.Dispose()
            }

            $fontSmall = New-Object System.Drawing.Font 'Segoe UI', 7.5, ([System.Drawing.FontStyle]::Bold)
            $fontTiny = New-Object System.Drawing.Font 'Segoe UI', 6.5
            $textBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'Text')
            $mutedBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'MutedText')
            $closeBrush = New-Object System.Drawing.SolidBrush (Get-ThemeColor 'Close')
            $g.DrawString("5h $five%", $fontSmall, $textBrush, 8, 82)
            $g.DrawString($fiveResetRemaining, $fontTiny, $mutedBrush, 8, 96)
            $g.DrawString("W $week%", $fontSmall, $textBrush, 72, 82)
            $g.DrawString($weeklyResetRemaining, $fontTiny, $mutedBrush, 72, 96)
            if ($script:UsageData.creditsRemaining -and ([double]$script:UsageData.creditsRemaining -ne 0)) {
                $g.DrawString("C $($script:UsageData.creditsRemaining)", $fontTiny, $mutedBrush, 8, 113)
            }

            $g.DrawString('x', $fontSmall, $closeBrush, 113, 3)

            $backPen.Dispose()
            $fivePen.Dispose()
            $weekPen.Dispose()
            $fontSmall.Dispose()
            $fontTiny.Dispose()
            $textBrush.Dispose()
            $mutedBrush.Dispose()
            $closeBrush.Dispose()
            $g.Restore($scaleState)
        })
        $panel.Add_MouseDown({
            param($sender, $eventArgs)
            $scale = $sender.Width / 128.0
            $logicalX = $eventArgs.X / $scale
            $logicalY = $eventArgs.Y / $scale
            if ($logicalX -ge 108 -and $logicalY -le 24) {
                return
            }
            if ($eventArgs.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
                $script:WidgetDrag = $true
                $script:WidgetDragStart = New-Object System.Drawing.Point -ArgumentList @($eventArgs.X, $eventArgs.Y)
            }
        })
        $panel.Add_MouseMove({
            param($sender, $eventArgs)
            if ($script:WidgetDrag -and $script:WidgetDragStart) {
                $mousePoint = New-Object System.Drawing.Point -ArgumentList @($eventArgs.X, $eventArgs.Y)
                $screenPoint = $sender.PointToScreen($mousePoint)
                $newX = $screenPoint.X - $script:WidgetDragStart.X
                $newY = $screenPoint.Y - $script:WidgetDragStart.Y
                $script:WidgetForm.Location = New-Object System.Drawing.Point -ArgumentList @($newX, $newY)
            }
        })
        $panel.Add_MouseUp({
            $script:WidgetDrag = $false
            $script:WidgetDragStart = $null
        })
        $panel.Add_MouseDoubleClick({
            param($sender, $eventArgs)
            $scale = $sender.Width / 128.0
            $logicalX = $eventArgs.X / $scale
            $logicalY = $eventArgs.Y / $scale
            if ($logicalX -ge 48 -and $logicalX -le 80 -and $logicalY -ge 28 -and $logicalY -le 64) {
                if ($script:WidgetSize -eq 128) {
                    $script:WidgetSize = 256
                } else {
                    $script:WidgetSize = 128
                }
                Save-AppSettings
                $script:WidgetForm.Size = New-Object System.Drawing.Size -ArgumentList @($script:WidgetSize, $script:WidgetSize)
                $script:WidgetPanel.Invalidate()
                Write-Log "Widget size changed to $script:WidgetSize."
            }
        })
        $panel.Add_MouseClick({
            param($sender, $eventArgs)
            $scale = $sender.Width / 128.0
            $logicalX = $eventArgs.X / $scale
            $logicalY = $eventArgs.Y / $scale
            if ($logicalX -ge 108 -and $logicalY -le 24) {
                Hide-UsageWidget
            }
        })
        if ($script:SharedMenu) {
            $panel.ContextMenuStrip = $script:SharedMenu
        }

        $form.Controls.Add($panel)
        $form.Add_FormClosing({
            param($sender, $eventArgs)
            try {
                if ($script:IsExiting) {
                    $eventArgs.Cancel = $false
                } else {
                    $eventArgs.Cancel = $true
                    Hide-UsageWidget
                }
            } catch {
                Write-Log ("Widget FormClosing failed: " + $_.Exception.Message)
                $eventArgs.Cancel = $script:IsExiting -eq $false
            }
        })

        $script:WidgetForm = $form
        $script:WidgetPanel = $panel
    }

    Update-Tray
    if ($null -ne $script:NotifyIcon -and -not $script:NotifyIconDisposed) {
        $script:NotifyIcon.Visible = $false
    }
    $script:WidgetVisible = $true
    $script:WidgetForm.Show()
    $script:WidgetForm.Activate()
}

Apply-AppSettings
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:NotifyIcon = New-Object System.Windows.Forms.NotifyIcon
$script:NotifyIconDisposed = $false
$script:NotifyIcon.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip
$script:SummaryItem = New-Object System.Windows.Forms.ToolStripMenuItem
$script:SummaryItem.Enabled = $false
$script:FiveHourItem = New-Object System.Windows.Forms.ToolStripMenuItem
$script:FiveHourItem.Enabled = $false
$script:WeeklyItem = New-Object System.Windows.Forms.ToolStripMenuItem
$script:WeeklyItem.Enabled = $false
$script:AckData = Get-AckData
$script:BlinkOn = $false

$refreshItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Reload display'
$refreshItem.Add_Click({
    try {
        Update-Tray
        $script:NotifyIcon.BalloonTipTitle = 'Codex usage'
        $script:NotifyIcon.BalloonTipText = Format-UsageSummary $script:UsageData
        $script:NotifyIcon.ShowBalloonTip(4000)
        Write-Log 'Refreshed tray data.'
    } catch {
        Write-Log ("Refresh failed: " + $_.Exception.Message)
    }
})

$fetchItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Fetch now (visible browser)'
$fetchItem.Add_Click({
    try {
        Write-Log 'Starting one-shot scraper from tray menu.'
        if (Start-VisibleFetch) {
            $script:NotifyIcon.BalloonTipTitle = 'Codex usage'
            $script:NotifyIcon.BalloonTipText = 'Fetching current usage. Display will refresh automatically.'
            $script:NotifyIcon.ShowBalloonTip(3000)
        }
    } catch {
        Write-Log ("Fetch now failed: " + $_.Exception.Message)
    }
})

$script:AckAlertItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Acknowledge zero alert'
$script:AckAlertItem.Add_Click({
    try {
        $ack = Get-AckData
        if ($script:UsageData -and ([int]$script:UsageData.fiveHourRemaining -le 0)) {
            $ack.fiveHourZeroAck = $true
        }
        if ($script:UsageData -and ([int]$script:UsageData.weeklyRemaining -le 0)) {
            $ack.weeklyZeroAck = $true
        }
        Save-AckData $ack
        $script:AckData = $ack
        $script:BlinkOn = $false
        Update-Tray
        Write-Log 'Zero alert acknowledged.'
    } catch {
        Write-Log ("Acknowledge zero alert failed: " + $_.Exception.Message)
    }
})

$widgetItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Show widget'
$widgetItem.Add_Click({
    try {
        Show-UsageWidget
        Write-Log 'Widget shown; tray icon hidden.'
    } catch {
        Write-Log ("Show widget failed: " + $_.Exception.Message)
    }
})

$graphMenu = New-Object System.Windows.Forms.ToolStripMenuItem 'Graph style'
$ringsItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Rings'
$barsItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Bars'
$metersItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Meters'
$batteryItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Battery'

$ringsItem.Add_Click({
    $script:GraphStyle = 'Rings'
    Save-AppSettings
    if ($script:WidgetPanel) { $script:WidgetPanel.Invalidate() }
    Write-Log 'Graph style changed to Rings.'
})
$barsItem.Add_Click({
    $script:GraphStyle = 'Bars'
    Save-AppSettings
    if ($script:WidgetPanel) { $script:WidgetPanel.Invalidate() }
    Write-Log 'Graph style changed to Bars.'
})
$metersItem.Add_Click({
    $script:GraphStyle = 'Meters'
    Save-AppSettings
    if ($script:WidgetPanel) { $script:WidgetPanel.Invalidate() }
    Write-Log 'Graph style changed to Meters.'
})
$batteryItem.Add_Click({
    $script:GraphStyle = 'Battery'
    Save-AppSettings
    if ($script:WidgetPanel) { $script:WidgetPanel.Invalidate() }
    Write-Log 'Graph style changed to Battery.'
})

[void]$graphMenu.DropDownItems.Add($ringsItem)
[void]$graphMenu.DropDownItems.Add($barsItem)
[void]$graphMenu.DropDownItems.Add($metersItem)
[void]$graphMenu.DropDownItems.Add($batteryItem)

$colorsMenu = New-Object System.Windows.Forms.ToolStripMenuItem 'Colors'
$fiveColorsMenu = New-Object System.Windows.Forms.ToolStripMenuItem '5-hour colors'
$weekColorsMenu = New-Object System.Windows.Forms.ToolStripMenuItem 'Weekly colors'
$uiColorsMenu = New-Object System.Windows.Forms.ToolStripMenuItem 'Interface colors'

$fiveColorOptions = @(
    @{ Label = 'Normal (76-100%)'; Key = 'FiveNormal' },
    @{ Label = 'Good (51-75%)'; Key = 'FiveGood' },
    @{ Label = 'Caution (26-50%)'; Key = 'FiveCaution' },
    @{ Label = 'Low (16-25%)'; Key = 'FiveLow' },
    @{ Label = 'Danger (6-15%)'; Key = 'FiveDanger' },
    @{ Label = 'Critical (0-5%)'; Key = 'FiveCritical' }
)
$weekColorOptions = @(
    @{ Label = 'Normal (76-100%)'; Key = 'WeekNormal' },
    @{ Label = 'Good (51-75%)'; Key = 'WeekGood' },
    @{ Label = 'Caution (26-50%)'; Key = 'WeekCaution' },
    @{ Label = 'Low (16-25%)'; Key = 'WeekLow' },
    @{ Label = 'Danger (6-15%)'; Key = 'WeekDanger' },
    @{ Label = 'Critical (0-5%)'; Key = 'WeekCritical' }
)
$uiColorOptions = @(
    @{ Label = 'Graph track'; Key = 'Track' },
    @{ Label = 'Main text'; Key = 'Text' },
    @{ Label = 'Secondary text'; Key = 'MutedText' },
    @{ Label = 'Close button'; Key = 'Close' },
    @{ Label = 'Codex mark'; Key = 'CodexMark' },
    @{ Label = 'Center fill'; Key = 'CenterFill' },
    @{ Label = 'Transparent edge'; Key = 'TransparentEdge' },
    @{ Label = 'Battery outline'; Key = 'BatteryOutline' },
    @{ Label = 'Zero alert flash'; Key = 'AlertFlash' }
)

foreach ($option in $fiveColorOptions) {
    $item = New-Object System.Windows.Forms.ToolStripMenuItem $option.Label
    $item.Tag = $option.Key
    $item.BackColor = Get-ThemeColor $option.Key
    $item.Add_Click({
        param($sender, $eventArgs)
        Select-ThemeColor ([string]$sender.Tag)
        $sender.BackColor = Get-ThemeColor ([string]$sender.Tag)
    })
    [void]$fiveColorsMenu.DropDownItems.Add($item)
}
foreach ($option in $weekColorOptions) {
    $item = New-Object System.Windows.Forms.ToolStripMenuItem $option.Label
    $item.Tag = $option.Key
    $item.BackColor = Get-ThemeColor $option.Key
    $item.Add_Click({
        param($sender, $eventArgs)
        Select-ThemeColor ([string]$sender.Tag)
        $sender.BackColor = Get-ThemeColor ([string]$sender.Tag)
    })
    [void]$weekColorsMenu.DropDownItems.Add($item)
}
foreach ($option in $uiColorOptions) {
    $item = New-Object System.Windows.Forms.ToolStripMenuItem $option.Label
    $item.Tag = $option.Key
    $item.BackColor = Get-ThemeColor $option.Key
    $item.Add_Click({
        param($sender, $eventArgs)
        Select-ThemeColor ([string]$sender.Tag)
        $sender.BackColor = Get-ThemeColor ([string]$sender.Tag)
    })
    [void]$uiColorsMenu.DropDownItems.Add($item)
}

$resetColorsItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Reset all colors'
$resetColorsItem.Add_Click({ Reset-ThemeColors })
[void]$colorsMenu.DropDownItems.Add($fiveColorsMenu)
[void]$colorsMenu.DropDownItems.Add($weekColorsMenu)
[void]$colorsMenu.DropDownItems.Add($uiColorsMenu)
[void]$colorsMenu.DropDownItems.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$colorsMenu.DropDownItems.Add($resetColorsItem)

$intervalMenu = New-Object System.Windows.Forms.ToolStripMenuItem 'Check interval'
$intervalOptions = @(
    @{ Label = '10 minutes (Recommended minimum)'; Seconds = 600 },
    @{ Label = '15 minutes'; Seconds = 900 },
    @{ Label = '30 minutes'; Seconds = 1800 },
    @{ Label = '60 minutes'; Seconds = 3600 }
)

foreach ($option in $intervalOptions) {
    $seconds = [int]$option.Seconds
    $item = New-Object System.Windows.Forms.ToolStripMenuItem $option.Label
    $item.CheckOnClick = $false
    $item.Checked = ($script:RefreshIntervalSeconds -eq $seconds)
    $item.Tag = $seconds
    $item.Add_Click({
        param($sender, $eventArgs)
        Set-RefreshInterval ([int]$sender.Tag)
    })
    $script:IntervalMenuItems[[string]$seconds] = $item
    [void]$intervalMenu.DropDownItems.Add($item)
}

$openItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Open usage page'
$openItem.Add_Click({
    Start-Process $SettingsUrl
})

$editItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Open data file'
$editItem.Add_Click({
    Start-Process notepad.exe $DataPath
})

$scraperLogItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Open scraper log'
$scraperLogItem.Add_Click({
    if (-not (Test-Path -LiteralPath $ScraperLogPath)) {
        New-Item -ItemType File -Path $ScraperLogPath -Force | Out-Null
    }
    Start-Process notepad.exe $ScraperLogPath
})

$exitItem = New-Object System.Windows.Forms.ToolStripMenuItem 'Exit'
$exitItem.Add_Click({
    try {
        Write-Log 'Exit selected from menu.'
        Invoke-AppCleanup -RequestApplicationExit
    } catch {
        Write-Log ("Exit menu cleanup failed: " + $_.Exception.Message)
        try {
            [System.Windows.Forms.Application]::Exit()
        } catch {
        }
    }
})

[void]$menu.Items.Add($script:SummaryItem)
[void]$menu.Items.Add($script:FiveHourItem)
[void]$menu.Items.Add($script:WeeklyItem)
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$menu.Items.Add($refreshItem)
[void]$menu.Items.Add($fetchItem)
[void]$menu.Items.Add($script:AckAlertItem)
[void]$menu.Items.Add($widgetItem)
[void]$menu.Items.Add($graphMenu)
[void]$menu.Items.Add($colorsMenu)
[void]$menu.Items.Add($intervalMenu)
[void]$menu.Items.Add($openItem)
[void]$menu.Items.Add($editItem)
[void]$menu.Items.Add($scraperLogItem)
[void]$menu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))
[void]$menu.Items.Add($exitItem)

$script:NotifyIcon.ContextMenuStrip = $menu
$script:SharedMenu = $menu
[System.Windows.Forms.Application]::add_ApplicationExit({
    try {
        Invoke-AppCleanup
    } catch {
        Write-Log ("ApplicationExit cleanup failed: " + $_.Exception.Message)
    }
})
$script:NotifyIcon.Add_DoubleClick({
    try {
        Show-UsageWidget
        Write-Log 'Widget shown by tray double-click; tray icon hidden.'
    } catch {
        Write-Log ("Tray double-click widget failed: " + $_.Exception.Message)
    }
})

try {
    Write-Log 'Starting tray app.'
    Update-Tray
    $script:NotifyIcon.BalloonTipTitle = 'Codex usage'
    $script:NotifyIcon.BalloonTipText = Format-UsageSummary $script:UsageData
    $script:NotifyIcon.ShowBalloonTip(5000)
    Write-Log 'Tray app is running.'
    $script:RefreshTimer = New-Object System.Windows.Forms.Timer
    $script:RefreshTimer.Interval = 60000
    $script:RefreshTimer.Add_Tick({
        try {
            Update-Tray
        } catch {
            Write-Log ("Timer refresh failed: " + $_.Exception.Message)
        }
    })
    $script:RefreshTimer.Start()
    $script:BlinkTimer = New-Object System.Windows.Forms.Timer
    $script:BlinkTimer.Interval = 700
    $script:BlinkTimer.Add_Tick({
        try {
            if ($script:UsageData -and $script:AckData -and (Test-ZeroAlertActive $script:UsageData $script:AckData)) {
                $script:BlinkOn = -not $script:BlinkOn
                if ($script:WidgetVisible -and $script:WidgetPanel) {
                    $script:WidgetPanel.Invalidate()
                } else {
                    Update-Tray
                }
            }
        } catch {
            Write-Log ("Blink refresh failed: " + $_.Exception.Message)
        }
    })
    $script:BlinkTimer.Start()
    [System.Windows.Forms.Application]::Run()
    Invoke-AppCleanup
} catch {
    Write-Log ("Fatal error: " + $_.Exception.Message)
    Write-Log $_.ScriptStackTrace
    try {
        Invoke-AppCleanup -RequestApplicationExit
    } catch {
    }
}
