# 게임 CLI 전면 재조사 (2026-10-22)

> **명령 표·필드 전체 목록은 `docs/CLI_COMMAND_TABLE.md`(자동 생성, 계속 갱신)를 기준으로 한다.** 이 문서는 2026-10-22 재조사 당시의 판단 기록이다.

> 승운 요청: "전체 CLI에 대해 전면 재조사를 하고, 처음 계획 대비 근본부터 고칠 것을 모두 재검토". 이 문서는 조사 방법·결과·결정 필요 사항과, 이번에 바로 고친 것을 적는다.
> CLI는 `C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe`(게임 업데이트 2026-10-15). 이후 변화는 앱이 `capabilities`를 6시간마다 비교해 로그·`cli_changes.txt`에 남긴다(FR-RF-10).

## 1. 조사 방법
- `capabilities`의 28개 명령 정의(설명·본문 예·출력 예·메모)를 모두 읽었다.
- **조회 19개를 실제로 실행**해 원본 응답을 저장하고(모양·크기·소요 시간), 설계 당시 명세(`docs/API_SPEC_SAMPLES.md`, 2026-09-22)의 응답 샘플과 **키 단위로 비교**했다.
- 앱 모델(`GameModels.cs`)·판정 코드가 쓰는 필드와 대조했다. 속도·동시 호출·오류 응답 형태도 측정했다.
- **조작 9개(`write_chat`·`execute_*`·`stop_action` 등)는 실행하지 않았다**: 게임 상태를 바꾸거나(캐릭터 이동·채집) 비용이 든다(정령의 날개). 설명 문서만 검토했다.

## 2. 결론 (중요한 것부터)
| # | 발견 | 영향 | 상태 |
|---|---|---|---|
| 1 | **`get_activity`가 호환되지 않게 바뀌었다.** 명세 시점의 `IsGathering`·`IsAltering`·`IsCrafting`·`IsPlayingInstrument`·`IsSitting`·`IsOverweight`·`CurrentAction`·`CanStopCurrentAction`·`IsTalking`·`IsCarrying`이 사라지고 `Mode`·`Performance`·`Interaction`·`IsReviving`이 생겼다 | 헤더의 "채집·가공·제작·연주·휴식 중" 표시와 "정지 가능" 판단이 **오류 없이 항상 꺼져 있었다** | **수정함**(새 필드로 읽음, 옛 필드는 호환 유지). 채집·제작·가공 중의 정확한 신호는 실측 필요(§5) |
| 2 | **오류 응답을 값으로 읽고 있었다.** 실패하면 CLI는 같은 자리에 오류 객체(`{"error":…}`, `{"pipe":"disconnected","reason":"game_off"}`)를 준다 | 배열 명령은 `PARSE_ERROR`, 객체 명령은 "빈 값의 성공"으로 조용히 틀릴 수 있었다 | **수정함**(`CliEnvelope`, 오류를 실패로 처리) |
| 3 | **숙제 판정의 "퀘스트가 사라지면 완료"는 구조적으로 약하다.** `get_quests`는 "현재 보이고 실행 가능한 *활성 트래커 탭* 항목만" 준다 | 탭 전환·로딩·던전 안 목록 변화가 "사라짐"으로 보여 오탐 가능(앞서 뱅가드 오탐에서 본 "로딩 중 목록이 일부만 옴"과 같은 종류일 수 있으나, 원인을 이 설명만으로 단정하지는 못한다) | **재설계 후보 — 결정 필요**(§4-A) |
| 4 | **호출이 훨씬 싸다.** 평균 93ms, 20개 동시 호출도 전부 정상(0.78초) | NFR-06(전역 단일 큐)·NFR-07(캐시 3초)·갱신 주기 설계가 필요 이상으로 보수적 | 제안(§4-B) |
| 5 | **주간 미션에 직접 카운터가 있고, 캐릭터별이다**: `뱅가드 브리치 3회 토벌 3/3`, `레이드 1회 토벌`, `어비스 1회 토벌`, `필드 보스 N회 토벌` … 같은 계정에서 캐릭터를 바꾸자 값이 줄었다(궁수 3/3 → 화염술사 0/3, 연금술 20/20 → 1/20) | 증가분 귀속이 필요 없다: 그 캐릭터의 카운터가 곧 그 캐릭터의 진행이다. (요구사양 K-01의 "계정 단위"는 틀렸다) | **뱅가드 적용함**(FR-HW-36). 레이드·어비스·필드 보스는 2단계(§4-A) |
| 6 | 캐릭터 이름·고유 ID는 여전히 **없다**(서버·직업·레벨·전투력만). 주변 플레이어도 이름이 없고 **사망 여부도 없다** | 캐릭터 키 `서버_직업` 한계 유지. 오토 의심의 "누워 있음"은 추정 | 변화 없음(문서화) |
| 7 | `execute_crafting`·`execute_altering`는 **호출마다 정령의 날개 5개를 쓴다**. `write_chat`은 게임 안 확인 팝업(사용자가 Send)·서버 문자 필터·50자·호출 제한이 있다 | 자동 실행 기능을 넣을 때 비용·확인 정책이 필요 | 정책 제안(§4-E) |
| 8 | 새 정보: `get_current_environment.WorldPosition`(내 좌표), `get_near_pcs`의 `Performance`(연주)·복장 정보, `get_inventory`(가방 무게 전용) | 오토 의심 강화, 무게 이상값 대체 경로 | 제안(§4-C·D) |

