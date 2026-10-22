import { describe, expect, it } from 'vitest';
import { plain } from './format';

describe('plain (게임 이름의 서식 태그 지우기)', () => {
  it('색 태그를 지우고 글자만 남긴다', () => {
    expect(plain('<color=#FFC448>★10</color> 초월의 정수')).toBe('★10 초월의 정수');
    expect(plain('<color=orange>에이렐</color>  토벌 완료')).toBe('에이렐 토벌 완료');
    expect(plain('빛의 흔적')).toBe('빛의 흔적');
    expect(plain(null)).toBe('');
  });
});
