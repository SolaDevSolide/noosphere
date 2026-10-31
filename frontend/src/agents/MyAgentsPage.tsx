import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type { AgentActivity, AgentCapabilities, AgentConnection, MyAgents, Proposal } from '../api/types';
import { useSettings } from '../app/settings';
import { useAuth } from '../auth/auth';
import { useDemo } from '../demo/context';
import { paths } from '../explorer/paths';
import { useI18n } from '../i18n';
import { PageShell } from '../records/common';
import { useInbox } from '../review/inbox';
import { IconBotFull, IconCopy, IconEdit, IconHourglass, IconPlug, IconSend, IconShield, IconWarn } from '../ui/icons';
import { useToast } from '../ui/Toast';
import {
  ActivityLine,
  ActivitySkeleton,
  CapList,
  clientHost,
  copyText,
  relTime,
  shortDate,
  ScopeChips,
  StateChip,
} from './bits';

export type GuideId = 'json' | 'cli' | 'vsc';

/** The configuration snippets of the "Connect an agent" guides, for one MCP address. */
export function guideText(id: GuideId, url: string): string {
  if (id === 'cli') return `claude mcp add --transport http noosphere ${url}`;
  const key = id === 'json' ? 'mcpServers' : 'servers';
  return JSON.stringify({ [key]: { noosphere: { type: 'http', url } } }, null, 2);
}

/** Copy button with a toast. */
export function CopyButton({ text, label, className }: { text: string; label: string; className?: string }) {
  const { t } = useI18n();
  const toast = useToast();
  return (
    <button
      type="button"
      className={'iconbtn' + (className ? ' ' + className : '')}
      title={label}
      aria-label={label}
      onClick={() => void copyText(text).then((ok) => toast(ok ? t.agents.copied : t.agents.copyFailed))}
    >
      <IconCopy size={15} />
    </button>
  );
}

function ConnectBox({ data }: { data: MyAgents }) {
  const { t } = useI18n();
  const { settings } = useSettings();
  const [guide, setGuide] = useState<GuideId>('json');
  const url = data.settings.mcpUrl;
  const org = settings.organisationName.trim() || settings.instanceName.trim() || t.brand.name;
  const g = guideText(guide, url);
  return (
    <section className="box" id="connect-box" aria-labelledby="cn-h">
      <h2 className="big" id="cn-h">
        {t.agents.mine.connectTitle}
      </h2>
      <p className="cn-p">{t.agents.mine.connectText}</p>
      <label className="lbl2" htmlFor="mcp-url">
        {t.agents.mine.address}
      </label>
      <div className="addr">
        <input className="input" id="mcp-url" readOnly value={url} onFocus={(e) => e.target.select()} />
        <CopyButton text={url} label={t.agents.mine.copyAddress} />
      </div>
      <ol className="steps">
        <li>
          <span>
            <b>{t.agents.mine.stepAdd}</b>
            {t.agents.mine.stepAddText}
          </span>
        </li>
        <li>
          <span>
            {t.agents.mine.stepOpens}
            <b>{t.agents.mine.stepCheck}</b>.
          </span>
        </li>
        <li>
          <span>
            {t.agents.mine.stepShows}
            <b>{t.agents.mine.stepRevoke}</b>
            {t.agents.mine.stepRevokeText}
          </span>
        </li>
      </ol>
      <div className="seg sm gtabs" role="group" aria-label={t.agents.mine.guides}>
        {(['json', 'cli', 'vsc'] as const).map((x) => (
          <button key={x} type="button" aria-pressed={guide === x} onClick={() => setGuide(x)}>
            {x === 'json' ? t.agents.mine.guideJson : x === 'cli' ? t.agents.mine.guideCli : t.agents.mine.guideVsc}
          </button>
        ))}
      </div>
      <p className="gsub">
        {guide === 'json' ? (
          t.agents.mine.guideJsonText
        ) : guide === 'cli' ? (
          t.agents.mine.guideCliText
        ) : (
          <>
            <span className="mono">.vscode/mcp.json</span>, {t.agents.mine.guideVscText}
          </>
        )}
      </p>
      <div className="cbw">
        <pre className={'codeblock' + (guide === 'cli' ? ' wrapc' : '')}>{g}</pre>
        <CopyButton
          className="copy"
          text={g}
          label={guide === 'cli' ? t.agents.mine.copyCommand : t.agents.mine.copyConfig}
        />
      </div>
      {!data.settings.enabled ? (
        <div className="callout danger">
          <IconWarn size={15} />
          <span>{t.agents.mine.offCallout}</span>
        </div>
      ) : (
        <div className="callout agentc">
          <IconShield size={15} />
          <span>
            <b>{t.agents.mine.ceilingCallout(org)}</b>
            {t.agents.mine.ceilingText(t.agents.ceilingText[data.settings.ceiling], data.settings.quotaPerHour)}
          </span>
        </div>
      )}
      {data.settings.whoConnects === 'approvalRequired' && (
        <div className="callout">
          <IconHourglass size={15} />
          <span>{t.agents.mine.approvalCallout}</span>
        </div>
      )}
    </section>
  );
}

