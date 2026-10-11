# MobiMateHomeWork — 숙제 전용 데스크톱 앱 계획서 (v2)

> 브랜치 `MobiMateHomeWork` (webapp `e632bbb`에서 분기), 작업 폴더 `V:\workspace\MobiMate\MobiMateHomeWork`.
> **진행: S0 완료(webapp e4963b5), H0·H1·H2a·H2b·H3 완료 — 다음 H4.**
> 계획 = Opus, 구현·검증 = Sonnet. v1 2026-10-11, v2 2026-10-11 (承雲 답변 반영: 기록 공유, 캐릭터 정보 12항목, 창 크기 = 화면 4분할).

## 1. 목표와 범위

웹앱(MobiMate Web)에서 **숙제 탭만** 떼어 낸 Windows 데스크톱 앱을 만든다.

- 실행 파일 `MobiMateHomeWork.exe` (단일 파일, .NET 8 데스크톱 런타임 필요)
- 화면은 숙제 하나지만, 숙제 판정에 필요한 **캐릭터 식별·기록·계정 묶기·자동 판정 로직은 웹앱 것을 그대로** 가져온다
- UI = WPF(.NET 8). 웹 서버·브라우저·LAN·SSE 없음
- **창 크기 = Windows 창 맞춤(Snap) 4분할에 딱 맞게.** FHD·QHD 모니터에서 게임 옆에 띄워 두는 용도. 고정 해상도가 아니라 **4분할 크기 범위(약 768×413 ~ 1280×696 DIP)에서 보기 좋게 바뀌는 반응형**
- **한 계정(최대 6캐릭터)은 어느 4분할 크기에서도 스크롤 없이 한눈에**, 그보다 많으면 세로 스크롤
- **캐릭터 정보 12항목**: 캐릭명 · 클래스 · 레벨 · 칭호 · 현재 맵 · 은동전 · 마족 공물 · 전투력 · 마도 저항 · 생활력 · 매력 · 가방 무게
- **웹앱과 기록 공유.** 두 앱을 같이 써도, 하나만 써도 문제가 없어야 한다(§3)

### 범위 밖

AI 엔진·Ollama·채팅·아무말 대잔치(페르소나), 랭킹·뱃지, 컷오프, 생활(가공·채집), 인벤토리, 주변 플레이어, 통계, LAN·mDNS·방화벽·기기 페어링·인증, 클래스 전신 이미지(P2).

## 2. 주요 결정

| 항목 | 결정 | 이유 |
|---|---|---|
| 기준 브랜치 | `webapp` | 숙제 판정이 가장 최신. Core가 UI 의존 0. master(WPF판) 숙제 로직은 v1.1 수준 |
| UI | WPF `net8.0-windows` | 설치된 SDK가 8.0.425뿐 |
| 패턴 | MVVM — `CommunityToolkit.Mvvm` | |
| 호스팅 | `Microsoft.Extensions.Hosting` (Generic Host) | 웹앱 `BackgroundService`를 거의 그대로 옮긴다 |
| 테마 | 직접 만든 다크 테마 ResourceDictionary, 외부 UI 라이브러리 없음 | 의존성 0 |
| 글꼴 | `Segoe UI Variable Text, Malgun Gothic`(한글은 맑은 고딕으로 대체), 숫자는 tabular, 아이콘은 `Segoe Fluent Icons, Segoe MDL2 Assets`. Pretendard는 설치돼 있지 않아 쓰지 않는다(H3 결정) | 외부 글꼴 내려받기·내장 없이 작은 창에서도 숫자·한글 가독성 |
| 창 | `WindowChrome` 직접 그린 제목 표시줄, **크기 조절 가능**(Snap 동작), 최소 760×380 DIP, 마지막 위치·크기 기억 | 4분할 Snap |
| 크기 대응 | 폭 기준 3단계 배치(§6). `Viewbox` 확대·축소는 쓰지 않는다(글자 흐림·크기 들쭉날쭉) | |
| 저장 폴더 | **`%APPDATA%\MobiMateWeb` 공유** (§3) | 웹앱 사용자는 옮길 것 없음 |
| 버전 | `1.0.MMdd.HHmm` | |
| 배포 | `dotnet publish -c Release` → `MobiMateHomeWork.exe` + `.zip` | |

