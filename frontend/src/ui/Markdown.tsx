import type { ReactNode } from 'react';

/** A small, safe Markdown subset: paragraphs, headings, lists, **bold**, *italic*, `code` and http(s) links. No HTML. */
export function Markdown({ text }: { text: string }) {
  const blocks: ReactNode[] = [];
  const lines = text.replace(/\r\n?/g, '\n').split('\n');
  let para: string[] = [];
  let list: { ordered: boolean; items: string[] } | null = null;
  const flushPara = () => {
    if (para.length) blocks.push(<p key={blocks.length}>{inline(para.join(' '))}</p>);
    para = [];
  };
  const flushList = () => {
    if (!list) return;
    const items = list.items.map((it, i) => <li key={i}>{inline(it)}</li>);
    blocks.push(list.ordered ? <ol key={blocks.length}>{items}</ol> : <ul key={blocks.length}>{items}</ul>);
    list = null;
  };
  for (const raw of lines) {
    const line = raw.trimEnd();
    const bullet = /^\s*[-*+]\s+(.*)$/.exec(line);
    const num = /^\s*\d+[.)]\s+(.*)$/.exec(line);
    const head = /^(#{1,4})\s+(.*)$/.exec(line);
    if (!line.trim()) {
      flushPara();
      flushList();
    } else if (head) {
      flushPara();
      flushList();
      blocks.push(
        head[1].length <= 3 ? (
          <h3 key={blocks.length}>{inline(head[2])}</h3>
        ) : (
          <h4 key={blocks.length}>{inline(head[2])}</h4>
        ),
      );
    } else if (bullet || num) {
      flushPara();
      const ordered = !bullet;
      if (list && list.ordered !== ordered) flushList();
      if (!list) list = { ordered, items: [] };
      list.items.push((bullet ?? num)![1]);
    } else {
      flushList();
      para.push(line.trim());
    }
  }
  flushPara();
  flushList();
  return <div className="md">{blocks}</div>;
}

const TOKEN = /(`[^`]+`)|(\*\*[^*]+\*\*)|(\*[^*\s][^*]*\*|_[^_\s][^_]*_)|(\[[^\]]+\]\([^)\s]+\))/g;

export function inline(s: string): ReactNode[] {
  const out: ReactNode[] = [];
  let last = 0;
  let k = 0;
  for (const m of s.matchAll(TOKEN)) {
    const i = m.index ?? 0;
    if (i > last) out.push(s.slice(last, i));
    const tok = m[0];
    if (m[1]) out.push(<code key={k++}>{tok.slice(1, -1)}</code>);
    else if (m[2]) out.push(<strong key={k++}>{inline(tok.slice(2, -2))}</strong>);
    else if (m[3]) out.push(<em key={k++}>{inline(tok.slice(1, -1))}</em>);
    else {
      const lm = /^\[([^\]]+)\]\(([^)\s]+)\)$/.exec(tok)!;
      const href = lm[2];
      if (/^https?:\/\//i.test(href))
        out.push(
          <a key={k++} href={href} target="_blank" rel="noopener noreferrer">
            {lm[1]}
          </a>,
        );
      else out.push(lm[1]);
    }
    last = i + tok.length;
  }
  if (last < s.length) out.push(s.slice(last));
  return out;
}
