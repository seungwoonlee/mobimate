import { groupItems } from './inventory';
import type { InvItem } from '../api/types';

describe('groupItems', () => {
  test('변화량은 위치마다 한 번만 더하고, 줄어든 것은 음수로 남는다', () => {
    const row = (location: string, count: number, sessionDelta: number): InvItem => ({ location, name: '옥수수', category: '재료', count, locked: false, sessionDelta, favorite: false });
    const [g] = groupItems([row('bag', 2, -3), row('bag', 1, -3), row('account', 5, 4)]);
    expect(g.sessionDelta).toBe(1);
    expect(g.total).toBe(8);
  });
  const it = (location: string, name: string, count: number): InvItem => ({ location, name, category: '재료', count, locked: false, sessionDelta: 0, favorite: false });
  test('같은 이름을 위치별 개수와 총합으로 합친다', () => {
    const g = groupItems([it('bag', '옥수수', 3), it('account', '옥수수', 5), it('character', '옥수수', 2), it('bag', '옥수수', 1), it('bag', '밀', 9)]);
    const c = g.find(x => x.name === '옥수수')!;
    expect(c.total).toBe(11);
    expect(c.by).toEqual({ bag: 4, account: 5, character: 2 });
    expect(g).toHaveLength(2);
  });
});
