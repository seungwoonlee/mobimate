import { useEffect, useMemo, useState } from 'react';
import { useLife, usePersonas } from '../api/queries';
import { Dialog, Icon } from '../components/ui';
import { collectAll, stopAction } from '../lib/actions';
import { ROUTES, useRouter, type RouteName } from '../state/router';
import { useUi } from '../state/ui';
import { BUILTIN_PERSONAS, setPersona } from './dock/GameChat';

/** 빠른 실행 항목: 화면 이동 · 퀵 액션 · 채집 아이템 · 페르소나 (FR-AC-04) */
export interface PaletteItem { id: string; label: string; kind: string; icon: string; run: () => void }

/** 검색: 공백으로 나눈 낱말이 모두 들어 있는 항목만 (대소문자 무시). 즐겨찾기·앞쪽 항목이 먼저 나온다. */
export function filterItems(items: PaletteItem[], query: string, limit = 30): PaletteItem[] {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  const hit = words.length ? items.filter(i => words.every(w => i.label.toLowerCase().includes(w) || i.kind.toLowerCase().includes(w))) : items;
  return hit.slice(0, limit);
}

export function CommandPalette() {
  const open = useUi(s => s.paletteOpen);
  const setOpen = useUi(s => s.setPaletteOpen);
  if (!open) return null;
  return <PaletteDialog onClose={() => setOpen(false)} />;
}

function PaletteDialog({ onClose }: { onClose: () => void }) {
  const go = useRouter(s => s.go);
  const life = useLife();
  const personas = usePersonas();
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);

  const items = useMemo<PaletteItem[]>(() => {
    const list: PaletteItem[] = [
      { id: 'act:stop', label: '긴급 정지', kind: '액션', icon: 'stop', run: () => void stopAction() },
    ];
    const ready = life.data?.data.works?.filter(w => w.done).map(w => w.name) ?? [];
    list.push({ id: 'act:collect', label: `완료된 가공물 모두 수거${ready.length ? ` (${ready.length})` : ''}`, kind: '액션', icon: 'inbox', run: () => void collectAll(ready) });
    for (const r of ROUTES) list.push({ id: `go:${r.name}`, label: `${r.label === '전체' ? '전체 현황' : r.label} 화면`, kind: '이동', icon: r.icon, run: () => go(r.name as RouteName) });
    const gather = [...(life.data?.data.gatherables ?? [])].sort((a, b) => Number(b.favorite) - Number(a.favorite));
    for (const g of gather) {
      list.push({ id: `gather:${g.name}`, label: `채집: ${g.name} (보유 ${g.inBag})`, kind: g.favorite ? '채집 ★' : '채집', icon: 'leaf', run: () => go('life', { gather: g.name }) });
    }
    for (const [value, label] of BUILTIN_PERSONAS) list.push({ id: `persona:${value}`, label: `페르소나: ${label.replace(/^\S+\s/u, '')}`, kind: '페르소나', icon: 'wand', run: () => void setPersona(value) });
    for (const p of personas.data?.data ?? []) list.push({ id: `persona:custom:${p.id}`, label: `페르소나: ${p.name}`, kind: '페르소나', icon: 'wand', run: () => void setPersona(`custom:${p.id}`) });
    return list;
  }, [life.data, personas.data, go]);

  const shown = useMemo(() => filterItems(items, query), [items, query]);
  useEffect(() => { setActive(0); }, [query]);

  const run = (i: PaletteItem | undefined) => {
    if (!i) return;
    onClose();
    i.run();
  };

  return (
    <Dialog title="빠른 실행" onClose={onClose} actions={<button type="button" className="btn" onClick={onClose}>닫기</button>}>
      <div className="pal">
        <input autoFocus value={query} onChange={e => setQuery(e.target.value)} placeholder="화면·채집 재료·페르소나 검색 (예: 사과, 재화, 정지)"
          role="combobox" aria-expanded="true" aria-controls="pal-list" aria-activedescendant={shown[active] ? `pal-${active}` : undefined} aria-label="빠른 실행 검색"
          onKeyDown={e => {
            if (e.key === 'ArrowDown') { e.preventDefault(); setActive(a => Math.min(shown.length - 1, a + 1)); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(a => Math.max(0, a - 1)); }
            else if (e.key === 'Enter' && !e.nativeEvent.isComposing) { e.preventDefault(); run(shown[active]); }
          }} />
        <ul id="pal-list" className="pal-list" role="listbox" aria-label="실행할 항목">
          {shown.length === 0 && <li className="muted small" style={{ padding: 8 }}>맞는 항목이 없습니다.</li>}
          {shown.map((i, n) => (
            <li key={i.id} id={`pal-${n}`} className="pal-item" role="option" aria-selected={n === active} onMouseMove={() => setActive(n)} onClick={() => run(i)}>
              <Icon name={i.icon} /><span>{i.label}</span><span className="kind">{i.kind}</span>
            </li>
          ))}
        </ul>
      </div>
    </Dialog>
  );
}
