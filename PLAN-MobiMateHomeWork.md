# MobiMateHomeWork — 숙제 전용 데스크톱 앱 계획서

> 브랜치 `MobiMateHomeWork` (webapp `e632bbb`에서 분기), 작업 폴더 `V:\workspace\MobiMate\MobiMateHomeWork`.
> 계획 = Opus, 구현·검증 = Sonnet. 작성 2026-10-11.

## 1. 목표와 범위

웹앱(MobiMate Web)에서 **숙제 탭만** 떼어 낸 Windows 데스크톱 앱을 만든다.

- 실행 파일 `MobiMateHomeWork.exe` (단일 파일, .NET 8 데스크톱 런타임 필요)
- 화면은 숙제 하나뿐이지만, 숙제 판정에 필요한 **캐릭터 식별·기록·계정 묶기·자동 판정 로직은 웹앱 것을 그대로** 가져온다
- UI 기술 = WPF(.NET 8). 웹 서버·브라우저·LAN·SSE 없음
- 창 크기 = **1280×720 고정** (UI를 보고 다시 정할 수 있음)
- 화면 기준: **한 계정(최대 6캐릭터)은 스크롤 없이 한눈에**, 그보다 많으면 세로 스크롤

### 범위 밖 (가져오지 않음)

AI 엔진·Ollama·채팅·아무말 대잔치(페르소나), 랭킹·뱃지, 컷오프(어비스·레이드 추천), 생활(가공·채집), 인벤토리, 주변 플레이어, 통계, LAN·mDNS·방화벽·기기 페어링·인증, 클래스 전신 이미지 내려받기(P2로 미룸).

## 2. 주요 결정

| 항목 | 결정 | 이유 |
|---|---|---|
| 기준 브랜치 | `webapp` | 숙제 판정이 가장 최신(레이드 증표·뱅가드·필드보스·요일던전 연속 관찰). Core가 UI 의존 0이라 그대로 쓸 수 있다. master(WPF판)의 숙제 로직은 v1.1 수준 |
| UI 프레임워크 | WPF `net8.0-windows` | 설치된 SDK가 8.0.425뿐. WinUI 3는 배포가 무겁다 |
| 패턴 | MVVM — `CommunityToolkit.Mvvm` | 소스 생성기로 코드가 짧다 |
| 호스팅 | `Microsoft.Extensions.Hosting` (Generic Host) | 웹앱의 `BackgroundService`(상태 감시·기록·캐릭터 전환 감시)를 거의 그대로 옮길 수 있다. DI·로깅도 같은 방식 |
| 테마 | 외부 UI 라이브러리 없이 직접 만든 다크 테마 ResourceDictionary | 의존성 0, 1280×720 고정이라 반응형 부담이 없다 |
| 글꼴 | Pretendard(OFL) 내장, 없으면 맑은 고딕 | 한글 숫자 정렬(`tnum`)·가독성 |
| 창 | `WindowStyle=None` + `WindowChrome` 직접 그린 제목 표시줄, `ResizeMode=CanMinimize`, 내용 영역 1280×720 DIP | 고정 크기, 깔끔한 외형 |
| 저장 폴더 | `%APPDATA%\MobiMateHomeWork` | 프로젝트 분리 원칙. 첫 실행 때 비어 있으면 `%APPDATA%\MobiMateWeb`에서 **복사만** 한다(웹앱 폴더에 쓰지 않음) |
| 버전 | `1.0.MMdd.HHmm` (웹앱과 같은 빌드 시각 방식) | |
| 배포 | `dotnet publish -c Release` → `MobiMateHomeWork.exe` + `.zip` | 메신저 전송용 |

## 3. 브랜치·폴더 운영

