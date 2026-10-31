import { useEffect, useState } from 'react';
import type { Language, Link, LinkExchange } from '../api/types';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { IconCheck, IconDoc, IconHourglass } from '../ui/icons';

const locale = (lang: Language) => (lang === 'fr' ? 'fr-FR' : 'en-GB');

export const longDate = (iso: string, lang: Language) =>
  new Intl.DateTimeFormat(locale(lang), { day: 'numeric', month: 'short', year: 'numeric' }).format(new Date(iso));

/**
 * Two letters for the tile of a linked application, from the name it announced: the first letters of its first and
 * last words ("MEP Alizé" gives MA, "Alizé CI" gives AC), or the first two letters of a single word.
 */
export function initialsOf(name: string): string {
  const words = name.match(/[\p{L}\p{N}]+/gu) ?? [];
  const first = words[0];
  const last = words[words.length - 1];
  if (!first || !last) return '?';
  if (words.length === 1) return first.slice(0, 2).toUpperCase();
  return (first.charAt(0) + last.charAt(0)).toUpperCase();
}

/** A stable tile colour per product, so that links of one product look alike; never a hardcoded product. */
export function tileVariant(product: string): '' | 'b' {
  let h = 0;
  for (const c of product) h = (h * 31 + c.charCodeAt(0)) >>> 0;
  return h % 2 === 0 ? '' : 'b';
}

export function AppTile({
  name,
  product,
  size,
  cut,
}: {
  name: string;
  product: string;
  size?: 'big2' | 'xs';
  cut?: boolean;
}) {
  return (
    <span
      className={['atile', 'ini', tileVariant(product), size ?? '', cut ? 'c' : ''].filter(Boolean).join(' ')}
      aria-hidden="true"
    >
      {initialsOf(name)}
    </span>
  );
}

/** "gestion-mep 1.11.0", or the product alone when no version was announced. */
export const productLine = (l: Pick<Link, 'product' | 'peerVersion'>) =>
  l.peerVersion ? `${l.product} ${l.peerVersion}` : l.product;

/** The capability's name and description, or the raw value for one this version does not know. */
export function capText(t: Dict, cap: string): [string, string] {
  return t.links.cap[cap] ?? [cap, ''];
}

export function CapChips({ caps }: { caps: string[] }) {
  const { t } = useI18n();
  if (caps.length === 0) return <span className="none">—</span>;
  return (
    <span className="chip-row">
      {caps.map((c) => (
        <span key={c} className="pchip">
          {capText(t, c)[0]}
        </span>
      ))}
    </span>
  );
}

export function LinkStateChip({ link }: { link: Pick<Link, 'state' | 'cutAt'> }) {
  const { t, lang } = useI18n();
  if (link.state === 'cut')
    return <span className="pchip muted">{t.links.state.cut(link.cutAt ? shortDay(link.cutAt, lang) : '—')}</span>;
  if (link.state === 'pending')
    return (
      <span className="pchip warn">
        <IconHourglass size={11} />
        {t.links.state.pending}
      </span>
    );
  return (
    <span className="pchip ok">
      <span className="st-dot" />
      {t.links.state.active}
    </span>
  );
}

/** What the other side said about its own confirmation. */
export function PeerChip({ link, long }: { link: Pick<Link, 'state' | 'peerConfirmed'>; long?: boolean }) {
  const { t } = useI18n();
  if (link.state === 'cut') return <span className="none">—</span>;
  if (link.peerConfirmed === true)
    return (
      <span className="pchip ok">
        <IconCheck size={11} />
        {long ? t.links.state.peerConfirmedLong : t.links.state.peerConfirmed}
      </span>
    );
  if (link.peerConfirmed === false)
    return (
      <span className="pchip warn">
        <IconHourglass size={11} />
        {t.links.state.peerWaiting}
      </span>
    );
  return <span className="pchip muted">{t.links.state.peerUnknown}</span>;
}

export const shortDay = (iso: string, lang: Language) =>
  new Intl.DateTimeFormat(locale(lang), { day: 'numeric', month: 'short' }).format(new Date(iso));

/** The four words of a fingerprint, as separate boxes. */
export function Fingerprint({ value, big }: { value: string; big?: boolean }) {
  const { t } = useI18n();
  const words = value.trim().split(/\s+/).filter(Boolean);
  return (
    <span
      className={'fp' + (big ? ' big' : '')}
      role="img"
      aria-label={`${t.links.detail.fingerprint}: ${words.join(' ')}`}
    >
      {words.map((w, i) => (
        <span key={i}>{w}</span>
      ))}
    </span>
  );
}

