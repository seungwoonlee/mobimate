# 게임 CLI 명령 표 (MabinogiMobile_CLI)

> 자동 생성: `web/tools/CliResearch/make_doc.py` — 마지막 생성 2026-10-22 22:38 (게임에서 새로 읽음). CLI 실행 파일 2026-10-22 09:25.
> 손으로 쓰는 실측 메모는 `notes.json`에 더하고 다시 생성한다. 게임하는 동안 `record.py`(녹화기)를 켜 두면 값이 바뀐 필드가 `records/`에 쌓인다.

## 개요

- CLI(`MabinogiMobile_CLI.exe`)는 .NET 10 NativeAOT로 만든 얇은 중계기다. 명령 이름·응답은 게임 클라이언트가 정한다(모르는 명령은 게임이 `unknown_command`로 거절). 사용법: `MabinogiMobile_CLI <dispatch-command> [body]`, `capabilities`, `status`.
- `status`는 게임 연결만 알려 준다: `{"pipe":"connected"}` 또는 `{"pipe":"disconnected","reason":"game_off",...}`. 게임이 꺼져 있으면 모든 명령이 약 5.03초 기다린 뒤 이 응답을 준다(2026-10-22 실측).
- 응답 시간은 보통 약 93ms, 20개를 동시에 불러도 문제없다(2026-10-22 실측).
- 파이프로 받은 출력은 대개 UTF-8이고 한글은 \uXXXX로 이스케이프된다. 콘솔에 바로 찍으면 cp949로 나올 수 있다.
- CLI 실행 파일은 게임 업데이트 없이도 갱신된다(2026-10-22 09:25 갱신, 명령 목록은 그대로 28개).
- 게임 데이터(global-metadata.dat 문자열 표)에는 공개 28개 외에 조회형 명령 이름이 더 있다. 2026-10-22 현재 모두 unknown_command — 아래 '숨은 명령' 표 참조. 녹화기(record.py)가 CLI가 바뀔 때마다 다시 확인한다.
- 랭킹·서버 순위·랭킹 딱지 정보는 어느 공개 명령에도 없다(2026-10-22 전수 조사). `Title`은 칭호 문자열뿐이다.

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
| `ChannelDisplayName` | string | 1 |
| `GameSpaceDisplayName` | string | 1 |
| `WorldPosition.X` | number | 6 |
| `WorldPosition.Y` | number | 6 |
| `Weather` | string |  |
| `ErinnNow` | string | 9 |
| `Housing.IsInHousing` | bool | 2 |
| `Housing.IsOwnedHousing` | bool |  |
| `Housing.CanEnterHousing` | bool | 2 |
| `Housing.CannotEnterHousingReason` | string |  |
| `Housing.CanExitHousing` | bool |  |

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

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `IsAutoPlaying` | bool | 3 |
| `CanStartAutoPlay` | bool |  |
| `AutoPlayTarget` | string | 2 |
| `AutoPlayTargetDisplayName` | string | 2 |
| `IsAutoTraveling` | bool | 7 |
| `AutoTravelRemainingPositionCount` | int | 8 |
| `IsDead` | bool |  |
| `IsReviving` | bool |  |
| `IsInCombat` | bool | 2 |
| `IsDialoguePlaying` | bool |  |
| `IsDialogueNextAvailable` | bool |  |
| `IsWaitingForSelection` | bool |  |
| `Dungeon.State` | string | 2 |
| `Dungeon.IsBossBattleInProgress` | bool | 1 |
| `Battlefield.IsInBattleField` | bool |  |
| `IsAbyssResultSequencePlaying` | bool |  |
| `Tutorial.IsPlaying` | bool |  |
| `Scenario.IsInScenario` | bool |  |
| `Scenario.IsSequencePlaying` | bool |  |
| `Performance.IsPlaying` | bool |  |
| `Performance.InstrumentName` | string |  |
| `Performance.MusicTitle` | string |  |
| `Performance.IsCopyingAllowed` | bool |  |
| `Performance.IsLoop` | bool |  |
| `Performance.StartAt` | string |  |
| `Performance.TotalDurationSeconds` | number |  |
| `Performance.ElapsedSeconds` | number |  |
| `Performance.RemainingSeconds` | number |  |
| `Performance.ChannelCount` | int |  |
| `Interaction.HasTarget` | bool | 7 |
| `Interaction.IsTargetAttackable` | bool | 6 |
| `Interaction.AvailableInteractionType` | string | 2 |
| `Interaction.LastRunningInteractionType` | string | 1 |
| `Interaction.TargetKind` | string | 8 |
| `Mode.MainButtonState` | string | 10 |
| `Mode.MountPartState` | string |  |
| `Mode.SitState` | string |  |
| `Mode.IsPlayingMiniGame` | bool |  |
| `Mode.IsHousingEditMode` | bool |  |

