import { describe, expect, it } from 'vitest';
import { jobLabel } from './players';

describe('주변 캐릭터 직업 (서버)', () => {
  it('내 서버와 다르면 서버를 붙인다', () => expect(jobLabel('힐러', '이그엘', '아이라')).toBe('힐러 (이그엘)'));
  it('같은 서버면 직업만', () => expect(jobLabel('힐러', '아이라', '아이라')).toBe('힐러'));
  it('내 서버를 모르면 직업만', () => expect(jobLabel('힐러', '이그엘', null)).toBe('힐러'));
});