## 3. 웹앱과 기록 공유 (핵심 설계)

### 지금 구조의 문제 (코드 확인)

- `JsonFileStore`의 잠금은 **프로세스 안에서만** 걸린다(`Storage/JsonFileStore.cs` `LockFor`). 파일 교체 자체는 원자적이라 깨지지는 않는다
- 그러나 `HomeworkService`·`SnapshotManager`·`AccountStore`는 기록을 **메모리에 들고 있다가 통째로 저장**한다. 두 앱이 동시에 돌면 나중에 저장한 쪽이 상대가 쓴 내용(수동 체크, 새 캐릭터 기록)을 지운다 — **잃어버린 갱신**

- 리뷰에서 추가로 확인한 함정 (v2 리뷰)
  - 레이드 증표·가공 **관찰 시각은 메모리에만** 두고 저장하지 않는다(`HomeworkService.cs:97`). 그런데 판정이 이 값을 읽는다(`HomeworkEvaluator.cs:315·393`, `HomeworkService.cs:227`). 파일을 다시 읽으면 이 값이 사라져 레이드 클리어를 놓친다
  - `SnapshotManager`는 읽기에 실패해도(잠김) **메모리를 먼저 비운다**(`SnapshotManager.cs:166-186`). 다음 저장 때 빈 목록이 기록된다
  - `File.Replace` 도중 대상 파일이 잠깐 없을 수 있다. `HomeworkStore.TryLoad`는 "없음"을 빈 기록으로 바꾸고, 새로 만든 장부는 저장된다 → 기존 기록을 덮어쓴다
  - `SnapshotManager.UpdateSnapshot`은 바뀐 게 없어도 **매번 저장**한다(634행, 4초 주기 감시가 부른다). 두 앱이면 서로 파일을 끝없이 갈아 쓴다
  - `SnapshotManager`는 두 파일(스냅숏·기록 DB)을 함께 고치고 따로 저장한다. `AccountStore`는 생성자에서 한 번만 읽는다

### 해결: "공유 저장 패치" (S0, 두 앱 Core에 똑같이)

1. **폴더 하나에 뮤텍스 하나**: 공유 파일 전체를 이름 있는 뮤텍스 `Local\MobiMate.Data` 하나로 묶는다(파일별 뮤텍스는 두 파일을 함께 고칠 때 순서·데드락 문제가 생긴다). 프로세스 안 잠금과 함께 쓰고, 버려진 뮤텍스(`AbandonedMutexException`)는 획득으로 처리. **읽기도 뮤텍스 안에서** 한다
2. **읽고-고치고-쓰기 한 묶음**: 저장소마다 `Mutate(change)` — 뮤텍스 → 파일이 바뀌었으면 다시 읽기 → 변경 → **실제로 바뀌었을 때만** 저장 → 해제. 바뀜 판정은 마지막 쓰기 시각(100ns 단위)과 크기로 하고, 자기가 쓴 직후의 값을 기억해 자기 쓰기는 무시
3. **읽기 실패 처리**: 다시 읽기는 `TryLoad`만 쓴다. `Failed`(잠김)면 메모리 값을 그대로 두고 이번 작업을 미룬다. 직전까지 있던 파일이 `Missing`이면 교체 중으로 보고 잠깐(50ms×3) 다시 시도하고, 그래도 없으면 메모리 값을 유지한다. **빈 기록으로 덮어쓰는 경로를 없앤다**
4. **판정에 쓰는 관찰 시각을 저장한다**: `RaidTokensObservedUtc`·`AlteringObservedUtc`를 파일에 남긴다(지금은 개수가 바뀔 때만 저장). 저장 횟수는 관찰 주기(60초)만큼 늘어난다
5. **세 저장소 고치기**: `HomeworkService`·`SnapshotManager`·`AccountStore`의 모든 수정 경로를 `Mutate`로. `SnapshotManager.SaveSnapshotsAsync`(`Task.Run`)는 순서가 뒤집힐 수 있으므로 뮤텍스 안 동기 저장으로 바꾼다. `UpdateSnapshot`은 기록할 것이 없으면 저장하지 않는다
6. **변경 감지**: `FileSystemWatcher`는 공유 파일 이름만 정확히 거른다(`.tmp.*`·`.bak` 무시), 300ms 묶음, 이벤트 뒤 읽기는 3번 규칙을 따른다. 감시가 끊겨도 되게 **10초 폴링을 함께** 둔다. 숙제 앱은 화면을 다시 그리고, 웹앱은 SSE `homework.changed`·`characters.changed`로 화면에 알린다
7. **`currency_changes.jsonl`**(레이드 보상 실측 로그)은 **웹앱만 쓴다.** 숙제 앱은 쓰지 않는다(두 앱이 같은 변화를 두 번 적는 것을 피함). 웹앱도 쓰기 실패를 무시하지 말고 로그에 남긴다