### `get_my_info`

- 설명: Query my character's basic info, stats, and current vitals
- 출력(명세): { Title, RealmName, Level, EnabledCombatJobDisplayName, CombatScore, LivingScore, AttractivenessScore, DecorScore, HealthMax, AttackPower, DefencePower, STR, DEX, INT, LUCK, WILL, ArcaneResistance, PaladinStats: { PaladinAttackPower, PaladinDefencePower, JusticePower, JudgementPower, OrderPower, BlessingPower }, Vitals: { HealthCurrent, HealthMax, ShieldAmount, ShieldMax, SatietyValue, SatietyMax, SatietyRatio, InventoryWeightCurrent, InventoryWeightMax, ActiveBuffCount } }. Every stat field is an object { DisplayName, Value }; use DisplayName verbatim as the stat name. Vitals = current condition (current HP/shield/satiety/bag weight and active buff count). Location/weather and housing are in get_current_environment; instrument performance and other live activity are in get_activity.
- 실측: `Title` = 칭호(랭킹 딱지 아님). 전투력·마도저항·스탯·가방 무게(Vitals.InventoryWeight*).
- 실측: 가방 최대 무게가 3.4e38(float 최대)로 오는 때가 있다 → 앱은 비정상 값으로 거른다.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `Title` | string | 2 |
| `RealmName` | string |  |
| `Level` | int |  |
| `EnabledCombatJobDisplayName` | string |  |
| `CombatScore.DisplayName` | string |  |
| `CombatScore.Value` | int | 2 |
| `LivingScore.DisplayName` | string |  |
| `LivingScore.Value` | int |  |
| `AttractivenessScore.DisplayName` | string |  |
| `AttractivenessScore.Value` | int | 2 |
| `DecorScore.DisplayName` | string |  |
| `DecorScore.Value` | int |  |
| `HealthMax.DisplayName` | string |  |
| `HealthMax.Value` | int | 2 |
| `AttackPower.DisplayName` | string |  |
| `AttackPower.Value` | int | 6 |
| `DefencePower.DisplayName` | string |  |
| `DefencePower.Value` | int | 2 |
| `ArcaneResistance.DisplayName` | string |  |
| `ArcaneResistance.Value` | int |  |
| `STR.DisplayName` | string |  |
| `STR.Value` | int | 2 |
| `DEX.DisplayName` | string |  |
| `DEX.Value` | int | 2 |
| `INT.DisplayName` | string |  |
| `INT.Value` | int | 2 |
| `LUCK.DisplayName` | string |  |
| `LUCK.Value` | int | 4 |
| `WILL.DisplayName` | string |  |
| `WILL.Value` | int | 2 |
| `PaladinStats.PaladinAttackPower.DisplayName` | string |  |
| `PaladinStats.PaladinAttackPower.Value` | int |  |
| `PaladinStats.PaladinDefencePower.DisplayName` | string |  |
| `PaladinStats.PaladinDefencePower.Value` | int |  |
| `PaladinStats.JusticePower.DisplayName` | string |  |
| `PaladinStats.JusticePower.Value` | int |  |
| `PaladinStats.JudgementPower.DisplayName` | string |  |
| `PaladinStats.JudgementPower.Value` | int |  |
| `PaladinStats.OrderPower.DisplayName` | string |  |
| `PaladinStats.OrderPower.Value` | int |  |
| `PaladinStats.BlessingPower.DisplayName` | string |  |
| `PaladinStats.BlessingPower.Value` | int |  |
| `Vitals.HealthCurrent` | int | 2 |
| `Vitals.HealthMax` | int | 2 |
| `Vitals.ShieldAmount` | int | 2 |
| `Vitals.ShieldMax` | int | 2 |
| `Vitals.SatietyValue` | int |  |
| `Vitals.SatietyMax` | int |  |
| `Vitals.SatietyRatio` | number |  |
| `Vitals.InventoryWeightCurrent` | number | 3 |
| `Vitals.InventoryWeightMax` | number | 2 |
| `Vitals.ActiveBuffCount` | int | 4 |

### `get_quests`