**정정**: 앞서 요구사양서 K-14에 "새 명령 2개(`get_social_actions`·`get_alterable_items`)"라고 적었으나 틀렸다. 설계 당시 명세에도 "총 지원 명령 28개"라고 적혀 있고 지금도 28개다. 두 명령은 새로 생긴 것이 아니라 **문서·앱이 다루지 않던 것**이다(K-14를 고쳤다).

## 3. 명세(2026-09-22) 대비 응답 변화 (키 비교)
- `get_my_info`·`get_currencies`·`get_current_environment`·`get_daily_missions`·`get_inventory`·`get_items`·`get_gatherable_items`: **키 동일**.
- `get_activity`: **크게 바뀜**(결론 1). 새 필드: `AutoTravelRemainingPositionCount`, `IsReviving`, `IsDialoguePlaying`, `IsDialogueNextAvailable`, `IsWaitingForSelection`, `Battlefield`, `IsAbyssResultSequencePlaying`, `Tutorial`, `Scenario`, `Performance{…}`, `Interaction{…}`, `Mode{MainButtonState, MountPartState, SitState, …}`. 사라진 필드는 결론 1. `Dungeon`은 명세에는 없었고 10-12 실측으로 앱에 들어와 있다.
- `get_near_pcs`: 새 키 `AttractivenessScore`, `ClothesCount`, `CrowdAppearanceColorCount`, `IsCoOwner`, `IsHoodOn`, `IsRobeWeared`, `IsWeaponHidden`, `Performance{…}`. **반경 30m 안만** 준다(`Distance`는 m).
- `get_altering_works`: `works[].IsCompleted` 추가.
- `get_quests`: 조사 시점에 트래커가 비어 있어 비교 불가(설명상 모양은 같다: `QuestTitle, Source, SourceDisplayName, Objectives[]`).
- 재화는 11개 → 29개로 보이지만 **CLI 변화가 아니라** 샘플을 받은 캐릭터의 보유 차이다(`원정의 증거`·`마물 퇴치 증표` 등은 앱 카탈로그에 이미 있다).
- 채팅 자동 이모티콘·행동(`ChatPlanService`의 8개 행동 명령·8개 이모지)은 **전부 현재 `get_social_actions` 목록에 있다**(행동 명령 98개·이모티콘 25개).

## 4. 설계 가정 재검토와 제안
### A. 숙제 판정 근거 (뱅가드는 적용 완료, 나머지는 2단계)
> **진행 상황(2026-10-22)**: 승운이 "미션 카운터 방식으로 진행"을 승인했다. 1단계로 **뱅가드 브리치**를 캐릭터별 주간 미션 카운터로 바꿨고(FR-HW-36), 실측에서 주간 미션이 캐릭터별임을 확인해 `주간 미션 완수`도 캐릭터별로 고쳤다. 아래 제안 중 **레이드·어비스·필드 보스**(미션은 "어느 레이드·어디"를 알려 주지 않아 항목을 단독으로 정하지 못한다 → 카운터가 오른 순간의 현재 지역으로 귀속)는 2단계다.
- 지금: 뱅가드·요일 던전은 "퀘스트가 보였다가 사라지면 완료"(약한 근거), 레이드·어비스는 재화·지역·조각 등 간접 근거.
- 제안: **미션 카운터 증가분으로 클리어를 확정**한다. `get_weekly_missions`의 `뱅가드 브리치 N회`·`레이드 N회`·`어비스 N회`·`필드 보스 N회`와 `get_daily_missions`의 `던전 N회`는 숫자가 오른 그 순간이 클리어다. 주간 미션은 계정 단위라 "지금 접속 중인 캐릭터가 지켜보는 동안 오른 값"을 그 캐릭터의 클리어로 귀속한다(캐릭터 전환·앱 꺼짐 사이의 증가는 귀속하지 않고 기존 근거로 보완).
- 효과: 뱅가드는 남은 횟수를 정확히 세고(3→2→1), 퀘스트 목록 변화에 흔들리지 않는다. 레이드·어비스·필드 보스도 확정 근거가 하나 더 생긴다.
- 위험: 주간 미션이 계정 단위라 다른 캐릭터 진행분과 섞일 수 있다(귀속 규칙으로 방어). 구현 규모는 중간(Core 판정 + 테스트), 실측 검증 필요.