- `MobiMateHomeWork` 브랜치가 이 서브 프로젝트의 줄기다. **webapp·master와 서로 병합하지 않는다**(어느 방향이든). 웹앱 Core의 숙제 판정이 나중에 바뀌면 필요한 커밋만 `cherry-pick`하거나 손으로 옮긴다.
- 직접 커밋하지 않는다: `hw/feat-…`, `hw/fix-…` 작업 브랜치 → `MobiMateHomeWork` 병합 → Gitea(`origin`) 푸시.
- 작업 폴더 `V:\workspace\MobiMate\MobiMateHomeWork` (worktree, `core.bare false` 설정 완료). 백그라운드 작업은 별도 worktree를 준다.
- 브랜치 안의 `CLAUDE.md`를 이 프로젝트용으로 바꾼다(H0).

### 폴더 구성 (H0 이후)

```
CLAUDE.md, README.md, PLAN-MobiMateHomeWork.md
docs/                         공유 요구사양·CLI 스펙 사본 (그대로)
hw/
  MobiMateHomeWork.sln
  src/MobiMate.Core/          웹앱 Core에서 이동(git mv) 후 가지치기
  src/MobiMateHomeWork/       WPF 앱 (exe)
  tests/MobiMate.Core.Tests/  남은 Core 테스트
  tests/MobiMateHomeWork.Tests/  호스트 서비스·ViewModel 테스트
  tools/FakeCli/              가짜 게임 CLI (테스트·화면 점검용, 그대로)
```

`web/` 나머지(서버·React·E2E·웹 테스트·디자인 시안)는 이 브랜치에서 지운다. 웹앱 브랜치에는 그대로 남으므로 되돌릴 수 있다.

## 4. 가져올 것 / 버릴 것

### Core (`MobiMate.Core`) — 남기는 것

| 폴더 | 내용 | 비고 |
|---|---|---|
| `Homework/` | Catalog(+json), Evaluator, Service, Records(Store·WPF 가져오기), KstClock, CurrencyDeltaLog | 핵심. 손대지 않는다 |
| `Account/` | AccountGrouping, CoinForecast | 계정 묶기. CoinForecast는 은동전·공물 표시를 안 하면 빼도 됨(보류: 우선 남김) |
| `Game/` | CharacterIdentity, CombatScoreGuard, GameStateCache, ScoreReading, DisplayFormat | 캐릭터 식별·전투력 재확인 |
| `Storage/` | SnapshotManager, JsonFileStore | 페르소나 저장(custom_personas) 부분만 떼어 낸다 |
| `Cli/` | GameCli | |
| `Models/` | GameModels | |

### Core — 지우는 것

`Ai/`, `Chat/`, `Chatter/`(PersonaTemplatesData 1,126줄 포함), `Intent/`, `Ranking/`, `Cutoff/`(+json), `Life/`(+json), `Refresh/`, `Game/NearbyRanker`. 해당 테스트(AiEngine, ChatPlan, InGameChatter, InteractiveFeatures, Ranking, Cutoff, WorkCategory)도 지운다.

### Web 계층 → 앱 서비스로 옮길 로직 (중요)

웹앱의 숙제 판정은 Core만으로 돌지 않는다. **캐릭터를 정하고 기록하는 연결부가 `MobiMate.Web`에 있다.** 이것을 빠뜨리면 캐릭터가 섞이거나 판정이 다른 캐릭터에 기록된다.

