import { Fragment, type ReactNode } from 'react';

/**
 * AI 답변용 최소 Markdown (FR-AI-07): 문단·줄바꿈, 목록(-, *, 1.), 제목(#~###), 코드 블록(```), 굵게(**), 기울임(*), 인라인 코드(`).
 * HTML은 해석하지 않는다: React 텍스트 노드로만 만들기 때문에 태그는 글자 그대로 보인다(이스케이프).
 */
export function Markdown({ text }: { text: string }) {
  const blocks: ReactNode[] = [];
  const lines = text.replace(/\r\n?/g, '\n').split('\n');
  let i = 0;
  let key = 0;
  while (i < lines.length) {
    const line = lines[i];
    if (line.trim().startsWith('```')) {
      const code: string[] = [];
      i++;
      while (i < lines.length && !lines[i].trim().startsWith('```')) code.push(lines[i++]);
      i++;   // 닫는 ``` (없으면 끝까지)
      blocks.push(<pre key={key++} className="md-code"><code>{code.join('\n')}</code></pre>);
      continue;
    }
    const heading = /^(#{1,3})\s+(.*)$/.exec(line);
    if (heading) {
      blocks.push(<p key={key++} className={`md-h md-h${heading[1].length}`}>{inline(heading[2])}</p>);
      i++;
      continue;
    }
    if (/^\s*([-*]|\d+[.)])\s+/.test(line)) {
      const ordered = /^\s*\d+[.)]\s+/.test(line);
      const items: string[] = [];
      while (i < lines.length && /^\s*([-*]|\d+[.)])\s+/.test(lines[i])) items.push(lines[i++].replace(/^\s*([-*]|\d+[.)])\s+/, ''));
      const List = ordered ? 'ol' : 'ul';
      blocks.push(<List key={key++} className="md-list">{items.map((t, j) => <li key={j}>{inline(t)}</li>)}</List>);
      continue;
    }
    if (!line.trim()) { i++; continue; }
    const para: string[] = [];
    while (i < lines.length && lines[i].trim() && !/^\s*([-*]|\d+[.)])\s+/.test(lines[i]) && !lines[i].trim().startsWith('```') && !/^#{1,3}\s/.test(lines[i])) para.push(lines[i++]);
    blocks.push(<p key={key++}>{para.map((p, j) => <Fragment key={j}>{j > 0 && <br />}{inline(p)}</Fragment>)}</p>);
  }
  return <div className="md">{blocks}</div>;
}

/** 줄 안의 `코드`, **굵게**, *기울임*. 순서대로 가장 먼저 나오는 표시를 처리한다. */
function inline(s: string): ReactNode[] {
  const out: ReactNode[] = [];
  const re = /`([^`]+)`|\*\*([^*]+)\*\*|\*([^*\s][^*]*)\*/g;
  let last = 0;
  let m: RegExpExecArray | null;
  let k = 0;
  while ((m = re.exec(s))) {
    if (m.index > last) out.push(s.slice(last, m.index));
    if (m[1] !== undefined) out.push(<code key={k++}>{m[1]}</code>);
    else if (m[2] !== undefined) out.push(<strong key={k++}>{m[2]}</strong>);
    else out.push(<em key={k++}>{m[3]}</em>);
    last = m.index + m[0].length;
  }
  if (last < s.length) out.push(s.slice(last));
  return out;
}
