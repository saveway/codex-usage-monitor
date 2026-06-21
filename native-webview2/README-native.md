# Codex Usage Monitor V2 - WebView2 prototype

This folder contains an isolated first prototype for a native Windows version. It does not replace or modify the v1 Python, PowerShell, Playwright, release, tag, or GitHub Actions structure.

## Design decision

- UI: C# WinForms on .NET Framework 4.8 (`net48`, x64).
- Browser: Microsoft Edge WebView2 Evergreen Runtime through `Microsoft.Web.WebView2` 1.0.4022.49.
- Build: .NET SDK 8.0.422 plus `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3, so the .NET Framework 4.8 targeting pack does not have to be installed separately on the build machine.
- Deployment: framework-dependent EXE and the WebView2 managed/native loader DLLs. Chromium is not bundled.
- State: `%LOCALAPPDATA%\CodexUsageMonitorV2` only. No developer server or telemetry is used.

Using .NET Framework 4.8 is practical for this prototype and keeps the app binaries small. The target PC must have .NET Framework 4.8 and the Microsoft Edge WebView2 Runtime. Windows 10/11 commonly has WebView2 already, but the app checks at runtime and offers the official Microsoft download page if it is missing.

## Implemented prototype scope

- Notification-area icon with `Open/Login`, `Fetch now`, `Open data file`, `Open log`, and `Exit`.
- A visible WebView2 window opens `https://chatgpt.com/codex/cloud/settings/analytics#usage`.
- Login happens only on the real ChatGPT/OpenAI page shown in WebView2.
- The persistent WebView2 user-data folder is `%LOCALAPPDATA%\CodexUsageMonitorV2\webview2-profile`.
- `Fetch now` reads `document.body.innerText` after navigation, parses the 5-hour and weekly remaining percentages, and writes `codex-usage.json`.
- Because the usage page is rendered as a single-page application, `Fetch now` waits up to 30 seconds for the usage labels instead of reading the body immediately after the navigation event.
- The window status bar and tray notifications distinguish login required, page access failure, network failure, missing WebView2 Runtime, parse failure, and successful collection.
- A failed parse writes only `codex-usage-debug-status.txt`, containing allowlisted host/category and present/absent flags for expected labels. It never contains page excerpts, percentages, email addresses, cookies, or tokens.
- Logs are written to `codex-usage-monitor-v2.log` and rotated at approximately 2 MB, retaining one `.1` backup.
- Startup and exit remove selected cache-only directories under WebView2's `EBWebView` data root, including `Cache`, `Code Cache`, `GPUCache`, shader caches, service-worker cache storage, and Crashpad reports. Cookies, Local Storage, IndexedDB, and session storage are not selected for deletion.
- WebView2 is launched with small disk/media cache limits. These flags are for storage control, not automation concealment.
- Exit uses one guarded cleanup path and disposes the browser form and notification icon inside exception-safe blocks. The app does not create PID or lock files.

## Build

Install the .NET 8 SDK or a newer compatible SDK, then run:

```powershell
cd native-webview2
dotnet restore
dotnet build -c Release
```

The minimal deployment directory is generated at:

```text
bin\Release\net48\deploy\
```

The initial x64 Release build contains five files:

| File | Bytes |
|---|---:|
| `CodexUsageMonitorV2.exe` | 32,768 |
| `CodexUsageMonitorV2.exe.config` | 174 |
| `Microsoft.Web.WebView2.Core.dll` | 698,248 |
| `Microsoft.Web.WebView2.WinForms.dll` | 38,792 |
| `runtimes\win-x64\native\WebView2Loader.dll` | 163,208 |
| **Total** | **933,190 (about 0.89 MiB)** |

The normal build directory is slightly larger because NuGet also copies assemblies that this WinForms-only package does not need. Use the `deploy` directory for size evaluation.

## Run

Run `bin\Release\net48\deploy\CodexUsageMonitorV2.exe`. Right-click the tray icon and choose `Open/Login`. Sign in directly on the ChatGPT/OpenAI page if requested. After the usage page is visible, choose `Fetch now` to read and save the two percentages.

If WebView2 Runtime is missing, install the Evergreen Runtime from the official Microsoft WebView2 download page and restart the app:

https://developer.microsoft.com/microsoft-edge/webview2/

