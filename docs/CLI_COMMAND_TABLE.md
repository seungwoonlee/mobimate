# 게임 CLI 명령 표 (MabinogiMobile_CLI)

> 자동 생성: `web/tools/CliResearch/make_doc.py` — 마지막 생성 2026-10-22 23:01 (게임에서 새로 읽음). CLI 실행 파일 2026-10-22 09:25.
> 손으로 쓰는 실측 메모는 `notes.json`에 더하고 다시 생성한다. 게임하는 동안 `record.py`(녹화기)를 켜 두면 값이 바뀐 필드가 `records/`에 쌓인다.

## 개요

- CLI(`MabinogiMobile_CLI.exe`)는 .NET 10 NativeAOT로 만든 얇은 중계기다. 명령 이름·응답은 게임 클라이언트가 정한다(모르는 명령은 게임이 `unknown_command`로 거절). 사용법: `MabinogiMobile_CLI <dispatch-command> [body]`, `capabilities`, `status`.
- `status`는 게임 연결만 알려 준다: `{"pipe":"connected"}` 또는 `{"pipe":"disconnected","reason":"game_off",...}`. 게임이 꺼져 있으면 모든 명령이 약 5.03초 기다린 뒤 이 응답을 준다(2026-10-22 실측).
- 응답 시간은 보통 약 93ms, 20개를 동시에 불러도 문제없다(2026-10-22 실측).
- 파이프로 받은 출력은 대개 UTF-8이고 한글은 \uXXXX로 이스케이프된다. 콘솔에 바로 찍으면 cp949로 나올 수 있다.
- CLI 실행 파일은 게임 업데이트 없이도 갱신된다(2026-10-22 09:25 갱신, 명령 목록은 그대로 28개).
- 게임 데이터(global-metadata.dat 문자열 표)에는 공개 28개 외에 조회형 명령 이름이 더 있다. 2026-10-22 현재 모두 unknown_command — 아래 '숨은 명령' 표 참조. 녹화기(record.py)가 CLI가 바뀔 때마다 다시 확인한다.
- 랭킹·서버 순위·랭킹 딱지 정보는 어느 공개 명령에도 없다(2026-10-22 전수 조사). `Title`은 칭호 문자열뿐이다.
- 게임의 '남은 클리어 전리품'(레이드 화면의 1/1·0/1)은 CLI에 없다. 그래서 앱이 보기 전에 깬 레이드는 알 수 없고, 레이드·어비스·필드 보스는 완료를 본 게 아니면 '미확인'으로 둔다(2026-10-22).

## 명령 목록 (28개)

| 명령 | 범위 | 종류 | 앱 사용 | 설명 |
|---|---|---|---|---|
| `capabilities` | - | 조회 |  | List available commands |
| `get_current_environment` | 캐릭터(현재 위치) | 조회 | ✓ | Query current location / weather / time |
| `write_chat` | - | 변경 |  | Send a chat message |
| `get_activity` | 캐릭터 | 조회 | ✓ | Query current auto-play / travel / combat / action state |
| `get_my_info` | 캐릭터 | 조회 | ✓ | Query my character's basic info, stats, and current vitals |
| `get_quests` | 캐릭터 | 조회 | ✓ | List Quest Tracker entries |
| `get_currencies` | 캐릭터 | 조회 | ✓ | Query my main currency balances |
| `get_near_npcs` | - | 조회 |  | Query nearby talkable NPCs |
| `get_near_pcs` | - | 조회 | ✓ | Query nearby players |
| `get_inventory` | 캐릭터 | 조회 |  | Query current/max inventory weight |
| `get_music_scores` | 캐릭터 | 조회 |  | List owned game-provided music scores |
| `play_music_score` | - | 변경 |  | Start playing a music score |
| `get_instruments` | 캐릭터 | 조회 |  | List owned instruments |
| `change_instrument` | - | 변경 |  | Equip (change to) an owned instrument |
| `get_items` | 캐릭터+계정 창고 | 조회 | ✓ | List owned items and their counts (bag / account storage / character storage), including gathered materials and foods |
| `get_social_actions` | - | 조회 |  | List behaviors (Behaviours) and facials (Facials) |
| `stop_action` | - | 변경 | ✓ | Stop the current stoppable action (instrument performance, chair sitting, auto-play, carrying, gathering) |
| `stand_up` | - | 변경 | ✓ | Stand up from sitting (/앉기) |
| `get_daily_missions` | **캐릭터**(명세: for my character) | 조회 | ✓ | List active daily missions for my character with goals, progress, and completion state |
| `get_weekly_missions` | **계정**(명세: for my account) | 조회 | ✓ | List active weekly missions for my account with goals, progress, and completion state |
| `get_gatherable_items` | 캐릭터 | 조회 | ✓ | List every gatherable item whose living-skill level requirement is met, with tool availability |
| `execute_gathering` | - | 변경 | ✓ | Gather a named item until the target quantity is reached, or start auto fishing for a fishing-only item |
| `get_alterable_items` | 캐릭터 | 조회 |  | List altering recipes with alterability and missing ingredients; altering is separate from crafting (get_craftable_items), and extracts, spores, powders, silk, and lumber are made here |
| `get_altering_works` | 캐릭터 | 조회 | ✓ | Query in-progress and completed altering works with remaining time |
| `execute_altering` | - | 변경 |  | Queue a named altering recipe, including travel to its facility |
| `complete_altering_work` | - | 변경 | ✓ | Collect all completed altering works at one facility, including travel to it |
| `get_craftable_items` | 캐릭터 | 조회 |  | List crafting recipes with craftability and missing ingredients; altering recipes are in get_alterable_items |
| `execute_crafting` | - | 변경 |  | Craft a named recipe, including travel to its facility and result collection |

## 명령별 상세

### `capabilities`

- 설명: List available commands
- 출력(명세): { commands: [...] }. When loading: true is included, the catalog is not fully ready yet; re-query after the user has entered the game.
- 실측: 명령 목록·설명·예시. `loading: true`면 아직 준비 중이니 게임 접속 뒤 다시 읽는다.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `commands[]` | array |  |
| `commands[].Command` | string |  |
| `commands[].Description` | string |  |
| `commands[].BodyExample` | string |  |
| `commands[].OutputExample` | string |  |
| `commands[].Note` | string |  |
| `commands[].Metadata.requiresConfirm` | string |  |

### `get_current_environment`

- 설명: Query current location / weather / time
- 출력(명세): { ChannelDisplayName, GameSpaceDisplayName, WorldPosition, Weather, ErinnNow, Housing: { IsInHousing, IsOwnedHousing, CanEnterHousing, CannotEnterHousingReason, CanExitHousing } }. Housing = whether I am inside a My Home, whether it is mine, whether I can go to my own My Home now, and whether I can leave the current one.
- 명세 메모: ErinnNow is the in-game time (Erinn calendar).
- 실측: `GameSpaceDisplayName`이 지역 판정의 핵심(레이드·어비스 전용 지역 이름, 요일 던전 'OO V' 등). 로딩·맵 이동 중엔 빈 값.
- 실측: 레이드 지역 실측: 카브락 어려움 `먼 바다의 바위 협곡`, 에이렐 어려움 `먼 바다의 춤추는 바람`, 서큐버스 매우 어려움 `먼 바다의 빛바랜 환영`. 클리어 뒤 '콜헨'으로 바뀐다.
- 실측: 어비스: `허상의 정박지`·`광기의 동굴`·`흩어진 물길`(지역 이름 = 어비스 이름).

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `ChannelDisplayName` | string | 13 |
| `GameSpaceDisplayName` | string | 13 |
| `WorldPosition.X` | number | 70 |
| `WorldPosition.Y` | number | 69 |
| `Weather` | string | 6 |
| `ErinnNow` | string | 147 |
| `Housing.IsInHousing` | bool | 24 |
| `Housing.IsOwnedHousing` | bool | 4 |
| `Housing.CanEnterHousing` | bool | 24 |
| `Housing.CannotEnterHousingReason` | string | 4 |
| `Housing.CanExitHousing` | bool | 4 |

### `write_chat`

