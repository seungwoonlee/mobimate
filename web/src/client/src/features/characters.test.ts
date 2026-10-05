import { cardTone, lastSeenText, membershipLeft, splitHwRows, waitText } from './characters';
import type { CoinView, HomeworkAuto } from '../api/types';

const coin = (level: CoinView['level']): CoinView => ({ held: 1, expected: 1, cap: 100, percent: 0.5, level, minutesToFull: 60 });
const card = (silver: CoinView['level'], tribute: CoinView['level'], stale = false) => ({ silver: coin(silver), tribute: coin(tribute), stale });

// 요청 4·5: 가득 = 빨강, 80% 이상 = 노랑, 데카·M캐시가 어긋남 = 진회색 (우선순위 순)
describe('전체 현황 카드 색', () => {
  it('은동전·마족 공물 중 하나라도 가득이면 빨강 (회색보다 우선)', () => {
    expect(cardTone(card('full', 'ok'))).toBe('red');
    expect(cardTone(card('ok', 'full'))).toBe('red');
    expect(cardTone(card('full', 'near', true))).toBe('red');
  });

  it('80% 이상이면 노랑', () => {
    expect(cardTone(card('near', 'ok'))).toBe('yellow');
    expect(cardTone(card('ok', 'near', true))).toBe('yellow');
  });

  it('데카·M캐시가 어긋났고 충전 재화는 여유로우면 진회색', () => {
    expect(cardTone(card('ok', 'ok', true))).toBe('gray');
    expect(cardTone(card('ok', 'ok', false))).toBe('none');
  });
});

describe('시간 표시', () => {
  const now = Date.parse('2026-10-01T12:00:00Z');
  it('마지막 접속', () => {
    expect(lastSeenText('2026-10-01T11:59:40Z', now)).toBe('방금');
    expect(lastSeenText('2026-10-01T11:20:00Z', now)).toBe('40분 전');
    expect(lastSeenText('2026-10-01T04:00:00Z', now)).toBe('8시간 전');
    expect(lastSeenText('2026-09-28T12:00:00Z', now)).toBe('3일 전');
  });

  it('가득 찰 때까지 남은 시간', () => {
    expect(waitText(45)).toBe('45분');
    expect(waitText(60)).toBe('1시간');
    expect(waitText(200)).toBe('3시간 20분');
    expect(waitText(60 * 50)).toBe('2일 2시간');
    expect(waitText(-5)).toBe('0분');
  });

  it('멤버십 남은 시간은 자동으로 줄고, 3일 이내면 경고', () => {
    expect(membershipLeft('2026-10-28T15:00:00Z', now)).toEqual({ text: '27일 3시간', urgent: false });
    expect(membershipLeft('2026-10-04T12:00:00Z', now)).toEqual({ text: '3일 0시간', urgent: true });   // 정확히 3일 = 경고
    expect(membershipLeft('2026-10-04T12:01:00Z', now)?.urgent).toBe(false);
    expect(membershipLeft('2026-10-01T17:20:00Z', now)).toEqual({ text: '5시간 20분', urgent: true });
    expect(membershipLeft('2026-10-01T12:00:30Z', now)).toEqual({ text: '1분', urgent: true });
  });

  it('등록이 없거나 이미 끝났으면 null', () => {
    expect(membershipLeft(null, now)).toBeNull();
    expect(membershipLeft('2026-10-01T11:59:00Z', now)).toBeNull();
  });
});

describe('자동 판정 칩 줄', () => {
  const hw = (id: string, category: string) => ({ id, title: id, category, period: 'weekly', state: 'unknown', evidence: null } as unknown as HomeworkAuto);
  it('요일던전·길드·뱅가드 / 레이드·필드 보스 / 어비스 세 줄로 나눈다', () => {
    const [top, raids, abyss] = splitHwRows([
      hw('abyss_madness_cave', 'abyss'), hw('fieldboss_angrbahan', 'fieldBoss'), hw('raid_airel', 'raid'), hw('daily_day_dungeon', 'daily'), hw('raid_cavrak', 'raid'),
    ]);
    expect(top.map(h => h.id)).toEqual(['daily_day_dungeon']);
    expect(raids.map(h => h.id)).toEqual(['raid_cavrak', 'raid_airel', 'fieldboss_angrbahan']);   // 카브락·에이렐·(화이트서큐)·앙그르바한 순
    expect(abyss.map(h => h.id)).toEqual(['abyss_madness_cave']);
  });
});

