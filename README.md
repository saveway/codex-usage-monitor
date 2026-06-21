# Codex Usage Monitor

[English README](README.en.md)

ChatGPT Codex 사용량 페이지의 값을 Windows 10/11 시스템 트레이와 데스크톱 위젯에 표시하는 비공식 개인용 도구입니다.

> 이 프로젝트는 OpenAI가 개발, 승인, 지원 또는 배포하는 공식 앱이 아닙니다. OpenAI, ChatGPT 및 Codex는 각 권리자의 상표입니다.

## 중요 안내

OpenAI는 현재 이 Codex 사용량 값에 대한 문서화된 공개 API를 제공하지 않습니다. 이 프로그램은 Playwright로 렌더링된 웹페이지를 읽으므로 사이트 구조가 바뀌면 작동하지 않을 수 있으며, 관련 약관이나 정책의 제한을 받을 수 있습니다.

사용 또는 재배포 전에 현재 OpenAI 약관, 정책, 요청 제한 및 계정 규칙을 직접 확인하고 준수해야 합니다. 자동 조회의 기본 및 최소 주기는 10분입니다.

이 프로그램에는 자동화 탐지 우회 플래그, 화면 밖 브라우저 배치 또는 브라우저 창 은폐 로직이 없습니다. 최초 로그인이나 사용자가 명시적으로 요청한 로그인에만 브라우저가 표시되고, 평상시 조회에는 Playwright 표준 headless 모드를 사용합니다. 페이지가 headless 모드에서 정상 렌더링되지 않으면 표시 브라우저를 숨겨 대신 실행하지 않고 조회 실패로 처리합니다.

## 개인정보와 보안

다음 사항은 프로그램을 사용하기 전에 꼭 확인하십시오.

- 이 프로그램은 **개발자 서버나 중계 서버를 사용하지 않습니다.**
- **텔레메트리나 사용 통계 전송 기능이 없습니다.**
- ChatGPT 비밀번호를 읽거나 저장하거나 외부로 전송하지 않습니다.
- 로그인은 사용자가 **ChatGPT/OpenAI 브라우저 페이지에서 직접** 수행합니다.
- 로그인 세션 유지를 위해 로컬 `browser-profile/`에 인증 쿠키와 브라우저 세션 정보가 저장될 수 있습니다.
- 사용량 JSON, 설정 및 로그는 `%LOCALAPPDATA%\CodexUsageMonitor`에 로컬로만 저장됩니다.
- 완전히 삭제하려면 앱을 종료한 뒤 `%LOCALAPPDATA%\CodexUsageMonitor` 폴더를 삭제할 수 있습니다.

`browser-profile/`은 로그인 세션을 포함할 수 있는 개인 데이터입니다. 폴더 전체를 GitHub, 메신저, 버그 보고서 또는 다른 사람에게 공유하지 마십시오.

## 요구 사항

- Windows 10 또는 Windows 11
- `PATH`에서 실행 가능한 Python 3.11 이상
- Windows PowerShell 5.1
- Playwright Chromium

## GitHub Release에서 Windows 배포본 실행

