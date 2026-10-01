import { jobIconName, jobKind } from './job';

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