- 설명: Send a chat message
- 입력(body) 예: `안녕하세요`
- 출력(명세): success: status accepted, body { message } / failure: status invalid_body, body { error, message } (message_too_long also includes length) / failure: status rejected, body { error: unsupported_command, message } for reserved '/' or '#' commands that are not behaviors or facials / failure: status rejected, body { error: rate_limited, message, retryAfterSeconds }; retry after retryAfterSeconds / failure: status rejected, body { error: inappropriate_text | user_declined | confirm_timeout | blocked | not_enough_currency, message }
- 명세 메모: Every call opens an in-game confirmation popup that shows the exact message; the chat is sent only after the user presses Send in the game window, and this call blocks until the user answers (up to several minutes), so tell the user to check the game. user_declined means the user pressed Cancel: do not resend the same message unless the user asks again. Before the popup, the message passes the server text filter; profanity, abusive language, or personal information is rejected with inappropriate_text, so rewrite the message instead of retrying it as is. Max 50 chars, raw string (not JSON). Accepts normal chat, a behavior command exactly as listed in get_social_actions ChatCommands (e.g. /전통댄스, /손인사1; display names such as /춤 or /인사 are NOT commands), or a facial's EmojiText. Other reserved commands (channel switching such as /지역 or /파티, emergency escape, passcode #...) and unrecognized '/' commands are rejected as unsupported_command. When rate_limited, wait retryAfterSeconds before retrying. Behavior and facial commands are executed instead of shown as chat. Related: get_social_actions
- 실측: 매번 게임 안 확인 창이 뜬다. 앱은 사용자가 누를 때만 쓴다.

### `get_activity`

- 설명: Query current auto-play / travel / combat / action state
- 출력(명세): autoPlay: { IsAutoPlaying, CanStartAutoPlay, AutoPlayTarget, AutoPlayTargetDisplayName }. AutoPlayTarget = none, quest, goddess_mission, shortcut, division_objective, recommended_activity, guide_mission. When naming the target category, use AutoPlayTargetDisplayName verbatim; if it is absent, do not translate AutoPlayTarget and omit the category / autoTravel: { IsAutoTraveling, AutoTravelRemainingPositionCount } / combatState: { IsDead, IsReviving, IsInCombat } / dialogue: { IsDialoguePlaying, IsDialogueNextAvailable, IsWaitingForSelection } / Dungeon: { State, IsBossBattleInProgress }. State = NotInDungeon, Entering, InProgress, Cleared / Battlefield: { IsInBattleField } / IsAbyssResultSequencePlaying / Tutorial: { IsPlaying } / Scenario: { IsInScenario, IsSequencePlaying } / Performance: { IsPlaying, InstrumentName, MusicTitle, IsCopyingAllowed, IsLoop, StartAt, TotalDurationSeconds, ElapsedSeconds, RemainingSeconds, ChannelCount }. InstrumentName = display name of my equipped instrument; empty when none is equipped / Interaction: { HasTarget, IsTargetAttackable, AvailableInteractionType, LastRunningInteractionType, TargetKind }. What the character can interact with right now. HasTarget = something is targeted in front. AvailableInteractionType = e.g. Gathering or Talk, or None. TargetKind = DungeonEntrance, Elevator, Fountain, CutscenePlayProp, Gimmick, Prop, Actor, None / Mode: { MainButtonState, MountPartState, SitState, IsPlayingMiniGame, IsHousingEditMode }. MainButtonState = Hide, Stop, Combat, Interaction, Compass, ScenarioQTE, Fishing, FishingPull, Housing. MountPartState = None, Mounted, Dismounted, Spawned. SitState = None, Sitting; Sitting means sitting on a chair, which stop_action stops. /앉기 is not reflected in SitState
- 실측: 2026-10-15 게임 업데이트로 IsGathering·IsAltering·IsCrafting·IsPlayingInstrument·IsSitting·IsOverweight·CurrentAction·CanStopCurrentAction이 빠지고 Mode·Performance·Interaction·IsReviving이 생겼다.
- 실측: `Dungeon.State`: 요일 던전 처치 순간부터 약 17초 `Cleared`(2026-10-12 실측). 레이드 중에는 계속 `NotInDungeon`(2026-10-22 서큐버스 실측).
- 실측: 판정 후보(실측 필요): `Dungeon.IsBossBattleInProgress`, `IsAbyssResultSequencePlaying`(어비스 결과 연출), `Battlefield.IsInBattleField`, `Scenario.*`, `IsDialoguePlaying`.
- 실측: 레이드 중(에이렐, 2026-10-22): `Dungeon.State`는 계속 `NotInDungeon`, `Dungeon.IsBossBattleInProgress`도 바뀌지 않았다. 레이드 판정에는 쓸 수 없다. 요일 던전·심층 던전은 처치 순간 `State`가 `Cleared`, `IsBossBattleInProgress`가 true로 바뀐다(나갈 때까지 유지).

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `IsAutoPlaying` | bool | 21 |
| `CanStartAutoPlay` | bool | 6 |
| `AutoPlayTarget` | string | 14 |
| `AutoPlayTargetDisplayName` | string | 14 |
| `IsAutoTraveling` | bool | 60 |
| `AutoTravelRemainingPositionCount` | int | 102 |
| `IsDead` | bool | 4 |
| `IsReviving` | bool | 4 |
| `IsInCombat` | bool | 30 |
| `IsDialoguePlaying` | bool | 6 |
| `IsDialogueNextAvailable` | bool | 6 |
| `IsWaitingForSelection` | bool | 4 |
| `Dungeon.State` | string | 10 |
| `Dungeon.IsBossBattleInProgress` | bool | 8 |
| `Battlefield.IsInBattleField` | bool | 4 |
| `IsAbyssResultSequencePlaying` | bool | 4 |
| `Tutorial.IsPlaying` | bool | 4 |
| `Scenario.IsInScenario` | bool | 4 |
| `Scenario.IsSequencePlaying` | bool | 6 |
| `Performance.IsPlaying` | bool | 4 |
| `Performance.InstrumentName` | string | 4 |
| `Performance.MusicTitle` | string | 4 |
| `Performance.IsCopyingAllowed` | bool | 4 |
| `Performance.IsLoop` | bool | 4 |
| `Performance.StartAt` | string | 4 |
| `Performance.TotalDurationSeconds` | number | 4 |
| `Performance.ElapsedSeconds` | number | 4 |
| `Performance.RemainingSeconds` | number | 4 |
| `Performance.ChannelCount` | int | 4 |
| `Interaction.HasTarget` | bool | 63 |
| `Interaction.IsTargetAttackable` | bool | 48 |
| `Interaction.AvailableInteractionType` | string | 20 |
| `Interaction.LastRunningInteractionType` | string | 13 |
| `Interaction.TargetKind` | string | 66 |
| `Mode.MainButtonState` | string | 94 |
| `Mode.MountPartState` | string | 18 |
| `Mode.SitState` | string | 4 |
| `Mode.IsPlayingMiniGame` | bool | 4 |
| `Mode.IsHousingEditMode` | bool | 4 |

### `get_my_info`

