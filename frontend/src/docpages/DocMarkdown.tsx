import { Marked, type Token, type Tokens } from 'marked';
import { Fragment, useMemo, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import type { RecordKind } from '../api/types';
import { useI18n } from '../i18n';
import { IconApp, IconBulb, IconCopy, IconDoc, IconDomain, IconNote, IconTeam, IconWarn, TypeIcon } from '../ui/icons';
import { useToast } from '../ui/Toast';
import type { Resolved } from './model';

/*
 * Page bodies are parsed with marked's lexer and rendered as React elements: raw HTML in a body is shown as text, never
 * injected, and links keep to http(s), mailto and in-app paths. Wiki links ([[kind:id|label]]) become record chips.
 */

interface WikiToken {
  type: 'wiki';
  raw: string;
  kind: RecordKind;
  id: string;
  label: string | null;
}

const WIKI_START =
  /^\[\[(application|flow|dataObject|page|domain|team):([a-z0-9]+(?:-[a-z0-9]+)*)(?:\|([^\]\r\n]*))?\]\]/;

const md = new Marked({ gfm: true, breaks: false });
md.use({
  extensions: [
    {
      name: 'wiki',
      level: 'inline',
      start: (src: string) => {
        const i = src.indexOf('[[');
        return i < 0 ? undefined : i;
      },
      tokenizer: (src: string) => {
        const m = WIKI_START.exec(src);
        if (!m) return undefined;
        return { type: 'wiki', raw: m[0], kind: m[1], id: m[2], label: m[3]?.trim() || null };
      },
    },
  ],
});

export interface TocEntry {
  id: string;
  text: string;
}

export interface ParsedDoc {
  tokens: Token[];
  toc: TocEntry[];
  /** Heading ids, in document order (h2 and h3). */
  headingIds: string[];
}

