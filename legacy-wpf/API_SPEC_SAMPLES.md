# 마비노기 모바일 CLI API 전체 스펙 및 실제 응답 샘플 (실측 데이터)

> **측정 환경**: 마비노기 모바일 PC 클라이언트 실행 중 (접속 캐릭터: [아이라] 격투가 Lv.100)  
> **CLI 파일**: `C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe`  
> **총 지원 명령**: 28개 (조회 20개 + 조작/실행 8개)

---

## 1. 캐릭터 기본 및 스탯 조회

### `get_my_info`
- **설명**: 캐릭터의 칭호, 렐름(서버), 레벨, 직업, 각종 점수(전투력/생활력/매력/데코), 기본 스탯(STR/DEX/INT/LUCK/WILL), 성기사 스탯, 현재 체력/만복도/가방 무게/버프 개수 조회
- **실제 응답 샘플**:
```json
{
  "Title": "이거밖에 안 되심?",
  "RealmName": "아이라",
  "Level": 100,
  "EnabledCombatJobDisplayName": "격투가",
  "CombatScore": { "DisplayName": "전투력", "Value": 88737 },
  "LivingScore": { "DisplayName": "생활력", "Value": 23011 },
  "AttractivenessScore": { "DisplayName": "매력", "Value": 19745 },
  "DecorScore": { "DisplayName": "데코 점수", "Value": 5445 },
  "HealthMax": { "DisplayName": "최대 체력", "Value": 72614 },
  "AttackPower": { "DisplayName": "공격력", "Value": 33338 },
  "DefencePower": { "DisplayName": "방어력", "Value": 19178 },
  "ArcaneResistance": { "DisplayName": "마도 저항", "Value": 4316 },
  "STR": { "DisplayName": "힘", "Value": 4996 },
  "DEX": { "DisplayName": "솜씨", "Value": 6019 },
  "INT": { "DisplayName": "지력", "Value": 5116 },
  "LUCK": { "DisplayName": "행운", "Value": 5402 },
  "WILL": { "DisplayName": "의지", "Value": 4812 },
  "PaladinStats": {
    "PaladinAttackPower": { "DisplayName": "신성력", "Value": 54 },
    "PaladinDefencePower": { "DisplayName": "항마력", "Value": 54 },
    "JusticePower": { "DisplayName": "정의", "Value": 694 },
    "JudgementPower": { "DisplayName": "심판", "Value": 459 },
    "OrderPower": { "DisplayName": "질서", "Value": 694 },
    "BlessingPower": { "DisplayName": "가호", "Value": 459 }
  },
  "Vitals": {
    "HealthCurrent": 72614,
    "HealthMax": 72614,
    "ShieldAmount": 0,
    "ShieldMax": 0,
    "SatietyValue": 0,
    "SatietyMax": 100,
    "SatietyRatio": 0.0,
    "InventoryWeightCurrent": 854.5651,
    "InventoryWeightMax": 1030.0,
    "ActiveBuffCount": 2
  }
}
```

---

## 2. 위치, 환경 및 실시간 활동 상태

### `get_current_environment`
- **설명**: 현재 채널/지역명, 공간명, 월드 좌표(X, Y), 날씨, 에린 현재 시각, 하우징 진입 가능 여부
- **실제 응답 샘플**:
```json
{
  "ChannelDisplayName": "페카 고분 심층 2층 3구역",
  "GameSpaceDisplayName": "페카 고분 심층 2층 3구역",
  "WorldPosition": { "X": 82.4251, "Y": 33.1718 },
  "Weather": "Sunny",
  "ErinnNow": "2959-4-23 15:51",
  "Housing": {
    "IsInHousing": false,
    "IsOwnedHousing": false,
    "CanEnterHousing": true,
    "CannotEnterHousingReason": "",
    "CanExitHousing": false
  }
}
```

### `get_activity`
- **설명**: 현재 캐릭터의 자동사냥(AutoPlay), 자동이동(AutoTravel), 전투중 여부, 연주중 여부, 의자 착석 여부, 과적(Overweight) 여부 등 **캐릭터의 현재 실시간 행동 상태 전체**
- **실제 응답 샘플**:
```json
{
  "IsAutoPlaying": false,
  "CanStartAutoPlay": false,
  "AutoPlayTarget": null,
  "AutoPlayTargetDisplayName": null,
  "IsAutoTraveling": false,
  "AutoTravelDestination": null,
  "IsInCombat": false,
  "IsDead": false,
  "IsTalking": false,
  "IsGathering": false,
  "IsAltering": false,
  "IsCrafting": false,
  "IsPlayingInstrument": false,
  "IsCarrying": false,
  "IsSitting": false,
  "IsOverweight": false,
  "CurrentAction": "Idle",
  "CanStopCurrentAction": false
}
```

