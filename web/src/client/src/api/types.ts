// 로컬 서버 API 응답 형식 (REQUIREMENTS §7). 서버가 camelCase로 만든 DTO와, 게임 CLI 원형을 그대로 넘기는 일부(PascalCase)가 섞여 있다.

export interface Envelope<T> { data: T; fetchedAt: string }
export interface ErrorBody { error: { code: string; message: string } }

export interface Session { kind: 'local' | 'lan' }

/** 음성 입력 (FR-VC) */
export type SttStateName = 'idle' | 'starting' | 'listening' | 'finishing';
export interface SttStatus {
  state: SttStateName; sessionId: string | null; byDevice: string | null; device: string | null;
  modelInstalled: boolean; install: SttInstall; engine?: SttEngineName; qwen3?: { installed: boolean; totalBytes: number; install: SttInstall }; autoSend: boolean; maxSeconds: number; hotkey: SttHotkeyStatus; elevation: SttElevation; refine?: SttRefineStatus;
}
export interface SttStateEvent {
  sessionId: string; state: SttStateName; byDevice?: string | null; error?: string | null; message?: string | null;
  warn?: string | null; device?: string | null; fallback?: boolean;
}
/** 이 앱과 게임의 관리자 권한 여부. game은 게임이 꺼져 있거나 알 수 없으면 null */
export interface SttElevation { app: boolean; game: boolean | null; mismatch: boolean }
export type SttEngineName = 'zipformer' | 'qwen3';
export interface SttInstall { phase: 'idle' | 'downloading' | 'done' | 'failed'; done: number; total: number; file: string | null; error: string | null }
export interface SttHotkeyStatus { key: string | null; registered: boolean; error: string | null; mode: 'hold' | 'toggle' }
export interface SttSettings {
  autoSend: boolean; maxSeconds: number; deviceId: string | null; threads: number; endSilenceMs: number; engine?: SttEngineName;
  hotkey: string | null; hotkeyMode: 'hold' | 'toggle'; cue: boolean; hotwords: boolean; spacing: boolean; spacingLevel: 0 | 1 | 2;
  refine: boolean; refineModel: string; refineMs: number; history: boolean;
}
/** 지난 음성 기록 한 건 (FR-VC-16): original = 인식기가 낸 글자, text = 다듬은 문장, corrected = 내가 고친 문장 */
export interface SttHistoryItem { id: string; at: string; original: string; text: string; autoSent: boolean; corrected: string | null }
/** AI로 다듬기 상태: lastError가 있으면 마지막 시도가 실패해 사전 방식으로 보냈다는 뜻 */
export interface SttRefineStatus { enabled: boolean; model: string | null; cloud: boolean; lastError: string | null; lastMs: number | null }
export interface SttRefineModels { online: boolean; models: { name: string; cloud: boolean }[]; selected: string }
export interface SttVocabWord { word: string; count: number; last: string; hotword: boolean }
export interface SttVocabPair { from: string; to: string; count: number; reverts: number; off: boolean; active: boolean; last: string }
export interface SttVocab { learningOn: boolean; warning: string | null; words: SttVocabWord[]; pairs: SttVocabPair[] }
export interface SttDevice { id: string; name: string; sampleRate: number; channels: number; isDefault: boolean }
export interface SttDevices { devices: SttDevice[]; selectedId: string | null }
export interface SttMicTest { device: string; fallback: boolean; peakDb: number; rmsDb: number; level: 'none' | 'low' | 'ok' | 'loud' }
export interface SttFinalEvent { sessionId: string; text: string; original: string; applied: string[]; autoSent?: boolean }
export interface SttLast extends SttFinalEvent { at: string }

export interface LanView {
  enabled: boolean; active: boolean; hosts: string[]; mdnsName: string | null; port: number;
  error: string | null; networkPrivate: boolean | null;
}
export interface Meta { version: string; serverId: string; chatCountMode: string; chatMaxLength: number; lan: LanView; wpfRunning: boolean }

export type ConnState = 'connected' | 'disconnected' | 'cli_missing' | 'unknown';
export interface Status { state: ConnState; since: string }

export type Level = 'ok' | 'warn' | 'danger';
export interface Header {
  characterKey: string;
  /** 캐릭터 선택창: 아래 값은 마지막으로 본 캐릭터의 것이다 */
  selecting: boolean;
  character: { realm: string; job: string; level: number; title: string; nickname: string | null; combatScore: number; combatDelta: number };
  /** 인게임 딱지 예측 (못 받으면 null) */
  rankBadge: NameBadgeData | null;
  /** 오토 의심을 보는 중(레이드·어비스 안): "주변" 탭이 "파티"로 바뀐다 */
  autoWatching?: boolean;
  scores: { combat: number; combatDelta: number; mdef: number; mdefDelta: number; living: number; attract: number };
  activity: { text: string; inCombat: boolean; canStop: boolean };
  /** 지금 있는 지역이 레이드·어비스·요일 던전이면 그 종류와 이름. 아니면 null */
  inProgress: { kind: string; title: string } | null;
  location: { channel: string | null; space: string | null; weather: string | null; erinn: string };
  weight: { current: number; max: number; pct: number; level: Level; delta: number } | null;
  session: { gold: number; wings: number; nyang: number; dailyMissions: number };
}

