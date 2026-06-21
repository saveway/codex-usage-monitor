# Codex Usage Monitor

[한국어 README](README.md)

An unofficial personal tool that displays values from the ChatGPT Codex usage page in the Windows 10/11 system tray and a desktop widget.

> This project is not an official app developed, endorsed, supported, or distributed by OpenAI. OpenAI, ChatGPT, and Codex are trademarks of their respective owners.

## Important Notice

OpenAI does not currently provide a documented public API for these Codex usage values. This program reads the rendered webpage with Playwright, so it may stop working when the site structure changes and may be restricted by applicable terms or policies.

Before using or redistributing it, you are responsible for reviewing and complying with OpenAI's current terms, policies, rate limits, and account rules. The default and minimum automatic check interval is 10 minutes.

The program contains no automation-detection bypass flags, off-screen browser placement, or browser-window concealment logic. A browser is shown only for the first login or a login explicitly requested by the user. Routine collection uses Playwright's standard headless mode. If the page does not render correctly in headless mode, collection fails instead of secretly substituting a visible browser.

## Privacy and Security

Please understand the following before using the program.

- This program **does not use a developer-operated server or relay server.**
- It contains **no telemetry or usage-statistics reporting.**
- It does not read, store, or transmit your ChatGPT password.
- You sign in **directly on the ChatGPT/OpenAI browser page.**
- Authentication cookies and browser session data may be stored in the local `browser-profile/` to preserve your login session.
- Usage JSON, settings, and logs are stored locally only in `%LOCALAPPDATA%\CodexUsageMonitor`.
- For complete removal, exit the app and delete `%LOCALAPPDATA%\CodexUsageMonitor`.

The `browser-profile/` may contain a live login session. Never upload or share this directory through GitHub, messaging, bug reports, or any other channel.

## Requirements

- Windows 10 or Windows 11
- Python 3.11 or newer available on `PATH`
- Windows PowerShell 5.1
- Playwright Chromium

## Installation

Run the following commands in the project directory:

```powershell
pip install -r requirements.txt
python -m playwright install chromium
```

Launcher:

```text
run-codex-usage-tray.bat
```

### Start Automatically with Windows

```powershell
powershell -ExecutionPolicy Bypass -File .\install-startup.ps1
```

