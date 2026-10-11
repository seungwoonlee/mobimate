# CLAUDE.md — MobiMateHomeWork (`MobiMateHomeWork` 브랜치 계열)

## 프로젝트

- **MobiMateHomeWork** = 마비노기 모바일 숙제 전용 Windows 데스크톱 앱(WPF, .NET 8). 웹앱(MobiMate Web)에서 숙제 탭만 떼어 낸 것이다.
- 단계 계획과 설계는 `PLAN-MobiMateHomeWork.md`가 기준이다. 현재 단계와 완료 기준은 계획서 §7을 본다.

## 브랜치 경계 (반드시 지킨다)

- 줄기 브랜치는 `MobiMateHomeWork`이고, 작업 브랜치는 `hw/…`(예: `hw/h1-prune`)이다.
- **`webapp`·`master`와 병합(merge)하지 않는다.** 어느 방향이든. 웹앱 Core의 변경이 필요하면 해당 커밋만 `git cherry-pick`한다.
- `Homework/` 판정 로직은 이 브랜치에서 가급적 고치지 않는다(웹앱과 어긋남 방지).
- 웹앱 문서·소스는 `git show webapp:web/REQUIREMENTS.md`처럼 읽기만 한다.
- 작업 폴더: `V:\workspace\MobiMate\MobiMateHomeWork`(git worktree). 백그라운드 작업은 별도 worktree에서 한다. 한 폴더에서 브랜치를 바꾸지 않는다. `V:\workspace\MobiMate` 자체는 bare 저장소다.

## 웹앱과 기록 공유 (핵심)

- 저장 폴더는 웹앱과 같은 **`%APPDATA%\MobiMateWeb`** 를 공유한다. 두 앱을 같이 써도, 하나만 써도 문제가 없어야 한다.
- 공유 파일: `character_snapshots.json`, `character_history_db.json`, `homework_records.json`, `accounts.json`. 저장소는 공유 저장 패치(폴더당 뮤텍스 `Local\MobiMate.Data`, 읽고-고치고-쓰기 한 묶음, 바뀔 때만 저장)를 따른다. 우회해서 직접 파일을 쓰지 않는다.
- **공유 파일의 형식을 바꿀 때는 두 브랜치(`webapp`, 이 계열)에 같은 변경을 함께 넣는다.** 한쪽만 필드를 더하면 다른 쪽이 저장하면서 지운다.
- 숙제 앱만 쓰는 값(창 위치·크기, CLI 경로, 캐릭터별 마지막 맵·가방 최대 무게)은 별도 파일 `homework_app.json`에 둔다.
- 웹앱 전용 파일(`web_settings.json`, `devices.json`, `rankings.json`, `favorites.json`, `custom_personas.json`, `currency_changes.jsonl`)은 쓰지 않는다. 자세한 규칙은 계획서 §3.

## 작업 방식

- 각 단계는 계획 → 서브에이전트 리뷰 → 구현 → 리뷰 → 테스트 → 리뷰 순서로 진행한다.
- `MobiMateHomeWork`에 직접 커밋하지 않는다. 작업 브랜치 `hw/…` → `MobiMateHomeWork` 병합 → Gitea(`origin`) 푸시.
- 폴더: `hw/src/MobiMate.Core`(공유 Core), `hw/tests/MobiMate.Core.Tests`, `hw/tools/FakeCli`(가짜 게임 CLI), `hw/tools/SharedStorageHarness`(두 프로세스 동시 쓰기 하네스). WPF 앱은 H2a부터 `hw/src/MobiMateHomeWork`.
- 빌드·테스트: `dotnet build hw/MobiMateHomeWork.sln`, `dotnet test hw/MobiMateHomeWork.sln` (경고 0, 전부 통과).