### B. CLI 호출 정책
- 평균 93ms·20개 동시 정상. 제안: ① 일반 레인 직렬 큐를 **동시 3~4개**로 완화(설정값), ② 캐시 TTL 3초 유지, ③ 개요 갱신 묶음(10개 안팎)이 1초 → 0.3초대로 준다. 단 게임 부하가 큰 순간의 안전성은 모르니 설정으로 되돌릴 수 있게 한다.
- 무거운 응답 주의: `get_craftable_items`는 **1,838개·약 400KB**(0.15초), `get_alterable_items` 91개, `get_items` 759개·136KB. 폴링 대상에서 빼고 필요할 때만, 필터(`name`)를 쓴다.

### C. 오토 의심 보강
- `WorldPosition`(내 좌표)으로 "따라가지 않음"을 30m 밖에서 사라지기 **전에** 알 수 있다: 내가 크게 움직였는데 그 사람과의 거리가 내 이동만큼 벌어지면 제자리다. 구현 규모 작음.
- 주변 플레이어의 사망 값이 없어 "누워 있음"은 "전투 안 함 + 거리 불변"의 추정이다(경험적으로 조정).

### D. 가방 무게 이상값
- 로딩 중 `Vitals`의 최대 무게가 3.4e38로 오는 때가 있다. 이상값은 이미 무시한다. `get_inventory`(가방 무게 전용)가 이때도 정상값을 주는지는 재현하지 못했다. 재현되면 이상값일 때 `get_inventory`로 대체한다.

### E. 조작 명령 정책
- `execute_crafting`·`execute_altering`: 호출마다 **정령의 날개 5개**. 앱은 `execute_altering`/`execute_crafting`을 호출하지 않는다(가공 수거 `complete_altering_work`만). 넣게 된다면 **항상 확인 창 + 비용 표시**를 둔다.
- `write_chat`: 게임 창의 확인 팝업에서 사용자가 Send를 눌러야 나간다(앱은 입력창에 채우기까지만). 서버 문자 필터에 걸리면 `inappropriate_text`, 호출 제한 `rate_limited(retryAfterSeconds)`.
- `execute_gathering`: 한 번에 최대 100개, 장소는 가장 가까운 곳 자동 선택(앱의 배치 처리와 일치). 낚시 전용 아이템은 자동 낚시를 켜고 즉시 반환, 끝내려면 `stop_action`.

### F. 새 기능 후보 (CLI는 이미 줄 수 있는 것)
- `get_craftable_items`·`get_alterable_items`의 `MissingIngredients`로 "재료가 모자란 레시피 / 지금 만들 수 있는 것" 추천.
- `get_social_actions`(행동 98개·이모티콘 25개)로 채팅의 행동 선택 목록.
- `get_music_scores`·`get_instruments`·`change_instrument`·`play_music_score` 연주 도우미.

## 5. 실측이 필요한 것 (게임을 하면서 확인)
- **채집·제작·가공을 *직접 할 때*의 `get_activity` 값**: `Mode.MainButtonState`(Stop)와 `Interaction.LastRunningInteractionType`가 어떻게 바뀌는지. 지금은 `MainButtonState == Stop`을 "작업 중"(정지 가능)으로, `LastRunningInteractionType == Gathering`을 "채집 중"으로 추정해 표시한다.
- 위 A의 미션 카운터가 실제로 클리어 순간에 오르는지(뱅가드 1판, 레이드, 어비스, 필드 보스).
- 가방 무게 이상값이 올 때 `get_inventory` 응답.