To remove only the automatic startup entry:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall-startup.ps1
```

## How to Use

### 1. First Run and Login

1. Double-click `run-codex-usage-tray.bat`.
2. If there is no saved login session, a browser opens for ChatGPT login. This browser does not collect your password; it lets you sign in directly on the OpenAI page and create a local session.
3. Complete the ChatGPT login in the browser. Once the usage page is read, the browser closes and the result is written to the local data file.
4. Hover over or right-click the Codex icon in the Windows notification area to see the result.

The login session may be retained in `%LOCALAPPDATA%\CodexUsageMonitor\browser-profile`, so you normally do not need to sign in on every run. If the session expires or OpenAI requires reauthentication, sign in again through the visible browser.

### 2. Displayed Values

The tray tooltip and widget display the following values when available:

- Remaining percentage of the five-hour limit and time until reset
- Remaining percentage of the weekly limit and time until reset
- Remaining credits when the credit value is not zero
- Current collection status and the latest saved data

Double-click the tray icon to open the widget and hide the tray icon. Closing the widget restores the tray icon. You can drag the widget to move it. Double-click the center Codex logo to switch between 128×128 and 256×256 sizes.

### 3. Context Menu

Right-clicking either the tray icon or the widget opens the same menu.

- `Reload display`: Reads the existing local data again and refreshes only the display. It does not fetch the webpage.
- `Fetch now (visible browser)`: Opens a visible browser and immediately fetches current usage.
- `Acknowledge zero alert`: Acknowledges and stops the flashing 0% alert.
- `Show widget`: Shows the widget from tray mode.
- `Graph style`: Selects Rings, Bars, Meters, or Battery.
- `Colors`: Changes five-hour, weekly, and interface colors or restores their defaults.
- `Check interval`: Changes automatic collection to 10, 15, 30, or 60 minutes.
- `Open usage page`: Opens the original ChatGPT Codex usage page.
- `Open data file`: Opens the current local usage JSON in Notepad.
- `Open scraper log`: Opens the collector log in Notepad.
- `Exit`: Exits the program.

### 4. When to Fetch Manually

Use `Fetch now (visible browser)` in these situations:

1. The program has started but no data exists yet.
2. The login session has expired and you need to sign in again.
3. Automatic headless collection repeatedly fails.
4. You want an immediate value instead of waiting for the next scheduled check.

Complete any required login or authentication directly in the visible browser. If collection fails, a notification displays the process exit code and collector log path. A warning dialog is also shown when the widget is open.

### 5. Status Messages

- `No data yet`: No usage data has been successfully saved yet.
- `Login or Fetch now required`: A login or manual collection through the visible browser is required.
- `error`: The latest collection failed. Possible causes include network failure, an expired login, a page-structure change, or headless rendering behavior. Check `Open scraper log`, then run `Fetch now (visible browser)`.

### 6. Change the Automatic Interval

Right-click the tray icon or widget, open `Check interval`, and select 10, 15, 30, or 60 minutes. The choice is saved immediately and the background collector restarts with the new interval. Intervals below 10 minutes are not offered to avoid unnecessary requests to the site and account.

### 7. Check Logs

Use `Open scraper log` in the context menu for webpage collection and login-related errors. Tray startup and UI errors are written to:

```text
%LOCALAPPDATA%\CodexUsageMonitor\codex-usage-scraper.log
%LOCALAPPDATA%\CodexUsageMonitor\codex-usage-tray.log
```

If the tray icon does not appear after launch, run `debug-codex-usage-tray.bat` to inspect the error. Logs can include page status or local paths, so review them before sharing.

### 8. Complete Removal

1. Select `Exit` from the tray or widget context menu.
2. If automatic startup was installed, run `uninstall-startup.ps1`.
3. Delete the program source directory.
4. To remove the saved login session, usage, settings, and logs, delete `%LOCALAPPDATA%\CodexUsageMonitor`.

Deleting the final directory also removes authentication cookies from the local `browser-profile`.

## Local Data

All mutable data is stored outside the source repository at:

```text
%LOCALAPPDATA%\CodexUsageMonitor
```

Main contents:

- `browser-profile/`: authentication cookies and browser session state
- `codex-usage.json`: current usage and credit values
- `codex-usage-settings.json`: display colors, graph, and interval settings
- `codex-usage-ack.json`: acknowledged alert state
- `*.log`, `*.lock`, and `*.pid`: operational and process-management files

Treat the entire directory as private data.

## Debug Capture

Full-page text capture is disabled by default. Enable it explicitly only when local debugging is necessary:

```powershell
$env:CODEX_USAGE_DEBUG_CAPTURE = '1'
```

Saved text has numeric values removed and email/account-related lines redacted, but sensitive page labels may remain. Review it manually before sharing and remove the environment variable when debugging is complete.

## Files That Must Never Be Published

Do not commit or distribute these files and directories:

```text
CodexUsageMonitor/
browser-profile/
codex-usage-browser-profile/
codex-usage.json
codex-usage-settings.json
codex-usage-ack.json
codex-usage-page-text.txt
codex-usage-scraper.log
codex-usage-tray.log
*.log
*.lock
*.pid
.env
```

Only `*.example.json` files are intended as public examples.

## Development and Security Checks

Inspect the current file tree before publishing. `rg` (ripgrep) is a separate tool and is not included with Windows or PowerShell. Install it before using the first command, for example with `winget install BurntSushi.ripgrep.MSVC`.

```powershell
rg -n -i "api[_-]?key|access[_-]?token|refresh[_-]?token|authorization|bearer|cookie|session" .
Get-ChildItem -Force -Recurse | Where-Object { $_.FullName -match 'browser-profile|CodexUsageMonitor|\.log$|page-text' }
```

Without `rg`, use PowerShell's built-in `Select-String`:

```powershell
Get-ChildItem -Force -Recurse -File | Select-String -Pattern 'api[_-]?key|access[_-]?token|refresh[_-]?token|authorization|bearer|cookie|session' -CaseSensitive:$false
```

After creating a Git repository, inspect every revision before pushing:

```powershell
git log --all --oneline
git rev-list --objects --all
git log -p --all -G "api[_-]?key|access[_-]?token|refresh[_-]?token|authorization|bearer|cookie|session"
```

Using a dedicated scanner such as Gitleaks or TruffleHog before publication is recommended.

## Limitations

- The parser depends on the current ChatGPT page structure and language labels.
- Headless browser rendering can fail depending on site behavior.
- An expired login session requires the user to sign in again.
- This project does not provide an official usage API or guarantee continued compatibility.

## License

MIT License. See [LICENSE](LICENSE).
