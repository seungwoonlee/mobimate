# MobiMateHomeWork

마비노기 모바일 **숙제 전용** Windows 데스크톱 앱입니다(WPF, .NET 8). 웹앱(MobiMate Web)의 숙제 판정·캐릭터 기록 로직을 그대로 쓰고, 기록은 웹앱과 같은 폴더를 공유합니다. 게임 옆에 작게 띄워 두고 캐릭터별 일일·주간·레이드·어비스·필드보스 숙제를 확인합니다.

## 사용자 안내

- **필요한 것**: Windows 10/11 x64, **.NET 8 데스크톱 런타임**(.NET Desktop Runtime 8). 게임 CLI(`MabinogiMobile_CLI.exe`)가 있어야 캐릭터를 읽습니다.
- **실행**: `MobiMateHomeWork.exe` 하나만 실행합니다(설치 없음, 압축 파일이 풀리는 곳도 없음). 처음에는 화면의 4분할 위치에 뜨고, 이후에는 마지막 위치·크기를 기억합니다.
- **트레이·항상 위**: 창을 닫으면 종료하지 않고 트레이로 내려가 계속 관찰합니다(설정 ⚙에서 끌 수 있음). 트레이 아이콘의 메뉴로 열기·항상 위·종료를 합니다. 완전히 끄려면 트레이 메뉴의 종료를 씁니다.
- **웹앱과 함께 쓸 때**: 웹앱은 **공유 저장 패치판(1.0.1011.1510 이후)** 이어야 합니다. 옛 웹앱이 떠 있으면 경고 띠가 뜹니다. 두 앱은 체크·캐릭터 기록을 몇 초 안에 서로 반영합니다. 하나만 써도 문제없습니다.
- **기록 위치**: `%APPDATA%\MobiMateWeb`(웹앱과 공유). 저장할 때마다 `*.bak` 사본이 같이 남지만, 처음 쓰기 전에 폴더를 통째로 복사해 두기를 권합니다.
- **숙제 앱 전용 설정**: `%APPDATA%\MobiMateWeb\homework_app.json`(창 위치·크기, CLI 경로, 항상 위 등).
- **로그**: `%APPDATA%\MobiMateWeb\logs\homework-*.log`. 시작 줄에 버전이 찍힙니다. 버전(`1.0.MMdd.HHmm`)은 제목줄 아이콘에 마우스를 올리거나 설정 ⚙에서 볼 수 있습니다.

## 개발자 안내

폴더

- `hw/src/MobiMate.Core` — 숙제 판정·캐릭터 기록·계정·저장 (웹앱 Core에서 이동)
- `hw/src/MobiMateHomeWork` — WPF 앱
- `hw/tests/MobiMate.Core.Tests`, `hw/tests/MobiMateHomeWork.Tests` — 테스트
- `hw/tools/FakeCli` — 가짜 게임 CLI, `hw/tools/SharedStorageHarness` — 동시 쓰기 하네스, `hw/tools/IconGen` — 아이콘 생성
- `docs/` — 공유 요구사양·CLI 스펙·H6 점검표, `PLAN-MobiMateHomeWork.md` — 단계 계획, `CLAUDE.md` — 작업 규칙

빌드와 테스트

```
dotnet build hw/MobiMateHomeWork.sln
dotnet test hw/MobiMateHomeWork.sln
```

게시(단일 exe + zip)

```
dotnet publish hw/src/MobiMateHomeWork -c Release -o hw/dist
```

`hw/dist/MobiMateHomeWork.exe`(win-x64, framework-dependent 단일 파일, 기호 없음)와 전달용 `hw/dist/MobiMateHomeWork.zip`(exe만)이 만들어집니다. `hw/dist/`는 커밋하지 않습니다. 실제 저장 폴더가 아닌 임시 폴더로 실행해 보려면:

```
MobiMateHomeWork.exe --storage <임시폴더> --cli <가짜CLI 경로>
```

아이콘을 바꿀 때는 `hw/tools/IconGen/Program.cs`를 고치고 `dotnet run --project hw/tools/IconGen -- hw/src/MobiMateHomeWork/app.ico`로 `app.ico`를 다시 만들어 커밋합니다.

## 웹앱 문서

웹앱 요구사양·설계는 이 브랜치에 없습니다. `git show webapp:web/REQUIREMENTS.md` 로 읽습니다.