### 두 앱이 동시에 관찰할 때

- 두 앱 모두 게임을 관찰·판정한다(각자 혼자서도 완전해야 하므로). 판정은 파일의 최신 기록 위에 적용되고, 이미 완료면 건너뛰므로 두 번 판정해도 결과가 같다(관찰 시각을 저장하므로 레이드 기준값도 공유된다)
- 앱별로 따로 보이는 것(허용): 진행 중 신호(`_signals`, 보스 교전·공간 입장 맥동)와 전투력 재확인 상태(`CombatScoreGuard`)는 메모리 전용이라 앱마다 잠깐 다를 수 있다. 결국 같은 값으로 수렴한다
- 게임 CLI 호출이 두 배가 된다. **H6에서 실측**하고, 문제가 되면 "관찰자 1명" 규칙(뮤텍스 `Local\MobiMate.Observer`를 쥔 앱만 주기 관찰, 다른 앱은 파일 변경만 따라감)을 더한다

### 혼자 쓰는 경우

| 경우 | 동작 |
|---|---|
| 웹앱만 | 지금과 같다. 패치는 잠금·재읽기만 더한다. 웹앱 테스트 전부 통과가 기준 |
| 숙제 앱만 (웹앱을 쓴 적 없음) | `%APPDATA%\MobiMateWeb`를 만들어 쓴다. 비어 있으면 웹앱과 같은 규칙으로 WPF판 기록(`%APPDATA%\MobiMate`)을 가져온다 |
| 둘 다 | 같은 파일을 안전하게 나눠 쓴다. 한쪽에서 체크하면 다른 쪽에 몇 초 안에 보인다 |
| 숙제 앱 + **패치 전 옛 웹앱** | 잃어버린 갱신 위험. 숙제 앱이 `MobiMateWeb.exe` 실행과 파일 버전을 확인해 패치 전 버전이면 상단에 "웹앱을 최신으로 바꿔 주세요" 경고 |

### 공유 규칙

- 공유 파일: `character_snapshots.json`, `character_history_db.json`, `homework_records.json`, `accounts.json`
- **공유 파일의 형식을 바꿀 때는 두 브랜치에 같은 변경을 함께 넣는다.** 한쪽만 필드를 더하면 다른 쪽이 저장하면서 지운다
- 숙제 앱만 쓰는 값은 **별도 파일** `homework_app.json`에 둔다: 창 위치·크기, CLI 경로, 캐릭터별 마지막 맵·가방 최대 무게(§5)
- 웹앱 전용 파일(`web_settings.json`, `devices.json`, `rankings.json`, `favorites.json`, `custom_personas.json`, `currency_changes.jsonl`)은 숙제 앱이 쓰지 않는다. CLI 경로는 `homework_app.json`에 없으면 `web_settings.json`에서 읽기만 한다

## 4. 브랜치·폴더 운영

- `MobiMateHomeWork`가 이 프로젝트의 줄기. **webapp·master와 병합하지 않는다.** 웹앱 Core 변경은 필요한 커밋만 `cherry-pick`
- 공유 저장 패치(S0)는 **webapp에 먼저**(웹앱 작업 방식 그대로: 작업 브랜치 → webapp 병합 → 푸시 → `web/dist` 다시 빌드), 그 커밋을 이 브랜치로 cherry-pick
- 작업 브랜치 `hw/feat-…`·`hw/fix-…` → `MobiMateHomeWork` 병합 → Gitea 푸시
- 작업 폴더 `V:\workspace\MobiMate\MobiMateHomeWork` (worktree 설정 완료). 백그라운드 작업은 별도 worktree

### 폴더 구성 (H0 이후)