| 웹앱 원본 | 새 위치 (`MobiMateHomeWork/Services`) | 바꿀 점 |
|---|---|---|
| `Infrastructure/QueryCache.cs` (QueryCache·GameQueries) | 그대로 | 없음 |
| `Services/StatusMonitor.cs` | 그대로 | SSE 대신 `AppEvents.StatusChanged` |
| `Services/GameViews.cs` 중 `HeaderAsync`·`ResolveKeyAsync`·`Stabilize`·`KeepTitle`·`RecheckCombatAsync`·`IsMixedRead`·`CurrenciesAsync`·`RecordNowAsync`·`Characters()` | `CharacterSession.cs` | 화면용 익명 객체 대신 형식 있는 레코드(`AccountView`·`CharacterView`). 랭킹·컷오프·생활·인벤토리 부분은 버린다. 캐릭터 선택창·재화 섞임 처리는 **그대로** |
| `Services/GameActions.cs` 중 `HomeworkWatcher` | `HomeworkWatcher.cs` | SSE 대신 `AppEvents.HomeworkChanged` |
| `Services/CharacterRecorder.cs`, `CharacterWatcher.cs` | 그대로 | 없음 (BackgroundService) |
| `Hosting/AccountStore.cs`, `FileLogger.cs`, `MobiMateOptions.cs` | 그대로 / 줄여서 | 옵션에서 포트·LAN·mDNS·SSE 제거 |
| `Hosting/WebSettings.cs` | `AppSettings.cs` | CLI 경로만 |
| `Hosting/Tray.cs` | `TrayIcon.cs` | 트레이 메뉴: 열기 / 종료 |
| `Program.cs`의 단일 인스턴스·첫 실행 가져오기·DI 등록 | `App.xaml.cs` | 이미 실행 중이면 기존 창을 앞으로 |
| `GameEndpoints`의 `PUT /homework/{id}` 처리 (현재 캐릭터면 키 생략 등) | `HomeworkCommands.cs` | API 대신 메서드 호출 |
| 화면 `homework.tsx`의 `HW_GROUPS`·`cellsOf`·필드보스 해제 규칙 | `HomeworkBoardViewModel` | 같은 규칙. 필드보스 완료→미완료는 넘긴 보스만, 이름 모르면 여섯 모두 해제 |

`AppEvents`는 단순 C# 이벤트 묶음이다. ViewModel은 이벤트를 받으면 `Dispatcher`로 넘겨 보드를 다시 만든다(연속 이벤트는 200ms 묶음 처리).

## 5. 화면 설계 (1280×720 DIP)

```
┌──────────────────────────────────────────────────────────────────────────────┐ 40
│ ◆ MobiMate 숙제   ● 게임 연결됨          일일 리셋 5시간 12분 · 주간 2일 5시간  ─ ✕│
├───────────────┬──────────────────────────────────────────────────────────────┤
│ 계정           │  OO 계정  · 접속 중                    남은 숙제 17개   ⟳      │ 52
│ ▶ OO   17 남음 │ ─────────────────────────────────────────────────────────────│
│   △△    4 남음 │ [직업] 닉네임  ● 접속중 │일일·주간│ 레이드  │ 어비스   │필드│ 7/10 │
│   □□   완료   │ 기사 Lv.85 · 전투력 2.1만│요일 길드 뱅가│카브 에이 화이│허상 광기 흩어│보스│      │ 96
│   ◇◇    9 남음 │ 마지막 접속 3시간 전     │ ✓  ○  남2 │ ✓  ·  ○ │ ○  ✓  · │✓페리│      │
│               │ ─ (최대 6줄, 그 이상은 이 영역만 스크롤) ─                      │ ×6
│               │                                                              │
│ ⚙ 설정        │                                                              │
├───────────────┴──────────────────────────────────────────────────────────────┤
│ 마지막 갱신 12초 전 · 칸을 누르면 완료↔미완료 · ✓완료 ○미완료 ·미확인          │ 28
└──────────────────────────────────────────────────────────────────────────────┘
   220                                   1060
```

높이: 제목 40 + 계정 머리 52 + 캐릭터 6줄×96 = 576 + 하단 28 → **696 ≤ 720**.

