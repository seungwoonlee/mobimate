# H2b 웹앱 테스트 → 앱 서비스 테스트 대응표

웹앱 `web/tests/MobiMate.Web.Tests`의 시나리오를 `hw/tests/MobiMateHomeWork.Tests`에서 서비스 메서드 호출로 다시 쓴 기록입니다.
HTTP·SSE 대신 서비스 메서드와 `AppEvents`를 사용합니다. 요약: 대상 파일의 원본 28개 중 옮김 22, 의도적 제외 6 (Theory는 1개로 셉니다). 대상 밖 ApiTests에서 수동 체크 시나리오 2개를 함께 옮겨 옮김은 모두 24개입니다.

## 옮긴 것

| 웹앱 원본 | 새 테스트 (파일 · 이름) | 바뀐 점 |
|---|---|---|
| CharacterSelectTests.CharacterSelectScreen_KeepsTheLastRealCharacter_AndMakesNoFakeOne | CharacterSelectTests · 같은 이름 | `/api/header` → `HeaderAsync`, `/api/characters` → `Characters()` |
| CharacterSelectTests.CharacterSelectScreen_WithNoRealCharacterYet_ExplainsInsteadOfInventing | CharacterSelectTests · 같은 이름 | 실패 메시지는 `CliData.Failure`로 확인 |
| CharacterSelectTests.OldFakeProfile_IsHiddenFromTheCharacterList | CharacterSelectTests · 같은 이름 | 저장 폴더에 옛 기록을 미리 넣고 하니스 시작 |
| CharacterWatcherTests.CharacterSwitch_IsPushedWithinSeconds_WithoutAnyClientRequest | CharacterWatcherTests · 같은 이름 | SSE `header` → `AppEvents.HeaderChanged`, 상태는 `CheckNowAsync` |
| CombatRecheckTests.TransientDip_KeepsPreviousValue_AndNeverShowsTheLowOne | CombatRecheckTests · 같은 이름 | 고정 대기(1.2초) → 재확인 뒤 오는 헤더 알림을 조건으로 대기 |
| CombatRecheckTests.RealDrop_IsAcceptedAfterTheRecheckStaysLow | CombatRecheckTests · 같은 이름 | 폴링 → 알림 조건 대기 |
| CombatRecheckTests.HigherValue_IsAcceptedImmediately | CombatRecheckTests · 같은 이름 | |
| InProgressTests (이론 3개: 알려진 지역 7, 요일 던전 3, 그 밖 3) | InProgressTests · 같은 이름 3개 | JSON → `InProgressInfo` |
| ViewsTests.Characters_ListsTheCurrentOneWithLiveValuesFirst | CharactersTests · 같은 이름 | 카드에 없는 골드·데카는 기록(history)에서 확인 |
| ViewsTests.Characters_RememberOtherCharactersAfterTheyLeave | CharactersTests · 같은 이름 | 서버 재시작 → 하니스 재시작 (게임을 읽기 전엔 `IsCurrent` 거짓) |
| AccountsTests.SameDecaAndMCash_GroupsCharactersIntoOneAccount_… | CharactersTests · 같은 이름 | 계정의 데카·M캐시 값은 화면 모델에 없어 제외하고, 어긋난 뒤에도 묶임이 유지되는지를 추가 |
| AccountsTests.Coins_AreForecastFromTheLastSeenAmount_… | CharactersTests · 같은 이름 | `PUT /api/membership` → `AccountGrouper.SetMembership` + `AccountStore.Update` |
| AccountsTests.ManualAssignment_MovesACharacterToAnotherAccount_… | CharactersTests · 같은 이름 | `PUT /api/accounts/assign` → `AccountGrouper.AssignManually` |
| AccountsTests.AccountOrder_IsSavedAndReturnedAsManualRank | CharactersTests · 같은 이름 | `AccountGrouper.SetOrder` |
| AccountsTests.SameServerAndJobOnAnotherAccount_… | CharactersTests · 같은 이름 | |
| AccountsTests.SameServerAndJobAndSameAccountValues_StaysOneCharacter | CharactersTests · 같은 이름 | |
| AccountsTests.Recorder_RecordsTheCurrentCharacterCoinsAndCurrencies | CharactersTests · 같은 이름 | |
| ExternalChangeTests.ExternalHomeworkWrite_IsDetected_AndBroadcastAsHomeworkChanged | ExternalChangeTests · 같은 이름 | SSE → `HomeworkChanged`(External), 감시 신호는 `Check()` 직접 호출 + 감시기 연결 확인 1개 |
| ExternalChangeTests.ExternalAccountWrite_IsBroadcastAsStateChangedAccounts | ExternalChangeTests · ExternalAccountWrite_IsBroadcastAsCharactersChanged | `state.changed(accounts)` → `CharactersChanged` |
| ExternalChangeTests.ExternalWatchOff_ReadsStillSeeTheChange | ExternalChangeTests · 같은 이름 | |
| ApiTests.Homework_SetForAnotherCharacter_RequiresKnownCharacterKey (대상 파일 밖, 함께 옮김) | HomeworkCommandsTests · Set_WithAKey_RejectsAnUnknownCharacter_…, Set_NeedsCompletedOrCount | 404/400 → `UnknownCharacter`/`Validation` |
| ApiTests.Homework_ListSetAndReset 중 수동 체크·멱등 (대상 파일 밖, 함께 옮김) | HomeworkCommandsTests · Set_WithoutKey_UsesTheCurrentCharacter_… | 목록 수(26)·초기화는 Core 테스트 몫이라 제외 |