```
CLAUDE.md, README.md, PLAN-MobiMateHomeWork.md
docs/                         공유 요구사양·CLI 스펙 사본
hw/
  MobiMateHomeWork.sln
  src/MobiMate.Core/          웹앱 Core에서 이동(git mv) 후 가지치기
  src/MobiMateHomeWork/       WPF 앱 (exe)
  tests/MobiMate.Core.Tests/
  tests/MobiMateHomeWork.Tests/
  tools/FakeCli/
```

`web/` 나머지는 이 브랜치에서 지운다(webapp에는 남음).

## 5. 가져올 것 / 버릴 것

### Core — 남기는 것

| 폴더 | 내용 | 비고 |
|---|---|---|
| `Homework/` | Catalog(+json), Evaluator, Service, Records(Store·WPF 가져오기), KstClock, CurrencyDeltaLog | 판정 로직은 손대지 않는다(S0 저장 패치만) |
| `Account/` | AccountGrouping, **CoinForecast** | 은동전·마족 공물 예상 보유량 표시에 쓴다 |
| `Game/` | CharacterIdentity, CombatScoreGuard, GameStateCache, ScoreReading, DisplayFormat | GameStateCache의 `BuildAiContext`·`BuildChatterContext` 제거(`DescribeActivity`는 남김) |
| `Storage/` | SnapshotManager, JsonFileStore | 페르소나(`CustomPersona` 사용처 101·114·132·419·657-684행, `ImportMissingFrom` 대상 목록 포함) 제거 |
| `Cli/` | GameCli | `SendGameChatAsync`(`ChatText` 의존) 제거 |
| `Models/` | GameModels | |

### Core — 지우는 것

`Ai/`, `Chat/`, `Chatter/`, `Intent/`, `Ranking/`, `Cutoff/`(+json), `Life/`(+json), `Refresh/`, `Game/NearbyRanker`. **`MobiMate.Core.csproj`의 EmbeddedResource 두 줄(cutoff·work_categories json)도 지운다.**

테스트: 파일째 — `AiEngineTests`, `ChatPlanTests`, `InGameChatterTests`, `InteractiveFeaturesTests`, `RankingTests`, `CutoffTests`, `WorkCategoryTests`. 클래스만(혼합 파일) — `CoreRulesTests`의 `ChatTextTests`·`CommandIntentParserTests`·`AiEngineManagerTests`, `GameStateCacheTests`의 AI·Chatter 문맥 테스트, `HomeworkTests`의 `DisplayAndRankingTests`·`PersonaGeneratorTests`. `FakeCliFixture`의 `GlobalStateCollection`(ChatText.Mode용) 정리. 숙제·레이드·계정·식별 테스트는 하나도 빠지면 안 되고, 지운 테스트 이름은 커밋 메시지에 남긴다.

`daily_altering`(가공시설 수거)은 수거 버튼이 유일한 자동 근거였으므로 수동 전용이 된다(숙제 칸 10개에는 원래 없음).

### Web 계층 → 앱 서비스 (빠뜨리면 캐릭터가 섞인다)