---

## 3. 인벤토리, 아이템 및 재화

### `get_inventory`
- **설명**: 현재 가방 무게와 최대 허용 무게 (소수점 정밀도 포함)
- **실제 응답 샘플**:
```json
{
  "CurrentInventoryWeight": 854.5651,
  "CurrentInventoryWeightAsDecimal": "854.5651",
  "MaxInventoryWeight": 1030.0,
  "MaxInventoryWeightAsDecimal": "1030"
}
```

### `get_currencies`
- **설명**: M캐시, 골드, 정령의 날개, 냥 토큰, 길드/하트 토큰, 각종 레이드 증거 등 전체 28가지 재화 잔액
- **실제 응답 샘플 (주요 발췌)**:
```json
[
  { "DisplayName": "골드", "Amount": 11144590 },
  { "DisplayName": "정령의 날개", "Amount": 18854 },
  { "DisplayName": "냥 토큰", "Amount": 124017 },
  { "DisplayName": "길드 토큰", "Amount": 4855 },
  { "DisplayName": "하트 토큰", "Amount": 1708 },
  { "DisplayName": "M캐시", "Amount": 5619 },
  { "DisplayName": "데카", "Amount": 10285 },
  { "DisplayName": "환생석", "Amount": 15 },
  { "DisplayName": "갱신권", "Amount": 673 }
]
```

### `get_items`
- **설명**: 가방, 계정 창고, 캐릭터 창고에 있는 모든 아이템(장비, 소모품, 재료, 음식 등 총 600여 개)의 위치, 이름, 카테고리, 개수, 잠금 여부
- **실제 응답 샘플 (일부 발췌)**:
```json
[
  { "Location": "Bag", "DisplayName": "나무 장작", "Category": "Material", "CategoryDisplayName": "재료", "Count": 15, "IsLocked": false },
  { "Location": "Bag", "DisplayName": "사과", "Category": "Food", "CategoryDisplayName": "음식", "Count": 8, "IsLocked": false },
  { "Location": "AccountStorage", "DisplayName": "철괴", "Category": "Material", "CategoryDisplayName": "재료", "Count": 200, "IsLocked": true }
]
```

---

## 4. 미션 및 퀘스트

### `get_daily_missions` & `get_weekly_missions`
- **설명**: 일일 미션(11개) 및 주간 미션(15개) 제목, 설명, 진행도(CurrentCount/GoalCount), 완료 여부(IsCompleted), 보상 수령 여부(IsRewardReceived)
- **실제 응답 샘플**:
```json
[
  {
    "Title": "에린에 돌아왔습니다",
    "Description": "접속하기",
    "CurrentCount": 1,
    "GoalCount": 1,
    "IsCompleted": true,
    "IsRewardReceived": true,
    "HasShortcut": true
  },
  {
    "Title": "오늘도 던전 한 바퀴",
    "Description": "던전 3회 토벌",
    "CurrentCount": 3,
    "GoalCount": 3,
    "IsCompleted": true,
    "IsRewardReceived": true,
    "HasShortcut": true
  },
  {
    "Title": "자급자족의 삶",
    "Description": "재료 아이템 3회 채집",
    "CurrentCount": 3,
    "GoalCount": 3,
    "IsCompleted": true,
    "IsRewardReceived": true,
    "HasShortcut": true
  }
]
```

### `get_quests`
- **설명**: 현재 우측 퀘스트 트래커에 등록된 주요 퀘스트(메인/가이드/사이드) 진행 상태 및 세부 목표
- **실제 응답 샘플**:
```json
[
  {
    "QuestTitle": "새로운 시작",
    "Source": "Guide",
    "SourceDisplayName": "가이드",
    "Objectives": [
      { "Description": "던바튼 관청의 에반과 대화", "IsCompleted": false }
    ]
  }
]
```

---

## 5. 주변 플레이어 및 NPC