const ENTITIES: Record<string, string> = { '&amp;': '&', '&lt;': '<', '&gt;': '>', '&quot;': '"', '&#39;': "'" };
const unescape = (s: string) => s.replace(/&(amp|lt|gt|quot|#39);/g, (e) => ENTITIES[e] ?? e);

/** The plain text of inline tokens (headings in the table of contents). */
export function plainText(tokens: Token[] | undefined): string {
  if (!tokens) return '';
  return tokens
    .map((tk) => {
      const t = tk as Tokens.Generic;
      if (t.type === 'wiki') return (t as unknown as WikiToken).label ?? (t as unknown as WikiToken).id;
      if (t.tokens) return plainText(t.tokens);
      return typeof t.text === 'string' ? unescape(t.text) : '';
    })
    .join('');
}

export function headingSlug(text: string, used: Set<string>): string {
  const base =
    'h-' +
    (text
      .normalize('NFD')
      .replace(/[̀-ͯ]/g, '')
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-|-$/g, '') || 'section');
  let id = base;
  for (let n = 2; used.has(id); n++) id = `${base}-${n}`;
  used.add(id);
  return id;
}

export function parseDoc(body: string): ParsedDoc {
  const tokens = md.lexer(body.replace(/\r\n?/g, '\n'));
  const used = new Set<string>();
  const toc: TocEntry[] = [];
  const headingIds: string[] = [];
  for (const tk of tokens)
    if (tk.type === 'heading') {
      const h = tk as Tokens.Heading & { anchor?: string };
      const text = plainText(h.tokens);
      h.anchor = headingSlug(text, used);
      headingIds.push(h.anchor);
      if (h.depth <= 2) toc.push({ id: h.anchor, text });
    }
  return { tokens, toc, headingIds };
}

/** In-app paths, http(s) and mailto links only; anything else is shown as text. */
export function safeHref(href: string): string | null {
  const h = href.trim();
  if (/^(https?:|mailto:)/i.test(h)) return h;
  if (/^\/(?!\/)/.test(h) || h.startsWith('#')) return h;
  return null;
}

export type Resolve = (kind: RecordKind, id: string) => Resolved;

function KindIcon({ kind, r }: { kind: RecordKind; r: Resolved }) {
  if (kind === 'application') return <IconApp size={14} />;
  if (kind === 'flow') return <TypeIcon type={r.flowType ?? 'batch'} size={14} />;
  if (kind === 'dataObject') return <span className={'sd ' + (r.sens ?? 'internal')} aria-hidden="true" />;
  if (kind === 'team') return <IconTeam size={14} />;
  if (kind === 'domain') return <IconDomain size={14} />;
  return <IconDoc size={14} />;
}

export function WikiChip({
  kind,
  id,
  label,
  resolve,
}: {
  kind: RecordKind;
  id: string;
  label: string | null;
  resolve: Resolve;
}) {
  const { t } = useI18n();
  const r = resolve(kind, id);
  const kindName = t.pages.kindNames[kind] ?? kind;
  if (!r.exists) {
    const tip = t.pages.brokenTip(kindName, id);
    return (
      <span className="wl broken" tabIndex={0} role="note" title={tip} data-tip={tip}>
        <IconWarn size={14} />
        {label ?? `${kind}:${id}`}
      </span>
    );
  }
  const cls = 'wl k-' + kind + (r.flowType ? ' t-' + r.flowType : '');
  const text = label ?? r.title;
  if (r.restricted || !r.href)
    return (
      <span className={cls + ' restricted'} title={t.pages.restrictedLink} tabIndex={0}>
        <KindIcon kind={kind} r={r} />
        {label ?? t.common.restrictedShort}
      </span>
    );
  const title = kindName.charAt(0).toUpperCase() + kindName.slice(1) + ' · ' + r.title;
  return (
    <Link className={cls} to={r.href} title={title}>
      <KindIcon kind={kind} r={r} />
      {text}
    </Link>
  );
}

/** Light highlighting for shell-like code: comment lines, and the command word of each line. */
function CodeLine({ line, lang }: { line: string; lang: string }) {
  if (/^\s*#/.test(line) && lang !== 'md' && lang !== 'markdown') return <span className="c">{line}</span>;
  const m = /^(\s*)([a-z][\w-]*)(.*)$/.exec(line);
  if (!m || !/^(bash|sh|shell|zsh|console|text|)$/.test(lang)) return <>{line}</>;
  return (
    <>
      {m[1]}
      <span className="k">{m[2]}</span>
      {m[3]}
    </>
  );
}

function CodeBlock({ text, lang }: { text: string; lang: string }) {
  const { t } = useI18n();
  const toast = useToast();
  const copy = () => {
    void navigator.clipboard?.writeText(text).then(
      () => toast(t.pages.codeCopied),
      () => toast(t.pages.copyFailed),
    );
  };
  const lines = text.split('\n');
  return (
    <div className="cb">
      <div className="cb-h">
        <span>{lang || 'text'}</span>
        <button type="button" onClick={copy}>
          <IconCopy size={14} />
          {t.pages.copy}
        </button>
      </div>
      <pre>
        <code>
          {lines.map((l, i) => (
            <Fragment key={i}>
              <CodeLine line={l} lang={lang.toLowerCase()} />
              {i < lines.length - 1 && '\n'}
            </Fragment>
          ))}
        </code>
      </pre>
    </div>
  );
}

type CalloutType = 'note' | 'tip' | 'warn';
const CALLOUT = /^\[!(NOTE|TIP|WARNING|CAUTION|IMPORTANT)\][ \t]*/i;

/** Splits the inline tokens of a callout's first paragraph into its title line and the text after it. */
function splitCallout(tokens: Token[]): { title: Token[]; body: Token[] } {
  const title: Token[] = [];
  const body: Token[] = [];
  let inBody = false;
  for (const tk of tokens) {
    if (inBody) {
      body.push(tk);
      continue;
    }
    if (tk.type === 'text' || tk.type === 'br') {
      const text = tk.type === 'br' ? '\n' : (tk as Tokens.Text).text;
      const nl = text.indexOf('\n');
      if (nl >= 0) {
        if (nl > 0) title.push({ ...tk, type: 'text', text: text.slice(0, nl), raw: text.slice(0, nl) } as Token);
        const rest = text.slice(nl + 1);
        if (rest) body.push({ ...tk, type: 'text', text: rest, raw: rest } as Token);
        inBody = true;
        continue;
      }
    }
    title.push(tk);
  }
  return { title, body };
}

function Doc({ tokens, resolve }: { tokens: Token[]; resolve: Resolve }) {
  const { t } = useI18n();

  const inline = (list: Token[] | undefined): ReactNode[] =>
    (list ?? []).map((tk, i) => {
      const x = tk as Tokens.Generic;
      switch (x.type) {
        case 'text':
          return x.tokens ? <Fragment key={i}>{inline(x.tokens)}</Fragment> : unescape(x.text as string);
        case 'escape':
          return unescape(x.text as string);
        case 'strong':
          return <strong key={i}>{inline(x.tokens)}</strong>;
        case 'em':
          return <em key={i}>{inline(x.tokens)}</em>;
        case 'del':
          return <del key={i}>{inline(x.tokens)}</del>;
        case 'codespan':
          return <code key={i}>{unescape(x.text as string)}</code>;
        case 'br':
          return <br key={i} />;
        case 'wiki': {
          const w = x as unknown as WikiToken;
          return <WikiChip key={i} kind={w.kind} id={w.id} label={w.label} resolve={resolve} />;
        }
        case 'link': {
          const href = safeHref(x.href as string);
          if (!href) return <Fragment key={i}>{inline(x.tokens)}</Fragment>;
          if (href.startsWith('/'))
            return (
              <Link key={i} to={href}>
                {inline(x.tokens)}
              </Link>
            );
          return (
            <a key={i} href={href} target="_blank" rel="noopener noreferrer">
              {inline(x.tokens)}
            </a>
          );
        }
        case 'image': {
          // Images are not shown yet: a link to the image keeps it reachable.
          const href = safeHref(x.href as string);
          const label = (x.text as string) || (x.href as string);
          return href ? (
            <a key={i} href={href} target="_blank" rel="noopener noreferrer">
              {label}
            </a>
          ) : (
            label
          );
        }
        case 'html':
          return x.raw;
        default:
          return x.raw ?? '';
      }
    });

  const block = (tk: Token, i: number): ReactNode => {
    const x = tk as Tokens.Generic;
    switch (x.type) {
      case 'space':
      case 'def':
        return null;
      case 'heading': {
        const h = x as Tokens.Heading & { anchor?: string };
        const id = h.anchor;
        if (h.depth <= 2)
          return (
            <h2 key={i} id={id}>
              {inline(h.tokens)}
            </h2>
          );
        if (h.depth === 3)
          return (
            <h3 key={i} id={id}>
              {inline(h.tokens)}
            </h3>
          );
        return <h4 key={i}>{inline(h.tokens)}</h4>;
      }
      case 'paragraph':
        return <p key={i}>{inline(x.tokens)}</p>;
      case 'text':
        return <p key={i}>{inline(x.tokens ?? [x as Token])}</p>;
      case 'code':
        return <CodeBlock key={i} text={x.text as string} lang={((x.lang as string) || '').trim().split(/\s/)[0]} />;
      case 'hr':
        return <hr key={i} />;
      case 'list': {
        const l = x as Tokens.List;
        const items = l.items.map((it, j) => (
          <li key={j}>
            {it.task && <input type="checkbox" checked={!!it.checked} disabled aria-label={plainText(it.tokens)} />}
            {it.tokens.map((c, k) =>
              c.type === 'text' && !l.loose ? (
                <Fragment key={k}>{inline((c as Tokens.Text).tokens ?? [c])}</Fragment>
              ) : (
                block(c, k)
              ),
            )}
          </li>
        ));
        return l.ordered ? (
          <ol key={i} start={typeof l.start === 'number' && l.start !== 1 ? l.start : undefined}>
            {items}
          </ol>
        ) : (
          <ul key={i}>{items}</ul>
        );
      }
      case 'table': {
        const tb = x as Tokens.Table;
        return (
          <div key={i} className="tbl">
            <table>
              <thead>
                <tr>
                  {tb.header.map((c, j) => (
                    <th key={j} style={c.align ? { textAlign: c.align } : undefined}>
                      {inline(c.tokens)}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {tb.rows.map((r, j) => (
                  <tr key={j}>
                    {r.map((c, k) => (
                      <td key={k} style={c.align ? { textAlign: c.align } : undefined}>
                        {inline(c.tokens)}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        );
      }
      case 'blockquote': {
        const q = x as Tokens.Blockquote;
        const first = q.tokens[0] as Tokens.Paragraph | undefined;
        const head = first?.type === 'paragraph' ? CALLOUT.exec(first.text) : null;
        if (!head || !first) return <blockquote key={i}>{q.tokens.map(block)}</blockquote>;
        const word = head[1].toUpperCase();
        const type: CalloutType = word === 'TIP' ? 'tip' : word === 'NOTE' || word === 'IMPORTANT' ? 'note' : 'warn';
        // Drop the [!TYPE] marker from the first text token, then split the title line from the text.
        const firstTokens = [...first.tokens];
        const lead = firstTokens[0] as Tokens.Text | undefined;
        if (lead?.type === 'text') {
          const rest = lead.text.replace(CALLOUT, '');
          firstTokens[0] = { ...lead, text: rest, raw: rest, tokens: undefined } as Token;
        }
        const { title, body } = splitCallout(firstTokens);
        const titleText = plainText(title).trim();
        const Icon = type === 'warn' ? IconWarn : type === 'tip' ? IconBulb : IconNote;
        return (
          <div key={i} className={'co ' + type} role="note">
            <Icon size={16} />
            <div>
              <b className="ct">
                {titleText ? inline(title) : type === 'warn' ? t.pages.callout.warning : t.pages.callout[type]}
              </b>
              {body.length > 0 && <p>{inline(body)}</p>}
              {q.tokens.slice(1).map((c, k) => block(c, k + 1))}
            </div>
          </div>
        );
      }
      case 'html':
        // Raw HTML is not rendered: it shows as the text that was typed.
        return (
          <p key={i} className="raw-html">
            {x.raw.trim()}
          </p>
        );
      default:
        return x.raw ? <p key={i}>{x.raw}</p> : null;
    }
  };

  return <>{tokens.map(block)}</>;
}

/** A page body rendered for readers. */
export function DocMarkdown({
  doc,
  resolve,
  className = '',
}: {
  doc: ParsedDoc;
  resolve: Resolve;
  className?: string;
}) {
  const { t } = useI18n();
  return (
    <div className={'doc ' + className}>
      {doc.tokens.some((x) => x.type !== 'space') ? (
        <Doc tokens={doc.tokens} resolve={resolve} />
      ) : (
        <p className="muted">{t.pages.emptyBody}</p>
      )}
    </div>
  );
}

export function useParsedDoc(body: string) {
  return useMemo(() => parseDoc(body), [body]);
}
