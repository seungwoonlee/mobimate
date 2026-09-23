# MobiMate WPF판 결함 목록 및 인수인계 (v1.0)

> **작성**: Claude Code, 2026-09-23 · **기준 코드**: `master@f64d2f7`
> **대상**: master 브랜치에서 WPF판 작업을 이어가는 에이전트 (Antigravity)
> **근거**: 웹앱 전환 작업(`webapp` 브랜치) 중 WPF판 코드를 옮기면서 발견한 결함이다. 모든 항목은 코드를 직접 읽거나 테스트로 재현해서 확인했다. 줄 번호는 `master@f64d2f7` 기준이다.

---

## 0. 작업 전에 반드시 알아야 할 것

1. **웹앱은 `webapp` 브랜치의 별개 프로젝트다.** 두 프로젝트는 요구사양 문서만 공유하고 코드·빌드·저장 데이터는 공유하지 않는다. 두 브랜치는 서로 병합하지 않는다. master 작업 폴더는 `V:\workspace\MobiMate`, webapp은 `V:\workspace\MobiMate-web`(git worktree)이다.
2. **한 폴더에서 브랜치를 바꾸지 않는다.** 바꾸면 웹앱 빌드 산출물(`web/**/obj/*.cs`)이 남아 WPF 빌드가 `CS0579 특성이 중복되었습니다`로 깨진다. 안전장치로 `MobiMate.csproj`에 `web\**` 제외를 넣어 두었다. 지우지 않는다.
3. **테스트가 실제 사용자 데이터를 오염시킨다** (§2 W-09). 테스트를 돌리기 전에 W-09부터 고친다. 2026-09-23에 오염된 기록을 한 번 정리했다. 백업: `%APPDATA%\MobiMate_backup_20260923_034009`.
4. **고친 구현을 참고할 수 있다.** 같은 결함을 웹앱용 공용 로직(`MobiMate.Core`)에서 먼저 고쳤다. 코드는 `webapp` 브랜치에 있으며 다음처럼 볼 수 있다.
   ```bash
   git show webapp:web/src/MobiMate.Core/Cli/GameCli.cs
   git show webapp:web/src/MobiMate.Core/Intent/CommandIntentParser.cs
   ```
   **참고만 한다.** 이 파일들을 master에 복사하거나 참조하지 않는다(코드 분리 원칙). 네임스페이스와 API도 다르다. 같은 원리로 WPF판 코드에 맞게 직접 고친다.
5. **프로젝트 규칙(`GEMINI.md`)은 그대로 적용된다**: 서브에이전트 리뷰 단계, 결론 우선 보고, master 직접 커밋 금지(작업 브랜치 → 병합).

---

## 1. 요약

