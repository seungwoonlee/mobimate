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
  scores: { combat: number; combatDelta: number; mdef: number; mdefDelta: number; living: number; attract: number };
  activity: { text: string; inCombat: boolean; canStop: boolean };
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
  inCombat: boolean; relation: 'party' | 'friend' | 'guild' | 'other'; relationLabel: string; isStronger: boolean;
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
export interface CharacterCard {
  key: string; realm: string; job: string; nickname: string | null; isCurrent: boolean; level: number; title: string;
  combat: number; mdef: number; living: number; attract: number; gold: number; deca: number; lastSeen: string;
}

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
export interface LanShare { urlIp: string; urlName: string | null; addresses: string[] }
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
