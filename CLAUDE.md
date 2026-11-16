# CLAUDE.md — MobiMate (단일 `main` 브랜치)

## 구조

| 폴더 | 내용 |
|---|---|
| `web/` | 웹앱 (MobiMate.Core, MobiMate.Web, React 클라이언트). 솔루션 `web/MobiMate.Web.sln` |
| `hw/` | 숙제 앱 WPF (MobiMateHomeWork, 솔루션 `hw/MobiMateHomeWork.sln`) |
| `legacy-wpf/` | 옛 WPF판. 보관용, 수정하지 않는다 |
| `docs/` | 조사·설계 문서, `docs/img` = README 그림(SVG) |

- 브랜치는 `main` 하나다. 작업 브랜치(`feat/…`, `fix/…`) → `main` 병합. `main`에 직접 커밋하지 않는다.
- 저장소: bare `V:\workspace\MobiMate`, 작업 폴더 `V:\workspace\MobiMate\main`(worktree). 원격 `origin`(Gitea), `github`.

## 반드시 지킬 것

- **공식 CLI만 쓴다** (`C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe`). 패킷 캡처·메모리 읽기·입력 흉내는 하지 않는다. CLI가 알려 주는 사실은 확정, 모르는 것은 추측하지 않는다.
- 외부 사이트(넥슨 랭킹, 모비라이프 등)가 거절하면 우회하지 않는다.
- **hw의 Core는 웹의 Core와 별도 복사본이다.** 공유 데이터 파일(`%APPDATA%\MobiMateWeb`의 homework_records.json, accounts.json, character_snapshots.json, character_history_db.json) 형식을 바꾸면 `web/`과 `hw/` 양쪽에 반영한다.
- 웹앱 저장 폴더는 `%APPDATA%\MobiMateWeb`. 옛 WPF판 `%APPDATA%\MobiMate`에는 쓰지 않는다.
- 토큰·비밀번호는 어떤 파일에도 쓰지 않는다.

## 빌드·테스트·배포

- 빌드: `dotnet build web/MobiMate.Web.sln` (경고 0)
- 테스트: `dotnet test web/MobiMate.Web.sln`, 클라이언트는 `web/src/client`에서 `npx tsc --noEmit`, `npx vitest run`. e2e(Playwright)는 먼저 `npx vite build` + `dotnet build`로 클라이언트를 내장한 뒤 실행한다.
- 숙제 앱: `dotnet test hw/MobiMateHomeWork.sln`
- **최종 빌드본 위치: `V:\workspace\MobiMate\` 바로 아래** (`MobiMateWeb.exe`, `MobiMateHomeWork.exe`, `allow-lan-firewall.bat`). 항상 여기만 쓰고 다른 곳(dist 등)에 빌드본을 두지 않는다. 시작 메뉴 바로가기도 이 파일을 가리킨다.
- 배포: `powershell -File tools\deploy.ps1` (`-Only web|hw`, `-NoLaunch`). 실행 중인 앱은 묻지 않고 종료하고, 기존 파일은 무조건 덮어쓰고, 배포 뒤 다시 실행한다. 웹은 단일 파일 self-contained, 숙제 앱은 단일 파일(.NET 런타임 필요). 버전은 `1.0.<MMdd.HHmm>`.
- 릴리스 파일은 위 배포본을 그대로 올린다: `gh release create <태그> -R seungwoonlee/mobimate`, 노트는 한국어.

## 작업 방식

- 문서: `web/REQUIREMENTS.md`(요구사양), `web/DESIGN.md`(설계), `web/design/`(구조도 HTML·eli5), `web/DEVELOPMENT.md`.
- 계획 → 리뷰 → 구현 → 리뷰 → 테스트 → 리뷰. 서브에이전트는 메인보다 한 등급 아래 모델.
- **최우선 점검: FHD(1920×1080)급 화면에서 화면이 비좁아 생기는 여러 문제**(잘림·겹침·가로 스크롤·카드 배치 등). UI를 만질 때 이 해상도 기준으로 먼저 확인한다.
- 알려진 미완: 제작 성공률 화면, 숙제 앱의 잠금 칸 UI, 별도 시세 앱, e2e "직업 이미지" 실패.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- When graphify-out/graph.json exists, run `graphify query "<question>"` before your first grep, search or source-file read for a codebase question, and put this rule in every subagent prompt that explores code. Read raw files only for lines the graph or an earlier read already located. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
- A committed graphify-out/graph.json is rewritten whole on every build, so a large diff is expected; do not flag it as churn.
