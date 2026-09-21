# MabiMate 상세설계 (개정판 v2): AI 엔진 표시, 1줄 탭 & 대형 폰트, 캐릭터 스냅샷 세션 누적 변화량 추적

---

## 1. 개요 및 설계 목표

1. **AI 엔진/모델 식별 및 전환**:
   - AI 질문 창 상단에 현재 어떤 AI 엔진/모델(`로컬 내장 코파일럿`, `Ollama: gemma4:31b`, `외부 LLM`)과 연결되어 있는지 상시 표기.
   - 드롭다운(ComboBox, `MaxWidth="190"`)을 통해 유저가 엔진을 전환할 수 있으며, 전환 시 대화창에 시스템 알림 메시지 삽입.
   - 백그라운드 핑(Ping) 헬스체크를 통해 🟢 온라인 / 🔴 오프라인 상태 램프 표시.

2. **UI 스케일 1.5~2배 대형화 & 1줄 탭 고정 (반응형 래퍼)**:
   - **글자 크기**: 기본 13~14pt -> **17~20pt**, 대제목 **22~26pt**, 핵심 수치 **28~34pt**로 대폭 확대.
   - **탭 1줄 고정 및 스크롤 래퍼**:
     - `ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled"`로 감싸 2줄 래핑을 원천 차단.
     - 가로폭이 좁아져도 마우스 휠이나 스크롤바로 6개 탭 전체를 손쉽게 탐색 가능.
     - 본문 카드 내부 패딩을 16px -> 12px로 슬림화하여 세로 오버플로 완화.

3. **캐릭터 식별 및 이중 기준(Dual-Baseline) 세션 변화량(Delta) 시스템**:
   - **캐릭터 식별(Identity) 강화**:
     - 1차 키: `[RealmName]_[JobName]` (예: `아이라_격투가`)
     - 2차 가중치 매칭: `Level` (동일하거나 1~2 상승 허용), `Title`(칭호 일치), 최근 접속 세션 타임스탬프 최우선 바인딩.
     - 장비 탈착으로 전투력이 15~30% 급변해도 동일 캐릭터로 완벽 식별.
   - **이중 기준(Dual-Baseline) 변화량 추적**:
     - **세션 누적 기준 (`SessionBaseline`)**: 앱 기동/캐릭터 최초 접속 시점의 상태 보존 -> 새로고침을 수십 번 해도 **"이번 세션 누적 획득량"** (예: `골드 +250,000 G ▲`, `전투력 +150 ▲`)이 유지됨!
     - **직전 틱 기준 (`PreviousSnapshot`)**: 직전 새로고침 대비 변화량 (툴팁이나 상세 확인 시 활용).
     - **로컬 영구 보존**: `character_snapshots.json`에 캐릭터별 세션 누적 및 최종 스냅샷 저장.

---

## 2. 세부 구현 아키텍처

### (1) 스냅샷 모델 (`SnapshotManager.cs`)
```csharp
public class CharacterSnapshot
{
    public string RealmName { get; set; } = "";
    public string JobName { get; set; } = "";
    public int Level { get; set; }
    public string Title { get; set; } = "";
    public long CombatScore { get; set; }
    public double WeightCurrent { get; set; }
    public long Gold { get; set; }
    public long Wings { get; set; }
    public long NyangToken { get; set; }
    public int CompletedDailyMissions { get; set; }
    public DateTime Timestamp { get; set; }
}

public class SessionDelta
{
    public long CombatScoreDiff => Current.CombatScore - Baseline.CombatScore;
    public double WeightDiff => Current.WeightCurrent - Baseline.WeightCurrent;
    public long GoldDiff => Current.Gold - Baseline.Gold;
    public long WingsDiff => Current.Wings - Baseline.Wings;
    public long NyangDiff => Current.NyangToken - Baseline.NyangToken;
    public int MissionDiff => Current.CompletedDailyMissions - Baseline.CompletedDailyMissions;

    public CharacterSnapshot Baseline { get; set; }
    public CharacterSnapshot Current { get; set; }
}
```

### (2) UI 렌더링 방식
- 수치 옆에 배지 형태로 표시:
  - 전투력: `88,737` `(+150 ▲)` (초록색 뱃지)
  - 골드: `11,144,590 G` `(+120,000 ▲)`
  - 정령의 날개: `18,854개` `(-5 ▼)` (붉은색/주황색 뱃지)
  - 가방 무게: `854.6` `(-15.2 ▼)` (가방 무게 감소는 좋은 것이므로 초록색)

---

## 3. UI 와이어프레임 (개정)

```
┌────────────────────────────────────────────────────────────────────────────────────────┐
│ 🟢 [아이라] 격투가 Lv.100  ⚔️ 88,737 (+150 ▲)  📍 페카 고분 심층 │ [🛑] [📌] [🔄 탭 새로고침] │
├────────────────────────────────────────────────────────────────────────────────────────┤
│ < [📊 캐릭터&스탯] [🎒 가방&아이템] [💰 재화] [📜 미션] [🌿 생활] [👥 주변] > (1줄 스크롤)│
├────────────────────────────────────────────────────────────────────────────────────────┤
│                                                                                        │
│  ▲ 상단 정보 탭 (18~22pt 대형 폰트 / 세션 누적 변화량 배지 표시)                         │
│                                                                                        │
├════════════════════════════════════════════════════════════════════════════════════════┤
│ ▼ 하단 상시 고정 2분할 채팅창                                                           │
│ ┌──────────────────────────────────────┬─────────────────────────────────────────────┐ │
│ │ 📢 인게임 전체 채팅 (최대 50자)       │ 🤖 AI 대화 [엔진: 🟢 Ollama gemma4:31b ▼]   │ │
│ │  (16pt 대형 폰트 / Enter 발송)       │  (모델 전환 ComboBox & 온라인 램프)         │ │
│ └──────────────────────────────────────┴─────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────────────────────────────┘
```