| ID | 심각도 | 한 줄 요약 | 사용자 영향 |
|---|---|---|---|
| W-01 | **높음** | 가방 과적 판정 정규식이 소수점 앞을 버린다 | 가방이 100%를 넘어도 과적 대사가 **한 번도 나오지 않는다** |
| W-02 | **높음** | 채집 중에는 긴급 정지가 최대 120초 막힌다 | 채집 도중 🛑 정지를 눌러도 채집이 끝날 때까지 안 멈춘다 |
| W-03 | **높음** | 채집 목표 수량을 게임에 보내지 않는다 | "목표 50개"로 설정해도 게임은 기본 수량만큼 채집한다 |
| W-04 | **높음** | AI 질문에 "정지"만 들어가도 긴급 정지가 실행된다 | "정지 기능 알려줘"라고 물으면 캐릭터가 멈춘다 |
| W-05 | 중간 | 자연어 채집이 확인 없이 정령의 날개를 쓴다 + 이름 오인식 | "사과 파이 채집해줘"로 사과 채집이 시작되고 날개 5개가 빠진다 |
| W-06 | 중간 | CLI JSON 본문을 문자열 보간으로 만든다 | 이름에 `"`나 `\`가 있으면 명령이 깨진다 |
| W-07 | 중간 | 저장이 동시에 여러 번 돌며 임시 파일이 쌓인다 | `%APPDATA%\MobiMate`에 `*.tmp.*` 고아 파일이 쌓인다(실측 21개) |
| W-08 | 중간 | 커스텀 페르소나의 폴백 대사가 악덕영애 말투다 | AI 응답이 늦으면 내가 만든 페르소나가 악덕영애 말투로 말한다 |
| W-09 | 중간 | 테스트가 실제 `%APPDATA%`에 테스트 캐릭터를 기록한다 | 테스트를 돌릴 때마다 `테스트_xxxx` 캐릭터가 사용자 기록에 쌓인다 |
| W-10 | 낮음 | 이모지가 들어간 채팅의 50자 계산이 어긋날 수 있다 | 끝 글자가 말없이 잘릴 수 있다 |
| W-11 | 낮음 | `send_chat`을 쓰는 죽은 코드 | 현재는 영향 없음. 다시 쓰이면 채팅이 안 나간다 |
| W-12 | 낮음 | 요구사양서의 가방 경고 기준이 코드와 다르다 | 문서만 틀림 |

---

## 2. 상세

### W-01 가방 과적 판정이 항상 실패한다 — 높음

- **위치**: `ChatterPersona.cs:72` `Regex.Match(weight, @"(\d+)%")`
- **원인**: 판정에 넘기는 문구는 `MainWindow.xaml.cs:468`에서 만든 `"{curW:F1} / {maxW:F1} ({pct:F1}%)"`이다. 예: `"1040.0 / 1030.0 (101.0%)"`. 정규식이 `%` 바로 앞 숫자 `0`만 잡아서 0%로 판정한다.
- **재현**: `WeightSummary = "1040.0 / 1030.0 (101.0%)"`로 `PersonaTemplates.DetermineCategory`를 부르면 `"가방_과적"`이 아니라 `"채집_자연"` 같은 다른 분류가 나온다.
- **왜 테스트가 못 잡았나**: 기존 테스트(`InGameChatterTests.cs`의 `PersonaTemplates_WeightCategory_OnlyTriggersWhen100PercentOrAbove`)는 `"105%"`처럼 소수점 없는 문구만 쓴다.
- **수정**:
  ```csharp
  var match = Regex.Match(weight, @"(\d+(?:\.\d+)?)\s*%");
  if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
  {
      if (pct >= 100) return "가방_과적";
  }
  ```
- **테스트 추가**: `"1040.0 / 1030.0 (101.0%)"` → 과적, `"1029.9 / 1030.0 (99.9%)"` → 과적 아님.

### W-02 채집 중 긴급 정지가 막힌다 — 높음

- **위치**: `GameCliService.cs:15, 47` (단일 `SemaphoreSlim _cliLock`), `MainWindow.xaml.cs:947` (`execute_gathering`, 타임아웃 120초), `:1023`·`:1610` (`stop_action`)
- **원인**: 모든 CLI 호출이 잠금 하나를 공유한다. 채집은 최대 120초 동안 잠금을 쥐고 있고, `stop_action`은 그 뒤에 줄을 선다. 조회(자동 새로고침)도 같이 멈춘다.
- **수정**: 호출을 명령별로 세 갈래로 나눈다.
  - `stop_action`, `stand_up`: 잠금을 쓰지 않는다 (우선 경로)
  - `execute_gathering`: 전용 잠금을 쓴다 (장시간 경로)
  - 나머지 명령: 기존 잠금을 쓴다 (일반 경로)
- **참고 구현**: `git show webapp:web/src/MobiMate.Core/Cli/GameCli.cs`의 `CliLanes`, `GameCli.RunAsync`.
- **확인 필요(실기)**: 게임 CLI가 채집 프로세스와 `stop_action`을 동시에 받아들이는지 실제 게임에서 확인한다. 받아들이지 않으면 채집 프로세스를 `Kill(entireProcessTree: true)`로 끝낸 뒤 `stop_action`을 보낸다.

### W-03 채집 목표 수량이 게임에 전달되지 않는다 — 높음

- **위치**: `MainWindow.xaml.cs:936` (부족 수량 `needed` 계산), `:940` `StartGatherAsync(string itemName, int neededCount)`, `:946` 본문 생성
- **원인**: `neededCount`를 토스트 문구에만 쓰고, CLI 본문은 `{"displayName":"..."}`만 보낸다. CLI 스펙(`API_SPEC_SAMPLES.md` §8)은 `{"displayName":"사과","count":5}`이다.
- **수정**: 본문에 `count = neededCount`를 넣는다(W-06처럼 직렬화기로). `neededCount`가 0이면 `count`를 생략한다.
- **확인 필요(실기)**: 게임이 `count`를 실제로 지키는지(`gained` 값) 확인한다. 지키지 않으면 부족 수량에 도달했을 때 앱이 `stop_action`을 보내는 방식으로 대체한다.

### W-04 "정지"가 들어간 질문이 긴급 정지를 실행한다 — 높음

- **위치**: `MainWindow.xaml.cs:1489` `if (query.Contains("정지") || query.Contains("멈춰"))`
- **재현**: AI 입력창에 "정지 기능 알려줘", "멈춰 있는 캐릭터는 어떻게 해?"를 입력하면 캐릭터가 멈춘다.
- **수정**: 입력 전체가 짧은 명령일 때만 정지한다. 공백과 문장부호를 지운 뒤 다음 정규식에 맞아야 한다.
  ```csharp
  var flat = Regex.Replace(query, @"[\s\p{P}\p{S}]", "");
  if (Regex.IsMatch(flat, @"^(긴급)?(정지|멈춰|스톱|그만)(해|해줘|해라)?$")) { /* 정지 */ }
  ```
- **참고 구현과 테스트**: `git show webapp:web/src/MobiMate.Core/Intent/CommandIntentParser.cs`, `git show webapp:web/tests/MobiMate.Core.Tests/CoreRulesTests.cs`의 `CommandIntentParserTests`.

### W-05 자연어 채집이 확인 없이 실행되고 이름을 잘못 알아듣는다 — 중간

- **위치**: `MainWindow.xaml.cs:1544` `FirstOrDefault(g => query.Contains(g.DisplayName, ...))`, `:1548` `_ = StartGatherAsync(matched.DisplayName)`
- **문제**:
  1. 확인 없이 바로 채집한다. 채집할 때마다 정령의 날개 5개가 든다. 채집 도우미 버튼도 확인을 거치지 않는다.
  2. 단순 포함 검사라서 "사과 파이 채집해줘", "사과파이 캐줘"도 사과 채집이 된다.
  3. 수량을 읽지 않는다. "사과 20개 캐줘"도 수량 없이 보낸다.
- **수정**:
  - 채집 전에 확인 창을 띄운다. 내용은 아이템명, 목표 수량, "정령의 날개 5개 소모"이다.
  - 아이템명 바로 뒤에 다른 명사가 붙으면 제외한다. 허용하는 것은 공백·조사(을/를/좀/도/만)·숫자·채집 동사뿐이다.
  - 수량은 `(\d+)\s*(개|마리)`를 먼저 찾는다. 없으면 단독 숫자를 쓰되 `Lv.100` 같은 레벨 표기는 뺀다.
- **참고 구현**: W-04와 같은 파일의 `MentionsItem`, `ParseCount`.

### W-06 CLI JSON 본문을 문자열 보간으로 만든다 — 중간

- **위치**: `MainWindow.xaml.cs:946` (`execute_gathering`), `:1049` (`complete_altering_work`). `InGameChatterService.cs:145`의 `send_chat` 본문은 `EscapeJson`으로 `\`·`"`를 이스케이프하므로 이 결함에 해당하지 않는다(W-11에서 함께 정리).
- **문제**: `$"{{\"displayName\":\"{itemName}\"}}"`처럼 만든다. 이름에 `"`나 `\`가 들어가면 JSON이 깨진다.
- **수정**: `JsonSerializer.Serialize(new { displayName = itemName, count = n })`로 만든다. 한글이 `\uXXXX`로 바뀌는 것이 싫으면 `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`을 쓴다.

### W-07 저장 경합으로 임시 파일이 쌓인다 — 중간

- **위치**: `SnapshotManager.cs:286` `SaveSnapshotsAsync() => Task.Run(SaveSnapshotsNow)`, `:271` `File.Move(tempFile, _storageFile, overwrite: true)`, `:539`·`:558` 커스텀 페르소나는 `File.WriteAllText`로 바로 덮어씀
- **문제**:
  - 새로고침할 때마다 `Task.Run`이 겹쳐서 여러 저장이 동시에 돈다.
  - 한 저장이 대상 파일을 쓰고 있을 때 다른 저장의 `File.Move`가 실패하고, `catch`가 조용히 삼킨다. 그 결과 `*.tmp.<guid>` 파일이 남는다. 2026-09-23에 실측한 고아 파일은 21개였다.
  - 커스텀 페르소나 파일은 쓰는 도중에 앱이 꺼지면 깨진다.
- **수정**:
  - 파일 경로별 잠금으로 저장을 직렬화한다.
  - 대상 파일이 있으면 `File.Replace(temp, target, target + ".bak")`, 없으면 `File.Move`를 쓴다.
  - 실패하면 임시 파일을 지운다.
  - 커스텀 페르소나도 같은 방식으로 저장한다.
- **참고 구현**: `git show webapp:web/src/MobiMate.Core/Storage/JsonFileStore.cs`.

### W-08 커스텀 페르소나 폴백이 악덕영애 말투다 — 중간

- **위치**: `ChatterPersona.cs:52` `ChatterPersona.Custom => PersonaTemplatesData.VillainessLines`
- **문제**: 내장 가이드 모드에서는 커스텀 페르소나를 기본 페르소나로 되돌린다(`MainWindow.xaml.cs:1407`). 그래서 이 결함은 **AI 엔진이 실패하거나 2.5초 안에 답하지 않을 때만** 드러난다.
- **수정**: 특정 말투가 없는 중립 대사 풀을 만들어 커스텀 폴백에 쓴다. 폴백이 쓰였다면 "AI 응답 지연으로 기본 대사를 넣었습니다" 토스트를 띄운다.
- **참고 구현**: `git show webapp:web/src/MobiMate.Core/Chatter/NeutralPersonaLines.cs`.

### W-09 테스트가 실제 사용자 데이터를 오염시킨다 — 중간 (테스트 전에 먼저 수정)

- **위치**: `tests/MobiMate.Tests/InGameChatterTests.cs:224` `var sm = new SnapshotManager();`
- **문제**: 인자 없는 생성자는 실제 `%APPDATA%\MobiMate`를 쓴다. 테스트를 돌릴 때마다 `테스트_xxxxxxxx_테스트직업` 캐릭터가 `character_history_db.json`과 `character_snapshots.json`에 쌓인다. 2026-09-23 기준 78건이 쌓여 있었다.
- **수정**: 같은 파일의 커스텀 페르소나 테스트처럼 임시 폴더를 넘긴다(`new SnapshotManager(tempDir)`). 끝나면 `finally`에서 폴더를 지운다.
- **추가 점검**: `AiEngineManager` 테스트가 실제 `where.exe`와 `localhost:11434`를 조회한다. PC 환경에 따라 결과가 달라지므로, 가능하면 탐색 함수를 주입해 가짜로 바꾼다.

### W-10 채팅 50자 계산이 이모지에서 어긋난다 — 낮음

- **위치**: `ChatPlanService.cs:107~113` (`maxBodyLength = MaxChatLength - suffix.Length`), `GameCliService.cs:166` (`sanitized.Length > 50`)
- **문제**: `string.Length`는 UTF-16 단위라 이모지를 2자로 센다. 게임이 실제로 어떤 단위로 50자를 세는지는 아직 확인하지 않았다. 입력창 카운터는 원문 길이만 보여 주므로, 이모지를 붙이면 끝 글자가 말없이 잘릴 수 있다.
- **할 일**:
  1. 실제 게임에서 이모지를 넣어 49·50·51자를 보내 보고, 게임이 세는 단위를 확인한다.
  2. 글자 수 계산을 한 함수로 모은다.
  3. 카운터는 이모지를 붙인 최종 문장 길이를 보여 준다.

### W-11 `send_chat` 죽은 코드 — 낮음

- **위치**: `InGameChatterService.cs:146` `_cli.RunRawAsync("send_chat", stdinJson: body, ...)` (`TriggerChatterAsync` 안)
- **문제**: CLI에는 `send_chat` 명령이 없다. 채팅 명령은 `write_chat` + 인자 문구다(`API_SPEC_SAMPLES.md` §8). 이 메서드는 UI에서 부르지 않고 테스트에서만 부른다. 실제 "💬 한마디"는 입력창을 채우기만 한다.
- **수정**: 메서드를 지우거나, 전송을 `GameCliService.SendGameChatAsync`로 바꾼다. 관련 테스트도 함께 정리한다.

### W-12 요구사양서의 가방 경고 기준이 코드와 다르다 — 낮음

- **위치**: `REQUIREMENTS.md:71` "80% 이상 주황색, 90% 이상 빨간색" ↔ `MainWindow.xaml.cs:461~490` 95% 주의 / 100% 위험
- **수정**: 최신 피드백을 반영한 코드 기준(95/100)으로 문서를 고친다.

---

## 3. 권장 작업 순서

1. **W-09** 테스트 격리. 이후 테스트가 사용자 데이터를 더럽히지 않게 가장 먼저 한다.
2. **W-01, W-04** 작은 수정이고 사용자 체감이 크다. 회귀 테스트를 함께 넣는다.
3. **W-06 → W-03 → W-05** 채집 관련. 본문 직렬화를 먼저 고치고, 그 위에 수량 전달과 확인 창을 얹는다.
4. **W-02** CLI 잠금 분리. 실제 게임으로 정지 동작을 확인한다.
5. **W-07, W-08** 저장 안정성과 폴백 대사.
6. **W-10, W-11, W-12** 정리.

각 단계는 `GEMINI.md` 규칙대로 **계획 → 서브에이전트 리뷰 → 구현 → 리뷰 → 테스트 → 리뷰**를 거친다. 작업 브랜치에서 하고 master에 병합한다.

## 4. 검증 기준

- `dotnet build`: 경고·오류 0
- `dotnet test tests/MobiMate.Tests`: 전부 통과
- 테스트 전후로 `%APPDATA%\MobiMate`의 캐릭터 수와 파일 수가 변하지 않는다 (W-09)
- 실기 확인 3건: 채집 중 정지(W-02), `count` 반영(W-03), 이모지 50자(W-10). 확인 결과는 이 문서 §5에 기록한다.

## 5. 실기 확인 기록 (작업자가 채운다)

| 항목 | 날짜 | 결과 | 비고 |
|---|---|---|---|
| W-02 채집 중 `stop_action` 병행 실행 | | | |
| W-03 `execute_gathering`의 `count` 반영 | | | |
| W-10 게임 채팅 50자 단위 | | | |
