import { describe, expect, it } from 'vitest';
import type { HomeworkAuto, HomeworkCard } from '../api/types';
import { withFieldBossChip, splitHwRows } from './characters';
import { checklistRows } from './overview';

const card = (id: string, status: HomeworkCard['status'], pool: string | null = null, extra: Partial<HomeworkCard> = {}): HomeworkCard => ({
  id, category: pool ? 'fieldBoss' : 'raid', categoryTitle: pool ? '필드 보스' : '레이드', period: 'weekly', share: 'character', mode: 'manual',
  title: id, subtitle: '', icon: '', count: 0, goal: 1, status, evidence: null, pool, reward: null, needsMeasurement: null, suggestion: null,
  isDone: status === 'autoDone' || status === 'manualDone' || status === 'poolDone', remaining: null, ...extra,
});

describe('필드 보스 택1 (2026-10-22)', () => {
  it('체크 목록은 여섯 보스를 한 줄로 묶고 넘긴 보스 이름을 보여 준다', () => {
    const rows = checklistRows([card('raid_a', 'pending'), card('크라마', 'autoDone', 'p'), card('페리', 'poolDone', 'p'), card('토르모그', 'poolDone', 'p')]);
    expect(rows.map(r => r.title)).toEqual(['raid_a', '필드 보스 (택1)']);
    expect(rows[1]).toMatchObject({ done: true, right: '크라마' });
  });
  it('카운터로만 확인돼 보스를 모르면 완료지만 이름은 비운다', () => {
    const rows = checklistRows([card('페리', 'poolDone', 'p'), card('크라마', 'poolDone', 'p')]);
    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({ done: true, right: '' });
  });
  it('미완료에 한 보스와 싸우는 중이면 진행 중', () => {
    const rows = checklistRows([card('페리', 'pending', 'p'), card('크라마', 'inProgress', 'p')]);
    expect(rows[0]).toMatchObject({ done: false, cls: 'doing', right: '진행 중' });
  });
  it('캐릭터 카드는 개별 보스 칩 대신 "필드보스" 칩 하나', () => {
    const hw: HomeworkAuto[] = [
      { id: 'abyss_madness_cave', title: '광기', period: 'weekly', state: 'todo', evidence: null, remaining: null, category: 'abyss' },
      { id: 'fieldboss_angrbahan', title: '앙그르바한', period: 'weekly', state: 'todo', evidence: null, remaining: null, category: 'fieldBoss' },
    ];
    const list = withFieldBossChip(hw, { state: 'done', item: null });
    expect(list.map(h => h.title)).toEqual(['광기', '필드보스']);
    expect(list[1].state).toBe('done');
    expect(splitHwRows(list)[2].map(h => h.title)).toEqual(['광기', '필드보스']);
  });
});