- **왼쪽 계정 목록(220)**: 계정 이름(대표 캐릭터 닉네임), 남은 숙제 수, 접속 중 표시. 접속 중인 계정이 맨 위·기본 선택. 계정이 하나뿐이면 목록은 그대로 두되 자동 선택
- **오른쪽 계정 보드(1060)**: 그 계정 캐릭터를 전투력 순으로. 6명을 넘으면 이 영역만 스크롤
- **캐릭터 줄(96)**: 왼쪽 정보 240 — 직업 뱃지(직업 색 + 첫 글자), 닉네임(없으면 서버명), 직업·레벨·전투력, 마지막 접속. 오른쪽 칸 10개(일일·주간 3 · 레이드 3 · 어비스 3 · 필드보스 1)를 묶음별 간격으로, 칸 하나 약 76×72. 끝에 `7/10`
- **칸 상태**: 완료(채운 초록 + ✓) / 미완료(외곽선 + ○, 뱅가드는 "남은 N회") / 미확인(흐림 + ·). 진행 중 신호(보스 교전·공간 입장)는 칸 테두리 맥동. 레이드 완료 제안(수동 미완료인데 증표 증가)은 칸 모서리 점 + 툴팁
- **칸 클릭** = 완료↔미완료 수동 전환. 필드보스 칸은 완료로 바꿀 때 보스 6개 고르기 팝업
- **우클릭 메뉴(캐릭터 정보 부분)**: 닉네임 바꾸기. (캐릭터 삭제·계정 옮기기는 P2)
- **모두 완료한 줄**: 은은하게 접고 "모두 완료" 표시
- 툴팁: 칸마다 판정 근거(Evidence) — "주간 미션 '…' 1/1" 등. 수동 체크는 "수동"
- 게임 미연결·CLI 없음: 제목 표시줄 점 색 + 하단 안내. 기록된 값은 계속 보인다
- 설정(팝업): 게임 CLI 경로, 시작 시 트레이로, Windows 시작 시 실행(P2)
- 닫기(✕) = 트레이로 숨김(관찰은 계속, 요일던전은 "보였다가 사라짐"을 지켜봐야 판정된다). 트레이 메뉴에서 종료

### DPI 주의

1280×720은 **논리 단위(DIP)** 다. Windows 배율 150%면 실제 1920×1080 픽셀이 되어 FHD 모니터를 꽉 채운다. 承雲 PC 배율을 확인해 필요하면 H6에서 크기를 다시 정한다(예: 배율 125%면 1600×900 픽셀).

## 6. 단계 계획

각 단계 = Sonnet 구현 → Sonnet 리뷰 → 테스트 → 리뷰. 단계마다 작업 브랜치 하나, 끝나면 `MobiMateHomeWork`에 병합·푸시.

| 단계 | 내용 | 완료 기준 |
|---|---|---|
| **H0** 뼈대 정리 | `web/src/MobiMate.Core`·`tests/MobiMate.Core.Tests`·`tools/FakeCli`를 `hw/`로 `git mv`. 나머지 `web/` 삭제. `hw/MobiMateHomeWork.sln`. CLAUDE.md·README를 이 프로젝트용으로 | `dotnet build`·`dotnet test` 통과 (Core 테스트 전부) |
| **H1** Core 가지치기 | §4의 지울 폴더·테스트 제거, SnapshotManager에서 페르소나 분리, 저장 폴더 기본값 `MobiMateHomeWork`·가져오기 원본 `MobiMateWeb`로 설정화, CurrencyDeltaLog 주석 경로 | 경고 0, 남은 Core 테스트 전부 통과(숙제 106개·레이드 15개·계정 25개 등 손실 0) |
| **H2** 앱 서비스 이식 | WPF 프로젝트 생성(빈 창), Generic Host, §4 표의 서비스 이식, `AppEvents`, 첫 실행 데이터 복사, 단일 인스턴스, 파일 로그 | 새 테스트 프로젝트에서 웹 테스트(CharacterSelect·CharacterWatcher·CombatRecheck·Views·Accounts 중 숙제·캐릭터 관련)를 FakeCli로 옮겨 통과 |
| **H3** 테마·창 | 색·글꼴·컨트롤 스타일 ResourceDictionary, 1280×720 고정 창, 제목 표시줄, 하단 상태줄, 트레이 | 빈 레이아웃이 디자인대로 보임(스크린샷 확인) |
| **H4** 숙제 보드 | 계정 목록, 계정 보드, 캐릭터 줄, 칸, 클릭 전환, 필드보스 팝업, 리셋 카운트다운, 툴팁, 닉네임 바꾸기, 실시간 갱신 | ViewModel 테스트(칸 만들기·전환·필드보스 해제·정렬·6명 초과 스크롤). FakeCli로 앱을 띄워 스크린샷 점검 |
| **H5** 배포 | 아이콘, 버전 도장, publish 단일 exe + zip, 설정 팝업(CLI 경로) | `hw/dist/MobiMateHomeWork.exe` 실행, 웹앱과 다른 저장 폴더 사용 확인 |
| **H6** 실기 점검 | 실제 게임으로 체크리스트: 캐릭터 전환·선택창·요일던전 사라짐·뱅가드 남은 횟수·레이드 증표·필드보스·리셋 경계(06:00)·트레이 상주 | 承雲 확인 후 해상도 재검토 |