- 설명: List Quest Tracker entries
- 출력(명세): array of { QuestTitle, Source, SourceDisplayName, Objectives[] }. Returns only currently visible and executable Quest Tracker items. Source: main, pinned_sub, auto_register_sub, auto_register_candidate_sub, event, goddess_mission, shortcut, division_objective, guide_mission, recommended_activity. When naming the category, use SourceDisplayName verbatim; for items without it, do not translate Source and describe by the quest title only. Objectives: { Description, IsCompleted, Count, Goal }
- 명세 메모: Returns only items in the active Quest Tracker tab.
- 실측: 퀘스트 트래커의 **현재 탭**에 보이는 것만 준다. 길드 의뢰 등은 트래커에 걸려 있어야 보인다.
- 실측: Source `recommended_activity`(추천, 예: '요일 던전 소탕')·`guide_mission`도 온다.
- 실측: 필드 보스 앙그르바한: 처치하면 '토벌' 퀘스트가 '영역 나가기'로 바뀐다(2026-10-05). 처치 순간 목록이 잠깐 빈다.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array | 5 |

### `get_currencies`

- 설명: Query my main currency balances
- 출력(명세): array of { DisplayName, Amount }
- 실측: 레이드 클리어 보상(실측): 에이렐·서큐버스 골드 약 +101,100~101,200·하트 토큰 +3, 카브락 골드 +1,100·하트 +3·원정의 증거 +24(어려움). 어비스(매우 어려움) 마물 퇴치 증표 +300·골드 +100,200·하트 +1.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array |  |
| `[].DisplayName` | string |  |
| `[].Amount` | int | 3 |

### `get_near_npcs`

- 설명: Query nearby talkable NPCs
- 출력(명세): array of { Name, Title, DisplayName, Distance }
- 명세 메모: Search radius 30. Returns only talkable NPCs and NPC companions.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array |  |

### `get_near_pcs`

- 설명: Query nearby players
- 출력(명세): array of { RealmName, Title, Distance, ClothesCount, CrowdAppearanceColorCount, IsRobeWeared, IsHoodOn, IsWeaponHidden, Level, EnabledCombatJobDisplayName, CombatScore, LivingScore, AttractivenessScore, IsFriend, IsInParty, HasGuild, IsSameGuild, IsCoOwner, IsInCombat, Performance }. IsCoOwner = co-owner of the My Home I am currently in; always false outside a My Home. Performance: { IsPlaying, MusicTitle, IsCopyingAllowed, IsLoop, StartAt, TotalDurationSeconds, ElapsedSeconds, RemainingSeconds, ChannelCount }. IsLoop is the current loop setting. MusicTitle/IsCopyingAllowed/StartAt/TotalDurationSeconds/ElapsedSeconds are only set when IsPlaying is true; RemainingSeconds is only set when TotalDurationSeconds > 0
- 명세 메모: Search radius 30. Related: write_chat
- 실측: 반경 약 30m만. 이름·사망 여부는 없다. `Performance.IsPlaying`(연주 중)은 있다.
- 실측: `Title`은 칭호. 랭킹 딱지 없음.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array | 1 |

### `get_inventory`

- 설명: Query current/max inventory weight
- 출력(명세): { CurrentInventoryWeight, CurrentInventoryWeightAsDecimal, MaxInventoryWeight, MaxInventoryWeightAsDecimal }
- 명세 메모: For the item list use get_items. Related: get_items, get_music_scores
- 실측: 가방 무게 현재/최대. Decimal 판도 같이 온다.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `CurrentInventoryWeight` | number | 3 |
| `CurrentInventoryWeightAsDecimal` | int | 3 |
| `MaxInventoryWeight` | number | 2 |
| `MaxInventoryWeightAsDecimal` | int | 2 |

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
| `[].Location` | string | 3 |
| `[].DisplayName` | string | 3 |
| `[].Category` | string | 3 |
| `[].CategoryDisplayName` | string | 3 |
| `[].Count` | int | 4 |
| `[].IsLocked` | bool | 3 |

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
| `[].Title` | string |  |
| `[].Description` | string |  |
| `[].CurrentCount` | int | 5 |
| `[].GoalCount` | int |  |
| `[].IsCompleted` | bool |  |
| `[].IsRewardReceived` | bool |  |
| `[].HasShortcut` | bool |  |

### `get_weekly_missions`

- 설명: List active weekly missions for my account with goals, progress, and completion state
- 출력(명세): success: status accepted, body: array of { Title, Description, CurrentCount, GoalCount, IsCompleted, IsRewardReceived, HasShortcut } / failure: status rejected, body { error, message }
- 명세 메모: HasShortcut indicates whether the mission supports an auto-play shortcut. Related: get_daily_missions
- 실측: 실측(2026-10-22): 같은 계정 전사·듀얼블레이드의 15개 값이 모두 같다. 레이드·어비스는 '1회 토벌'(목표 1)이라 계정에서 첫 클리어 한 번만 오른다.
- 실측: 서큐버스 처치 순간 '레이드 1회 토벌' 0→1(2026-10-22 09:32:40, 진입 약 1분 50초 뒤).
- 실측: 앱 규칙: 이 캐릭터로 지켜보던 중(직전 읽기 10분 안)에 오른 것만 그 캐릭터의 근거.

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `[]` | array |  |
| `[].Title` | string |  |
| `[].Description` | string |  |
| `[].CurrentCount` | int |  |
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

