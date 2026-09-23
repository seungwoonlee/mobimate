# MobiMate Web (모비노기 AI도우미 웹앱)

> 이 브랜치(`webapp`)는 **웹앱 전용**입니다. WPF 데스크톱판은 `master` 브랜치의 별개 프로젝트입니다.
> 두 프로젝트는 요구사양 문서만 공유하고, 코드·빌드·저장 데이터는 완전히 분리되어 있습니다. 두 브랜치는 서로 병합하지 않습니다.

## 폴더

| 경로 | 내용 |
|---|---|
| `web/REQUIREMENTS.md` | 웹앱 요구사양서 |
| `web/DESIGN.md` | 웹앱 상세설계서 |
| `web/design/mockup.html` | 화면 시안 |
| `web/MobiMate.Web.sln` | 웹앱 솔루션 (Core · 가짜 CLI · 테스트) |
| 루트 `REQUIREMENTS.md`, `API_SPEC_SAMPLES.md`, `0x-상세*.md` | WPF판과 공유하는 요구사양·게임 CLI 스펙 문서 (사본) |

## 작업 폴더 (git worktree)

| 폴더 | 브랜치 | 프로젝트 |
|---|---|---|
| `V:\workspace\MobiMate` | `master` | WPF판 |
| `V:\workspace\MobiMate-web` | `webapp` | 웹앱 (이 폴더) |

한 폴더에서 브랜치를 바꾸지 마세요. 무시 대상인 빌드 산출물(`bin/`, `obj/`)이 남아 다른 프로젝트의 빌드를 깨뜨립니다.

## 빌드·테스트

```bash
dotnet build web/MobiMate.Web.sln
dotnet test web/MobiMate.Web.sln
```

## 저장 데이터

웹앱은 `%APPDATA%\MobiMateWeb\`를 씁니다. 첫 실행 때 이 폴더가 비어 있으면 WPF판 기록(`%APPDATA%\MobiMate\`)을 복사해 옵니다. WPF판 폴더에는 쓰지 않습니다.