## 6. 이번에 바로 고친 것
1. `get_activity` 새 응답 호환(`ActivityInfo`에 `IsReviving`·`Mode`·`Performance`·`Interaction`, `DescribeActivity`·`canStop`을 새 필드로). 가짜 CLI 샘플도 실제 응답으로 교체. 테스트 `ActivityCompatTests`.
2. CLI 오류 응답 인식(`CliEnvelope`, `GameQueries`·`RunJsonAsync`). 테스트 `CliEnvelopeTests`.
3. CLI 업데이트 자동 점검(`CliChangeWatcher`, FR-RF-10)과 K-14·K-15 기록(정정 포함).

## 7. 결정이 필요한 것
1. **A(미션 카운터 기반 클리어 확정)**를 진행할지 — 가장 큰 변경이라 승인이 필요하다.
2. **B(동시 호출 완화)**를 켤지, 켠다면 동시 개수(권장 3).
3. **C(내 좌표로 따라가지 않음 강화)** 진행 여부.
4. F의 새 기능 중 먼저 할 것.

## 부록: 28개 명령 (실측)
| 명령 | 종류 | 앱 | 소요 | 크기 | 설명 |
|---|---|---|---|---|---|
| `capabilities` | 조회 | **미사용** | — (실행 안 함) | — | List available commands |
| `get_current_environment` | 조회 | 사용 | 120ms | 0.3KB | Query current location / weather / time |
| `write_chat` | 조작 | 사용 | — (실행 안 함) | — | Send a chat message |
| `get_activity` | 조회 | 사용 | 80ms | 1.1KB | Query current auto-play / travel / combat / action state |
| `get_my_info` | 조회 | 사용 | 80ms | 1.5KB | Query my character's basic info, stats, and current vitals |
| `get_quests` | 조회 | 사용 | 80ms | 0.0KB | List Quest Tracker entries |
| `get_currencies` | 조회 | 사용 | 90ms | 1.9KB | Query my main currency balances |
| `get_near_npcs` | 조회 | **미사용** | 90ms | 0.1KB | Query nearby talkable NPCs |
| `get_near_pcs` | 조회 | 사용 | 80ms | 13.8KB | Query nearby players |
| `get_inventory` | 조회 | **미사용** | 130ms | 0.1KB | Query current/max inventory weight |
| `get_music_scores` | 조회 | **미사용** | 100ms | 1.6KB | List owned game-provided music scores |
| `play_music_score` | 조작 | **미사용** | — (실행 안 함) | — | Start playing a music score |
| `get_instruments` | 조회 | **미사용** | 120ms | 1.4KB | List owned instruments |
| `change_instrument` | 조작 | **미사용** | — (실행 안 함) | — | Equip (change to) an owned instrument |
| `get_items` | 조회 | 사용 | 100ms | 132.9KB | List owned items and their counts (bag / account storage / character s |
| `get_social_actions` | 조회 | **미사용** | 120ms | 8.2KB | List behaviors (Behaviours) and facials (Facials) |
| `stop_action` | 조작 | 사용 | — (실행 안 함) | — | Stop the current stoppable action (instrument performance, chair sitti |
| `stand_up` | 조작 | 사용 | — (실행 안 함) | — | Stand up from sitting (/앉기) |
| `get_daily_missions` | 조회 | 사용 | 80ms | 2.3KB | List active daily missions for my character with goals, progress, and  |
| `get_weekly_missions` | 조회 | 사용 | 80ms | 3.2KB | List active weekly missions for my account with goals, progress, and c |
| `get_gatherable_items` | 조회 | 사용 | 90ms | 8.9KB | List every gatherable item whose living-skill level requirement is met |
| `execute_gathering` | 조작 | 사용 | — (실행 안 함) | — | Gather a named item until the target quantity is reached, or start aut |
| `get_alterable_items` | 조회 | **미사용** | 90ms | 20.7KB | List altering recipes with alterability and missing ingredients; alter |
| `get_altering_works` | 조회 | 사용 | 80ms | 0.9KB | Query in-progress and completed altering works with remaining time |
| `execute_altering` | 조작 | **미사용** | — (실행 안 함) | — | Queue a named altering recipe, including travel to its facility |
| `complete_altering_work` | 조작 | 사용 | — (실행 안 함) | — | Collect all completed altering works at one facility, including travel |
| `get_craftable_items` | 조회 | **미사용** | 150ms | 399.3KB | List crafting recipes with craftability and missing ingredients; alter |
| `execute_crafting` | 조작 | **미사용** | — (실행 안 함) | — | Craft a named recipe, including travel to its facility and result coll |
