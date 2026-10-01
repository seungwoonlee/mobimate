import { classImageId, figureStyle, jobIconName, jobKind } from './job';

// WPF판 GetJobIcon과 같은 분류 (격투 = 도적 계열, 술사 = 마법 계열)
describe('직업 계열', () => {
  it.each([
    ['대검전사', 'warrior'], ['전사', 'warrior'], ['성기사 나이트', 'warrior'],
    ['궁수', 'archer'], ['석궁사수', 'archer'],
    ['마법사', 'mage'], ['화염술사', 'mage'],
    ['힐러', 'healer'], ['사제', 'healer'],
    ['격투가', 'rogue'], ['도적', 'rogue'],
    ['음유시인', 'bard'],
    ['밀레시안', 'default'], ['', 'default'],
  ])('%s → %s', (job, kind) => {
    expect(jobKind(job)).toBe(kind);
  });

  it('값이 없으면 기본 아이콘', () => {
    expect(jobKind(null)).toBe('default');
    expect(jobKind(undefined)).toBe('default');
    expect(jobIconName('궁수')).toBe('job-archer');
  });
});

// 직업별 전신 이미지: 이름으로 이미지 id를 찾는다 (서버가 내려받는 27개와 같은 id)
describe('직업별 이미지', () => {
  it('27개 직업 모두 이미지 id가 있다', () => {
    const names = ['견습 전사', '기사', '전사', '대검전사', '검술사', '견습 궁수', '궁수', '석궁사수', '장궁병', '견습 도적', '도적', '격투가', '듀얼블레이드',
      '견습 마법사', '전격술사', '마법사', '화염술사', '빙결술사', '견습 음유시인', '음유시인', '댄서', '악사', '견습 힐러', '암흑술사', '힐러', '사제', '수도사'];
    const ids = names.map(n => classImageId(n));
    expect(ids.every(Boolean)).toBe(true);
    expect(new Set(ids).size).toBe(27);                                   // 직업마다 다른 이미지
  });

  it('게임이 주는 이름 그대로 연결된다 (공백은 무시)', () => {
    expect(classImageId('격투가')).toBe('thief_3');
    expect(classImageId('대검전사')).toBe('warrior_4');
    expect(classImageId('음유시인')).toBe('bard_2');
    expect(classImageId('견습전사')).toBe('warrior_1');
    expect(classImageId(' 화염술사 ')).toBe('mage_4');
  });

  it('모르는 직업·값 없음은 null (SVG 아이콘으로 대신)', () => {
    expect(classImageId('밀레시안')).toBeNull();
    expect(classImageId('')).toBeNull();
    expect(classImageId(null)).toBeNull();
  });

  it('전신 이미지는 자르지 않고 그대로 보여 준다', () => {
    expect(figureStyle('thief_3')).toEqual({ backgroundImage: 'url(/api/class-image/thief_3)' });
  });
});
