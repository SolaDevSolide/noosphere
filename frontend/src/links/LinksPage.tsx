import { useEffect, useState, type ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { api } from '../api/client';
import { LINK_CAPABILITIES, type Link, type LinkCode } from '../api/types';
import { relTime, shortDate } from '../agents/bits';
import { useAuth } from '../auth/auth';
import { useDemo } from '../demo/context';
import { useI18n } from '../i18n';
import { PageShell } from '../records/common';
import { IconCheck, IconClock, IconInfo, IconLink, IconLock, IconTerminal, IconWarn } from '../ui/icons';
import { AppTile, CapChips, capText, LinkStateChip, PeerChip, productLine, ProtocolLink, useCountdown } from './bits';

/** Display names by username, for "created by" and "paired by"; usernames until loaded. */
export function usePeopleNames(): (username: string) => string {
  const [names, setNames] = useState<Map<string, string>>(new Map());
  useEffect(() => {
    let alive = true;
    api
      .people()
      .then((ps) => alive && setNames(new Map(ps.map((p) => [p.username, p.displayName]))))
      .catch(() => {});
    return () => {
      alive = false;
    };
  }, []);
  return (u) => names.get(u) ?? u;
}

export function LinksHeader({ action }: { action?: boolean }) {
  const { t } = useI18n();
  return (
    <div className="page-h">
      <div className="ph-t">
        <h1>{t.links.title}</h1>
        <p>{t.links.intro}</p>
      </div>
      <div className="page-actions">
        {action && (
          <RouterLink className="btn primary" to="/admin/links/pair">
            <IconLink />
            {t.links.pair}
          </RouterLink>
        )}
      </div>
    </div>
  );
}

export function AdminOnly({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  const { isAdmin } = useAuth();
  if (isAdmin) return <>{children}</>;
  return (
    <PageShell title={t.links.title}>
      <div className="banner danger" role="alert">
        <IconWarn />
        <span className="bt">{t.common.noAccess}</span>
      </div>
    </PageShell>
  );
}

export function CapabilityList({ caps = LINK_CAPABILITIES as string[] }: { caps?: string[] }) {
  const { t } = useI18n();
  return (
    <ul className="can">
      {caps.map((c) => {
        const [name, text] = capText(t, c);
        return (
          <li key={c} className="ok">
            <IconCheck />
            <span>
              <b>{name}</b>
              <small>{text}</small>
            </span>
          </li>
        );
      })}
      <li className="lock">
        <IconLock size={16} />
        <span>
          <b>{t.links.empty.never}</b>
          <small>{t.links.empty.neverText}</small>
        </span>
      </li>
    </ul>
  );
}

function EmptyLinks() {
  const { t } = useI18n();
  return (
    <>
      <section className="box lk-empty" aria-labelledby="le-h">
        <span className="atile big" aria-hidden="true">
          <IconLink size={22} />
        </span>
        <h2 id="le-h" className="big">
          {t.links.empty.title}
        </h2>
        <p className="cn-p">{t.links.empty.text}</p>
        <div className="page-actions lk-center">
          <RouterLink className="btn primary" to="/admin/links/pair">
            <IconLink />
            {t.links.pair}
          </RouterLink>
        </div>
        <div className="lk-what">
          <h3 className="can-h">{t.links.empty.whatTitle}</h3>
          <CapabilityList />
        </div>
        <div className="lk-dev">
          <IconTerminal />
          <span>
            <b>{t.links.empty.devLead}</b> {t.links.empty.devText} <ProtocolLink />
          </span>
        </div>
      </section>
      <p className="muted2 lk-foot">
        <IconInfo size={14} /> {t.links.fromServer} <span className="mono">noosphere link code --expires 15m</span>
      </p>
    </>
  );
}

function OpenCodeBanner({ codes, nameOf }: { codes: LinkCode[]; nameOf: (u: string) => string }) {
  const { t } = useI18n();
  const first = codes[0];
  const { left } = useCountdown(first.expiresAt);
  const by = first.bootstrap ? t.links.detail.bootstrapCode : nameOf(first.createdBy);
  return (
    <div className="banner warn" role="status">
      <IconClock size={16} />
      <div className="bt">
        <b>{codes.length === 1 ? t.links.list.codeOpen : t.links.list.codesOpen(codes.length)}</b>
        {t.links.list.codeLine(first.codeId, left, by)}{' '}
        <RouterLink to={`/admin/links/pair?code=${encodeURIComponent(first.id)}`}>{t.links.list.seeIt}</RouterLink>.
      </div>
    </div>
  );
}

export function SharedWithEveryLink() {
  const { t } = useI18n();
  const l = t.links.list;
  const rows: [string, string][] = [
    [l.shRecords, l.shRecordsV],
    [l.shNotes, l.shNotesV],
    [l.shObjects, l.shObjectsV],
    [l.shLog, l.shLogV],
    [l.shEvents, l.shEventsV],
    [l.shWho, l.shWhoV],
  ];
  return (
    <section className="box setbox" aria-labelledby="sh-h">
      <div className="set-hd">
        <h2 id="sh-h">{l.sharedTitle}</h2>
        <ProtocolLink />
      </div>
      <dl className="setgrid">
        {rows.map(([k, v]) => (
          <div key={k}>
            <dt>{k}</dt>
            <dd>{v}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}

function ListSkeleton() {
  return (
    <div aria-busy="true">
      <section className="box">
        <span className="sk line" style={{ width: 160 }} />
        <span className="sk line" style={{ width: '50%', marginTop: 10 }} />
        <span className="sk block" style={{ height: 180, marginTop: 16 }} />
      </section>
      <section className="box">
        <span className="sk line" style={{ width: 180 }} />
        <span className="sk block" style={{ height: 90, marginTop: 16 }} />
      </section>
    </div>
  );
}

const isToday = (iso: string, now = new Date()) => new Date(iso).toDateString() === now.toDateString();

function LinkRow({ x }: { x: Link }) {
  const { t, lang } = useI18n();
  const l = t.links.list;
  const to = `/admin/links/${encodeURIComponent(x.id)}`;
  const today = isToday(x.pairedAt);
  const cut = x.state === 'cut';
  return (
    <tr className={cut ? 'cutrow' : ''}>
      <td className="lead">
        <span className="lk-app">
          <AppTile name={x.instanceName} product={x.product} cut={cut} />
          <span>
            <RouterLink className="an2 rowpick" to={to}>
              {x.instanceName}
            </RouterLink>
            <span className="am2">
              {productLine(x)} · {today ? l.pairedToday : l.paired(shortDate(x.pairedAt, lang))}
              {x.demo && <span className="pchip muted lk-demo">{t.links.state.demo}</span>}
            </span>
            <span className="am2 mono">{x.url}</span>
          </span>
        </span>
      </td>
      <td data-l={l.colState}>
        <LinkStateChip link={x} />
      </td>
      <td data-l={l.colTheirs}>
        <PeerChip link={x} />
      </td>
      <td data-l={l.colMay}>{cut ? <span className="none">—</span> : <CapChips caps={x.capabilities} />}</td>
      <td data-l={l.colLast}>
        {x.lastExchangeAt ? (
          cut ? (
            <span className="none">{relTime(t, lang, x.lastExchangeAt)}</span>
          ) : (
            relTime(t, lang, x.lastExchangeAt)
          )
        ) : (
          <span className="none">{l.never}</span>
        )}
      </td>
      <td className="acts-td">
        <span className="acts2">
          <RouterLink className="btn sm" to={to}>
            {l.open}
          </RouterLink>
        </span>
      </td>
    </tr>
  );
}

function LinksList() {
  const { t } = useI18n();
  const { tick } = useDemo();
  const nameOf = usePeopleNames();
  const [links, setLinks] = useState<Link[] | null>(null);
  const [codes, setCodes] = useState<LinkCode[]>([]);
  const [error, setError] = useState(false);
  const [reload, setReload] = useState(0);
  useEffect(() => {
    let alive = true;
    setError(false);
    Promise.all([api.links(), api.linkCodes().catch(() => [] as LinkCode[])])
      .then(([ls, cs]) => {
        if (!alive) return;
        setLinks(ls);
        setCodes(cs.filter((c) => c.state === 'active'));
      })
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, [tick, reload]);

  const l = t.links.list;
  const live = links?.filter((x) => x.state !== 'cut') ?? [];
  const cut = (links?.length ?? 0) - live.length;
  const products = new Set(live.map((x) => x.product)).size;
  const waiting = live.filter((x) => x.peerConfirmed !== true).length;
  return (
    <PageShell wide title={t.links.title}>
      <LinksHeader action={!!links && links.length > 0} />
      {error && (
        <div className="banner danger" role="alert">
          <IconWarn />
          <div className="bt">
            <b>{t.links.loadError}</b>
            <div className="page-actions" style={{ marginTop: 10 }}>
              <button type="button" className="btn" onClick={() => setReload((x) => x + 1)}>
                {t.common.retry}
              </button>
            </div>
          </div>
        </div>
      )}
      {!error && !links && <ListSkeleton />}
      {links && codes.length > 0 && <OpenCodeBanner codes={codes} nameOf={nameOf} />}
      {links && links.length === 0 && <EmptyLinks />}
      {links && links.length > 0 && (
        <>
          <section className="box tb" aria-labelledby="ll-h">
            <div className="tb-h">
              <div>
                <h2 id="ll-h">{l.title}</h2>
                <p>{l.summary(live.length, products, waiting, cut)}</p>
              </div>
            </div>
            <table className="table rtable atable lk-table">
              <thead>
                <tr>
                  <th scope="col">{l.colApp}</th>
                  <th scope="col">{l.colState}</th>
                  <th scope="col">{l.colTheirs}</th>
                  <th scope="col">{l.colMay}</th>
                  <th scope="col">{l.colLast}</th>
                  <th scope="col">
                    <span className="sr">{l.actions}</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {links.map((x) => (
                  <LinkRow key={x.id} x={x} />
                ))}
              </tbody>
            </table>
          </section>
          <SharedWithEveryLink />
        </>
      )}
    </PageShell>
  );
}

/** The links page, for administrators: linked applications, an open pairing code, and what every link shares. */
export function LinksPage() {
  return (
    <AdminOnly>
      <LinksList />
    </AdminOnly>
  );
}