export interface Score { DisplayName: string; Value: number }
export interface CharacterRaw {
  Title: string; RealmName: string; Level: number; EnabledCombatJobDisplayName: string;
  CombatScore?: Score; LivingScore?: Score; AttractivenessScore?: Score; DecorScore?: Score;
  HealthMax?: Score; AttackPower?: Score; DefencePower?: Score; ArcaneResistance?: Score;
  STR?: Score; DEX?: Score; INT?: Score; LUCK?: Score; WILL?: Score;
  PaladinStats?: Record<string, Score>;
  Vitals?: {
    HealthCurrent: number; HealthMax: number; InventoryWeightCurrent: number; InventoryWeightMax: number;
    SatietyValue: number; SatietyMax: number; ActiveBuffCount: number;
  };
}
export interface CharacterView { character: CharacterRaw; activity: Record<string, unknown> | null }

export interface InvItem { location: string; name: string; category: string; count: number; locked: boolean; sessionDelta: number; favorite: boolean }
export interface StockHolder { character: string; bag: number; storage: number; atUtc: string }
export interface StockRow { name: string; category: string; total: number; bag: number; storage: number; account: number; holders: StockHolder[] }
export interface StockAccount { id: string; characters: { key: string; label: string; atUtc: string | null }[]; rows: StockRow[] }
export interface Stock { currentAccount: string | null; currentCharacter: string | null; accounts: StockAccount[]; prefs: { targets: Record<string, number>; timed: string[] }; favorites: string[] }
export interface Inventory { weight: { current: number; max: number; pct: number; level: Level } | null; items: InvItem[] }

export interface Currency { DisplayName: string; Amount: number }
export interface Currencies { items: Currency[]; session: { gold: number; wings: number; nyang: number } }

export interface Mission { Title: string; Description: string; CurrentCount: number; GoalCount: number; IsCompleted: boolean; IsRewardReceived: boolean }
export interface MissionGroup { done: number; total: number; items: Mission[] }
export interface Missions { daily: MissionGroup; weekly: MissionGroup }

export interface Work { name: string; facility: string; remainingSeconds: number; done: boolean; remainingText: string; kind: string; kindLabel: string }
export interface Gatherable { name: string; toolOk: boolean; inBag: number; favorite: boolean }
/** 채집 작업 진행 (서버 GatherJob.Snapshot). 게임은 한 번에 100개씩만 캐므로 목표가 크면 서버가 batches번 이어서 호출한다. */
export interface GatherJob {
  jobId: string; item: string;
  state: 'running' | 'completed' | 'stopped' | 'failed' | 'started';
  gained: number; target: number; batch: number; batches: number; message: string | null;
  /** 이 작업 뒤에 이어서 채집할 재료와 목표 개수(순서대로) */ queued?: { item: string; count: number }[];
}
export interface LivingSkill { name: string; level: number; exp: number; maxExp: number }
export interface Life { works: Work[] | null; gatherables: Gatherable[] | null; /** 지금 캐릭터의 생활 스킬(레벨 높은 순) */ skills?: LivingSkill[] | null }

export interface NearPlayer {
  title: string; realm: string; job: string; level: number; combatScore: number; distance: number;
  inCombat: boolean; relation: 'friend' | 'guild' | 'other'; relationLabel: string; isStronger: boolean;
  living: number; attract: number;
  /** 입은 옷 수·옷 색 수, 로브·후드·무기 숨김 */ clothes: number; colors: number; robe: boolean; hood: boolean; weaponHidden: boolean;
  /** 연주 중이면 곡·남은 초·채널 수 */ performing: { title: string | null; remaining: number | null; channels: number | null } | null;
  /** 오토 의심 표식: 던전·레이드 안에서 다른 사람은 모두 전투 중인데 혼자 오래 전투하지 않는 사람 */ autoSuspect?: { reason: 'lying' | 'idle' | 'playing' | 'far' | 'frozen' | 'relative'; seconds: number; intensity: number } | null;
}
/** 함께 보이다가 던전·레이드 안인데 주변에서 오래 보이지 않는 사람(목록에 없어 따로 온다) */
export interface MissingPlayer { /** missing = 안 보임, deserter = 4인 파티에서 싸우는 도중 나감(탈주) */ reason?: string; job: string; realm?: string; level: number; combatScore: number; title: string | null; seconds: number; intensity: number }
export interface Nearby { count: number; /** 던전 안이면 전투력 순, 아니면 가까운 순으로 온다 */ inDungeon?: boolean; myCombatScore: number; /** 내 서버. 서버가 다른 사람만 "직업 (서버)"로 보인다 */ myRealm?: string | null; players: NearPlayer[]; missing?: MissingPlayer[] }