GitHub 저장소의 [Releases](https://github.com/saveway/codex-usage-monitor/releases)에서 Full 또는 Lite 배포본과 같은 이름의 SHA256 파일을 내려받습니다.

```text
CodexUsageMonitor-windows-full.zip
CodexUsageMonitor-windows-full.zip.sha256
CodexUsageMonitor-windows-lite.zip
CodexUsageMonitor-windows-lite.zip.sha256
```

1. 두 파일을 같은 폴더에 저장합니다.
2. PowerShell에서 다음 명령으로 ZIP의 SHA256을 확인합니다.

   ```powershell
   Get-FileHash .\CodexUsageMonitor-windows-full.zip -Algorithm SHA256
   Get-Content .\CodexUsageMonitor-windows-full.zip.sha256
   ```

   Lite를 선택했다면 명령의 `full`을 `lite`로 바꾸십시오.

3. 두 해시가 같은지 확인한 뒤 ZIP을 원하는 폴더에 압축 해제합니다.
4. 압축을 푼 폴더의 `run-codex-usage-tray.bat`를 실행합니다.

Full Windows package에는 PyInstaller로 만든 스크래퍼 exe와 visible 로그인·headless 자동 조회에 사용하는 Playwright Chromium이 포함됩니다. 따라서 다운로드 및 압축 해제 크기가 수백 MB가 될 수 있지만 Python이나 Playwright를 별도로 설치하지 않고 실행할 수 있습니다.

Lite Windows package에는 Chromium과 exe가 포함되지 않으며 공개 소스·스크립트·문서·예제 JSON만 들어 있습니다. 용량은 작지만 사용자가 Python 3.11 이상을 설치한 뒤 압축 해제 폴더에서 다음 명령을 직접 실행해야 합니다.

```powershell
pip install -r requirements.txt
python -m playwright install chromium
```

설치가 끝나면 `run-codex-usage-tray.bat`를 실행합니다. Lite도 실행 후 로그인 세션과 사용량 데이터는 `%LOCALAPPDATA%\CodexUsageMonitor`에 저장합니다.

GitHub Actions에서 artifact를 내려받으면 `CodexUsageMonitor-windows-full` 또는 `CodexUsageMonitor-windows-lite`라는 바깥쪽 ZIP 안에 실제 배포 ZIP과 같은 이름의 SHA256 파일이 들어 있습니다. 바깥쪽 ZIP을 먼저 푼 다음 안쪽 배포 ZIP의 SHA256을 확인하십시오. GitHub Release에서는 네 파일을 직접 내려받습니다.

배포 exe는 코드 서명이 되어 있지 않으므로 Windows SmartScreen 경고가 표시될 수 있습니다. 파일을 신뢰하지 않는다면 경고를 우회하지 말고 아래의 소스 실행 방법을 사용해 직접 실행하십시오.

ZIP 배포본이 저장하거나 전송하는 데이터의 범위는 소스 실행 방식과 같습니다. 개발자 서버나 텔레메트리를 사용하지 않으며, 로그인 세션·사용량·설정·로그는 `%LOCALAPPDATA%\CodexUsageMonitor`에 로컬로 저장됩니다. 앱 종료 후 이 폴더를 삭제하면 배포본이 만든 로컬 데이터를 제거할 수 있습니다.

수동으로 실행한 Actions 빌드는 저장소의 Actions 실행 화면에서 `CodexUsageMonitor-windows-full`과 `CodexUsageMonitor-windows-lite` artifact로 내려받을 수 있습니다. `v*` 태그 빌드는 Full/Lite ZIP과 각 SHA256 파일을 GitHub Release에 자동 첨부합니다.

TODO: Microsoft Edge가 설치된 Windows에서 Playwright `channel="msedge"`를 사용하면 Chromium을 동봉하지 않는 더 간단한 Edge 기반 패키지가 가능합니다. 다만 시스템 Edge 버전과 Playwright 호환성, visible/headless 실행 및 프로필 유지 동작을 충분히 검증한 뒤 별도 배포 방식으로 추가해야 합니다.

## 소스에서 실행

프로젝트 폴더에서 다음 명령을 실행합니다.

```powershell
pip install -r requirements.txt
python -m playwright install chromium
```

실행 파일:

```text
run-codex-usage-tray.bat
```

### Windows 시작 시 자동 실행

```powershell
powershell -ExecutionPolicy Bypass -File .\install-startup.ps1
```

자동 실행 등록만 제거하려면:

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall-startup.ps1
```

## 사용 방법

### 1. 첫 실행과 로그인

1. `run-codex-usage-tray.bat`를 더블클릭합니다.
2. 저장된 로그인 세션이 없으면 ChatGPT 로그인용 브라우저가 열립니다. 이는 비밀번호를 수집하기 위한 창이 아니라, 사용자가 OpenAI 페이지에서 직접 로그인하고 로컬 세션을 만드는 과정입니다.
3. 브라우저에서 ChatGPT 로그인을 완료합니다. 사용량 페이지가 읽히면 브라우저가 닫히고 결과가 로컬 데이터 파일에 저장됩니다.
4. Windows 알림 영역의 Codex 아이콘에 마우스를 올리거나 우클릭해 결과를 확인합니다.

로그인 세션은 `%LOCALAPPDATA%\CodexUsageMonitor\browser-profile`에 유지될 수 있으므로 다음 실행부터는 보통 다시 로그인하지 않습니다. 세션이 만료되거나 OpenAI가 재인증을 요구하면 다시 표시 브라우저에서 로그인해야 합니다.

### 2. 표시되는 값

트레이 툴팁과 위젯에는 수집 가능한 범위에서 다음 값이 표시됩니다.

- 5시간 한도의 남은 비율과 재충전까지 남은 시간
- 주간 한도의 남은 비율과 재충전까지 남은 시간
- 크레딧이 0이 아닐 때 남은 크레딧
- 현재 조회 상태와 마지막으로 저장된 데이터

트레이 아이콘을 더블클릭하면 위젯이 열리고 트레이 아이콘은 숨겨집니다. 위젯을 닫으면 트레이 아이콘이 다시 나타납니다. 위젯은 드래그해 이동할 수 있고, 중앙 Codex 로고를 더블클릭하면 128×128과 256×256 크기를 전환합니다.

### 3. 우클릭 메뉴

트레이 아이콘과 위젯을 우클릭하면 같은 메뉴가 표시됩니다.

- `Reload display`: 이미 로컬에 저장된 데이터를 다시 읽어 화면만 갱신합니다. 웹페이지를 새로 조회하지 않습니다.
- `Fetch now (visible browser)`: 표시 브라우저를 열어 지금 즉시 사용량을 다시 조회합니다.
- `Acknowledge zero alert`: 잔여량 0% 깜빡임을 확인하고 중지합니다.
- `Show widget`: 트레이 모드에서 위젯을 표시합니다.
- `Graph style`: Rings, Bars, Meters, Battery 중 그래프 형태를 선택합니다.
- `Colors`: 5시간, 주간 및 인터페이스 색상을 바꾸거나 기본값으로 복원합니다.
- `Check interval`: 자동 조회 주기를 10, 15, 30 또는 60분으로 변경합니다.
- `Open usage page`: 원본 ChatGPT Codex 사용량 페이지를 엽니다.
- `Open data file`: 현재 로컬 사용량 JSON을 메모장으로 엽니다.
- `Open scraper log`: 수집기 로그를 메모장으로 엽니다.
- `Exit`: 프로그램을 종료합니다.

### 4. 수동 조회가 필요한 경우

다음 상황에서는 `Fetch now (visible browser)`를 사용하십시오.

1. 처음 실행했지만 데이터가 아직 없는 경우
2. 로그인 세션이 만료되어 재로그인이 필요한 경우
3. 자동 headless 조회가 반복해서 실패하는 경우
4. 다음 자동 조회를 기다리지 않고 즉시 값을 확인하려는 경우

수동 조회 중 열린 브라우저에서 필요한 로그인이나 인증을 직접 완료하십시오. 조회에 실패하면 프로세스 종료 코드와 수집기 로그 경로가 알림으로 표시되며, 위젯이 열려 있으면 경고 대화상자도 표시됩니다.

### 5. 상태 메시지의 의미

- `No data yet`: 아직 성공적으로 저장된 사용량 데이터가 없습니다.
- `Login or Fetch now required`: 로그인 또는 표시 브라우저를 통한 수동 조회가 필요합니다.
- `error`: 최근 조회가 실패했습니다. 네트워크, 로그인 만료, 페이지 구조 변경 또는 headless 렌더링 문제가 원인일 수 있습니다. `Open scraper log`로 원인을 확인한 뒤 `Fetch now (visible browser)`를 실행하십시오.

### 6. 자동 갱신 주기 변경

트레이 아이콘 또는 위젯을 우클릭하고 `Check interval`에서 10, 15, 30, 60분 중 하나를 선택합니다. 선택 즉시 저장되고 백그라운드 수집기가 새 주기로 다시 시작됩니다. 사이트와 계정에 불필요한 요청을 만들지 않도록 10분보다 짧은 주기는 제공하지 않습니다.

### 7. 로그 확인

우클릭 메뉴의 `Open scraper log`에서 웹페이지 조회 및 로그인 관련 오류를 확인할 수 있습니다. 트레이 앱 자체의 시작·UI 오류는 다음 파일에 기록됩니다.

```text
%LOCALAPPDATA%\CodexUsageMonitor\codex-usage-scraper.log
%LOCALAPPDATA%\CodexUsageMonitor\codex-usage-tray.log
```

실행 직후 트레이 아이콘이 나타나지 않으면 `debug-codex-usage-tray.bat`로 오류를 확인할 수 있습니다. 로그에는 페이지 상태나 로컬 경로가 포함될 수 있으므로 공개 전에 내용을 검토하십시오.

### 8. 완전 삭제

1. 트레이 또는 위젯 우클릭 메뉴에서 `Exit`를 선택합니다.
2. 자동 실행을 등록했다면 `uninstall-startup.ps1`을 실행합니다.
3. 프로그램 소스 폴더를 삭제합니다.
4. 저장된 로그인 세션, 사용량, 설정과 로그까지 지우려면 `%LOCALAPPDATA%\CodexUsageMonitor` 폴더를 삭제합니다.

마지막 폴더를 삭제하면 로컬 `browser-profile`의 인증 쿠키도 함께 제거됩니다.

## 로컬 데이터

변경 가능한 모든 데이터는 소스 저장소 밖의 다음 위치에 저장됩니다.

```text
%LOCALAPPDATA%\CodexUsageMonitor
```

주요 내용:

- `browser-profile/`: 인증 쿠키와 브라우저 세션 상태
- `codex-usage.json`: 현재 사용량 및 크레딧 값
- `codex-usage-settings.json`: 화면 색상, 그래프 및 조회 주기 설정
- `codex-usage-ack.json`: 경고 확인 상태
- `*.log`, `*.lock`, `*.pid`: 실행 및 프로세스 관리 파일

이 디렉터리 전체를 개인 데이터로 취급하십시오.

## 디버그 캡처

전체 페이지 텍스트 캡처는 기본적으로 꺼져 있습니다. 로컬 디버깅이 꼭 필요할 때만 명시적으로 활성화할 수 있습니다.

```powershell
$env:CODEX_USAGE_DEBUG_CAPTURE = '1'
```

저장 시 숫자를 제거하고 이메일·계정 관련 줄을 마스킹하지만 민감한 페이지 문구가 남을 수 있습니다. 공유 전에 직접 검토하고 디버깅이 끝나면 환경 변수를 제거하십시오.

## 공개하면 안 되는 파일

다음 파일과 폴더를 커밋하거나 배포하지 마십시오.

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

`*.example.json` 파일만 공개 예제로 사용합니다.

## 개발 및 보안 검사

공개 전에 현재 파일 트리를 검사하십시오. `rg`(ripgrep)는 Windows나 PowerShell에 기본 포함되지 않는 별도 도구입니다. 첫 번째 명령을 사용하려면 별도로 설치하십시오. 예: `winget install BurntSushi.ripgrep.MSVC`

```powershell
rg -n -i "api[_-]?key|access[_-]?token|refresh[_-]?token|authorization|bearer|cookie|session" .
Get-ChildItem -Force -Recurse | Where-Object { $_.FullName -match 'browser-profile|CodexUsageMonitor|\.log$|page-text' }
```

`rg`가 없다면 PowerShell 기본 명령을 사용할 수 있습니다.

```powershell
Get-ChildItem -Force -Recurse -File | Select-String -Pattern 'api[_-]?key|access[_-]?token|refresh[_-]?token|authorization|bearer|cookie|session' -CaseSensitive:$false
```

Git 저장소를 만든 뒤에는 push 전에 모든 revision을 확인하십시오.

```powershell
git log --all --oneline
git rev-list --objects --all
git log -p --all -G "api[_-]?key|access[_-]?token|refresh[_-]?token|authorization|bearer|cookie|session"
```

공개 전 Gitleaks 또는 TruffleHog 같은 전용 검사 도구를 함께 사용하는 것을 권장합니다.

## 제한 사항

- 파서는 현재 ChatGPT 페이지 구조와 언어 표시에 의존합니다.
- 사이트 동작에 따라 headless 브라우저 렌더링이 실패할 수 있습니다.
- 로그인 세션이 만료되면 사용자가 직접 다시 로그인해야 합니다.
- 공식 사용량 API를 제공하지 않으며 지속적인 호환성을 보장하지 않습니다.

## 라이선스

MIT License입니다. [LICENSE](LICENSE)를 확인하십시오.