| 웹앱 원본 | 새 위치 (`MobiMateHomeWork/Services`) | 바꿀 점 |
|---|---|---|
| `Infrastructure/QueryCache.cs` (QueryCache·GameQueries) | 그대로 | |
| `Services/StatusMonitor.cs` | 그대로 | SSE 대신 `AppEvents.StatusChanged` |
| `GameViews`의 `HeaderAsync`·`ResolveKeyAsync`·`Stabilize`·`KeepTitle`·`RecheckCombatAsync`·`IsMixedRead`·`CurrenciesAsync`·`RecordNowAsync`·`Characters()`·`InProgressOf`·`IsKnownCharacter`·`IsCurrentCharacter`, 가방 무게 계산(194·253행) | `CharacterSession.cs` | 형식 있는 레코드(`AccountView`·`CharacterView`). 칸 데이터 원천은 `Characters()` 안의 `GetAutoStatuses`·`GetFieldBossStatuses`. 진행 중 신호는 `InProgressOf`. 랭킹·컷오프·생활·인벤토리 호출 제거. 캐릭터 선택창·재화 섞임 처리는 그대로. `RecheckCombatAsync` 종료 신호를 Host 종료와 연결. 헤더를 읽을 때 현재 맵·가방 최대 무게를 `homework_app.json`에 캐릭터별로 남긴다 |
| `GameActions`의 `HomeworkWatcher` | `HomeworkWatcher.cs` | SSE 대신 `AppEvents.HomeworkChanged` |
| `CharacterRecorder`, `CharacterWatcher` | 그대로 | |
| `AccountStore`, `FileLogger`, `MobiMateOptions` | 그대로 / 줄여서 | 옵션에서 포트·LAN·mDNS·SSE 제거. 로그는 `logs/homework-*.log`로 웹앱 로그와 구분 |
| `WebSettings` | `AppSettings` (`homework_app.json`) | |
| `Tray.cs` | `TrayIcon.cs` + `SingleInstance.cs` | 뮤텍스 `Local\MobiMateHomeWork.Single.*`(웹앱과 다른 이름) |
| `Program.cs`의 단일 인스턴스·WPF 가져오기·DI | `App.xaml.cs` | 이미 실행 중이면 기존 창을 앞으로 |
| `PUT /homework/{id}` 처리 | `HomeworkCommands.cs` | 대상 확인, 현재 캐릭터면 키 생략·아니면 키 지정 |
| `PUT /profile/nickname` | `HomeworkCommands.RenameCharacter` | 원본은 현재 캐릭터 전용 → 아무 캐릭터나 바꾸게 새로 만든다 |
| `homework.tsx`의 `HW_GROUPS`·`cellsOf`·필드보스 해제 | `HomeworkBoardViewModel` | 같은 규칙 |

`AppEvents` = C# 이벤트 묶음. ViewModel은 `Dispatcher`로 넘겨 다시 그린다(200ms 묶음). 공유 파일 변경(`FileSystemWatcher`)도 같은 경로로 들어온다.

## 6. 화면 설계

### 목표 크기 — Windows 4분할 Snap (높이는 작업 표시줄 크기·배율에 따라 몇 DIP 달라지는 대략값)

| 모니터·배율 | 4분할 크기 (DIP) | 배치 단계 |
|---|---|---|
| FHD 125% | 768 × 413 | 좁게 |
| QHD 150% | 853 × 464 | 좁게 |
| FHD 100% | 960 × 516 | 보통 |
| QHD 125% | 1024 × 557 | 보통 |
| QHD 100% | 1280 × 696 | 넓게 |

배치 단계(창 폭): **좁게 < 900 ≤ 보통 < 1200 ≤ 넓게**. 처음 켜면 현재 모니터 작업 영역의 4분할 크기로 오른쪽 위에 연다. 이후 위치·크기를 기억한다. 최소 760×380(4분할보다 작아야 Snap이 맞고, 좁게 단계의 카드 232 + 표 502 + 여백이 들어가는 폭). 커스텀 제목 표시줄에서도 Snap(Win+화살표·끌어 붙이기)이 되게 `WindowChrome`의 크기 조절 테두리를 살린다. 최대화 버튼 Snap 레이아웃 메뉴(Windows 11)는 P2.

### 구성

```
┌ 제목줄 32 ─ ◆ 숙제  ● 연결                    일 5:12 · 주 2일 5h   ⚙  ─ □ ✕ ┐
├ 계정 탭 28 ─ [OO 17] [△△ 4] [□□ ✓] [◇◇ 9]  (넘치면 가로 스크롤) ──────────────┤
├─────────────────────────────────────────────────────────┬──────────────────────┤
│ 묶음 머리 34    일일·주간 │   레이드    │   어비스    │보스│ 선택 캐릭터 카드        │
│                요일 길드 뱅가│카브 에이 화이│허상 광기 물길│    │ ┌──┐ 닉네임            │
│ [직]닉네임  7/10 │ ✓  ○  2 │ ✓  ·  ○ │ ○  ✓  · │ ✓ │ │직│ 기사 Lv.85        │
│ 기사 85 2.1만    │          │          │          │   │ └──┘ 〈칭호〉           │
│ ... 최대 6줄 (넘으면 이 영역만 스크롤)                       │ 📍 티르 코네일 (접속 중) │
│                                                         │ 전투력 21,350 마저 410 │
│                                                         │ 생활력 1,204 매력 980  │
│                                                         │ 은동전 ██████░ 82/100 │
│                                                         │ 마족공물 ███░░ 31/50  │
│                                                         │ 가방  ████░ 412/600kg │
└─────────────────────────────────────────────────────────┴──────────────────────┘
```