export type CutoffStatus = 'locked' | 'marginal' | 'near' | 'overwhelm';
export interface CutoffContent {
  id: string; name: string; icon: string; maxEntryTier: string | null; recommendedTier: string | null; status: CutoffStatus;
  overwhelmPct: number; combatToOverwhelm: number; mdefShort: number;
  next: { tier: string; combatShort: number; mdefShort: number; readyNow: boolean } | null;
  entryShort: { tier: string; combatShort: number; mdefShort: number } | null;
}
export interface Cutoffs { combat: number; mdef: number; stale: boolean; contents: CutoffContent[] }

export type HomeworkStatus = 'pending' | 'inProgress' | 'poolDone' | 'autoDone' | 'manualDone';
export interface HomeworkSuggestion { code: string; item: string; from: number; to: number }
export interface HomeworkCard {
  id: string; category: string; categoryTitle: string; period: 'daily' | 'weekly'; share: 'character' | 'account';
  mode: string; title: string; subtitle: string; icon: string; count: number; goal: number; status: HomeworkStatus;
  evidence: string | null; pool: string | null; reward: string | null; needsMeasurement: string | null;
  suggestion: HomeworkSuggestion | null; isDone: boolean;
  /** 남은 횟수 (뱅가드 등, 아직 끝나지 않았고 알 때만) */ remaining: number | null;
}
export interface Progress { done: number; total: number }
export interface HomeworkBoard {
  characterKey: string; cards: HomeworkCard[]; daily: Progress; weekly: Progress;
  nextDailyResetUtc: string; nextWeeklyResetUtc: string;
}

/** 내 캐릭터 전체 현황 카드 (FR-AL). 지금 접속한 캐릭터는 실시간 값, 나머지는 마지막으로 관찰한 값이다. */
/** 충전 재화(은동전·마족 공물)의 예상 보유 상태. 가득이면 충전이 멈춰 있다. */
export interface CoinView { held: number; expected: number; cap: number; percent: number; level: 'ok' | 'near' | 'full'; minutesToFull: number }
export interface CharacterCard {
  key: string; /** 같은 서버·직업의 몇 번째 캐릭터인가 (기본 1) */ variant: number; realm: string; job: string; nickname: string | null; isCurrent: boolean; level: number; title: string;
  combat: number; mdef: number; living: number; attract: number; gold: number; deca: number; mcash: number; lastSeen: string;
  silver: CoinView; tribute: CoinView;
  /** 데카·M캐시가 같은 계정의 최신 값과 다르다 = 마지막 접속 이후 동기화되지 않았다 */
  stale: boolean;
  /** 접속 시급도: 은동전·마족 공물 중 더 찬 쪽의 비율 (0~1) */
  urgency: number;
  /** 자동 판정되는 숙제 현황 (요일 던전·카브락·정기 의뢰·가공 수거): done 완료 / todo 이번 주기에 확인한 미완료 / unknown 아직 확인 못 함 */
  homework: HomeworkAuto[];
  /** 필드 보스 택1: 이번 주기에 넘긴 보스 (done이면 item에 보스 이름) */
  fieldBoss: { state: 'done' | 'todo' | 'unknown'; item: string | null; /** 클리어 추정(미션 카운터는 올랐는데 어느 보스인지 모름) */ guess?: boolean };
  /** 인게임 딱지 예측: 색 = 네 분류 중 가장 높은 등급, 숫자 = 그 등급에 든 분류 수 (못 받으면 null) */
  rankBadge: NameBadgeData | null;
  /** get_my_info 스탯(접속 중이면 지금 값, 아니면 마지막 기록, 못 받았으면 null) */
  stats: CharStats;
  /** 어비스·레이드 추천(이 캐릭터의 전투력·마도저항 기준) */
  recommend: { id: string; name: string; status: CutoffStatus; tier: string | null; maxEntry: string | null; entry?: number; entryLevel?: number }[];
}
export interface CharStats { str: number | null; dex: number | null; int: number | null; will: number | null; luck: number | null; attack: number | null; defence: number | null; health: number | null; decor: number | null }
export interface HomeworkAuto { id: string; title: string; period: 'daily' | 'weekly'; state: 'done' | 'todo' | 'unknown'; evidence: string | null; /** 남은 횟수 (뱅가드 등, 알 때만) */ remaining: number | null; category?: string; /** 클리어 추정: 주간 미션 카운터는 올랐는데 어느 항목인지 못 가렸다 */ guess?: boolean; /** 손으로 입력한 수치(랭킹 순위 등), 입력 전이면 없음 */ value?: number | null; /** 게임의 잠금 안내(레벨·퀘스트 조건). 있으면 아직 못 하는 콘텐츠 */ locked?: string | null }
export interface AccountGroup {
  id: string; name: string; solo: boolean; deca: number; mcash: number;
  /** 계정 공통 재화(우선순위 순) · 그 값을 마지막으로 읽은 시각과 캐릭터. sub는 패키지 포인트의 시즌 이름 */
  shared: { name: string; label: string; sub: string | null; amount: number }[]; sharedAt: string | null; sharedBy: string | null;
  membership: { expiresAt: string | null; active: boolean }; caps: { silver: number; tribute: number };
  hasCurrent: boolean; topCombat: number; manualRank: number; members: CharacterCard[];
}
export interface Characters { nowUtc: string; currentAccountId: string | null; accounts: AccountGroup[] }