### `get_near_pcs`
- **설명**: 현재 내 주변에 있는 다른 유저들(최대 수십 명)의 거리(Distance), 닉네임/칭호, 레벨, 직업, 전투력, 생활력, **길드원 여부(IsSameGuild)**, **파티원 여부(IsInParty)**, **친구 여부(IsFriend)**, 연주 중 여부
- **실제 응답 샘플 (유저 1명 발췌)**:
```json
{
  "RealmName": "아이라",
  "Title": "어둠을 가르는",
  "Distance": 4.82,
  "Level": 100,
  "EnabledCombatJobDisplayName": "대검전사",
  "CombatScore": 92450,
  "LivingScore": 18200,
  "IsFriend": false,
  "IsInParty": false,
  "HasGuild": true,
  "IsSameGuild": true,
  "IsInCombat": false
}
```
*(💡 활용: "근처에 같은 길드원이 나타나면 알림 띄우기" 또는 "파티원 체력/전투 상태 감지" 가능)*

### `get_near_npcs`
- **설명**: 주변 대화 가능한 NPC 목록 및 거리 (현재 던전 내부라 빈 리스트 반환)

---

## 6. 악기 및 음악 연주

### `get_instruments` & `get_music_scores`
- **설명**: 보유 중인 악기(류트, 플루트 등)와 내구도/장착 여부, 보유 악보 제목 및 잠금 여부
- **실제 응답 샘플**:
```json
// 악기 목록
[
  { "Name": "류트", "Durability": 100, "IsEquipped": true },
  { "Name": "플루트", "Durability": 85, "IsEquipped": false }
]
// 악보 목록
[
  { "DisplayTitle": "할머니가 들려주신 옛 전설", "Location": "Bag", "IsLocked": false }
]
```

---

## 7. 생활/생산 (채집, 제작, 연금 작업 대기열)

### `get_gatherable_items`
- **설명**: 현재 생활 스킬로 채집 가능한 150여 개 아이템 목록 및 필수 도구 보유 여부(`ToolOk`)
```json
{
  "items": [
    { "DisplayName": "달걀", "ToolOk": true },
    { "DisplayName": "사과", "ToolOk": true },
    { "DisplayName": "나무 장작", "ToolOk": true },
    { "DisplayName": "철광석", "ToolOk": true }
  ]
}
```

### `get_altering_works` & `get_craftable_items`
- **설명**: 현재 진행 중인 연금/가공 작업대 현황(남은 시간, 완료 수량 `completedCount`) 및 제작 레시피
```json
{
  "completedCount": 2,
  "works": [
    { "DisplayName": "질긴 가죽", "FacilityName": "가죽 작업대", "State": "Completed", "RemainingSeconds": 0 },
    { "DisplayName": "최고급 실크", "FacilityName": "베틀", "State": "InProgress", "RemainingSeconds": 145 }
  ]
}
```
*(💡 활용: "2분 뒤 가공 완료 알림", "수거 버튼 클릭 시 complete_altering_work 자동 호출")*

---

## 8. 실행/조작 API 목록 (Action & Chat)

| 명령 | 인자 형식 | 동작 내용 |
|---|---|---|
| **`write_chat`** | 커맨드라인 argv: `"<문구>"` | 게임 내 전체 채팅 즉시 발송 (최대 50자) |
| **`execute_gathering`** | stdin JSON: `{"displayName":"사과","count":5}` | 지정한 아이템을 목표 수량만큼 이동하여 자동 채집 |
| **`execute_crafting`** | stdin JSON: `{"displayName":"레시피명","craftCount":1}` | 해당 시설로 이동하여 제작 실행 후 결과 수거 |
| **`execute_altering`** | stdin JSON: `{"displayName":"가공아이템명"}` | 연금/가공 대기열에 작업 등록 (5 정령의 날개 소모) |
| **`complete_altering_work`**| stdin JSON: `{"displayName":"작업명"}` | 완료된 가공물 일괄 수거 |
| **`play_music_score`** | stdin JSON: `{"displayName":"악보명"}` | 장착된 악기로 해당 악보 연주 시작 |
| **`change_instrument`** | stdin JSON: `{"displayName":"악기명"}` | 보유 악기로 장착 변경 |
| **`stop_action`** | 인자 없음 | 채집, 연주, 이동, 자동사냥 등 현재 진행 중인 모든 행동 즉시 정지 |
| **`stand_up`** | 인자 없음 | 의자/바닥 착석 상태에서 일어서기 |
