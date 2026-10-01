import { scoreClass, scoreTone, vitalLevel } from './score';

// FR-DT-11: 게임 안의 색 기준과 같게
describe('점수 색상', () => {
  it('전투력은 103,000 이상일 때만 색이 난다', () => {
    expect(scoreTone('combat', 102_999)).toBe('base');
    expect(scoreTone('combat', 103_000)).toBe('combat');
    expect(scoreTone('combat', 150_000)).toBe('combat');
  });

  it('마도저항은 4,400 이상일 때만 색이 난다', () => {
    expect(scoreTone('mdef', 4_399)).toBe('base');
    expect(scoreTone('mdef', 4_400)).toBe('mdef');
  });

  it('매력은 8,000 / 15,000 / 37,000 기준으로 네 단계다', () => {
    expect(scoreTone('attract', 0)).toBe('att-blue');
    expect(scoreTone('attract', 7_999)).toBe('att-blue');
    expect(scoreTone('attract', 8_000)).toBe('att-purple');
    expect(scoreTone('attract', 14_999)).toBe('att-purple');
    expect(scoreTone('attract', 15_000)).toBe('att-pink');
    expect(scoreTone('attract', 36_999)).toBe('att-pink');
    expect(scoreTone('attract', 37_000)).toBe('att-gold');
  });

  it('값이 없으면 0으로 본다', () => {
    expect(scoreTone('combat', null)).toBe('base');
    expect(scoreTone('attract', undefined)).toBe('att-blue');
    expect(scoreClass('mdef', 5_000)).toBe('sc-mdef');
  });
});

describe('생명·만복도 색', () => {
  it('50% 이상 넉넉함, 25% 이상 보통, 그 아래 부족', () => {
    expect(vitalLevel(100)).toBe('ok');
    expect(vitalLevel(50)).toBe('ok');
    expect(vitalLevel(49.9)).toBe('warn');
    expect(vitalLevel(25)).toBe('warn');
    expect(vitalLevel(24.9)).toBe('danger');
    expect(vitalLevel(0)).toBe('danger');
  });
});