export interface Overview {
  header: Header;
  currencies: Currencies | null;
  missions: Missions | null;
  life: Life | null;
  nearby: Nearby | null;
  homework: { daily: Progress; weekly: Progress; nextDailyReset: string; nextWeeklyReset: string };
  cutoffs: Cutoffs | null;
}

// ── S5: 채팅·아무말·AI·페어링 ──

export interface ChatLogEntry { at: string; message: string; behaviour: string | null; ok: boolean; error: string | null; source: string; deviceId: string }
export interface ChatPreview { final: string; emoji: string | null; behaviour: string | null; count: number; max: number }
export interface CustomPersona { id: string; name: string; tagEmoji: string; systemPrompt: string; createdAt: string; displayName: string }
export interface PersonaDraft { name: string; emoji: string; prompt: string; fromAi: boolean }

export type CostTier = 'builtin' | 'localFree' | 'paid';
export interface Engine { id: string; name: string; type: string; costTier: CostTier; description: string; /** 추천 모델(gemma4:e4b) */ recommended?: boolean }
export interface Engines { current: string | null; engines: Engine[]; recommendedModel?: string; recommendedInstalled?: boolean }

/** 폰·태블릿 접속 주소 (QR용) */
/** 방화벽: Missing = 포트 허용 규칙 없음, Blocked = 막는 규칙 있음 (폰에서 "응답 시간이 너무 오래 걸립니다"로 보인다). script = 앱 옆에 allow-lan-firewall.bat가 있음 */
export interface LanShare { urlIp: string; urlName: string | null; addresses: string[]; firewall?: { state: 'Ok' | 'Missing' | 'Blocked' | 'Unknown'; script: boolean } }
export interface Settings { maxRefreshSec: number; autoEmoteDefault: boolean; smartEmote?: boolean; personaTone?: boolean; lanEnabled?: boolean; chatterPersona: string; cliPath: string | null; cliAvailable: boolean }

/** POST /api/ai/ask 응답(NDJSON) 한 줄 */
export type AskEvent =
  | { type: 'token'; t: string }
  | { type: 'done'; engine?: string }
  | { type: 'error'; message: string }
  | { type: 'action'; kind: string; ok: boolean; message: string }
  | { type: 'navigate'; to: string }
  | { type: 'intent'; kind: 'collect'; items: string[] }
  | { type: 'intent'; kind: 'gather'; item: string; count: number | null; wingsCost: number };

// ── 서버 랭킹 (v0.3) ──
export type RankTierName = 'gold' | 'orange' | 'pink' | 'purple' | 'none';
/** 인게임 딱지 예측: 색 = 전투력·생활력·매력·종합 중 가장 높은 등급, 숫자 = 그 등급에 든 분류 수(1~4) */
export interface NameBadgeData { tier: Exclude<RankTierName, 'none'>; count: number }
export type RankKindKey = 'total' | 'combat' | 'living' | 'attract';
export interface RankEntryView { rank: number | null; score: number | null; tier: RankTierName; at: string; source: 'bookmarklet'; stale: boolean }
export interface RankView {
  key: string; name: string | null; serverName: string | null;
  /** 캐릭터 이름(별칭)을 입력했는가 */ hasName: boolean;
  /** 서버를 랭킹 조회 대상으로 아는가 */ supported: boolean;
  /** 순위를 가져온 뒤 이름·서버가 바뀌었다: 옛 순위는 쓰지 않는다 */ nameChanged: boolean;
  entries: Partial<Record<RankKindKey, RankEntryView>>;
}
export interface Rankings { rankingUrl: string; freshHours: number; targets: string[]; characters: Record<string, RankView> }
export interface RankingSetup { rankingUrl: string; bookmarklet: string }