## Local files

The prototype may create:

```text
%LOCALAPPDATA%\CodexUsageMonitorV2\
  codex-usage.json
  codex-usage-monitor-v2.log
  codex-usage-monitor-v2.log.1
  codex-usage-debug-status.txt
  webview2-profile\
```

The profile can contain authentication cookies needed to keep the user signed in. Treat it as private account data. Exit the app before deleting the directory. Deleting it resets all v2 local state and requires login again.

## Security and privacy principles

- The app never asks for or stores a ChatGPT password in its own UI.
- Authentication is performed directly in the real OpenAI/ChatGPT page.
- No cookies, page text, usage data, credentials, or tokens are sent to a developer server.
- There is no telemetry and no unofficial internal API/token call.
- There is no hidden/off-screen browser, automation-detection bypass, cookie extraction, or bundled Chromium.
- Only the two parsed percentages and basic timestamp/source metadata are saved to JSON. Full page text is not persisted.
- Failure diagnostics contain only boolean label-presence results and a coarse destination category. Full page text and numeric page content are never persisted.
- Logs use coarse destinations such as `chatgpt.com/usage` or `authentication-provider`; detailed authentication URLs and actual percentage values are not logged.

This is an unofficial personal prototype, not an OpenAI product. Page scraping is inherently fragile and users remain responsible for complying with applicable terms.

## V1 comparison

Advantages over v1:

- No Python or Playwright installation.
- No bundled Chromium, reducing the deployment payload from hundreds of megabytes to about 0.89 MiB for app files.
- Login and collection share one visible, persistent WebView2 profile.
- One process with no scraper PID/lock lifecycle.

Trade-offs:

- Requires .NET Framework 4.8 and Microsoft Edge WebView2 Runtime.
- DOM text parsing can break when ChatGPT wording or layout changes.
- This prototype has not yet reproduced v1's widget, graphs, colors, alerts, credits, reset-time parsing, or scheduling.
- `Fetch now` currently keeps the browser visible by design.

## Not implemented yet

- Widget UI and graph styles.
- Automatic periodic collection.
- Credits and reset-time parsing.
- Installer, startup registration, code signing, packaging, GitHub Actions, and Release publishing.
- Localization and polished icons.
- Automated end-to-end testing against an authenticated account.
- Resilience against future ChatGPT wording, localization, routing, or DOM changes beyond the current text parser.

## Prototype verification status

- Restore and Release build: successful with zero warnings and zero errors.
- WebView2 Runtime detection on the development PC: successful (`149.0.4022.80`).
- Visible WebView2 sign-in and navigation: successful. Authentication was completed by the user on the real provider and ChatGPT pages; the app did not receive credentials.
- Session persistence: successful. A new process reused `%LOCALAPPDATA%\CodexUsageMonitorV2\webview2-profile` and reached the usage page without another sign-in.
- Session persistence after cache cleanup: successful. Cache-only directories were removed before the successful authenticated fetch; authentication cookies and session-storage candidates remained intact.
- Actual authenticated usage-page reading and parsing: successful. `document.body.innerText` contained both the 5-hour and weekly fields after SPA rendering completed. The real percentage values are intentionally not documented.
- Example Korean usage-text parsing and JSON persistence: successful (75% 5-hour, 60% weekly). This fixture is not presented as real account data.
- Cache cleanup against a real WebView2 profile: removed 231 cache files (32,188,683 measured bytes), reducing the test profile from about 33.5 MiB to about 2.82 MiB while leaving session-storage candidates untouched.
- Guarded application-context exit after authenticated testing: successful with exit code 0. No related app process, PID file, or lock file remained, and the log file could be reopened with exclusive access.
- V1 tracked files: intentionally unchanged by this prototype.

## Remaining risks after authenticated verification

- The parser depends on visible ChatGPT wording. A UI copy or localization change can require regex updates.
- The persistent session can expire or be revoked by ChatGPT/OpenAI, after which interactive sign-in is required again.
- Enterprise authentication, CAPTCHA, passkeys, and additional verification screens can require user interaction and were not generalized.
- WebView2 Runtime and network policy differences across Windows installations still need broader machine testing.
- The local profile contains authentication material and must never be committed, uploaded, or shared.