- **왼쪽 = 숙제 표**(계정의 캐릭터 × 숙제 10칸), **오른쪽 = 선택 캐릭터 카드**(정보 12항목). 캐릭터 줄을 누르면 카드가 그 캐릭터로 바뀐다. 기본 선택 = 접속 중 캐릭터(없으면 계정 첫 캐릭터)
- **계정 = 제목줄 아래 탭 줄(28)**(폭을 아끼려고 왼쪽 목록 대신). 탭에 남은 숙제 수 배지, 접속 중 계정 표시. 접속 중 계정이 기본. 리셋 카운트다운은 제목줄에 짧게
- 캐릭터 순서 = 전투력 순. 모두 완료한 줄은 흐리게

### 단계별 치수 (6줄이 스크롤 없이 들어가는지)

| | 좁게 (768×413) | 보통 (960×516) | 넓게 (1280×696) |
|---|---|---|---|
| 카드 폭 | 232 | 270 | 320 |
| 표 폭 (여백 16·스크롤바 자리 10 뺀 값) | 510 | 664 | 934 |
| 캐릭터 정보 열 | 146: 닉네임·`7/10` / 직업 Lv·전투력 | 190: + 마지막 접속 | 250: + 은동전·공물 작은 막대 |
| 숙제 칸 | 32×32, 상태 기호만 | 42×44, 기호 + 남은 횟수 | 60×56, 두 줄 이름 + 상태 |
| 칸 이름 | 묶음 머리에 두 글자(요일·길드·뱅가·카브·에이·화이·허상·광기·물길·보스) | 같음 | 칸 안에 두 줄(요일/던전 … 흩어진/물길) |
| 줄 높이 | 44 | 60 | 76 |
| 높이 합 (제목 32 + 탭 28 + 묶음 머리 + 6줄) | 32+28+34+6×44 = **358 ≤ 약 405** | 32+28+34+6×60 = **454 ≤ 약 510** | 32+28+38+6×76 = **554 ≤ 약 690** |
| 폭 합(표) | 146+10×32+묶음 간격 3×8+칸 간격 6×2 = **502 ≤ 510** | 190+10×42+36+12 = **658 ≤ 664** | 250+10×60+48+24 = **922 ≤ 934** |

스크롤바는 얇은 겹침형(10)으로 자리를 미리 빼 두어 7명 이상이 되어도 폭이 넘치지 않는다. 숫자는 H3에서 실제 화면으로 다시 맞춘다.

전체 이름·판정 근거는 칸 툴팁(예: "카브락 — 주간 미션 '…' 1/1", 수동 체크는 "수동").

### 캐릭터 카드 (정보 12항목)

출처: 실시간 = `get_my_info`(칭호·레벨·점수 4종·`Vitals.InventoryWeightCurrent/Max`), `get_current_environment`(`GameSpaceDisplayName` = 현재 맵), `get_currencies`(은동전·마족 공물). 접속 안 한 캐릭터 = `CharacterHistoryRecord`(`SnapshotManager.cs:35-59`: 칭호·레벨·전투력·마도 저항·생활력·매력·은동전·마족 공물·현재 무게). 기록에 없는 **현재 맵·가방 최대 무게만** `homework_app.json`에 캐릭터별로 따로 남긴다.

| 항목 | 접속 중 캐릭터 | 접속 안 한 캐릭터 |
|---|---|---|
| 캐릭명 | 게임이 이름을 주지 않는다 → **사용자가 붙인 닉네임**, 없으면 "서버 · 직업". 카드의 이름을 눌러 바로 바꾼다 | 같음 |
| 클래스·레벨·칭호 | 실시간 | 마지막 기록 |
| 현재 맵 | 실시간(`GameSpaceDisplayName`) | "마지막 위치: …" (숙제 앱이 본 적 있을 때만, 아니면 —) |
| 전투력·마도 저항·생활력·매력 | 실시간 | 마지막 기록 |
| 은동전·마족 공물 | 실시간 보유량 / 상한 막대 | **예상 보유량**(마지막 값 + 지난 시간만큼 충전, 멤버십 반영) — 가득 차기 전 경고색 |
| 가방 무게 | 실시간 현재/최대, 95%↑ 주의·100%↑ 위험 색 | 마지막 현재 무게, 최대를 알면 막대 |

