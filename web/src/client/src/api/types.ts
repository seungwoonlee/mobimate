// 로컬 서버 API 응답 형식 (REQUIREMENTS §7). 서버가 camelCase로 만든 DTO와, 게임 CLI 원형을 그대로 넘기는 일부(PascalCase)가 섞여 있다.

export interface Envelope<T> { data: T; fetchedAt: string }
export interface ErrorBody { error: { code: string; message: string } }

export interface Session { kind: 'local' | 'lan' }

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
  scores: { combat: number; combatDelta: number; /** 전투력 서버 순위 뱃지 (순위를 모르면 null) */ combatRank: RankBadgeData | null; mdef: number; mdefDelta: number; living: number; attract: number };
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
export interface Inventory { weight: { current: number; max: number; pct: number; level: Level } | null; items: InvItem[] }

export interface Currency { DisplayName: string; Amount: number }
export interface Currencies { items: Currency[]; session: { gold: number; wings: number; nyang: number } }

export interface Mission { Title: string; Description: string; CurrentCount: number; GoalCount: number; IsCompleted: boolean; IsRewardReceived: boolean }
export interface MissionGroup { done: number; total: number; items: Mission[] }
export interface Missions { daily: MissionGroup; weekly: MissionGroup }

export interface Work { name: string; facility: string; remainingSeconds: number; done: boolean; remainingText: string; kind: string; kindLabel: string }
export interface Gatherable { name: string; toolOk: boolean; inBag: number; favorite: boolean }
export interface Life { works: Work[] | null; gatherables: Gatherable[] | null }

export interface NearPlayer {
  title: string; realm: string; job: string; level: number; combatScore: number; distance: number;
  inCombat: boolean; relation: 'friend' | 'guild' | 'other'; relationLabel: string; isStronger: boolean;
}
export interface Nearby { count: number; myCombatScore: number; players: NearPlayer[] }

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
  /** 전투력 서버 순위 뱃지 (10000위 이내만, 모르면 null) */
  combatRank: RankBadgeData | null;
}
export interface HomeworkAuto { id: string; title: string; period: 'daily' | 'weekly'; state: 'done' | 'todo' | 'unknown'; evidence: string | null; /** 남은 횟수 (뱅가드 등, 알 때만) */ remaining: number | null; category?: string }
export interface AccountGroup {
  id: string; name: string; solo: boolean; deca: number; mcash: number;
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
export interface Engine { id: string; name: string; type: string; costTier: CostTier; description: string }
export interface Engines { current: string | null; engines: Engine[] }

/** 폰·태블릿 접속 주소 (QR용) */
/** 방화벽: Missing = 포트 허용 규칙 없음, Blocked = 막는 규칙 있음 (폰에서 "응답 시간이 너무 오래 걸립니다"로 보인다). script = 앱 옆에 allow-lan-firewall.bat가 있음 */
export interface LanShare { urlIp: string; urlName: string | null; addresses: string[]; firewall?: { state: 'Ok' | 'Missing' | 'Blocked' | 'Unknown'; script: boolean } }
export interface Settings { maxRefreshSec: number; autoEmoteDefault: boolean; lanEnabled?: boolean; chatterPersona: string; cliPath: string | null; cliAvailable: boolean }

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
export interface RankBadgeData { rank: number; tier: Exclude<RankTierName, 'none'>; stale: boolean; at: string }
export type RankKindKey = 'total' | 'combat' | 'living' | 'attract';
export interface RankEntryView { rank: number | null; score: number | null; tier: RankTierName; at: string; source: 'bookmarklet' | 'manual'; stale: boolean }
export interface RankView {
  key: string; name: string | null; serverName: string | null;
  /** 캐릭터 이름(별칭)을 입력했는가 */ hasName: boolean;
  /** 서버를 랭킹 조회 대상으로 아는가 */ supported: boolean;
  /** 순위를 가져온 뒤 이름·서버가 바뀌었다: 옛 순위는 쓰지 않는다 */ nameChanged: boolean;
  entries: Partial<Record<RankKindKey, RankEntryView>>;
}
export interface Rankings { rankingUrl: string; freshHours: number; targets: string[]; characters: Record<string, RankView> }
export interface RankingSetup { rankingUrl: string; bookmarklet: string }

