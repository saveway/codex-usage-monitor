# Codex Usage Monitor V2 - WebView2 네이티브 Preview

[English document](README-native.en.md)

이 폴더는 Python, Playwright, Chromium 동봉 없이 Microsoft Edge WebView2 Runtime을 사용하는 Windows 네이티브 Preview 앱입니다. 기존 v1 Full/Lite 배포본을 즉시 대체하는 안정판은 아니며, v1 소스, Release, 태그, workflow와 독립적으로 유지됩니다.

## 현재 상태

- 최신 공개 v2 Release: [v2.0.0-preview.7](https://github.com/saveway/codex-usage-monitor/releases/tag/v2.0.0-preview.7)
- 현재 `main`에는 preview.7 이후 수정도 포함되어 있습니다.
- preview.7 이후 `main`에 추가된 내용: 256x256 위젯의 animated GIF 센터 로고 선택, animated 모드에서 그래프 채움 애니메이션, Windows balloon 알림 On/Off 메뉴, 기본 알림 Off 정책.
- 새 Release가 만들어지기 전까지 GitHub Release ZIP에는 preview.7 기준 기능만 포함됩니다. 최신 main 기능은 소스 빌드 또는 다음 v2 preview Release에서 사용할 수 있습니다.

## 설계 판단

- UI: C# WinForms, .NET Framework 4.8 (`net48`, x64)
- 브라우저: Microsoft Edge WebView2 Evergreen Runtime
- 빌드: .NET SDK 8.x 및 NuGet 패키지
- 배포: framework-dependent EXE, WebView2 managed/native loader DLL, README, LICENSE
- 브라우저 엔진: Chromium을 동봉하지 않고 Windows의 WebView2 Runtime 사용
- 로컬 상태: `%LOCALAPPDATA%\CodexUsageMonitorV2`
- 외부 서버/텔레메트리: 없음

Windows 10/11에는 WebView2 Runtime이 이미 설치된 경우가 많지만, 없는 경우 앱은 사용자에게 Runtime 설치 안내를 표시합니다.

## 주요 기능

- 트레이 아이콘과 WinForms 위젯으로 Codex 사용량 표시
- `Open/Login usage page`로 실제 ChatGPT/OpenAI 페이지를 WebView2 창에 표시
- 사용자가 실제 로그인 페이지에서 직접 로그인
- 동일한 로컬 WebView2 프로필로 로그인 세션 유지
- `Fetch now`는 사용자가 만든 로컬 WebView2 세션으로 usage 페이지를 읽고 저장
- 로그인 필요 상태에서는 자동 로그인 폼이나 토큰 처리를 하지 않고 사용자가 `Open/Login usage page`를 누르도록 안내
- 일반 Codex 5시간/주간 한도와 GPT-5.3-Codex-Spark 한도를 분리 파싱
- 툴팁과 위젯의 `5H`/`W`는 일반 Codex 한도 기준
- 일반 Codex reset 시간과 credits 파싱
- 0% 잔여량 zero alert 및 acknowledge 메뉴
- 잔여율 단계별 색상과 21개 색상 사용자 설정
- 동적 트레이 아이콘
- 위젯 그래프 스타일: Rings, Bars, Meters, Battery
- 위젯 드래그 이동, 128x128/256x256 크기 전환, 위치/크기/표시 여부 저장
- 위젯은 작업표시줄에 표시하지 않는 툴윈도우 형태를 유지하면서, Windows 가상 데스크톱 전체 표시를 best-effort로 시도
- `Center logo` 메뉴: 정적 Codex.exe 아이콘 또는 animated GIF 선택
- animated GIF 로고는 256x256 위젯에서만 표시, 128x128은 compact 유지를 위해 정적 아이콘 사용
- animated 모드에서는 그래프가 0%에서 현재 남은량까지 채워진 뒤 잠깐 멈추고 반복
- `Balloon notifications` 메뉴: 사용량 갱신과 UI 동작 balloon 알림 On/Off
- balloon 알림 기본값 Off. Off여도 툴팁, 위젯, 로그, JSON 저장은 계속 갱신
- `Auto refresh` 메뉴: Off, 10분, 15분, 30분, 60분. 기본 Off, 10분보다 짧은 주기 없음
- `Clear WebView2 cache`는 로그인 세션 후보(Cookies, Local Storage, IndexedDB, Session Storage)를 삭제하지 않고 캐시성 데이터만 정리

## 실행 방법

GitHub Release ZIP을 사용하는 경우:

1. `CodexUsageMonitorV2-windows-webview2.zip`과 `.sha256` 파일을 내려받습니다.
2. ZIP을 임시 폴더나 압축파일 내부에서 바로 실행하지 말고, 계속 사용할 영구 폴더에 압축 해제합니다.
3. `CodexUsageMonitorV2.exe`를 실행합니다.
4. 트레이 아이콘을 우클릭하고 `Open/Login usage page`를 선택해 실제 ChatGPT/OpenAI 페이지에서 로그인합니다.
5. 로그인 후 `Fetch now`를 실행해 사용량을 읽습니다.

소스에서 실행하는 경우:

```powershell
cd native-webview2
dotnet restore
dotnet build -c Release
.\bin\Release\net48\deploy\CodexUsageMonitorV2.exe
```

배포 최소 파일은 `bin\Release\net48\deploy\`에 생성됩니다.

## 메뉴

- `Open/Login usage page`: 실제 usage/login 페이지를 표시합니다.
- `Fetch now`: 표시 창을 열지 않고 현재 세션으로 사용량을 갱신합니다. 로그인 필요 시 자동으로 로그인 창을 열지 않습니다.
- `Auto refresh`: 자동 조회 주기를 선택합니다.
- `Colors`: 색상을 개별 선택하거나 기본값으로 복원합니다.
- `Graph style`: Rings, Bars, Meters, Battery 중 위젯 그래프를 선택합니다.
- `Center logo`: 정적 아이콘 또는 256x256 animated GIF 로고를 선택합니다.
- `Balloon notifications`: Windows balloon 알림을 켜거나 끕니다. 기본값은 Off입니다.
- `Show widget`: 위젯을 표시합니다.
- `Acknowledge current alert`: 현재 0% 잔여량 경고를 확인 처리합니다.
- `Reload saved data`: 웹페이지를 열지 않고 로컬 JSON만 다시 읽습니다.
- `Open data file`: 로컬 사용량 JSON을 엽니다.
- `Open log`: v2 로그를 엽니다.
- `Clear WebView2 cache`: 로그인 저장소는 보존하고 캐시성 파일만 정리합니다.
- `About`: 앱 이름, preview 버전, 비공식 도구 고지, 로컬 데이터 위치, 저장소 주소를 표시합니다.
- `Exit`: 앱을 종료합니다.

## 로컬 데이터와 개인정보

v2는 다음 폴더에만 상태를 저장합니다.

```text
%LOCALAPPDATA%\CodexUsageMonitorV2
```

주요 파일과 폴더:

- `webview2-profile\`: WebView2 로그인 세션 프로필. 인증 쿠키가 포함될 수 있습니다.
- `codex-usage.json`: 최근 파싱된 사용량 데이터
- `codex-usage-settings.json`: 색상, 그래프 스타일, 위젯 위치/크기, 자동 조회, 알림 설정
- `codex-usage-monitor-v2.log`: 앱 로그
- `codex-usage-debug-status.txt`: 파싱 실패 시 제한된 진단 정보

이 폴더는 개인 계정 데이터로 취급해야 합니다. GitHub, 메신저, 버그 리포트 등에 업로드하지 마십시오. 앱 종료 후 이 폴더를 삭제하면 v2 로컬 상태가 초기화되며 다음 실행 시 다시 로그인이 필요합니다.

## 보안 원칙

- ChatGPT 비밀번호를 앱 자체 UI로 입력받지 않습니다.
- 로그인은 실제 OpenAI/ChatGPT 페이지에서 직접 수행합니다.
- 쿠키, 토큰, 전체 페이지 원문, 계정 정보, 사용량 데이터를 개발자 서버로 전송하지 않습니다.
- 텔레메트리가 없습니다.
- 비공식 내부 API 토큰 호출을 하지 않습니다.
- 자동화 탐지 우회 플래그, 쿠키 탈취, 외부 서버 중계, 숨김 로그인 폼을 사용하지 않습니다.
- 파싱 실패 진단 파일에는 전체 페이지 텍스트나 실제 숫자를 저장하지 않습니다.

이 프로젝트는 OpenAI 공식 앱이 아닙니다. 웹페이지 파싱은 사이트 문구와 레이아웃 변경에 취약하며, 사용자는 관련 약관과 계정 정책을 직접 확인하고 준수해야 합니다.

## v1과의 차이

| 항목 | v1 Stable | v2 WebView2 Preview |
|---|---|---|
| 기술 | Python, PowerShell, Playwright | C# WinForms, WebView2 |
| 브라우저 | Full은 Chromium 동봉, Lite는 별도 설치 | Windows WebView2 Runtime 사용 |
| 다운로드 크기 | Full은 수백 MB 가능 | 약 0.3 MB 수준의 ZIP |
| 설치 난이도 | Full은 쉬움, Lite는 Python 필요 | WebView2 Runtime이 있으면 간단 |
| 안정성 | 기존 안정 배포 | 아직 preview |
| 위젯 | 성숙한 v1 기능 | 핵심 기능을 단계적으로 이식 중 |

v2가 동작하지 않거나 안정성이 더 중요하면 v1 Stable을 사용하십시오.

## GitHub Actions와 Release

- v1 workflow는 `v1.*` 태그에만 반응합니다.
- v2 workflow는 `v2.*-preview.*` 태그에만 반응합니다.
- 수동 workflow 실행은 artifact만 만들 수 있습니다.
- v2 preview 태그를 push하면 prerelease가 생성되고 `CodexUsageMonitorV2-windows-webview2.zip`과 `.sha256`이 첨부됩니다.

GitHub Actions artifact는 바깥 ZIP 안에 실제 배포 ZIP과 SHA256 파일이 들어 있을 수 있습니다.

## 아직 지원하지 않는 것

- 설치 프로그램
- Windows 시작프로그램 자동 등록
- 코드 서명
- 정식 stable v2 Release
- 다국어 UI
- 인증 계정에 대한 완전 자동 E2E 테스트

## 검증 상태

현재 개발 PC 기준으로 다음을 확인했습니다.

- Release 빌드 경고 0, 오류 0
- WebView2 Runtime 감지
- 실제 ChatGPT 로그인 페이지 표시
- 로그인 세션 유지
- 사용량 페이지 접근 및 파싱
- 일반 Codex/Spark 한도 분리
- reset/credits 파싱
- Fetch now 숨김 갱신
- 0% zero alert와 acknowledge
- 4종 위젯 그래프
- Codex.exe 아이콘 및 animated GIF 로고
- animated 모드 그래프 채움 애니메이션
- balloon 알림 기본 Off
- 캐시 정리 후 세션 유지
- Exit 후 잔여 프로세스 없음
- v1 파일과 v1 Release 구조에 영향 없음