- 설명: Query my character's basic info, stats, and current vitals
- 출력(명세): { Title, RealmName, Level, EnabledCombatJobDisplayName, CombatScore, LivingScore, AttractivenessScore, DecorScore, HealthMax, AttackPower, DefencePower, STR, DEX, INT, LUCK, WILL, ArcaneResistance, PaladinStats: { PaladinAttackPower, PaladinDefencePower, JusticePower, JudgementPower, OrderPower, BlessingPower }, Vitals: { HealthCurrent, HealthMax, ShieldAmount, ShieldMax, SatietyValue, SatietyMax, SatietyRatio, InventoryWeightCurrent, InventoryWeightMax, ActiveBuffCount } }. Every stat field is an object { DisplayName, Value }; use DisplayName verbatim as the stat name. Vitals = current condition (current HP/shield/satiety/bag weight and active buff count). Location/weather and housing are in get_current_environment; instrument performance and other live activity are in get_activity.
- 실측: `Title` = 칭호(랭킹 딱지 아님). 전투력·마도저항·스탯·가방 무게(Vitals.InventoryWeight*).
- 실측: 가방 최대 무게가 3.4e38(float 최대)로 오는 때가 있다 → 앱은 비정상 값으로 거른다.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `Title` | string | 14 |
| `RealmName` | string | 4 |
| `Level` | int | 4 |
| `EnabledCombatJobDisplayName` | string | 4 |
| `CombatScore.DisplayName` | string | 4 |
| `CombatScore.Value` | int | 14 |
| `LivingScore.DisplayName` | string | 4 |
| `LivingScore.Value` | int | 4 |
| `AttractivenessScore.DisplayName` | string | 4 |
| `AttractivenessScore.Value` | int | 14 |
| `DecorScore.DisplayName` | string | 4 |
| `DecorScore.Value` | int | 4 |
| `HealthMax.DisplayName` | string | 4 |
| `HealthMax.Value` | int | 15 |
| `AttackPower.DisplayName` | string | 4 |
| `AttackPower.Value` | int | 58 |
| `DefencePower.DisplayName` | string | 4 |
| `DefencePower.Value` | int | 14 |
| `ArcaneResistance.DisplayName` | string | 4 |
| `ArcaneResistance.Value` | int | 4 |
| `STR.DisplayName` | string | 4 |
| `STR.Value` | int | 14 |
| `DEX.DisplayName` | string | 4 |
| `DEX.Value` | int | 14 |
| `INT.DisplayName` | string | 4 |
| `INT.Value` | int | 14 |
| `LUCK.DisplayName` | string | 4 |
| `LUCK.Value` | int | 26 |
| `WILL.DisplayName` | string | 4 |
| `WILL.Value` | int | 14 |
| `PaladinStats.PaladinAttackPower.DisplayName` | string | 4 |
| `PaladinStats.PaladinAttackPower.Value` | int | 4 |
| `PaladinStats.PaladinDefencePower.DisplayName` | string | 4 |
| `PaladinStats.PaladinDefencePower.Value` | int | 4 |
| `PaladinStats.JusticePower.DisplayName` | string | 4 |
| `PaladinStats.JusticePower.Value` | int | 4 |
| `PaladinStats.JudgementPower.DisplayName` | string | 4 |
| `PaladinStats.JudgementPower.Value` | int | 4 |
| `PaladinStats.OrderPower.DisplayName` | string | 4 |
| `PaladinStats.OrderPower.Value` | int | 4 |
| `PaladinStats.BlessingPower.DisplayName` | string | 4 |
| `PaladinStats.BlessingPower.Value` | int | 4 |
| `Vitals.HealthCurrent` | int | 19 |
| `Vitals.HealthMax` | int | 15 |
| `Vitals.ShieldAmount` | int | 40 |
| `Vitals.ShieldMax` | int | 31 |
| `Vitals.SatietyValue` | int | 84 |
| `Vitals.SatietyMax` | int | 4 |
| `Vitals.SatietyRatio` | number | 84 |
| `Vitals.InventoryWeightCurrent` | number | 25 |
| `Vitals.InventoryWeightMax` | number | 14 |
| `Vitals.ActiveBuffCount` | int | 61 |

### `get_quests`

