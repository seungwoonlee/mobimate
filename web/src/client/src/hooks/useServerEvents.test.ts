import { describe, expect, it } from 'vitest';
import { characterSwitched } from './useServerEvents';
import type { Header } from '../api/types';

const h = (characterKey: string, selecting = false) => ({ characterKey, selecting }) as Header;

describe('characterSwitched', () => {
  it('다른 캐릭터로 바뀌면 전환', () => expect(characterSwitched(h('아이라_댄서'), h('아이라_석궁사수'))).toBe(true));
  it('같은 캐릭터면 전환이 아니다', () => expect(characterSwitched(h('아이라_댄서'), h('아이라_댄서'))).toBe(false));
  it('처음 받은 헤더는 전환이 아니다', () => expect(characterSwitched(undefined, h('아이라_댄서'))).toBe(false));
  it('캐릭터 선택창은 전환으로 치지 않는다', () => expect(characterSwitched(h('아이라_댄서'), h('아이라_댄서', true))).toBe(false));
});
