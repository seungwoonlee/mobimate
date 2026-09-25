# CLAUDE.md — MobiMate Web (`webapp` 브랜치)

## 프로젝트 경계 (반드시 지킨다)

- 이 브랜치는 **웹앱 전용**이다. WPF판은 `master` 브랜치의 별개 프로젝트이며 Antigravity가 맡는다.
- 두 프로젝트는 **요구사양 문서만 공유**한다. 코드·빌드·저장 데이터는 공유하지 않는다.
  - `webapp`과 `master`는 **서로 병합하지 않는다** (어느 방향이든).
  - WPF 코드(`*.cs`, `*.xaml`, `MobiMate.csproj`, `tests/MobiMate.Tests`)를 이 브랜치에 들이지 않는다.
  - 웹앱 저장 폴더는 `%APPDATA%\MobiMateWeb`이다. WPF판 폴더 `%APPDATA%\MobiMate`에는 쓰지 않는다(첫 실행 때 읽어서 복사만 한다).
  - WPF판의 결함 정보가 필요하면 `git show master:08-분석보고-WPF판_결함목록_인수인계.md`로 읽기만 한다.
- 작업 폴더: 이 브랜치는 `V:\workspace\MobiMate\webapp`(git worktree)에서 다룬다. `V:\workspace\MobiMate\master`는 master 전용이고, `V:\workspace\MobiMate` 자체는 bare 저장소다. 한 폴더에서 브랜치를 바꾸지 않는다.

## 작업 방식

- 문서: `web/REQUIREMENTS.md`(요구사양), `web/DESIGN.md`(상세설계), `web/design/mockup.html`(시안). 단계 계획은 요구사양서 §9.
- 각 단계는 계획 → 서브에이전트 리뷰 → 구현 → 리뷰 → 테스트 → 리뷰 순서로 진행한다.
- `webapp`에 직접 커밋하지 않는다. 작업 브랜치(`feat/…`, `chore/…`) → `webapp` 병합 → Gitea(`origin`) 푸시.
- 빌드·테스트: `dotnet build web/MobiMate.Web.sln`, `dotnet test web/MobiMate.Web.sln` (경고 0, 전부 통과).
