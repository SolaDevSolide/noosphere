import { useCallback, useEffect, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Link as RouterLink, useParams } from 'react-router-dom';
import { useGo } from '../ui/useGo';
import { api, ApiError } from '../api/client';
import { LINK_CAPABILITIES, type Link, type LinkCapability, type LinkExchange } from '../api/types';
import { ActivitySkeleton, clock, logTime } from '../agents/bits';
import { useDemo } from '../demo/context';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { PageShell } from '../records/common';
import { IconBack, IconCheck, IconClock, IconInfo, IconKey, IconLock, IconScissors, IconWarn } from '../ui/icons';
import { useToast } from '../ui/Toast';
import {
  AppTile,
  CapChips,
  capText,
  EXCHANGE_KINDS,
  exchangeChip,
  exchangeMatches,
  exchangeSentence,
  exchangeTech,
  Fingerprint,
  LinkStateChip,
  longDate,
  PeerChip,
  productLine,
  type ExchangeKind,
} from './bits';
import { AdminOnly, LinksHeader, usePeopleNames } from './LinksPage';

const PAGE = 50;

function Switch({
  label,
  checked,
  disabled,
  onChange,
}: {
  label: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (v: boolean) => void;
}) {
  const { t } = useI18n();
  const flip = () => !disabled && onChange(!checked);
  const key = (e: KeyboardEvent) => {
    if (e.key === ' ' || e.key === 'Enter') {
      e.preventDefault();
      flip();
    }
  };
  return (
    <span
      className="switch"
      role="switch"
      aria-checked={checked}
      aria-disabled={disabled || undefined}
      aria-label={label}
      tabIndex={disabled ? -1 : 0}
      onClick={flip}
      onKeyDown={key}
    >
      <span className="tr" />
      <span className="sl">{checked ? t.agents.console.on : t.agents.console.off}</span>
    </span>
  );
}

