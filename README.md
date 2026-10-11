# MobiMateHomeWork

마비노기 모바일 **숙제 전용** Windows 데스크톱 앱(WPF, .NET 8)을 만드는 저장소 브랜치입니다. 웹앱(MobiMate Web)의 숙제 판정·캐릭터 기록 로직을 그대로 쓰고, `%APPDATA%\MobiMateWeb` 기록을 웹앱과 안전하게 공유합니다. 현재는 뼈대 정리 단계(H0)로, 앱 화면은 아직 없습니다.

## 폴더

- `hw/src/MobiMate.Core` — 숙제 판정·캐릭터 기록·계정·저장 (웹앱 Core에서 이동)
- `hw/tests/MobiMate.Core.Tests` — Core 테스트
- `hw/tools/FakeCli` — 가짜 게임 CLI, `hw/tools/SharedStorageHarness` — 동시 쓰기 하네스
- `docs/` — 공유 요구사양·CLI 스펙
- `PLAN-MobiMateHomeWork.md` — 단계 계획, `CLAUDE.md` — 작업 규칙

## 빌드와 테스트

```
dotnet build hw/MobiMateHomeWork.sln
dotnet test hw/MobiMateHomeWork.sln
```

## 웹앱 문서

웹앱 요구사양·설계는 이 브랜치에 없습니다. `git show webapp:web/REQUIREMENTS.md` 로 읽습니다.