function ConnectionItem({ c, onRevoked }: { c: AgentConnection; onRevoked: (c: AgentConnection) => void }) {
  const { t, lang } = useI18n();
  const toast = useToast();
  const [open, setOpen] = useState(false);
  const [cap, setCap] = useState<AgentCapabilities | null>(null);
  const [capErr, setCapErr] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [busy, setBusy] = useState(false);
  const revoked = c.state === 'revoked';

  const toggle = () => {
    setOpen((o) => !o);
    if (!cap)
      api
        .agentCapabilities(c.id)
        .then(setCap)
        .catch(() => setCapErr(true));
  };
  const revoke = async () => {
    setBusy(true);
    try {
      const next = await api.revokeAgent(c.id);
      toast(t.agents.mine.revoked);
      setConfirm(false);
      onRevoked(next);
    } catch {
      toast(t.common.genericError);
    } finally {
      setBusy(false);
    }
  };
  const meta = [
    clientHost(t, c),
    c.state === 'pending'
      ? t.agents.mine.requested(shortDate(c.createdAt, lang))
      : t.agents.mine.allowed(shortDate(c.approvedAt ?? c.createdAt, lang)),
    c.lastUsedAt ? t.agents.mine.lastCall(relTime(t, lang, c.lastUsedAt)) : t.agents.mine.noCall,
  ];
  return (
    <li className={revoked ? 'revoked' : undefined}>
      <span className="atile" aria-hidden="true">
        <IconBotFull size={20} />
      </span>
      <span className="an">
        {c.clientName}{' '}
        {revoked && c.revokedAt ? (
          <span className="pchip muted">{t.agents.mine.revokedOn(shortDate(c.revokedAt, lang))}</span>
        ) : (
          <StateChip c={c} />
        )}
      </span>
      {!revoked && (
        <span className="aa">
          <button type="button" className="btn sm" aria-expanded={open} onClick={toggle}>
            {t.agents.mine.whatItCan}
          </button>
          <button type="button" className="btn sm" onClick={() => setConfirm(true)} disabled={confirm}>
            {t.agents.mine.revoke}
          </button>
        </span>
      )}
      <span className="am">
        {meta.join(' · ')} · <ScopeChips scopes={c.scopes} />
      </span>
      {open && (
        <div className="ax">
          {cap ? (
            <CapList items={cap.items} />
          ) : capErr ? (
            <p className="muted">{t.common.genericError}</p>
          ) : (
            <div className="can-sk" aria-busy="true">
              <span className="sk line" style={{ width: '80%' }} />
              <span className="sk line" style={{ width: '65%' }} />
              <span className="sk line" style={{ width: '72%' }} />
            </div>
          )}
        </div>
      )}
      {confirm && (
        <div className="inl-confirm" role="alert">
          <span>
            <b>{t.agents.mine.revokeTitle(c.clientName)}</b>
            {t.agents.mine.revokeText(c.openDrafts)}
          </span>
          <button type="button" className="btn sm danger" disabled={busy} onClick={() => void revoke()}>
            {t.agents.mine.revoke}
          </button>
          <button type="button" className="btn sm" onClick={() => setConfirm(false)}>
            {t.agents.mine.cancel}
          </button>
        </div>
      )}
    </li>
  );
}