/** Pages of the exchange log, newest first, walked with Older and Newer. */
function useExchanges(id: string, key: string) {
  const [cursors, setCursors] = useState<(string | null)[]>([null]);
  const [items, setItems] = useState<LinkExchange[] | null>(null);
  const [next, setNext] = useState<string | null>(null);
  const [error, setError] = useState(false);
  const cursor = cursors[cursors.length - 1];
  useEffect(() => setCursors([null]), [id, key]);
  useEffect(() => {
    let alive = true;
    setItems(null);
    setError(false);
    api
      .linkExchanges(id, cursor, PAGE)
      .then((p) => {
        if (!alive) return;
        setItems(p.items);
        setNext(p.nextCursor ?? null);
      })
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, [id, cursor, key]);
  return {
    items,
    error,
    hasOlder: !!next,
    hasNewer: cursors.length > 1,
    older: () => next && setCursors((c) => [...c, next]),
    newer: () => setCursors((c) => (c.length > 1 ? c.slice(0, -1) : c)),
  };
}

function ExchangeLine({ x, peer }: { x: LinkExchange; peer: string }) {
  const { t, lang } = useI18n();
  const s = exchangeSentence(t, x, peer);
  const chip = exchangeChip(t, x);
  return (
    <li data-k={chip.k}>
      <span className="tm" title={new Date(x.at).toLocaleString(lang === 'fr' ? 'fr-FR' : 'en-GB')}>
        {logTime(t, lang, x.at)}
      </span>
      <span className="dot2" aria-hidden="true" />
      <span>
        {s.who && <span className="who2">{s.who}</span>} {s.text}
        {s.reason && (
          <>
            {' '}
            <b>{s.reason}</b>.
          </>
        )}
        <span className="rs mono">{exchangeTech(x)}</span>
      </span>
      <span className={'pchip ' + chip.cls}>{chip.label}</span>
    </li>
  );
}

function ExchangesBox({ link, reloadKey }: { link: Link; reloadKey: string }) {
  const { t } = useI18n();
  const d = t.links.detail;
  const [kind, setKind] = useState<ExchangeKind>('all');
  const ex = useExchanges(link.id, reloadKey);
  const shown = ex.items?.filter((x) => exchangeMatches(x, kind)) ?? null;
  const refused = ex.items?.filter((x) => x.result === 'refused').length ?? 0;
  return (
    <section className="box tb" aria-labelledby="ex-h">
      <div className="tb-h">
        <div>
          <h2 id="ex-h">{d.exchanges}</h2>
          <p>{d.exchangesHint}</p>
        </div>
        <span className="ret">
          <IconClock size={14} />
          {d.kept}
        </span>
      </div>
      <div className="afilt">
        <div className="seg sm wrap2" role="group" aria-label={d.kindGroup}>
          {EXCHANGE_KINDS.map((k) => (
            <button key={k} type="button" aria-pressed={kind === k} onClick={() => setKind(k)}>
              {d.kinds[k]}
              {k === 'refused' && refused > 0 && <span className="cnt">{refused}</span>}
            </button>
          ))}
        </div>
      </div>
      {ex.error ? (
        <p className="log-empty">{t.common.genericError}</p>
      ) : !shown ? (
        <ActivitySkeleton n={6} />
      ) : shown.length === 0 ? (
        <p className="log-empty">{ex.items?.length ? d.noLinesKind : d.noLines}</p>
      ) : (
        <ul className="alog">
          {shown.map((x) => (
            <ExchangeLine key={x.id} x={x} peer={link.instanceName} />
          ))}
        </ul>
      )}
      <div className="tb-f">
        <span>{shown ? d.lines(shown.length) : ' '}</span>
        <span className="pager">
          <button type="button" className="btn sm" disabled={!ex.hasNewer} onClick={ex.newer}>
            {d.newer}
          </button>
          <button type="button" className="btn sm" disabled={!ex.hasOlder} onClick={() => ex.older()}>
            {d.older}
          </button>
        </span>
      </div>
    </section>
  );
}

function SharedBox({ link }: { link: Link }) {
  const { t } = useI18n();
  const { model } = useLandscape();
  const d = t.links.detail;
  if (!link.capabilities.includes('catalog:read') || link.state === 'cut')
    return (
      <section className="box" aria-labelledby="sc-h">
        <h2 id="sc-h">{d.sharedTitle}</h2>
        <ul className="can">
          <li className="lock">
            <IconLock size={16} />
            <span>{d.noCatalog}</span>
          </li>
        </ul>
      </section>
    );
  const open = (x: { restricted?: boolean; visibility?: string | null }) =>
    !x.restricted && x.visibility !== 'restricted';
  const apps = model ? [...model.apps.values()].filter(open) : [];
  const appIds = new Set(apps.map((a) => a.id));
  const flows = model ? model.flows.filter((f) => open(f) && appIds.has(f.fromId) && appIds.has(f.toId)) : [];
  const restricted = model
    ? [...model.apps.values()].filter((a) => !open(a)).length + model.flows.filter((f) => !open(f)).length
    : 0;
  return (
    <section className="box" aria-labelledby="sc-h">
      <h2 id="sc-h">{d.sharedTitle}</h2>
      {!model ? (
        <div className="can-sk" aria-busy="true">
          <span className="sk line" style={{ width: '80%' }} />
          <span className="sk line" style={{ width: '60%' }} />
        </div>
      ) : (
        <ul className="can">
          <li className="ok">
            <IconCheck />
            <span>
              {d.sharedRecords(apps.length, flows.length, model.domains.length, model.teams.size)}
              <small>{d.sharedHint}</small>
            </span>
          </li>
          <li className="lock">
            <IconLock size={16} />
            <span>{d.notShared(restricted)}</span>
          </li>
        </ul>
      )}
    </section>
  );
}

function Header({ link, actions }: { link: Link; actions?: ReactNode }) {
  const { t } = useI18n();
  return (
    <>
      <RouterLink className="backlink" to="/admin/links">
        <IconBack size={14} /> {t.links.back}
      </RouterLink>
      <div className="page-h">
        <div className="ph-t lk-ph">
          <AppTile name={link.instanceName} product={link.product} size="big2" cut={link.state === 'cut'} />
          <span>
            <h1>{link.instanceName}</h1>
            <p>
              {productLine(link)} · <span className="mono">{link.url}</span>
            </p>
            <div className="chip-row" style={{ marginTop: 8 }}>
              <LinkStateChip link={link} />
              {link.state !== 'cut' && <PeerChip link={link} long />}
              {link.demo && <span className="pchip muted">{t.links.state.demo}</span>}
            </div>
          </span>
        </div>
        {actions && <div className="page-actions">{actions}</div>}
      </div>
    </>
  );
}

function ConfirmView({ link, onChanged }: { link: Link; onChanged: (l: Link) => void }) {
  const { t, lang } = useI18n();
  const toast = useToast();
  const c = t.links.confirm;
  const [busy, setBusy] = useState(false);
  const run = async (call: () => Promise<Link>, msg: string) => {
    setBusy(true);
    try {
      const l = await call();
      toast(msg);
      onChanged(l);
    } catch (e) {
      toast(e instanceof ApiError && e.detail ? e.detail : t.common.genericError);
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      <LinksHeader />
      <div className="split2">
        <div>
          <section className="box" aria-labelledby="pf-h">
            <div className="set-hd">
              <h2 id="pf-h" className="big">
                {c.title}
              </h2>
              <span className="muted2">{t.links.code.stepOf(3)}</span>
            </div>
            <span className="lbl2">{c.usedBy}</span>
            <div className="idcard lk-peer">
              <AppTile name={link.instanceName} product={link.product} />
              <span>
                <b>{link.instanceName}</b>
                <small>
                  {productLine(link)} · <span className="mono">{link.url}</span> · {clock(link.pairedAt, lang)}
                </small>
              </span>
            </div>
            <p className="muted2" style={{ margin: '6px 0 0' }}>
              {c.announced}
            </p>
            <p className="cn-p" style={{ marginTop: 16 }}>
              {c.read}
            </p>
            <div className="fp-w">
              <Fingerprint value={link.fingerprint} big />
            </div>
            <div className="banner warn" role="note" style={{ marginTop: 16 }}>
              <IconWarn />
              <div className="bt">
                <b>{c.warnLead}</b>
                {c.warnText}
              </div>
            </div>
            <div className="set-f">
              <button
                type="button"
                className="btn danger"
                disabled={busy}
                onClick={() => void run(() => api.cutLink(link.id), c.differCut(link.instanceName))}
              >
                {c.differ}
              </button>
              <button
                type="button"
                className="btn primary"
                disabled={busy}
                onClick={() =>
                  void run(() => api.confirmLink(link.id, link.fingerprint), c.confirmed(link.instanceName))
                }
              >
                <IconCheck />
                {c.match}
              </button>
            </div>
          </section>
        </div>
        <div>
          <section className="box" aria-labelledby="ps-h">
            <h2 id="ps-h">{c.stateTitle}</h2>
            <dl className="setgrid one">
              <div>
                <dt>{c.here}</dt>
                <dd>
                  <LinkStateChip link={link} />
                </dd>
              </div>
              <div>
                <dt>{c.inPeer(link.instanceName)}</dt>
                <dd>
                  {link.peerConfirmed ? <PeerChip link={link} /> : <span className="pchip warn">{c.notYet}</span>}
                </dd>
              </div>
              <div>
                <dt>{c.exchanges}</dt>
                <dd>{c.exchangesNone}</dd>
              </div>
              <div>
                <dt>{c.may}</dt>
                <dd>
                  <CapChips caps={link.capabilities} />
                </dd>
              </div>
            </dl>
          </section>
        </div>
      </div>
    </>
  );
}

function DetailView({ link, onChanged }: { link: Link; onChanged: (l: Link) => void }) {
  const { t, lang } = useI18n();
  const toast = useToast();
  const nameOf = usePeopleNames();
  const d = t.links.detail;
  const [cutting, setCutting] = useState(false);
  const [busy, setBusy] = useState<null | 'renew' | 'cut' | 'caps'>(null);
  const [exKey, setExKey] = useState(0);
  const cut = link.state === 'cut';
  const fail = (e: unknown) => toast(e instanceof ApiError && e.detail ? e.detail : t.common.genericError);

  const renew = async () => {
    setBusy('renew');
    try {
      onChanged(await api.renewLinkKeys(link.id));
      toast(d.renewed);
    } catch (e) {
      fail(e);
    } finally {
      setBusy(null);
      setExKey((k) => k + 1);
    }
  };
  const doCut = async () => {
    setBusy('cut');
    try {
      onChanged(await api.cutLink(link.id));
      setCutting(false);
      toast(d.cutDone(link.instanceName));
    } catch (e) {
      fail(e);
    } finally {
      setBusy(null);
      setExKey((k) => k + 1);
    }
  };
  const setCap = async (cap: LinkCapability, on: boolean) => {
    const next = LINK_CAPABILITIES.filter((c) => (c === cap ? on : link.capabilities.includes(c)));
    if (next.length === 0) {
      toast(d.lastOne);
      return;
    }
    setBusy('caps');
    try {
      onChanged(await api.setLinkCapabilities(link.id, next));
      toast(d.saved);
    } catch (e) {
      fail(e);
    } finally {
      setBusy(null);
    }
  };
  const who = (u: string) => (u === 'bootstrap' ? d.bootstrapCode : nameOf(u));

  return (
    <>
      <Header
        link={link}
        actions={
          !cut && (
            <>
              <button type="button" className="btn" disabled={busy !== null} onClick={() => void renew()}>
                <IconKey />
                {busy === 'renew' ? d.renewing : d.renew}
              </button>
              <button
                type="button"
                className="btn danger"
                aria-expanded={cutting}
                disabled={busy !== null}
                onClick={() => setCutting(true)}
              >
                <IconScissors />
                {d.cut}
              </button>
            </>
          )
        }
      />
      {cutting && !cut && (
        <div className="inl-confirm lk-cut" role="alert">
          <span>
            <b>{d.cutQ(link.instanceName)}</b> {d.cutText(link.instanceName)}
          </span>
          <button type="button" className="btn sm danger-solid" disabled={busy === 'cut'} onClick={() => void doCut()}>
            {busy === 'cut' ? t.common.saving : d.cut}
          </button>
          <button type="button" className="btn sm" onClick={() => setCutting(false)}>
            {d.cancel}
          </button>
        </div>
      )}
      {cut && (
        <div className="banner" role="note">
          <IconInfo />
          <span className="bt">
            {d.cutBanner(
              link.cutAt ? longDate(link.cutAt, lang) : '—',
              link.cutBy === 'peer' || !link.cutBy ? d.cutByPeer : who(link.cutBy),
            )}
          </span>
        </div>
      )}
      {link.demo && !cut && (
        <div className="banner" role="note">
          <IconInfo />
          <span className="bt">{d.demoNote}</span>
        </div>
      )}
      <div className="cgrid lk-grid">
        <div>
          <ExchangesBox link={link} reloadKey={`${exKey}-${link.version}`} />
        </div>
        <div>
          <section className="box" aria-labelledby="an-h">
            <h2 id="an-h">{d.announcedTitle}</h2>
            <dl className="setgrid one lk-dl">
              <div>
                <dt>{d.product}</dt>
                <dd>{link.product}</dd>
              </div>
              <div>
                <dt>{d.instance}</dt>
                <dd>{link.instanceName}</dd>
              </div>
              <div>
                <dt>{d.version}</dt>
                <dd>{link.peerVersion ?? <span className="none">{d.unknown}</span>}</dd>
              </div>
              <div>
                <dt>{d.address}</dt>
                <dd>
                  <span className="mono">{link.url}</span>
                </dd>
              </div>
              <div>
                <dt>{d.paired}</dt>
                <dd>{d.pairedBy(longDate(link.pairedAt, lang), who(link.pairedBy))}</dd>
              </div>
              {link.confirmedAt && link.confirmedBy && (
                <div>
                  <dt>{d.confirmedHere}</dt>
                  <dd>{d.confirmedBy(longDate(link.confirmedAt, lang), who(link.confirmedBy))}</dd>
                </div>
              )}
            </dl>
          </section>
          {!cut && (
            <section className="box" aria-labelledby="fpk-h">
              <h2 id="fpk-h">{d.fingerprint}</h2>
              <Fingerprint value={link.fingerprint} />
              <p className="muted2" style={{ marginTop: 10 }}>
                <IconKey size={14} />
                {link.keysRenewedAt ? d.renewedOn(longDate(link.keysRenewedAt, lang)) : d.sameSince}
              </p>
            </section>
          )}
          <section className="box" aria-labelledby="cp-h">
            <div className="set-hd">
              <h2 id="cp-h">{d.mayTitle}</h2>
            </div>
            {LINK_CAPABILITIES.map((cap) => {
              const [name, text] = capText(t, cap);
              return (
                <div key={cap} className="swrow">
                  <b>{name}</b>
                  <small>
                    {text} <span className="mono">{cap}</span>
                  </small>
                  <Switch
                    label={name}
                    checked={!cut && link.capabilities.includes(cap)}
                    disabled={cut || busy !== null}
                    onChange={(v) => void setCap(cap, v)}
                  />
                </div>
              );
            })}
            <p className="muted2" style={{ marginTop: 6 }}>
              {d.mayHint}
            </p>
          </section>
          <SharedBox link={link} />
        </div>
      </div>
    </>
  );
}

function DetailSkeleton() {
  return (
    <div aria-busy="true">
      <span className="sk line" style={{ width: 60 }} />
      <span className="sk title" style={{ width: '40%', marginTop: 12 }} />
      <span className="sk line" style={{ width: '30%', marginTop: 8 }} />
      <div className="cgrid" style={{ marginTop: 20 }}>
        <div>
          <section className="box">
            <span className="sk line" style={{ width: 100 }} />
            <span className="sk block" style={{ height: 320, marginTop: 16 }} />
          </section>
        </div>
        <div>
          <section className="box">
            <span className="sk line" style={{ width: 140 }} />
            <span className="sk block" style={{ height: 160, marginTop: 16 }} />
          </section>
        </div>
      </div>
    </div>
  );
}

function LinkDetail() {
  const { t } = useI18n();
  const { id = '' } = useParams();
  const { tick } = useDemo();
  const nav = useGo();
  const [link, setLink] = useState<Link | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const load = useCallback(() => {
    let alive = true;
    setError(null);
    api
      .link(id)
      .then((l) => alive && setLink(l))
      .catch((e: unknown) => alive && setError(e instanceof ApiError ? e : new ApiError(0, 'network')));
    return () => {
      alive = false;
    };
  }, [id]);
  useEffect(() => load(), [load, tick]);

  // A cut from the fingerprint step goes back to the list: there is nothing left to look at.
  const changed = (l: Link) => {
    if (link?.state === 'pending' && l.state === 'cut') nav('/admin/links');
    else setLink(l);
  };

  return (
    <PageShell wide title={link?.instanceName ?? t.links.title}>
      {error ? (
        <>
          <LinksHeader />
          <div className="banner danger" role="alert">
            <IconWarn />
            <div className="bt">
              <b>{error.status === 404 ? t.links.detail.notFound : t.links.loadError}</b>
              <div className="page-actions" style={{ marginTop: 10 }}>
                <RouterLink className="btn" to="/admin/links">
                  {t.links.back}
                </RouterLink>
                {error.status !== 404 && (
                  <button type="button" className="btn" onClick={load}>
                    {t.common.retry}
                  </button>
                )}
              </div>
            </div>
          </div>
        </>
      ) : !link ? (
        <DetailSkeleton />
      ) : link.state === 'pending' && !link.confirmedHere ? (
        <ConfirmView link={link} onChanged={changed} />
      ) : (
        <DetailView link={link} onChanged={changed} />
      )}
    </PageShell>
  );
}

/** One link: the fingerprint to confirm while it is pending, then its exchanges, keys, capabilities and cut. */
export function LinkPage() {
  return (
    <AdminOnly>
      <LinkDetail />
    </AdminOnly>
  );
}
