import { filterItems, type PaletteItem } from './palette';

const item = (id: string, label: string, kind = '이동'): PaletteItem => ({ id, label, kind, icon: 'home', run: () => {} });
const items = [item('a', '재화 화면'), item('b', '채집: 사과 (보유 8)', '채집'), item('c', '채집: 달걀 (보유 0)', '채집 ★'), item('d', '페르소나: 악덕영애', '페르소나')];

// FR-AC-04: 화면 이동·퀵 액션·채집 아이템 이름 검색·페르소나 전환을 한 곳에서
describe('빠른 실행 검색', () => {
  it('검색어가 없으면 전부 (한도까지)', () => {
    expect(filterItems(items, '')).toHaveLength(4);
    expect(filterItems(items, '   ', 2)).toHaveLength(2);
  });

  it('이름 일부로 찾는다', () => {
    expect(filterItems(items, '사과').map(i => i.id)).toEqual(['b']);
    expect(filterItems(items, '재화').map(i => i.id)).toEqual(['a']);
  });

  it('종류 이름으로도 찾고, 여러 낱말은 모두 들어 있어야 한다', () => {
    expect(filterItems(items, '채집').map(i => i.id)).toEqual(['b', 'c']);
    expect(filterItems(items, '채집 달걀').map(i => i.id)).toEqual(['c']);
    expect(filterItems(items, '페르소나 영애').map(i => i.id)).toEqual(['d']);
    expect(filterItems(items, '없는것')).toEqual([]);
  });
});
