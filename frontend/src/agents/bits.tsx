import { Link } from 'react-router-dom';
import { KIND_PATH } from '../api/client';
import type { AgentActivity, AgentConnection, Capability, Language, LocalizedText, RecordKind } from '../api/types';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { IconCheck, IconGauge, IconHourglass, IconInfo, IconLock, IconNo } from '../ui/icons';

const locale = (lang: Language) => (lang === 'fr' ? 'fr-FR' : 'en-GB');
const sameDay = (a: Date, b: Date) =>
  a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();

export const clock = (iso: string, lang: Language) =>
  new Intl.DateTimeFormat(locale(lang), { hour: '2-digit', minute: '2-digit' }).format(new Date(iso));
export const shortDate = (iso: string, lang: Language) =>
  new Intl.DateTimeFormat(locale(lang), { day: 'numeric', month: 'short' }).format(new Date(iso));

/** "just now", "12 min ago", "Today 10:02", "Yesterday 17:12" or "28 Oct". */
export function relTime(t: Dict, lang: Language, iso: string, now = new Date()): string {
  const d = new Date(iso);
  const min = Math.floor((now.getTime() - d.getTime()) / 60000);
  if (min < 1) return t.agents.justNow;
  if (min < 60) return t.agents.minutesAgo(min);
  if (sameDay(d, now)) return t.agents.today(clock(iso, lang));
  const y = new Date(now);
  y.setDate(y.getDate() - 1);
  if (sameDay(d, y)) return t.agents.yesterday(clock(iso, lang));
  return shortDate(iso, lang);
}

/** The time column of an activity line: "10:42" today, then "Yesterday", then "28 Oct". */
export function logTime(t: Dict, lang: Language, iso: string, now = new Date()): string {
  const d = new Date(iso);
  if (sameDay(d, now)) return clock(iso, lang);
  const y = new Date(now);
  y.setDate(y.getDate() - 1);
  if (sameDay(d, y)) return t.agents.yesterday('').trim();
  return shortDate(iso, lang);
}

export const localized = (x: LocalizedText, lang: Language) => x[lang] || x.en;