| 필드 | 타입 | 녹화 중 바뀐 횟수 |
|---|---|---|
| `items[]` | array |  |
| `items[].DisplayName` | string |  |
| `items[].Alterable` | bool |  |
| `items[].ProducedPerWork` | int |  |
| `items[].Reason` | string |  |
| `items[].MissingIngredients[]` | array |  |
| `items[].MissingIngredients[].DisplayName` | string |  |
| `items[].MissingIngredients[].Required` | int |  |
| `items[].MissingIngredients[].Owned` | int |  |

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
| `items[].MissingIngredients[].DisplayName` | string |  |
| `items[].MissingIngredients[].Required` | int |  |
| `items[].MissingIngredients[].Owned` | int | 1 |

### `execute_crafting`

- 설명: Craft a named recipe, including travel to its facility and result collection
- 입력(body) 예: `{"displayName":"<DisplayName from get_craftable_items>","craftCount":1}`
- 출력(명세): success: status accepted, body.result is 'completed' (craft completed; the result and facility UI are closed automatically; body also includes craftCount, rewards, and criticalRewards) or 'stopped_by_user' (the user ended crafting in the game before it completed; nothing new was produced) / interrupted or timed out after starting: status accepted, body { error, message }; blocked carries kind = the UI or state the user must resolve; timeout means the action was stopped; canceled means another command replaced this action / rejected before starting: status rejected, body { error, message }. errors: crafting_locked, not_found, not_available, invalid_count, insufficient_living_skill_level, insufficient_facility_level, insufficient_decor_score, not_enough_ingredient, ingredient_locked, insufficient_transfer_cost, blocked, overweight, not_in_field, facility_not_found. invalid_count also includes maxCount. / invalid body (displayName missing): status invalid_body
- 명세 메모: displayName must exactly match an item from get_craftable_items. craftCount is the NUMBER OF CRAFTS, not the number of output items: each craft yields get_craftable_items.ProducedPerCraft items. craftCount defaults to 1 and has a per-facility upper limit; exceeding it is rejected as invalid_count with maxCount. This call blocks through travel, crafting, and result collection. The result and facility UI close automatically after completion. If a UI or state that requires user input appears before or during the action, crafting stops and returns blocked with its kind; tell the user what to resolve before retrying. To make more items, call again after a completed response. Related: get_craftable_items, get_activity, get_quests. Running this command consumes 5 정령의 날개. It is rejected with not_enough_currency when the user lacks it and with cost_payment_failed when payment fails; success bodies include cost, a sentence with the amount spent and the remaining balance.

## 숨은 명령 (게임 데이터에 이름만 있음)

게임의 IL2CPP 문자열 표(`MabinogiMobile_Data/il2cpp_data/Metadata/global-metadata.dat`)에서 뽑은 이름이다. 공개 목록(capabilities)에는 없다. 조회형만 실제로 불러 확인한다.

| 명령 | 상태 (확인 2026-10-22T22:38) |
|---|---|
| `get_growth_guide` | unknown_command |
| `get_chats` | unknown_command |
| `get_camera_state` | unknown_command |
| `get_dialogue_host_state` | unknown_command |
| `get_login_state` | unknown_command |
| `get_quick_navigation_shortcuts` | unknown_command |
| `get_housing_inventory` | unknown_command |
| `get_housing_placements` | unknown_command |
| `get_housing_placement_mode_state` | unknown_command |
| `get_photo_mode_state` | unknown_command |
| `list_dev_actions` | unknown_command |
| `game_list_destinations` | unknown_command |

상태를 바꾸는 것으로 보이는 후보(호출하지 않음): `execute_mission_shortcut`, `execute_quest_tracker`, `execute_quick_navigation_shortcut`, `run_dev_action`, `game_move_to_named_location`, `start_housing_edit_mode`, `end_housing_edit_mode`, `move_housing_prop`, `cancel_housing_placement`, `set_photo_mode_blur`, `set_photo_mode_zoom`, `take_photo`, `set_camera`

## 변경 이력

- 2026-10-22: 문서 신설. 공개 28개 명령 전수 조회·필드 목록, 게임 데이터에서 숨은 명령 이름 추출(조회형 12개 모두 unknown_command), 주간 미션=계정·일일 미션=캐릭터 확인, 녹화기(record.py) 가동.