/** Changed fields of a proposal, from the server's diff labels. */
export const changedFields = (p: Proposal) => [...new Set(p.changes.flatMap((c) => c.diff.map((d) => d.label)))];

function DraftRow({ p, onChanged }: { p: Proposal; onChanged: () => void }) {
  const { t, lang } = useI18n();
  const toast = useToast();
  const { refresh } = useInbox();
  const [busy, setBusy] = useState(false);
  const agent = p.agentName ?? t.agents.demoClient;
  const teams = p.ownerTeams.map((o) => o.name).join(', ');
  const target = [...new Set(p.changes.map((c) => c.targetTitle || c.targetId).filter(Boolean))].join(', ');
  const act = async (call: () => Promise<unknown>, done: string) => {
    setBusy(true);
    try {
      await call();
      toast(done);
      refresh();
      onChanged();
    } catch (e) {
      toast(e instanceof ApiError && e.detail ? e.detail : t.common.genericError);
    } finally {
      setBusy(false);
    }
  };
  let state;
  let why;
  if (p.status === 'draft') {
    state = (
      <span className="pchip agent">
        <IconEdit size={11} />
        {t.agents.mine.draftChip}
      </span>
    );
    why = t.agents.mine.byAgent(changedFields(p).join(', '), agent);
  } else if (p.status === 'submitted') {
    state = <span className="pchip info">{t.agents.mine.toValidate(teams)}</span>;
    why = t.agents.mine.byAgent(target, agent);
  } else if (p.status === 'refused') {
    state = <span className="pchip danger">{t.review.status.refused}</span>;
    why = t.agents.mine.refusedBy(p.decidedBy?.displayName ?? '—', p.refusalReason ?? '');
  } else if (p.status === 'accepted') {
    state = <span className="pchip ok">{t.review.status.accepted}</span>;
    why = t.agents.mine.acceptedBy(agent, p.decidedBy?.displayName ?? '—');
  } else {
    state = <span className="pchip muted">{t.review.status[p.status]}</span>;
    why = t.agents.mine.byAgent(target, agent);
  }
  const updated = p.decidedAt ?? p.submittedAt ?? p.createdAt;
  return (
    <tr>
      <td className="lead">
        <span className="ttl">{p.title}</span>
        <span className="why">{why}</span>
      </td>
      <td data-l={t.agents.mine.colState}>{state}</td>
      <td data-l={t.agents.mine.colUpdated}>{relTime(t, lang, updated)}</td>
      <td className="acts-td">
        <span className="acts2">
          {p.status === 'draft' && p.canEdit && (
            <button
              type="button"
              className="btn sm primary"
              disabled={busy}
              onClick={() => void act(() => api.submitProposal(p.id), t.agents.mine.submitted(teams))}
            >
              <IconSend size={14} />
              {t.agents.mine.submit}
            </button>
          )}
          <Link className="btn sm" to={paths.proposal(p.id)}>
            {t.agents.mine.open}
          </Link>
          {p.status === 'draft' && p.canEdit && (
            <button
              type="button"
              className="btn sm ghost"
              disabled={busy}
              onClick={() => void act(() => api.withdrawProposal(p.id), t.agents.mine.discarded)}
            >
              {t.agents.mine.discard}
            </button>
          )}
        </span>
      </td>
    </tr>
  );
}

