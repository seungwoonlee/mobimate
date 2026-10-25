# AutoReplay — 오토 의심 기준 점검 도구

앱이 판(던전·어비스·레이드) 안에서 늘 남기는 관찰 기록과 사람이 확인한 정답을 모아 두고, **지금 코드의 기준**으로 다시 돌려 오토는 몇 명 잡고 정상은 몇 명 오탐하는지 센다.
기준(`MobiMate.Core/Game/AutoSuspicionTracker.cs`의 상수)을 바꾼 뒤 같은 데이터로 다시 돌리면 전후를 비교할 수 있다.

## 실행

```
dotnet run --project web/tools/AutoReplay -c Release
```

코퍼스 폴더를 인자로 줄 수 있다(기본: 이 폴더의 `corpus/`).

## 코퍼스 (`corpus/`)

- `날짜.jsonl`: 앱의 `AutoWatchRecorder`가 쓴 관찰 기록. 원본은 `%APPDATA%\MobiMateWeb\logs\watch-yyyyMMdd.jsonl`이고, 정답이 생긴 날의 파일을 여기에 복사해 둔다.
- `labels.jsonl`: 판마다 한 줄. 필드
  - `file`, `from`, `to`(시각, 그 파일 안의 시간대), `space`(맵 이름 접두어), `note`
  - `maxPlayers`: 한 번에 보이는 사람이 이 수를 넘는 기록은 뺀다(마을·입구 인파). 0이면 제한 없음
  - `minSeen`: 이 초 수 이상 본 사람만 채점(기본 20)
  - `auto`: 승운이 오토로 확인한 사람 `[{job,title}]`, `human`: 정상으로 확인한 사람, `othersHuman`: 목록 밖은 모두 정상(직접 모집 파티)
  - 정답을 모르는 사람은 채점하지 않는다(`?`로 표시).

## 정답이 생기면

승운이 판이 끝나고 알려 준 오토(직업·칭호)를 `labels.jsonl`에 한 줄 더하고, 그날 `watch-날짜.jsonl`을 `corpus/`에 복사한다. 오토 정답이 쌓일수록 기준을 얼마나 낮추거나 올릴지 숫자로 정할 수 있다.