## 의도적으로 제외한 것 (6)

| 웹앱 원본 | 이유 |
|---|---|
| ViewsTests.Favorites_AreSavedAndShownOnInventoryAndGatherables | 즐겨찾기·인벤토리·생활은 숙제 앱에 없음 (H1에서 제거) |
| ViewsTests.Favorites_SurviveARestart | 위와 같음 |
| ViewsTests.Favorites_RejectBadInput | 위와 같음 |
| ViewsTests.Life_WorksCarryTheirKind | 생활(가공) 화면은 숙제 앱에 없음 |
| AccountsTests.Membership_RejectsBadInput | 웹 API의 입력 검증 (일수 범위·없는 캐릭터). 앱에는 멤버십·계정 지정 명령 서비스가 아직 없음 → 화면(H3)에서 명령을 만들 때 함께 테스트하겠습니다 |
| AccountsTests.DeleteCharacter_RemovesItsRecord_ButNotTheOneThatIsConnected | 웹 API의 삭제 규칙(접속 중 캐릭터 거부). 앱에 삭제 명령이 아직 없음 → 마찬가지로 H3 |

## 위험 지점 테스트 (H2a가 지적)

| 지점 | 테스트 |
|---|---|
| a. 캐릭터 선택창 진입 시 재화 섞임·직업 전환 | CharacterSelectTests (WalletArriving…, MixedWalletBefore…, JobSwap…, SwitchingToAnother…) |
| b. 전투력 재확인이 앱 종료에서 멈춤 | CombatRecheckTests.Recheck_StopsAtApplicationStopping |
| c. RememberCharacter 규칙 | AppSettingsStoreTests (Remember_*), CharactersTests.Remember_SkipsTheFirstReadAfterACharacterSwitch_… |
| d. SetHomework·RenameCharacter | HomeworkCommandsTests |
| e. 외부 변경 | ExternalChangeTests (별개 객체·별개 프로세스 SharedStorageHarness·감시기) |
| f. Characters() | CharactersTests (묶기·코인 예상·무게·맵·숙제 현황) |
| g. 옛 웹앱 감지·단일 인스턴스 | HostingTests (LegacyWebGuard·LegacyWebWatcher·SingleInstance) |
| h. 잠긴 설정 파일 | AppSettingsStoreTests.LockedAtStart_… |
| i. 종료 순서 | HostingTests.Shutdown_* |