export function ProtocolLink({ withPath = true }: { withPath?: boolean }) {
  const { t } = useI18n();
  return (
    <a className="lk-doc" href={t.links.protocolUrl} target="_blank" rel="noopener noreferrer">
      <IconDoc size={14} /> {t.links.protocol}
      {withPath && <span className="mono">{t.links.protocolPath}</span>}
    </a>
  );
}

/** mm:ss left until `iso`, ticking every second; "0:00" once past. */
export function useCountdown(iso: string | null | undefined): { left: string; over: boolean } {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!iso) return;
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [iso]);
  if (!iso) return { left: '—', over: false };
  const ms = Math.max(0, new Date(iso).getTime() - now);
  const s = Math.floor(ms / 1000);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const ss = String(s % 60).padStart(2, '0');
  return { left: h ? `${h}:${String(m).padStart(2, '0')}:${ss}` : `${m}:${ss}`, over: ms === 0 };
}

/* ---------- exchange log ---------- */

export type ExchangeKind = 'all' | 'catalog' | 'notices' | 'events' | 'refused' | 'keys';
export const EXCHANGE_KINDS: ExchangeKind[] = ['all', 'catalog', 'notices', 'events', 'refused', 'keys'];

export function exchangeMatches(x: LinkExchange, kind: ExchangeKind): boolean {
  switch (kind) {
    case 'all':
      return true;
    case 'catalog':
      return x.operation === 'catalog';
    case 'notices':
      return x.operation === 'notices';
    case 'events':
      return x.operation === 'deployments';
    case 'refused':
      return x.result === 'refused';
    case 'keys':
      return x.operation === 'pair' || x.operation === 'rekey' || x.operation === 'unpair';
  }
}

const METHOD: Record<string, string> = {
  pair: 'POST',
  status: 'GET',
  catalog: 'GET',
  notices: 'POST',
  deployments: 'POST',
  rekey: 'POST',
  unpair: 'POST',
};

/** The technical line under an exchange: method, path, status, reason and the server's detail. */
export function exchangeTech(x: LinkExchange): string {
  const parts = [`${METHOD[x.operation] ?? 'POST'} /link/v1/${x.operation}`];
  if (x.status != null) parts.push(String(x.status) + (x.reason ? ` ${x.reason}` : ''));
  else if (x.reason) parts.push(x.reason);
  if (x.detail) parts.push(x.detail);
  return parts.join(' · ');
}

type Sentence = { who: string | null; text: string; reason?: string };

/** The exchange in plain words, from its direction, operation and result; never from a product name. */
export function exchangeSentence(t: Dict, x: LinkExchange, peer: string): Sentence {
  const e = t.links.ex;
  const op = e.op[x.operation] ?? e.op.other;
  if (x.result === 'refused') {
    const reason = x.reason ? (e.reason[x.reason] ?? x.reason) : '—';
    return x.direction === 'in'
      ? { who: null, text: e.refusedIn, reason }
      : { who: null, text: e.refusedOut(peer), reason };
  }
  if (x.result === 'failed') return { who: e.who.noosphere, text: e.failed(op) };
  if (x.result === 'demo') return { who: null, text: e.demo(op) };
  if (x.direction === 'in') {
    const i = e.in;
    const text =
      x.operation === 'catalog' && x.status === 304
        ? i.catalogSame
        : x.operation in i && x.operation !== 'other'
          ? i[x.operation as keyof typeof i]
          : i.other;
    return { who: peer, text };
  }
  const o = e.out;
  let text: string;
  switch (x.operation) {
    case 'status':
      text = o.status(peer);
      break;
    case 'catalog':
      text = o.catalog(peer);
      break;
    case 'unpair':
      text = o.unpair(peer);
      break;
    case 'pair':
    case 'notices':
    case 'deployments':
    case 'rekey':
      text = o[x.operation];
      break;
    default:
      text = o.other;
  }
  return { who: e.who.noosphere, text };
}

export function exchangeChip(t: Dict, x: LinkExchange): { cls: string; label: string; k: string } {
  const c = t.links.ex.chip;
  if (x.result === 'refused') return { cls: 'danger', label: c.refused, k: 'refused' };
  if (x.result === 'failed') return { cls: 'warn', label: c.failed, k: 'quota' };
  if (x.result === 'demo') return { cls: 'muted', label: c.demo, k: 'revoked' };
  if (x.operation === 'rekey') return { cls: 'muted', label: c.keys, k: 'revoked' };
  if (x.operation === 'unpair') return { cls: 'muted', label: c.cut, k: 'revoked' };
  if (x.operation === 'deployments') return { cls: 'ok', label: c.accepted, k: 'draft' };
  if (x.operation === 'notices' && x.direction === 'out') return { cls: 'ok', label: c.delivered, k: 'read' };
  return { cls: 'ok', label: c.done, k: 'read' };
}