/** Where an agent comes from: the host of its site or metadata document, or the demo. */
export function clientHost(t: Dict, c: Pick<AgentConnection, 'clientUri' | 'clientId' | 'demo'>): string {
  if (c.demo) return t.agents.demoClient;
  for (const u of [c.clientUri, c.clientId]) {
    if (!u || !/^https?:\/\//.test(u)) continue;
    try {
      return new URL(u).host;
    } catch {
      /* not a URL */
    }
  }
  return t.agents.registeredClient;
}

export function StateChip({ c }: { c: Pick<AgentConnection, 'state' | 'pausedUntil'> }) {
  const { t, lang } = useI18n();
  if (c.state === 'pending')
    return (
      <span className="pchip warn">
        <IconHourglass size={11} />
        {t.agents.state.pending}
      </span>
    );
  if (c.state === 'paused')
    return (
      <span className="pchip warn">
        <IconGauge size={11} />
        {t.agents.state.paused(c.pausedUntil ? clock(c.pausedUntil, lang) : '—')}
      </span>
    );
  if (c.state === 'revoked') return <span className="pchip muted">{t.agents.state.revoked}</span>;
  return (
    <span className="pchip ok">
      <span className="st-dot" />
      {t.agents.state.active}
    </span>
  );
}

/** Scope names in plain words, for chips and summaries. */
export function scopeNames(t: Dict, scopes: string[]): string[] {
  const out: string[] = [];
  if (scopes.includes('docs:read')) out.push(t.agents.scope.read);
  if (scopes.includes('docs:draft')) out.push(t.agents.scope.draft);
  if (scopes.includes('docs:read:restricted')) out.push(t.agents.scope.restricted);
  return out;
}

export function ScopeChips({ scopes }: { scopes: string[] }) {
  const { t } = useI18n();
  return (
    <span className="chip-row">
      {scopes.includes('docs:read') && <span className="pchip">{t.agents.scope.read}</span>}
      {scopes.includes('docs:draft') && <span className="pchip agent">{t.agents.scope.draft}</span>}
      {scopes.includes('docs:read:restricted') && <span className="pchip warn">{t.agents.scope.restricted}</span>}
    </span>
  );
}

/** The look of a capability line: allowed (check), held back by the ceiling (lock), never (no), or a limit (info). */
export function capClass(c: Pick<Capability, 'key' | 'allowed'>): 'ok' | 'lock' | 'no' | '' {
  if (c.key === 'quota') return '';
  if (c.allowed) return 'ok';
  return c.key === 'restricted' || c.key === 'protectedNotes' ? 'lock' : 'no';
}

export function CapIcon({ cls }: { cls: ReturnType<typeof capClass> }) {
  if (cls === 'ok') return <IconCheck />;
  if (cls === 'lock') return <IconLock size={16} />;
  if (cls === 'no') return <IconNo />;
  return <IconInfo />;
}

export function CapList({ items, extra }: { items: Capability[]; extra?: Partial<Record<string, string>> }) {
  const { lang } = useI18n();
  return (
    <ul className="can">
      {items.map((c) => {
        const cls = capClass(c);
        return (
          <li key={c.key} className={cls}>
            <CapIcon cls={cls} />
            <span>
              {localized(c.text, lang)}
              {extra?.[c.key] && <small>{extra[c.key]}</small>}
            </span>
          </li>
        );
      })}
    </ul>
  );
}

const recordPath = (kind: RecordKind, id: string) =>
  kind === 'dataObject' ? paths.lineage(id) : `/${KIND_PATH[kind]}/${encodeURIComponent(id)}`;

/** One line of agent activity; the summary comes from the server, the record it touched is a link. */
export function ActivityLine({ a, showPerson }: { a: AgentActivity; showPerson?: boolean }) {
  const { t, lang } = useI18n();
  const chip =
    a.kind === 'refused'
      ? 'danger'
      : a.kind === 'quota'
        ? 'warn'
        : a.kind === 'draft' || a.kind === 'proposal'
          ? 'agent'
          : a.kind === 'revoked'
            ? 'muted'
            : '';
  const link = a.recordKind && a.recordId && a.recordTitle ? recordPath(a.recordKind, a.recordId) : null;
  return (
    <li data-k={a.kind}>
      <span className="tm" title={new Date(a.at).toLocaleString(locale(lang))}>
        {logTime(t, lang, a.at)}
      </span>
      <span className="dot2" aria-hidden="true" />
      <span>
        <span className="who2">
          {a.agentName}
          {showPerson && <span className="for"> {t.agents.forPerson(a.person.displayName)}</span>}
        </span>{' '}
        {a.summary}
        {link && (
          <span className="rs">
            <Link to={link}>{a.recordTitle}</Link>
          </span>
        )}
      </span>
      <span className={'pchip ' + chip}>{t.agents.kind[a.kind]}</span>
    </li>
  );
}

export function ActivitySkeleton({ n = 4 }: { n?: number }) {
  return (
    <ul className="alog" aria-hidden="true">
      {Array.from({ length: n }, (_, i) => (
        <li key={i}>
          <span className="sk line" style={{ width: 40 }} />
          <span />
          <span className="sk line" style={{ width: `${55 + ((i * 13) % 35)}%` }} />
          <span className="sk line" style={{ width: 52 }} />
        </li>
      ))}
    </ul>
  );
}

/** Copies a text; resolves false when the browser refuses. */
export async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    try {
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.setAttribute('readonly', '');
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      const ok = document.execCommand('copy');
      ta.remove();
      return ok;
    } catch {
      return false;
    }
  }
}

export const initials = (name: string) =>
  name
    .split(/\s+/)
    .map((w) => w[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();