좁게 단계에서는 4개 점수를 2×2 격자, 막대 3개를 아래로. 카드 높이 약 300 ≤ 약 345.

### 동작

- **칸 클릭** = 완료↔미완료 수동 전환. 필드보스 칸은 완료로 바꿀 때 보스 6개 고르기 팝업, 해제는 넘긴 보스만(모르면 여섯 모두)
- 칸 상태: 완료(채운 초록 ✓) / 미완료(외곽선 ○, 뱅가드는 남은 횟수) / 미확인(흐림 ·) / 진행 중(테두리 맥동) / 완료 제안(모서리 점)
- **시각 갱신**: 30초마다 리셋 카운트다운·마지막 접속·예상 보유량을 다시 계산. 06:00 KST 일일·주간 리셋을 지나면 보드를 다시 만든다
- 게임 미연결·CLI 없음: 제목줄 점 색 + 툴팁. 기록된 값은 계속 보인다
- 닫기(✕) = 트레이로 숨김(관찰 계속). 트레이 메뉴: 열기 / 항상 위(게임 옆에 띄우기용) / 종료
- 설정: 제목줄 ⚙ → CLI 경로, 항상 위, 닫으면 트레이로

## 7. 단계 계획

각 단계 = Sonnet 구현 → Sonnet 리뷰 → 테스트 → 리뷰. 단계마다 작업 브랜치 하나, 끝나면 병합·푸시.

| 단계 | 내용 | 완료 기준 |
|---|---|---|
| **S0** 공유 저장 패치 (webapp) | §3 해결 1~7을 webapp Core·Web에 넣는다. S0a 저장 계층(뮤텍스·`Mutate`·읽기 실패 처리·관찰 시각 저장), S0b 변경 감지·SSE 알림으로 나눠도 된다 | 웹앱 테스트 전부 통과 + 새 테스트: **두 프로세스 동시 쓰기**(자식 프로세스 2개가 같은 폴더에 수동 체크·캐릭터 기록을 번갈아 1,000회 → 잃어버린 갱신 0), 교체 중 읽기(빈 기록으로 덮어쓰지 않음), 잠긴 파일 읽기 실패 시 메모리 유지, 다른 프로세스가 쓴 레이드 관찰 시각으로 판정, 변경 없으면 저장 안 함. `web/dist` 다시 빌드 |
| **H0** 뼈대 정리 | S0 커밋 cherry-pick → `MobiMate.Core`·Core 테스트·FakeCli를 `hw/`로 `git mv`, 나머지 `web/` 삭제, `hw/MobiMateHomeWork.sln`, CLAUDE.md·README | build·test 통과 |
| **H1** Core 가지치기 | §5 제거 목록, 페르소나 분리 | 경고 0, 남은 테스트 전부 통과 |
| **H2a** 앱 서비스 | WPF 프로젝트(빈 창), Generic Host, §5 표 이식, `AppEvents`, `homework_app.json`, WPF 기록 가져오기, 단일 인스턴스, 로그, 공유 파일 감시, 옛 웹앱 경고 | 빌드, 앱이 FakeCli로 떠서 로그에 관찰 기록 |
| **H2b** 서비스 테스트 | 서비스 직접 호출 하니스(FakeCli + 임시 폴더). 웹 테스트 시나리오를 다시 씀: CharacterSelect·CharacterWatcher·CombatRecheck·InProgress·Views·Accounts 중 숙제·캐릭터 관련 + 웹앱과 공유(다른 프로세스가 쓴 체크가 보임) | 전부 통과 |
| **H3** 테마·창 | 색·글꼴·스타일, 커스텀 제목줄, Snap 동작, 3단계 배치 틀, 첫 실행 4분할 위치, 위치 기억, 트레이·항상 위 | 다섯 가지 4분할 크기 스크린샷이 §6 치수대로 |
| **H4** 숙제 표·카드 | 계정 탭, 숙제 표, 칸·클릭·필드보스 팝업, 캐릭터 카드 12항목, 닉네임 바꾸기, 리셋 카운트다운, 툴팁, 실시간 갱신 | ViewModel 테스트(칸·전환·필드보스 해제·정렬·선택·예상 보유량·무게 색) + 다섯 크기 스크린샷에서 6캐릭터가 스크롤 없이 보임 |
| **H5** 배포 | 아이콘, 버전, 단일 exe + zip | exe 실행, 웹앱과 같은 폴더 공유 확인 |
| **H6** 실기 점검 | 실제 게임: 캐릭터 전환·선택창·요일던전·뱅가드·레이드 증표·필드보스·06:00 리셋·트레이 상주·**웹앱과 동시 실행(CLI 부하, 체크 공유)**·FHD/QHD 4분할 | 承雲 확인 |