### 테스트 전략

- Core: 기존 테스트를 그대로 유지 — 판정 로직은 바꾸지 않으므로 회귀 기준이 된다
- 앱 서비스: `FakeCli`(웹앱 E2E에서 쓰던 가짜 CLI)와 임시 저장 폴더로 실제 흐름 검증
- ViewModel: UI 없이 단위 테스트(xUnit). WPF 컨트롤은 테스트하지 않는다
- 화면: FakeCli로 앱을 띄워 스크린샷으로 확인(computer-use). 자동 UI 테스트(FlaUI)는 하지 않는다 — 고정 크기 단일 화면이라 비용 대비 효과가 낮다

## 7. 위험과 대응

| 위험 | 대응 |
|---|---|
| 웹앱과 동시에 켜면 게임 CLI를 두 앱이 함께 부르고, 숙제 기록이 두 폴더로 갈라진다 | 시작할 때 `MobiMateWeb.exe`가 떠 있으면 안내만 한다(막지 않음). 기록 공유는 하지 않는다(아래 열린 질문 ①) |
| 연결부 이식 누락 → 캐릭터 섞임 | §4 표를 체크리스트로 쓰고, 웹 테스트를 옮겨 같은 시나리오를 통과시킨다 |
| 트레이 없이 창을 닫으면 관찰이 멈춰 요일던전 판정을 놓침 | 닫기 = 트레이 숨김이 기본 |
| 웹앱 Core가 이후 바뀜 | 숙제 관련 커밋만 골라 옮긴다. 판정 파일(`Homework/`)은 이 브랜치에서 가급적 고치지 않는다 |
| DPI 배율로 창이 화면보다 큼 | H6에서 실측, 필요하면 배율 보정 또는 크기 조정 |

## 8. 열린 질문 (承雲 결정 필요, 기본값으로 먼저 진행)

1. **웹앱과 숙제 기록을 공유할까?** 기본값: 공유하지 않음(첫 실행 때 한 번 복사). 두 앱을 번갈아 쓸 생각이면 공유 폴더 방식이 필요하다
2. **은동전·마족 공물 예상 보유량**을 캐릭터 줄에 넣을까? 기본값: 넣지 않음(숙제 전용). 넣는다면 정보 칸 둘째 줄에 작게
3. **직업 이미지**: 기본값 직업 색 뱃지 + 첫 글자. 웹앱처럼 직업 이미지를 원하면 P2

## 9. 모델 배정

- 계획(이 문서)·단계 경계의 판단 = Opus
- 구현·테스트·리뷰 = Sonnet. 단계마다 Sonnet 서브에이전트를 별도 작업 브랜치에서 돌리고, 결과를 코드로 확인한 뒤 병합한다