/** "What my agents did": newest first, one page at a time. */
export function useActivity(connectionId: string | undefined, kind: string | undefined, reloadKey: unknown) {
  const [pages, setPages] = useState<(string | undefined)[]>([undefined]);
  const [items, setItems] = useState<AgentActivity[] | null>(null);
  const [next, setNext] = useState<string | null>(null);
  const [error, setError] = useState(false);
  const cursor = pages[pages.length - 1];
  useEffect(() => {
    setPages([undefined]);
  }, [connectionId, kind, reloadKey]);
  useEffect(() => {
    let alive = true;
    setItems(null);
    setError(false);
    api
      .agentActivity({ connectionId, kind: kind as AgentActivity['kind'] | undefined, cursor, limit: 20 })
      .then((p) => {
        if (!alive) return;
        setItems(p.items);
        setNext(p.nextCursor);
      })
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, [connectionId, kind, cursor, reloadKey]);
  return {
    items,
    error,
    hasNewer: pages.length > 1,
    hasOlder: !!next,
    older: () => next && setPages((p) => [...p, next]),
    newer: () => setPages((p) => (p.length > 1 ? p.slice(0, -1) : p)),
  };
}

function MySkeleton() {
  return (
    <div className="split2" aria-busy="true">
      <div>
        <section className="box">
          <span className="sk line" style={{ width: 120 }} />
          <span className="sk line" style={{ width: '50%', marginTop: 10 }} />
          {[0, 1].map((i) => (
            <div key={i} className="sk-row">
              <span className="sk block" style={{ width: 40, height: 40 }} />
              <span style={{ flex: 1 }}>
                <span className="sk line" style={{ width: '40%' }} />
                <span className="sk line" style={{ width: '70%', marginTop: 8 }} />
              </span>
            </div>
          ))}
        </section>
        <section className="box">
          <span className="sk line" style={{ width: 160 }} />
          <span className="sk block" style={{ height: 140, marginTop: 14 }} />
        </section>
      </div>
      <div>
        <section className="box">
          <span className="sk title" style={{ width: '60%' }} />
          <span className="sk line" style={{ width: '90%', marginTop: 12 }} />
          <span className="sk block" style={{ height: 40, marginTop: 16 }} />
          <span className="sk block" style={{ height: 160, marginTop: 16 }} />
        </section>
      </div>
    </div>
  );
}

/** "My agents": my connections, my agents' drafts and proposals, their activity, and how to connect one. */
export function MyAgentsPage() {
  const { t } = useI18n();
  const { me } = useAuth();
  const { tick } = useDemo();
  const [data, setData] = useState<MyAgents | null>(null);
  const [error, setError] = useState(false);
  const [reload, setReload] = useState(0);
  const [showRevoked, setShowRevoked] = useState(false);
  const activity = useActivity(undefined, undefined, `${tick}-${reload}`);
  const guest = me?.role === 'guest';

  useEffect(() => {
    if (guest) return;
    let alive = true;
    setError(false);
    api
      .myAgents()
      .then((d) => alive && setData(d))
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, [tick, reload, guest]);

  if (guest)
    return (
      <PageShell title={t.agents.mine.title}>
        <div className="page-h">
          <div className="ph-t">
            <h1>{t.agents.mine.title}</h1>
          </div>
        </div>
        <div className="banner" role="note">
          <IconBotFull />
          <div className="bt">
            <b>{t.agents.mine.guestTitle}</b>
            {t.agents.mine.guestText}
          </div>
        </div>
      </PageShell>
    );

  const live = data?.connections.filter((c) => c.state !== 'revoked') ?? [];
  const gone = data?.connections.filter((c) => c.state === 'revoked') ?? [];
  const shown = showRevoked ? [...live, ...gone] : live;
  const list = data ? [...data.drafts, ...data.proposals] : [];

  return (
    <PageShell wide title={t.agents.mine.title}>
      <div className="page-h">
        <div className="ph-t">
          <h1>{t.agents.mine.title}</h1>
          <p>{t.agents.mine.intro}</p>
        </div>
        <div className="page-actions">
          <a
            className="btn primary"
            href="#connect-box"
            onClick={(e) => {
              e.preventDefault();
              document.getElementById('connect-box')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
              document.getElementById('mcp-url')?.focus({ preventScroll: true });
            }}
          >
            <IconPlug size={16} />
            {t.agents.mine.connect}
          </a>
        </div>
      </div>
      {error && (
        <div className="banner danger" role="alert">
          <IconWarn />
          <div className="bt">
            <b>{t.agents.loadError}</b>
            <div className="page-actions" style={{ marginTop: 10 }}>
              <button type="button" className="btn" onClick={() => setReload((x) => x + 1)}>
                {t.common.retry}
              </button>
            </div>
          </div>
        </div>
      )}
      {!data && !error && <MySkeleton />}
      {data && (
        <div className="split2">
          <div>
            <section className="box tb" aria-labelledby="ma-conn">
              <div className="tb-h">
                <div>
                  <h2 id="ma-conn">{t.agents.mine.connected(live.length)}</h2>
                  <p>{t.agents.mine.connectedHint}</p>
                </div>
              </div>
              {shown.length === 0 ? (
                <p className="log-empty">{t.agents.mine.none}</p>
              ) : (
                <ul className="alist">
                  {shown.map((c) => (
                    <ConnectionItem
                      key={c.id}
                      c={c}
                      onRevoked={(n) =>
                        setData((d) => d && { ...d, connections: d.connections.map((x) => (x.id === n.id ? n : x)) })
                      }
                    />
                  ))}
                </ul>
              )}
              {gone.length > 0 && (
                <div className="tb-f">
                  <span />
                  <button type="button" className="btn sm ghost" onClick={() => setShowRevoked((s) => !s)}>
                    {showRevoked ? t.agents.mine.hideRevoked : t.agents.mine.showRevoked(gone.length)}
                  </button>
                </div>
              )}
            </section>

            <section className="box tb" aria-labelledby="ma-drafts">
              <div className="tb-h">
                <div>
                  <h2 id="ma-drafts">{t.agents.mine.draftsTitle}</h2>
                  <p>{t.agents.mine.draftsHint}</p>
                </div>
              </div>
              {list.length === 0 ? (
                <p className="log-empty">{t.agents.mine.noDrafts}</p>
              ) : (
                <table className="table rtable dtable">
                  <thead>
                    <tr>
                      <th scope="col">{t.agents.mine.colChange}</th>
                      <th scope="col">{t.agents.mine.colState}</th>
                      <th scope="col">{t.agents.mine.colUpdated}</th>
                      <th scope="col">
                        <span className="sr">{t.users.actions}</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {list.map((p) => (
                      <DraftRow key={p.id} p={p} onChanged={() => setReload((x) => x + 1)} />
                    ))}
                  </tbody>
                </table>
              )}
            </section>

            <section className="box tb" aria-labelledby="ma-act">
              <div className="tb-h">
                <div>
                  <h2 id="ma-act">{t.agents.mine.activityTitle}</h2>
                  <p>{t.agents.mine.activityHint}</p>
                </div>
              </div>
              {activity.error ? (
                <p className="log-empty">{t.common.genericError}</p>
              ) : !activity.items ? (
                <ActivitySkeleton />
              ) : activity.items.length === 0 ? (
                <p className="log-empty">{t.agents.mine.noActivity}</p>
              ) : (
                <ul className="alog">
                  {activity.items.map((a) => (
                    <ActivityLine key={a.id} a={a} />
                  ))}
                </ul>
              )}
              {(activity.hasNewer || activity.hasOlder) && (
                <div className="tb-f">
                  <span />
                  <span className="pager">
                    <button type="button" className="btn sm" disabled={!activity.hasNewer} onClick={activity.newer}>
                      {t.agents.mine.newer}
                    </button>
                    <button type="button" className="btn sm" disabled={!activity.hasOlder} onClick={activity.older}>
                      {t.agents.mine.older}
                    </button>
                  </span>
                </div>
              )}
            </section>
          </div>

          <div>
            <ConnectBox data={data} />
            <section className="box">
              <h2>{t.agents.mine.alsoTitle}</h2>
              <p className="cn-p" style={{ margin: 0 }}>
                {t.agents.mine.alsoText1}
                <a className="mono" href="/llms.txt" target="_blank" rel="noreferrer">
                  /llms.txt
                </a>
                {t.agents.mine.alsoText2}
                <span className="mono">/application/billing.md</span>
                {t.agents.mine.alsoText3}
              </p>
            </section>
          </div>
        </div>
      )}
    </PageShell>
  );
}
