import { cardTone, lastSeenText, membershipLeft, splitHwRows, waitText, fillText } from './characters';
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
  it('요일던전·길드·뱅가드 / 레이드 / 어비스·필드 보스 세 줄로 나눈다', () => {
    const [top, raids, abyss] = splitHwRows([
      hw('abyss_madness_cave', 'abyss'), hw('fieldboss_angrbahan', 'fieldBoss'), hw('raid_airel', 'raid'), hw('daily_day_dungeon', 'daily'), hw('raid_cavrak', 'raid'),
    ]);
    expect(top.map(h => h.id)).toEqual(['daily_day_dungeon']);
    expect(raids.map(h => h.id)).toEqual(['raid_cavrak', 'raid_airel']);
    expect(abyss.map(h => h.id)).toEqual(['abyss_madness_cave', 'fieldboss_angrbahan']);   // 어비스 칩 뒤에 앙그르바한
  });
});


describe('fillText: 다가오는 오전 6시 전이면 남은 시간, 이후면 현실 시각', () => {
  const evening = new Date(2026, 9, 10, 18, 0, 0).getTime();   // 10/10 18:00 → 기준은 내일 06:00
  const night = new Date(2026, 9, 11, 2, 0, 0).getTime();      // 10/11 02:00 → 기준은 오늘 06:00
  it('저녁에 내일 새벽 6시 전에 차면 남은 시간', () => expect(fillText(11 * 60, evening)).toBe('11시간 뒤'));     // 05:00
  it('저녁에 6시 이후에 차면 내일 시각', () => expect(fillText(13 * 60, evening)).toBe('내일 오전 07:00에'));
  it('이틀 뒤는 월/일 시각', () => expect(fillText(50 * 60, evening)).toBe('10/12 오후 08:00에'));
  it('새벽에 6시 전에 차면 남은 시간', () => expect(fillText(3 * 60, night)).toBe('3시간 뒤'));             // 05:00
  it('새벽에 6시 이후에 차면 오늘 시각', () => expect(fillText(5 * 60, night)).toBe('오늘 오전 07:00에'));
  it('정각 6시는 현실 시각', () => expect(fillText(12 * 60, evening)).toBe('내일 오전 06:00에'));
});