- 설명: List Quest Tracker entries
- 출력(명세): array of { QuestTitle, Source, SourceDisplayName, Objectives[] }. Returns only currently visible and executable Quest Tracker items. Source: main, pinned_sub, auto_register_sub, auto_register_candidate_sub, event, goddess_mission, shortcut, division_objective, guide_mission, recommended_activity. When naming the category, use SourceDisplayName verbatim; for items without it, do not translate Source and describe by the quest title only. Objectives: { Description, IsCompleted, Count, Goal }
- 명세 메모: Returns only items in the active Quest Tracker tab.
- 실측: 퀘스트 트래커의 **현재 탭**에 보이는 것만 준다. 길드 의뢰 등은 트래커에 걸려 있어야 보인다.
- 실측: Source `recommended_activity`(추천, 예: '요일 던전 소탕')·`guide_mission`도 온다.
- 실측: 필드 보스 앙그르바한: 처치하면 '토벌' 퀘스트가 '영역 나가기'로 바뀐다(2026-10-05). 처치 순간 목록이 잠깐 빈다.
- 실측: 코드에 `includeTrackerId` 입력이 있지만 공개 채널에서 `{"includeTrackerId":true}`를 줘도 ID가 붙지 않는다(2026-10-22). 잠긴 `execute_quest_tracker`(QuestTrackerId로 트래커 퀘스트 실행)용으로 보인다.
- 실측: **레이드 클리어 = 퀘스트**(2026-10-22 에이렐 매우 어려움 실측): 레이드 지역에 들어가면 `[에이렐] 클리어`(목표: 파티의 남은 부활 횟수 5/5)가 뜨고, 처치 약 10초 뒤 `[에이렐] 토벌 완료`(목표: 포탈을 사용하여 출항선으로 이동)로 바뀐다. 앱은 `<보스> 토벌 완료`가 보이면 그 레이드를 완료로 본다(카브락·서큐버스 이름은 같은 형식으로 추정, 실측 필요).
- 실측: **뱅가드 브리치**(2026-10-22 힐러#2 1회 실측): 평소 트래커에 `[긴급 의뢰] 뱅가드 브리치`(목표 `작전 장소로 이동 N/3`, 캐릭터별 진행)가 있다. 안에 들어가면 `뱅가드 브리치 클리어`(목표가 단계별로 바뀜: 소환의 결계 활성화 → 적 처치 → 캠프파이어 → 지름길 → 이동 → 보스 처치하기)로 바뀌고, 처치 순간 목록이 잠깐 빈 뒤 `[긴급 의뢰]`가 돌아오면서 `0/3 → 1/3`이 된다. 지역 이름 `뱅가드 브리치 • 늑대의 숲`. 앱은 이 N/3으로 남은 횟수를 센다.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array | 57 |

### `get_currencies`

- 설명: Query my main currency balances
- 출력(명세): array of { DisplayName, Amount }
- 실측: 레이드 클리어 보상(실측): 에이렐·서큐버스 골드 약 +101,100~101,200·하트 토큰 +3, 카브락 골드 +1,100·하트 +3·원정의 증거 +24(어려움). 어비스(매우 어려움) 마물 퇴치 증표 +300·골드 +100,200·하트 +1.
- 실측: 에이렐 매우 어려움(2026-10-22 22:55:05): 골드 +101,200, 하트 토큰 +3 — 토벌 완료 퀘스트보다 약 10초 먼저 들어온다.
- 실측: 뱅가드 브리치 1회(2026-10-22): 골드 +100,000, 마물 퇴치 증표 +10, 정령의 날개 +300.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array |  |
| `[].DisplayName` | string | 174 |
| `[].Amount` | int | 216 |

### `get_near_npcs`

- 설명: Query nearby talkable NPCs
- 출력(명세): array of { Name, Title, DisplayName, Distance }
- 명세 메모: Search radius 30. Returns only talkable NPCs and NPC companions.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array | 6 |

### `get_near_pcs`

- 설명: Query nearby players
- 출력(명세): array of { RealmName, Title, Distance, ClothesCount, CrowdAppearanceColorCount, IsRobeWeared, IsHoodOn, IsWeaponHidden, Level, EnabledCombatJobDisplayName, CombatScore, LivingScore, AttractivenessScore, IsFriend, IsInParty, HasGuild, IsSameGuild, IsCoOwner, IsInCombat, Performance }. IsCoOwner = co-owner of the My Home I am currently in; always false outside a My Home. Performance: { IsPlaying, MusicTitle, IsCopyingAllowed, IsLoop, StartAt, TotalDurationSeconds, ElapsedSeconds, RemainingSeconds, ChannelCount }. IsLoop is the current loop setting. MusicTitle/IsCopyingAllowed/StartAt/TotalDurationSeconds/ElapsedSeconds are only set when IsPlaying is true; RemainingSeconds is only set when TotalDurationSeconds > 0
- 명세 메모: Search radius 30. Related: write_chat
- 실측: 반경 약 30m만. 이름·사망 여부는 없다. `Performance.IsPlaying`(연주 중)은 있다.
- 실측: `Title`은 칭호. 랭킹 딱지 없음.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array | 14 |
| `[].RealmName` | string | 712 |
| `[].Title` | string | 711 |
| `[].Distance` | number | 1199 |
| `[].ClothesCount` | int | 848 |
| `[].CrowdAppearanceColorCount` | int | 711 |
| `[].IsRobeWeared` | bool | 775 |
| `[].IsHoodOn` | bool | 803 |
| `[].IsWeaponHidden` | bool | 766 |
| `[].Performance.IsPlaying` | bool | 711 |
| `[].Performance.MusicTitle` | string | 711 |
| `[].Performance.IsCopyingAllowed` | bool | 711 |
| `[].Performance.IsLoop` | bool | 711 |
| `[].Performance.StartAt` | string | 711 |
| `[].Performance.TotalDurationSeconds` | number | 711 |
| `[].Performance.ElapsedSeconds` | number | 711 |
| `[].Performance.RemainingSeconds` | number | 711 |
| `[].Performance.ChannelCount` | int | 711 |
| `[].Level` | int | 714 |
| `[].EnabledCombatJobDisplayName` | string | 868 |
| `[].CombatScore` | int | 887 |
| `[].LivingScore` | int | 875 |
| `[].AttractivenessScore` | int | 885 |
| `[].IsFriend` | bool | 711 |
| `[].IsInParty` | bool | 715 |
| `[].HasGuild` | bool | 725 |
| `[].IsSameGuild` | bool | 711 |
| `[].IsCoOwner` | bool | 711 |
| `[].IsInCombat` | bool | 738 |

### `get_inventory`

- 설명: Query current/max inventory weight
- 출력(명세): { CurrentInventoryWeight, CurrentInventoryWeightAsDecimal, MaxInventoryWeight, MaxInventoryWeightAsDecimal }
- 명세 메모: For the item list use get_items. Related: get_items, get_music_scores
- 실측: 가방 무게 현재/최대. Decimal 판도 같이 온다.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `CurrentInventoryWeight` | number | 25 |
| `CurrentInventoryWeightAsDecimal` | int | 22 |
| `MaxInventoryWeight` | number | 14 |
| `MaxInventoryWeightAsDecimal` | int | 14 |

### `get_music_scores`

- 설명: List owned game-provided music scores
- 출력(명세): array of { Location, DisplayTitle, IsCopyingAllowed, IsLocked }. Location: inventory, account_storage, character_storage
- 명세 메모: The body filter (raw string) is matched against the title; case-insensitive substring. Empty body returns everything. Only scores provided by the game (NPC shop, rewards) are listed; player-composed or copied scores are excluded and cannot be played through the AI connector. Related: play_music_score, get_inventory

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array |  |
| `[].Location` | string |  |
| `[].DisplayTitle` | string |  |
| `[].IsCopyingAllowed` | bool |  |
| `[].IsLocked` | bool |  |

### `play_music_score`

- 설명: Start playing a music score
- 입력(body) 예: `{"title":"<DisplayTitle from get_music_scores>"}`
- 출력(명세): success: status accepted / failure: status invalid_body (missing title), rejected (error: not_found | user_composed_not_allowed | no_instrument | not_available_on_combat | not_available_on_riding | not_available_on_dead | system_error)
- 명세 메모: title: string (DisplayTitle from get_music_scores). If multiple scores share the title, one of them is played. Only scores provided by the game (NPC shop, rewards) can be played; a title that matches only a player-composed or copied score is rejected with user_composed_not_allowed. Returns rejected with no_instrument / not_available_on_combat / not_available_on_riding / not_available_on_dead when playing is not possible, and system_error for other failures. Related: get_music_scores

### `get_instruments`

- 설명: List owned instruments
- 출력(명세): array of { Name, Durability, IsEquipped }. IsEquipped marks the instrument currently equipped and used when playing a music score
- 명세 메모: The body filter (raw string) is matched against the name; case-insensitive substring. Empty body returns everything. Playing a music score requires an equipped instrument (IsEquipped); if none is equipped play_music_score is rejected with no_instrument, so equip one first with change_instrument. Related: change_instrument, play_music_score

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array |  |
| `[].Name` | string |  |
| `[].Durability` | int |  |
| `[].IsEquipped` | bool |  |

### `change_instrument`

- 설명: Equip (change to) an owned instrument
- 입력(body) 예: `{"name":"<Name from get_instruments>"}`
- 출력(명세): success: status accepted / failure: status invalid_body (missing name), rejected (error: not_found | is_playing_instrument | not_available_on_dead | level_requirement | invalid_target | failed_unequip | system_error)
- 명세 메모: name: string (Name from get_instruments). If multiple owned instruments share the name, one of them is equipped. Rejected with is_playing_instrument while playing, not_available_on_dead when dead, and level_requirement when the level is not enough. Requesting the already equipped instrument returns accepted. Related: get_instruments, play_music_score

### `get_items`

- 설명: List owned items and their counts (bag / account storage / character storage), including gathered materials and foods
- 입력(body) 예: `{"category":"Ingredient","name":"<part of the name, optional>"}`
- 출력(명세): array of { DisplayName, Category, CategoryDisplayName, Count, Location, IsLocked }. Location: inventory, account_storage, character_storage. Category examples: Food, Ingredient, Consumable, Consumable_Box, Consumable_Growth, Quest. When naming the category, use CategoryDisplayName verbatim.
- 명세 메모: Covers consumable-category items (foods, materials, gathered items, etc.); not equipment/costume/pet. Optional filters, both substring and case-insensitive: 'category' matches the inventory sub-category code (the English Category value), 'name' matches display name. Omit the body for the full list. Related: get_inventory
- 실측: Location: inventory·account_storage·character_storage. 응답이 커서 앱은 시간 제한 10초.
- 실측: 레이드 고유 조각: 에이렐 '선율을 품은 하프 조각', 서큐버스 '꿈을 비추는 거울 조각'. 100개 모이면 자동으로 합쳐지는 조각(정수·열쇠)은 개수가 줄어 보인다.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array |  |
| `[].Location` | string | 8 |
| `[].DisplayName` | string | 8 |
| `[].Category` | string | 8 |
| `[].CategoryDisplayName` | string | 8 |
| `[].Count` | int | 52 |
| `[].IsLocked` | bool | 8 |

### `get_social_actions`

- 설명: List behaviors (Behaviours) and facials (Facials)
- 출력(명세): { Behaviours: [ { DisplayName, ChatCommands: [] } ], Facials: [ { DisplayName, EmojiText } ] }
- 명세 메모: The body filter (raw string) is matched against a behavior's name/ChatCommands or a facial's name/EmojiText; case-insensitive substring. Empty body returns everything. Execute by sending a behavior's ChatCommands or a facial's EmojiText via write_chat. Related: write_chat

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `Behaviours[]` | array |  |
| `Behaviours[].DisplayName` | string |  |
| `Behaviours[].ChatCommands[]` | string |  |
| `Facials[]` | array |  |
| `Facials[].DisplayName` | string |  |
| `Facials[].EmojiText` | string |  |

### `stop_action`

- 설명: Stop the current stoppable action (instrument performance, chair sitting, auto-play, carrying, gathering)
- 출력(명세): success: status accepted, body { message } / failure: status rejected, body { error, message }. error: invalid_state (nothing stoppable in progress), timeout
- 명세 메모: Works only while a stoppable action is in progress (the in-game Stop button is showing); otherwise it rejects with invalid_state. Sitting (/앉기) has no Stop button; use stand_up for that. Related: stand_up, get_activity

### `stand_up`

- 설명: Stand up from sitting (/앉기)
- 출력(명세): success: status accepted, body { message } / failure: status rejected, body { error, message }. error: not_sitting, no_control_object, timeout
- 명세 메모: Works only while sitting (/앉기); otherwise it rejects with not_sitting. get_activity SitState does not indicate /앉기. Standing up is not possible until the sit-down motion has finished, so after a timeout right after sitting, retry shortly. To stop other actions (instrument, auto-play, carrying) use stop_action. Related: stop_action

### `get_daily_missions`

- 설명: List active daily missions for my character with goals, progress, and completion state
- 출력(명세): success: status accepted, body: array of { Title, Description, CurrentCount, GoalCount, IsCompleted, IsRewardReceived, HasShortcut } / failure: status rejected, body { error, message }
- 명세 메모: HasShortcut indicates whether the mission supports an auto-play shortcut. Related: get_weekly_missions
- 실측: 실측(2026-10-22): 같은 계정의 전사 '던전 3회 토벌' 3/3, 듀얼블레이드 0/3.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array |  |
| `[].Title` | string | 22 |
| `[].Description` | string | 22 |
| `[].CurrentCount` | int | 44 |
| `[].GoalCount` | int | 22 |
| `[].IsCompleted` | bool | 26 |
| `[].IsRewardReceived` | bool | 28 |
| `[].HasShortcut` | bool | 22 |

### `get_weekly_missions`

- 설명: List active weekly missions for my account with goals, progress, and completion state
- 출력(명세): success: status accepted, body: array of { Title, Description, CurrentCount, GoalCount, IsCompleted, IsRewardReceived, HasShortcut } / failure: status rejected, body { error, message }
- 명세 메모: HasShortcut indicates whether the mission supports an auto-play shortcut. Related: get_daily_missions
- 실측: 실측(2026-10-22): 같은 계정 전사·듀얼블레이드의 15개 값이 모두 같다. 레이드·어비스는 '1회 토벌'(목표 1)이라 계정에서 첫 클리어 한 번만 오른다.
- 실측: 서큐버스 처치 순간 '레이드 1회 토벌' 0→1(2026-10-22 09:32:40, 진입 약 1분 50초 뒤).
- 실측: 앱 규칙: 이 캐릭터로 지켜보던 중(직전 읽기 10분 안)에 오른 것만 그 캐릭터의 근거.
- 실측: 뱅가드 1회 클리어(2026-10-22 23:00:57) 뒤 약 4초 만에 계정 미션 `우리 집에 왜 왔니?`(뱅가드 브리치 3회 토벌) 0→1.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array |  |
| `[].Title` | string |  |
| `[].Description` | string |  |
| `[].CurrentCount` | int | 1 |
| `[].GoalCount` | int |  |
| `[].IsCompleted` | bool |  |
| `[].IsRewardReceived` | bool |  |
| `[].HasShortcut` | bool |  |

### `get_gatherable_items`

- 설명: List every gatherable item whose living-skill level requirement is met, with tool availability
- 입력(body) 예: `통나무`
- 출력(명세): { items: [{ DisplayName, ToolOk }] }
- 명세 메모: Only items whose living-skill level requirement is already met are listed; locked items are hidden, and naming one in execute_gathering returns insufficient_living_skill_level with the required level. ToolOk=false means the required tool is missing or its durability is 0. The body filter (raw string) is matched against the item name; case-insensitive substring. Empty body returns everything. Related: execute_gathering

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `items[]` | array |  |
| `items[].DisplayName` | string |  |
| `items[].ToolOk` | bool |  |

### `execute_gathering`

- 설명: Gather a named item until the target quantity is reached, or start auto fishing for a fishing-only item
- 입력(body) 예: `{"displayName":"<DisplayName from get_gatherable_items>"}`
- 출력(명세): gathering success: status accepted, body { result: completed, gained, target } after gathering reaches its target / fishing success: status accepted, body { result: started } after auto fishing begins, with no gained or target because auto fishing has no goal / ended early: status accepted, body { result: stopped, gained, target, message } / interrupted after starting: status accepted, body { error: blocked | overweight | timeout | canceled | tool_broken | required_consumable_missing | interrupted_by_attack, message, gained, target }; fishing omits gained/target in every stopped or error body; blocked also carries kind = the UI or state the user must resolve / rejected before starting: status rejected, body { error, message }. errors: not_found, no_route, insufficient_living_skill_level, overweight, tool_missing, tool_broken, required_consumable_missing, not_in_field, blocked / invalid body (displayName missing): status invalid_body
- 명세 메모: displayName must exactly match an item name from get_gatherable_items. One call gathers up to 100 of the named item, then stops; call again for more. If each attempt consumes a required item (e.g. an empty bottle), gathering stops early with error required_consumable_missing if the required item runs out before reaching the target. The gathering spot is chosen automatically as the nearest one you can reach. That spot also yields its other drops, but gained and target count the named item only. For a fishing-only item, the character travels to a fishery, auto fishing is turned on, and the call returns as soon as fishing starts. Auto fishing has no target and never ends by itself; when the user has enough, end it with stop_action. The auto fishing setting stays on afterwards. If a UI or state that requires user input appears before or during gathering, the action stops and returns blocked with its kind; tell the user what to resolve before retrying. Related: get_gatherable_items, get_activity, get_quests, stop_action. Running this command consumes 5 정령의 날개. It is rejected with not_enough_currency when the user lacks it and with cost_payment_failed when payment fails; success bodies include cost, a sentence with the amount spent and the remaining balance.

### `get_alterable_items`

- 설명: List altering recipes with alterability and missing ingredients; altering is separate from crafting (get_craftable_items), and extracts, spores, powders, silk, and lumber are made here
- 입력(body) 예: `버섯`
- 출력(명세): { items: [{ DisplayName, Alterable, ProducedPerWork, Reason, MissingIngredients: [{ DisplayName, Required, Owned }] }] }. ProducedPerWork = items yielded by one altering work. Reason: queue_full, insufficient_facility_level, not_enough_ingredient, ingredient_locked, insufficient_transfer_cost
- 명세 메모: The body filter (raw string) is matched against the recipe name OR any ingredient name; case-insensitive substring. Empty body returns everything. Altering is queue-based: start with execute_altering, watch progress with get_altering_works, and collect with complete_altering_work. Pass a returned DisplayName verbatim to execute_altering. Related: execute_altering, get_altering_works
- 실측: 입력은 JSON이 아니라 날것의 문자열(예: `버섯`)이다. 레시피 이름이나 재료 이름에 부분 일치. JSON을 주면 빈 목록이 온다(2026-10-22).

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `items[]` | array |  |
| `items[].DisplayName` | string |  |
| `items[].Alterable` | bool | 1 |
| `items[].ProducedPerWork` | int |  |
| `items[].Reason` | string | 1 |
| `items[].MissingIngredients[]` | array |  |
| `items[].MissingIngredients[].DisplayName` | string | 2 |
| `items[].MissingIngredients[].Required` | int | 2 |
| `items[].MissingIngredients[].Owned` | int | 8 |

### `get_altering_works`

- 설명: Query in-progress and completed altering works with remaining time
- 출력(명세): { completedCount, works: [{ DisplayName, FacilityName, State, IsCompleted, RemainingSeconds }] }. FacilityName = the facility's display name; works sharing it are collected together by one complete_altering_work call. State: NotStarted, InProgress, Completed. RemainingSeconds is 0 when completed. completedCount = works ready to collect.
- 명세 메모: Shows the altering queue across all facilities. When a work is completed (completedCount > 0), collect it with complete_altering_work. Related: execute_altering, complete_altering_work
- 실측: 가공 대기열·남은 시간.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `completedCount` | int |  |
| `works[]` | array |  |
| `works[].DisplayName` | string |  |
| `works[].FacilityName` | string |  |
| `works[].State` | string |  |
| `works[].IsCompleted` | bool |  |
| `works[].RemainingSeconds` | int |  |

### `execute_altering`

- 설명: Queue a named altering recipe, including travel to its facility
- 입력(body) 예: `{"displayName":"<DisplayName from get_alterable_items>"}`
- 출력(명세): success: status accepted, body.result 'started' after the work is queued; the facility UI is closed automatically / interrupted: status accepted, body { error, message }; blocked carries kind = the UI or state the user must resolve / rejected before starting: status rejected, body { error, message }. errors: not_found, not_available, requires_user_interaction (this recipe cannot be queued through this command; ask the user to start it in the game), queue_full, insufficient_facility_level, not_enough_ingredient, ingredient_locked, insufficient_transfer_cost, blocked, overweight, not_in_field, facility_not_found / error after starting: status accepted, body { error: component_not_found | facility_not_found | queue_full | blocked | timeout | canceled, message } or body { result: stopped_by_user } when travel to the facility is stopped before arrival / invalid body (displayName missing): status invalid_body
- 명세 메모: displayName must exactly match an item from get_alterable_items. Altering is asynchronous and queue-based: a successful call queues one work and does not produce the item immediately. This call blocks through travel and queue registration, then closes the facility UI. Check get_altering_works for remaining time and completion, then collect with complete_altering_work. If a UI or state that requires user input appears before or during the action, it stops and returns blocked with its kind; tell the user what to resolve before retrying. To queue N works, call again after each started response. Related: get_alterable_items, get_altering_works, complete_altering_work, get_activity. Running this command consumes 5 정령의 날개. It is rejected with not_enough_currency when the user lacks it and with cost_payment_failed when payment fails; success bodies include cost, a sentence with the amount spent and the remaining balance.

### `complete_altering_work`

- 설명: Collect all completed altering works at one facility, including travel to it
- 입력(body) 예: `{"displayName":"<DisplayName from get_altering_works>"}`
- 출력(명세): success: status accepted, body { collected, rewards, criticalRewards, message } after all completed works at the selected facility are collected; the reward and facility UI are closed automatically / collection not confirmed after waiting: status accepted, body { error: timeout, message }; re-check with get_altering_works / interrupted: status accepted, body { error, message } (blocked | timeout | canceled; blocked carries kind = the UI or state the user must resolve) or body { result: stopped_by_user } when travel is stopped before arrival / rejected before starting: status rejected, body { error, message }. errors: no_altering, no_completed_work, not_found, no_completed_work_at_facility, not_completed_yet, blocked, overweight, not_in_field, facility_not_found / invalid body (displayName missing): status invalid_body
- 명세 메모: Collects all completed altering works at ONE facility only. This call blocks through travel, collection, and reward handling; after collection the reward and facility UI close automatically. The body displayName selects the facility: every completed work at the facility where that item is made is collected, and if that item itself is still in progress the call is rejected with not_completed_yet. To collect works across multiple facilities, call once per facility. If a UI or state that requires user input appears before or during the action, it stops and returns blocked with its kind; tell the user what to resolve before retrying. Related: get_altering_works, execute_altering.
- 실측: 시설까지 이동이 포함돼 오래 걸린다(앱 시간 제한 12초).

### `get_craftable_items`

- 설명: List crafting recipes with craftability and missing ingredients; altering recipes are in get_alterable_items
- 입력(body) 예: `거미줄`
- 출력(명세): { craftingUnlocked: bool, items: [{ DisplayName, Craftable, ProducedPerCraft, Reason, MissingIngredients: [{ DisplayName, Required, Owned }] }] }. When craftingUnlocked is false the crafting system is not yet unlocked and items is empty. ProducedPerCraft = items yielded by ONE craft (craftCount 1). Reason: insufficient_living_skill_level, insufficient_facility_level, insufficient_decor_score, not_enough_ingredient, ingredient_locked, insufficient_transfer_cost
- 명세 메모: The body filter (raw string) is matched against the recipe name OR any ingredient name; case-insensitive substring, so sending '거미줄' also lists recipes that use it as an ingredient. Empty body returns everything. Craftable reflects living skill level, facility level, decor score, ingredient quantity, and ingredient transfer cost. Pass a returned DisplayName verbatim to execute_crafting. Related: execute_crafting

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `craftingUnlocked` | bool |  |
| `items[]` | array |  |
| `items[].DisplayName` | string |  |
| `items[].Craftable` | bool |  |
| `items[].ProducedPerCraft` | int |  |
| `items[].Reason` | string |  |
| `items[].MissingIngredients[]` | array |  |
| `items[].MissingIngredients[].DisplayName` | string | 7 |
| `items[].MissingIngredients[].Required` | int | 7 |
| `items[].MissingIngredients[].Owned` | int | 22 |

### `execute_crafting`

- 설명: Craft a named recipe, including travel to its facility and result collection
- 입력(body) 예: `{"displayName":"<DisplayName from get_craftable_items>","craftCount":1}`
- 출력(명세): success: status accepted, body.result is 'completed' (craft completed; the result and facility UI are closed automatically; body also includes craftCount, rewards, and criticalRewards) or 'stopped_by_user' (the user ended crafting in the game before it completed; nothing new was produced) / interrupted or timed out after starting: status accepted, body { error, message }; blocked carries kind = the UI or state the user must resolve; timeout means the action was stopped; canceled means another command replaced this action / rejected before starting: status rejected, body { error, message }. errors: crafting_locked, not_found, not_available, invalid_count, insufficient_living_skill_level, insufficient_facility_level, insufficient_decor_score, not_enough_ingredient, ingredient_locked, insufficient_transfer_cost, blocked, overweight, not_in_field, facility_not_found. invalid_count also includes maxCount. / invalid body (displayName missing): status invalid_body
- 명세 메모: displayName must exactly match an item from get_craftable_items. craftCount is the NUMBER OF CRAFTS, not the number of output items: each craft yields get_craftable_items.ProducedPerCraft items. craftCount defaults to 1 and has a per-facility upper limit; exceeding it is rejected as invalid_count with maxCount. This call blocks through travel, crafting, and result collection. The result and facility UI close automatically after completion. If a UI or state that requires user input appears before or during the action, crafting stops and returns blocked with its kind; tell the user what to resolve before retrying. To make more items, call again after a completed response. Related: get_craftable_items, get_activity, get_quests. Running this command consumes 5 정령의 날개. It is rejected with not_enough_currency when the user lacks it and with cost_payment_failed when payment fails; success bodies include cost, a sentence with the amount spent and the remaining balance.

## 숨은 명령 (게임 데이터에 이름만 있음)

게임의 IL2CPP 문자열 표(`MabinogiMobile_Data/il2cpp_data/Metadata/global-metadata.dat`)에서 뽑은 이름이다. 공개 목록(capabilities)에는 없다. 조회형만 실제로 불러 확인한다.

| 명령 | 상태 (확인 2026-10-22T23:01) |
|---|---|
| `game_list_destinations` | unknown_command |
| `get_camera_state` | unknown_command |
| `get_chats` | unknown_command |
| `get_dialogue_host_state` | unknown_command |
| `get_growth_guide` | unknown_command |
| `get_housing_inventory` | unknown_command |
| `get_housing_placement_mode_state` | unknown_command |
| `get_housing_placements` | unknown_command |
| `get_login_state` | unknown_command |
| `get_photo_mode_state` | unknown_command |
| `get_quick_navigation_shortcuts` | unknown_command |
| `list_commands` | unknown_command |
| `list_dev_actions` | unknown_command |

상태를 바꾸는 것으로 보이는 후보(호출하지 않음): `execute_mission_shortcut`, `execute_quest_tracker`, `execute_quick_navigation_shortcut`, `run_dev_action`, `game_move_to_named_location`, `start_housing_edit_mode`, `end_housing_edit_mode`, `move_housing_prop`, `cancel_housing_placement`, `set_photo_mode_blur`, `set_photo_mode_zoom`, `take_photo`, `set_camera`

## 게임 코드 안의 명령 클래스 전체 (정적 분석)

게임 코드(`MM.Client.Game.Integration.MCP.Commands` 이름공간)에 있는 `Mcp…Command` 클래스다. 공개 여부는 게임 데이터 표(`cliForAiCommandSheetId`)와 등급(`McpCliTier`: CliChannel / Dev)으로 정해진다(`McpCommandCatalog.IsCommandEnabled`). 그래서 잠긴 명령은 게임 업데이트 없이 서버 데이터만으로 열릴 수 있다. 명령 이름은 클래스 이름에서 만든 추정이라 실제와 다를 수 있다. 오른쪽 열은 그 클래스 근처의 필드·메서드 이름(응답 구조의 단서)이다.

| 구분 | 명령(추정) | 상태 | 관련 이름 |
|---|---|---|---|
| 잠김 | `cancel_housing_placement` | 확인 안 함(상태 변경형) | McpCategoryLabels GetQuestSourceDisplayName |
| 공개 | `change_instrument` | 공개 | BuildEquipErrorBody TryParseName alreadyEquipped foundTool TryFindInstrumentByName TryGetLocalBlockReason McpChangeInstrumentBody |
| 잠김 | `close_photo_mode` | 확인 안 함(상태 변경형) | McpCommandCost cliForAiCommandSheetId BuildNote AppendNote payment AppendSpent TryGetCost Payment errorBody spent Free usedAmount ownedBeforePay Paid ErrorBody Spent |
| 공개 | `complete_altering_work` | 공개 | TryGetOpenAlteringFacility bodyStr TryParseBody CompleteAlteringWorkBody arrivedNow sheets targetFacility |
| 잠김 | `connect_to_gateway` | 확인 안 함(상태 변경형) | ConnectBody server BeginRun EndRun CheckExceeded OpenConfirmPopup accumulatedSeconds runStartedAt activeRunCount isConfirming LimitSeconds TotalSeconds |
| 잠김 | `create_character_from_preset` | 확인 안 함(상태 변경형) | TryResolvePresetSheetId ToErrorCode CreateBody |
| 잠김 | `dialogue_next` | 확인 안 함(상태 변경형) |  |
| 잠김 | `end_housing_edit_mode` | 확인 안 함(상태 변경형) |  |
| 잠김 | `enter_my_housing` | 확인 안 함(상태 변경형) |  |
| 공개 | `execute_altering` | 공개 | alteringData EnqueueAtFacility IsAtFacilityInProductionMode McpExecuteAlteringBody enqueueFailBody targetQueueId beforeCount |
| 공개 | `execute_crafting` | 공개 | McpExecuteCraftingBody |
| 공개 | `execute_gathering` | 공개 | ExecuteGatheringAsync eligiblePairs BuildStoppedBody missingConsumableName missingConsumableAmount CheckRequiredConsumablesMissing BuildRequiredConsumableMissingBody BuildNonCombatAttackedBody IsKnockedDownInNonCombat objectiveCap AppendProgress CountGained CountOwned ExecuteFishingAsync fisheryGroup IsInFisheryRegion rejectionBody TryFindFishingCandidate TryFindGatheringPairsByDisplayName DefaultObjectiveCount McpExecuteGatheringBody wasInterruptedByNonCombatAttack fishingRequested McpExecuteGuards CheckBlocked |
| 잠김 | `execute_mission_shortcut` | 확인 안 함(상태 변경형) | McpShortcutBody |
| 잠김 | `execute_quest_tracker` | 확인 안 함(상태 변경형) | requestBody executeResponse TryExecuteQuestTracker McpExecutedQuestTrackerItem QuestTrackerId |
| 잠김 | `execute_quick_navigation_shortcut` | 확인 안 함(상태 변경형) | TryParseShortcutId McpExecuteQuickNavigationShortcutBody |
| 잠김 | `exit_housing` | 확인 안 함(상태 변경형) | McpFishingGathering FindCandidates fishery FindCandidatesForFishery hasLivingSkill hasToolInventory toolInventoryComponent GetToolStatus IsFishingActive GetAutomaticFishingRewardIds |
| 잠김 | `game_list_destinations` | unknown_command | ParseListDestinationsFilter FormatNavigationError CreateNavigationService NavigationService |
| 잠김 | `game_move_to_named_location` | 확인 안 함(상태 변경형) | TryParseMoveToNamedLocationBody |
| 공개 | `get_activity` | 공개 | BuildActivityJson BuildInteraction ResolveTargetKind GetAutoPlayTargetLabel BuildPerformanceInfo McpActivity CanStartAutoPlay AutoPlayTargetDisplayName IsAutoTraveling AutoTravelRemainingPositionCount IsDialogueNextAvailable IsWaitingForSelection IsAbyssResultSequencePlaying McpMode |
| 공개 | `get_alterable_items` | 공개 | BuildAlterableItemsJson MatchesFilter IsFacilityQueueFull TryValidateAltering TryFindAlteringByDisplayName BuildMissingIngredients McpAlterableItem Alterable ProducedPerWork MissingIngredients McpMissingIngredient |
| 공개 | `get_altering_works` | 공개 | BuildWorksJson McpAlteringWork |
| 잠김 | `get_chats` | unknown_command | MakeChatResponse returnChats hasThreshold maxFetchedChatId hasMaxFetchedChatId AddChatLogs lastFetchedChatId hasFetchedChatId McpFetchedChat SentAtRawValue SentAtKst ChannelDisplayName Caller Sender LocalChatGameSpaceName |
| 공개 | `get_craftable_items` | 공개 | BuildCraftableItemsJson visibilityComponent CanDisplayRecipe GetControlObjectDecorScore GetMaxCraftCount IsCraftingSystemUnlocked EvaluateRecipe TryValidateRecipe TryFindRecipeByDisplayName McpCraftableItem Craftable ProducedPerCraft |
| 공개 | `get_currencies` | 공개 | BuildCurrenciesJson EnumerateSheetIdsToShow McpCurrencyInfo |
| 공개 | `get_current_environment` | 공개 | BuildCurrentEnvironmentJson BuildHousingInfo McpCurrentEnvironment GameSpaceDisplayName ErinnNow McpHousingInfo IsInHousing CanEnterHousing CannotEnterHousingReason CanExitHousing |
| 공개 | `get_daily_missions` | 공개 | McpMissionProgress HasShortcut |
| 잠김 | `get_dialogue_host_state` | unknown_command | ReadBool ExtractLastLine ReadChoiceDisplayNames Attr_IsVisibleDialogueHost Attr_IsHostTurn Attr_AllowGuestFreeInput Attr_DialogueHistoryText Attr_GuestSpeakerName Attr_GuestDialogueText Attr_Choices Attr_DisplayName StateNotActive StateHostTurn StateGuestTurn StateWaiting McpDialogueHostState IsHostTurn IsGuestTurn AllowGuestFreeInput DialogueHistory LastLine GuestSpeaker GuestPrompt Choices |
| 공개 | `get_gatherable_items` | 공개 | BuildGatherableItemsJson HasUsableTool FindNearbyGatherableProps NearbySearchDistance NearbySearchDistanceSquared ToolStatus Usable Missing McpNearbyGatherableProp McpGatherableItem ToolOk |
| 잠김 | `get_growth_guide` | unknown_command | ToCardDto BuildContentsTargets GetDayType McpGrowthGuide IsLevelGuideEnabled IsContentsGuideEnabled IsAttractivenessGuideEnabled RecommendedByPriority ContentsTargets McpGrowthGuideRecommendation TopCard McpGrowthGuideCard ContentType McpGrowthGuideContentsTarget DayType |
| 잠김 | `get_housing_inventory` | unknown_command | McpPropItem PropStackKey |
| 잠김 | `get_housing_placement_mode_state` | unknown_command |  |
| 잠김 | `get_housing_placements` | unknown_command | McpPlacedProp |
| 공개 | `get_instruments` | 공개 | BuildInstrumentsJson McpInstrument |
| 공개 | `get_inventory` | 공개 | BuildInventoryJson McpInventoryWeight CurrentInventoryWeightAsDecimal MaxInventoryWeightAsDecimal |
| 공개 | `get_items` | 공개 | nameFilter categoryFilter ParseFilters BuildItemsJson haystack McpItem CategoryDisplayName McpGetItemsBody |
| 잠김 | `get_login_state` | unknown_command | MapScreen |
| 공개 | `get_music_scores` | 공개 | BuildMusicScoresJson AddMusicScores McpMusicScore |
| 공개 | `get_my_info` | 공개 | BuildMyInfoJson MakeStat MakeScore McpMyInfo EnabledCombatJobDisplayName DecorScore DefencePower STR DEX INT LUCK WILL PaladinStats Vitals McpLabeledStat McpPaladinStats McpVitals |
| 공개 | `get_near_npcs` | 공개 | BuildNearNPCsJson BuildTitledName McpNearbyNpc |
| 공개 | `get_near_pcs` | 공개 | BuildNearPCsJsonAsync McpNearbyPc ClothesCount CrowdAppearanceColorCount IsInParty IsSameGuild |
| 잠김 | `get_photo_mode_state` | unknown_command | GetStageObjectDirectionDegree |
| 공개 | `get_quests` | 공개 | questTrackerUIComponent includeTrackerId MakeQuestTrackerResponse GetSource MakeQuestObjectives McpFetchedQuestTrackerItem SourceDisplayName McpFetchedQuestObjective |
| 잠김 | `get_quick_navigation_shortcuts` | unknown_command | BuildQuickNavigationShortcutsJson GetQuickNavigationShortcuts markerType GetShortcutType McpQuickNavigationShortcutsResponse shortcuts McpQuickNavigationShortcut |
| 공개 | `get_social_actions` | 공개 | BuildSocialActionsJson MatchesBehaviour McpSocialActionsResult Behaviours Facials McpBehaviourInfo McpFacialInfo EmojiText |
| 공개 | `get_weekly_missions` | 공개 | McpInteractionLabels ResolveInteractionType McpItemNameMatching |
| 잠김 | `list_commands` | unknown_command | BuildCommandListJson BuildLoadingCommandListJson LoadingSafeCommandTypes |
| 잠김 | `login_with_webo` | 확인 안 함(상태 변경형) | ResolveDefaultAccountName FallbackAccountName LoginWithWeboBody |
| 잠김 | `logout` | 확인 안 함(상태 변경형) |  |
| 잠김 | `move_housing_prop` | 확인 안 함(상태 변경형) | McpMoveBody |
| 잠김 | `open_photo_mode` | 확인 안 함(상태 변경형) | OpenBody McpPhotoModeSupport recognized ParseCropRatio ToTakingOptions |
| 잠김 | `pickup_housing_prop` | 확인 안 함(상태 변경형) | McpPickupBody |
| 잠김 | `play_growth_guide` | 확인 안 함(상태 변경형) | McpPlayGrowthGuideBody McpPlayGrowthGuideResult |
| 공개 | `play_music_score` | 공개 | BuildPlayErrorBody TryParseTitle TryFindMusicNoteItemByTitle TryFindInCollection McpPlayMusicScoreBody |
| 잠김 | `press_login_button` | 확인 안 함(상태 변경형) | McpRewardEntry McpRewardSummary ResolveName |
| 잠김 | `select_character` | 확인 안 함(상태 변경형) | ToErrorBody SelectBody |
| 잠김 | `set_housing_room_setting` | 확인 안 함(상태 변경형) | BuildErrorBody McpSetRoomSettingBody propStackKey |
| 잠김 | `set_photo_mode_blur` | 확인 안 함(상태 변경형) | MinBlurStep MaxBlurStep BlurBody blurStep |
| 잠김 | `set_photo_mode_studio` | 확인 안 함(상태 변경형) | SetStudioBody |
| 잠김 | `set_photo_mode_zoom` | 확인 안 함(상태 변경형) | MinZoomIndex MaxZoomIndex ZoomBody |
| 잠김 | `stand_toward_photo_mode` | 확인 안 함(상태 변경형) |  |
| 공개 | `stand_up` | 공개 | ConfirmPollIntervalSeconds ConfirmTimeoutSeconds |
| 잠김 | `start_compass` | 확인 안 함(상태 변경형) |  |
| 잠김 | `start_housing_edit_mode` | 확인 안 함(상태 변경형) |  |
| 잠김 | `stash_housing_prop` | 확인 안 함(상태 변경형) | McpStashBody |
| 공개 | `stop_action` | 공개 |  |
| 잠김 | `submit_dialogue_host` | 확인 안 함(상태 변경형) | choices SetHostChoices parsed TryReadEvent Attr_HostDialogueTextChanged Attr_ToggleAllowGuestFreeInput Attr_SubmitEvent Attr_EndEvent Attr_InputTextChanged McpSubmitDialogueHostBody AllowFreeInput |
| 잠김 | `take_photo` | 확인 안 함(상태 변경형) | TakeBody |
| 잠김 | `try_to_place_housing_prop` | 확인 안 함(상태 변경형) | McpPlacementBody |
| 공개 | `write_chat` | 공개 | CheckTextRejectedAsync ConfirmWithUserAsync nextAllowedTime WriteChatCooldown Approved Declined TimedOut ChatConfirmPopupHandler TimeOut SendButtonIndex CheatMcpSupport LoadCheatInfosAsync ParseCommandName found TryFindByCommandName badCommandName Suggest CalculateScore MatchScore CheatInfosLoad cheatDisabled CheatDisabled HasList McpCameraControl followCamera |
| 개발·테스트 | `capture_screen` | - |  |
| 개발·테스트 | `click_screen` | - | IsWorldTouchReceiver ClickRequest |
| 개발·테스트 | `dismiss_ingame_blockers` | - | DefaultQuietSeconds DefaultMaxSeconds MinSecondsLimit MaxSecondsLimit DismissBody quietSeconds maxSeconds |
| 개발·테스트 | `drag_screen` | - | DragRequest fromX fromY toX toY |
| 개발·테스트 | `dump_u_i_hierarchy` | - | CollectSortedRootCanvases ScanFlatNodes FlattenAll FlattenInteractables accum AccumulateTexts AccumulateDynTex AccumulateSprites FindFallbackSpriteInChildren AccumulateBinds DecorateContext HasInteractable IsListContainer CollectSiblingTexts StripRichText sel MapSelectableKind masked ReadTextField ReadUITextString TryBuildDynamicTextureMap CamFor GetScreenRect IsFullyClippedByAncestorMask es |
| 개발·테스트 | `dump_world_objects` | - | rawBody onScreenOnly noRadiusLimit ParseFlags BuildWorldObjectsJson Round DefaultRadius MaxProbeObjects McpWorldDump objects excluded excludedImpls probeCapped selectCapped McpDumpCamera halfFovV halfFovH screenW screenH McpWorldObject |
| 개발·테스트 | `get_camera_state` | unknown_command |  |
| 개발·테스트 | `get_cheat_arguments` | - | argIndex DefaultLimit MaxLimit MaxSuggestions McpArgResult ArgIndex ArgName ArgType McpArgCandidate McpArgErrorResult Suggestions McpGetCheatArgumentsBody |
| 개발·테스트 | `get_cheats` | - | ParseBody BuildCheatInfo McpGetCheatsResult ReturnedCount Cheats McpCheatInfo McpCheatArgumentInfo McpGetCheatsBody |
| 개발·테스트 | `input_text` | - | InputTextRequest |
| 개발·테스트 | `interact_world_object` | - | select targetLabel ExplainSelectBlock InteractionWaitSeconds PollSeconds InteractRequest expect |
| 개발·테스트 | `list_dev_actions` | unknown_command | Matches ListBody |
| 개발·테스트 | `look_at_world_object` | - | LookAtRequest |
| 개발·테스트 | `rec_step` | - | CaptureShot NormalizeResult RecStepBody ShotResult RelativePath |
| 개발·테스트 | `run_cheat` | - | ExecuteOne TryParseCommands MaxCommandCount McpRunCheatResult AllSuccess McpRunCheatResultItem |
| 개발·테스트 | `run_dev_action` | - | RunBody |
| 개발·테스트 | `set_camera` | - | TryValidate currentYaw ResolveYaw currentPitch hasOrbitLimits ResolvePitch currentDistance ResolveDistance SetCameraRequest yawDelta pitchDelta distanceDelta McpUiInputProbe BuildPenetrationChecker screenPos TryPickReceiver FindNearbyLabel ReadFirstTextInChildren penetrationTrackerField tryGetPenetrationCheckerMethod isPenetrableProp tracker McpWorldAffordance ResolveInteraction ResolvePropKind |

### 정적 분석으로 본 공통 구조

- 명령 목록(`McpCommandCatalog`): 명령마다 등급(`McpCliTier`: CliChannel=공개 채널, Dev=개발)과 데이터 표 항목(`cliForAiCommandSheetId`)이 있고 `IsCommandEnabled`로 켜고 끈다. `capabilities`(=`McpListCommandsCommand`)는 켜진 것만 보여 주고, 로딩 중엔 `LoadingSafeCommandTypes`만 보여 준다.
- 비용(`McpCommandCost`): 일부 명령은 재화를 쓴다(Free/Paid, 지불 전 보유량·사용량을 응답에 적는다). 부족하면 `not_enough_currency`.
- 실행 전 검사(`McpExecuteGuards`): 가방 무게 초과, 채널 이동 필요, 시설 없음, 비용 부족, 다른 동작 중(선점), 시간 초과, 사용자가 멈춤 → 각각 오류 응답. 실행형 명령은 '이동 → 실행 → 결과 표시' 단계를 기다린다(`WaitPhase`: Traveling·Running).
- 연속 실행 제한(`McpContinuousRunLimit`, `CheckExceeded`, `OpenConfirmPopup`): 자동 실행이 일정 시간 넘게 이어지면 게임이 확인 창을 띄운다.
- 설정: 게임 설정의 'Mabinogi Mobile AI Connector'(`UseCliForAi`)는 마지막 사용 뒤 일정 기간이 지나면 만료된다(`IsCliForAiExpired`, `cliForAiExpirationDuration`). 켤 때 확인 창(`CliForAiActivatePopup`)이 뜬다.
- 통신: CLI(`MabinogiMobile_CLI.exe`)가 네임드 파이프 서버(Bridge)이고 게임이 붙는다. 게임이 TCP 포트를 열지는 않는다(2026-10-22 확인). 코드에 `McpEndpointConfig`(Host·Port·RequestPath)·`DefaultMcpServerPort`·`McpConnectToGatewayCommand`가 있어 다른 연결 방식(게이트웨이)도 준비돼 있다.
- 녹화(`MM.Client.Game.Integration.MCP.Recording`): 설치 폴더에 '무장(armed)' 파일이 있으면 명령 세션을 이벤트·스크린샷으로 기록하는 개발 기능이 있다. 건드리지 않는다.
- 잠긴 명령 중 활용 가치가 큰 것: `get_growth_guide`(콘텐츠 가이드: 레벨/콘텐츠/매력 가이드, 추천 우선순위, 콘텐츠 목표 ContentType·DayType → 숙제 목록 그 자체), `get_chats`(채팅 로그: 보낸 시각 KST·채널·보낸 사람, 마지막으로 읽은 ID 이후만), `get_dialogue_host_state`(대화 진행 상태·선택지), `get_quick_navigation_shortcuts`(빠른 이동 목록), `game_list_destinations`(이동 가능한 장소 목록), `get_login_state`(로그인 화면 상태). 상태 변경형으로는 `execute_quest_tracker`(트래커 퀘스트 실행), `execute_mission_shortcut`(미션 바로가기), `play_growth_guide`(가이드 콘텐츠 실행), `game_move_to_named_location`(이름으로 이동).

## 변경 이력

- 2026-10-22: 문서 신설. 공개 28개 명령 전수 조회·필드 목록, 게임 데이터에서 숨은 명령 이름 추출(조회형 12개 모두 unknown_command), 주간 미션=계정·일일 미션=캐릭터 확인, 녹화기(record.py) 가동.
- 2026-10-22: 게임 코드 이름 표(IL2CPP 식별자 40만 개) 분석 — 명령 클래스 86개(공개 28·잠김·개발용) 확인, 공개 여부는 서버 데이터 표로 결정됨, 비용·실행 검사·연속 실행 제한·AI Connector 만료 구조 파악.
- 2026-10-22: 레이드 클리어를 CLI로 직접 확인하는 방법 발견 — `get_quests`의 `<보스> 토벌 완료` 퀘스트. 녹화기로 에이렐 매우 어려움 한 판을 기록해 확인.
- 2026-10-22: 뱅가드 브리치 1회 녹화 — 캐릭터별 진행은 `[긴급 의뢰] 뱅가드 브리치`의 목표 N/3, 던전 안 퀘스트 `뱅가드 브리치 클리어`, 지역 이름 확인.