### 테스트 전략

- Core: 기존 테스트 유지(판정 회귀 기준) + S0 저장 패치 테스트(동시 쓰기·외부 변경 재읽기·버려진 뮤텍스)
- 앱 서비스: FakeCli + 임시 폴더
- ViewModel: UI 없는 단위 테스트. 배치 단계 선택(폭 → 단계)도 단위 테스트
- 화면: FakeCli로 띄워 다섯 크기 스크린샷(computer-use). FlaUI 자동 UI 테스트는 하지 않는다

## 8. 위험과 대응

| 위험 | 대응 |
|---|---|
| 두 앱 동시 쓰기로 기록 유실 | S0 패치 + 동시 쓰기 테스트. 패치 전 웹앱이 떠 있으면 경고 |
| 한쪽만 공유 파일 형식을 바꿈 | §3 공유 규칙. 앱 전용 값은 `homework_app.json` |
| 두 앱의 CLI 호출 중복 | H6 실측, 필요하면 관찰자 1명 규칙 |
| 연결부 이식 누락 → 캐릭터 섞임 | §5 표를 체크리스트로, 웹 테스트 시나리오 재작성 |
| 창을 닫으면 관찰 멈춤 | 닫기 = 트레이 |
| 작은 창(768×413)에서 글자가 너무 작음 | 좁게 단계 최소 글자 11px, 칸은 기호만. H6에서 承雲 확인 후 조정 |
| 웹앱 Core가 이후 바뀜 | 숙제 관련 커밋만 cherry-pick. `Homework/`는 이 브랜치에서 가급적 고치지 않는다 |

## 9. 결정 기록

| 질문 | 承雲 답 (2026-10-11) | 반영 |
|---|---|---|
| 웹앱과 기록 공유 | 공유. 단 각 앱만 쓰는 사람도 문제없게 | §3, S0 |
| 캐릭터 정보 | 캐릭명·클래스·레벨·칭호·현재 맵·은동전·마족 공물·전투력·마도 저항·생활력·매력·가방 무게 (12항목) | §6 캐릭터 카드 |
| 창 크기 | QHD·FHD의 4분할 크기, Snap 했을 때 적당히 | §6 목표 크기·3단계 배치 |
| 한 화면 기준 | 스크롤 허용, 한 계정 최대 6캐릭은 한눈에 | §6 치수표 |
| 직업 이미지 | (미정) 기본값 직업 색 뱃지 + 첫 글자, 이미지는 P2 | |

## 10. 모델 배정

- 계획·단계 경계 판단 = Opus
- 구현·테스트·리뷰 = Sonnet. 단계마다 Sonnet 서브에이전트를 별도 작업 브랜치(별도 worktree)에서 돌리고, 결과를 코드로 확인한 뒤 병합한다

## 11. H3 진행 기록 (테마·창)

- 색 토큰은 `Themes/Colors.xaml`. 계획의 `더 흐린 글자 #6B7280`은 면 위 대비 3.6:1이라 AA 미달 → `#8590A0`으로 올렸다(`ThemeTests`가 글자색 대비 4.5:1을 검사).
- 배치 단계 값(글자 12/13/14, 카드 232/270/320, 줄 높이·칸 크기 등)은 `Themes/Tier.*.xaml`에 두고 창 폭이 바뀌면 통째로 바꿔 끼운다. H4는 `Metrics.*` 키를 쓴다.
- 창 위치·크기는 `homework_app.json`에 **물리 픽셀**로 저장한다(모니터별 배율이 달라도 복원이 맞도록 PerMonitorV2 manifest 사용). 새로 추가한 값: `TrayHintShown`(트레이 안내 풍선 한 번만).
- 화면 확인용 숨은 옵션: `--render-snapshots <폴더> --storage <임시폴더> --cli <가짜CLI>`(저장 폴더를 안 주면 거부, 데모 데이터 자동 생성).
