# MobiMate Web (모비노기 AI도우미 웹앱)

> 이 브랜치(`webapp`)는 **웹앱 전용**입니다. WPF 데스크톱판은 `master` 브랜치의 별개 프로젝트입니다.
> 두 프로젝트는 요구사양 문서만 공유하고, 코드·빌드·저장 데이터는 완전히 분리되어 있습니다. 두 브랜치는 서로 병합하지 않습니다.

## 폴더

| 경로 | 내용 |
|---|---|
| `web/REQUIREMENTS.md` | 웹앱 요구사양서 |
| `web/DESIGN.md` | 웹앱 상세설계서 |
| `web/design/mockup.html` | 화면 시안 |
| `web/MobiMate.Web.sln` | 웹앱 솔루션 (Core · 서버 · 가짜 CLI · 테스트) |
| `web/src/client` | 프론트엔드 (Vite + React + TypeScript) |
| 루트 `REQUIREMENTS.md`, `API_SPEC_SAMPLES.md`, `0x-상세*.md` | WPF판과 공유하는 요구사양·게임 CLI 스펙 문서 (사본) |

## 작업 폴더 (git worktree)

저장소 하나(`V:\workspace\MobiMate\.git`, bare)에 브랜치별 작업 폴더를 둡니다 (2026-09-25 재구성).

| 폴더 | 브랜치 | 프로젝트 |
|---|---|---|
| `V:\workspace\MobiMate\master` | `master` | WPF판 (Antigravity) |
| `V:\workspace\MobiMate\webapp` | `webapp` | 웹앱 (이 폴더) |

`V:\workspace\MobiMate` 자체는 작업 폴더가 아닙니다(bare 저장소). 새 작업 폴더는 `git -C V:\workspace\MobiMate worktree add <폴더> <브랜치>`로 만들고, 만든 뒤 `git -C <폴더> config --worktree core.bare false`를 한 번 실행하세요(이 git 버전은 작업 폴더도 bare 설정을 물려받습니다).

한 폴더에서 브랜치를 바꾸지 마세요. 무시 대상인 빌드 산출물(`bin/`, `obj/`)이 남아 다른 프로젝트의 빌드를 깨뜨립니다.

## 빌드·테스트

```bash
# 프론트엔드: 빌드 결과(web/src/client/dist)를 서버 빌드가 wwwroot로 복사한다. 없으면 안내용 index.html만 나간다.
cd web/src/client && npm ci && npm test && npm run build && cd -

dotnet build web/MobiMate.Web.sln
dotnet test web/MobiMate.Web.sln
```

### 배포물 (단일 exe + zip)

```bash
dotnet publish web/src/MobiMate.Web -c Release -o web/dist
```

화면 빌드(npm) → `web/dist/MobiMateWeb.exe`(화면 포함 단일 파일, .NET 8 데스크톱 런타임 필요) → 메신저 전송용 `web/dist/MobiMateWeb.zip`까지 한 번에 만듭니다. 실기 점검은 `web/실기점검-체크리스트.md`를 따릅니다.

### 화면 자동 테스트 (E2E·접근성)

```bash
cd web/src/client && npm run e2e
```

`npm run e2e`는 화면 빌드 → 서버 빌드(방금 만든 화면을 dll에 넣음) → 테스트 순서로 돕니다. 순서가 바뀌면 옛 화면으로 테스트되므로, 이미 둘 다 빌드했을 때만 `npm run e2e:only`를 씁니다.
Safari 엔진(WebKit) 검사는 Playwright 브라우저를 한 번 받은 뒤(`npx playwright install webkit`) `E2E_WEBKIT=1`을 붙여 돌립니다.

설치된 Edge로 PC·태블릿·폰 8개 뷰포트(트라이폴드 펼침·아이폰 가로 포함)에서 핵심 흐름과 폴더블 전환, 접근성(axe, serious 이상 0건)을 검사합니다. 서버는 가짜 CLI와 임시 저장 폴더로 자동으로 띄웁니다.

화면 개발 중에는 `npm run dev`(포트 5173)가 `/api`·`/auth`를 로컬 서버(기본 `http://127.0.0.1:17800`, 환경변수 `MOBIMATE_SERVER`로 변경)로 넘깁니다.
가짜 CLI로 서버를 띄울 때 `MobiMate__BootUrlFile=<파일>`을 주면 브라우저를 여는 대신 1회용 기동 URL을 그 파일에 남깁니다(개발·E2E 전용).

## 저장 데이터

웹앱은 `%APPDATA%\MobiMateWeb\`를 씁니다. 첫 실행 때 이 폴더가 비어 있으면 WPF판 기록(`%APPDATA%\MobiMate\`)을 복사해 옵니다. WPF판 폴더에는 쓰지 않습니다.
