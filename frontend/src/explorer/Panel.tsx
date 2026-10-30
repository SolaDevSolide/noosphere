import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import type { LandscapeFlow } from '../api/types';
import { isRestrictedApp, isRestrictedFlow } from '../api/types';
import { appOrder, flowsOf, fullApp, fullFlow, lineage, makeMatcher, type Model } from '../domain/model';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { IconApp, IconLock, IconWarn, TypeIcon } from '../ui/icons';
import { LineSample, nameOf, objName, pct, Sdot, StatusTag, teamName } from './bits';
import { useLandscape } from './landscape';
import { paths } from './paths';
import type { View } from './view';
import { DataObjectPages } from '../docpages/RecordPages';

export function flowTitle(t: Dict, m: Model, f: LandscapeFlow) {
  const from = nameOf(t, m, f.fromId);
  const to = nameOf(t, m, f.toId);
  if (isRestrictedFlow(f)) return `${t.common.restrictedFlow} · ${t.type[f.type]} · ${from} → ${to}`;
  let s = t.lens.flowTitle(f.name, t.type[f.type], from, to, f.schedule ?? '', t.sens[f.sensitivity]);
  if (f.status !== 'live') s += ' · ' + t.status[f.status];
  if (f.plannedChange?.summary) s += ` · ${t.legend.plannedChange}: ${f.plannedChange.summary}`;
  return s;
}

export function FlowRow({ f, perspective }: { f: LandscapeFlow; perspective?: 'in' | 'out' }) {
  const { t } = useI18n();
  const { model: m, filters } = useLandscape();
  if (!m) return null;
  const ok = makeMatcher(m, filters)(f);
  const other =
    perspective === 'in'
      ? `${t.panel.fromPrefix} ${nameOf(t, m, f.fromId)}`
      : perspective === 'out'
        ? `${t.panel.toPrefix} ${nameOf(t, m, f.toId)}`
        : `${nameOf(t, m, f.fromId)} → ${nameOf(t, m, f.toId)}`;
  const full = fullFlow(f);
  return (
    <Link className={`row t-${f.type}${ok ? '' : ' filtered'}`} to={paths.flowLens(f.id)} title={flowTitle(t, m, f)}>
      <span className="ri">{full ? <TypeIcon type={f.type} /> : <IconLock />}</span>
      <span className="rn">
        {full ? full.name : t.common.restrictedFlow}
        <small>{other}</small>
      </span>
      <span className="rr">
        {full && (
          <span style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
            {full.schedule}
            <Sdot s={full.sensitivity} />
          </span>
        )}
        <StatusTag f={f} />
      </span>
    </Link>
  );
}

export function AppRow({ id, note, onRestricted }: { id: string; note?: string; onRestricted?: () => void }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  if (!m) return null;
  const a = m.apps.get(id);
  const r = !a || isRestrictedApp(a);
  const inner = (
    <>
      <span className="ri">{r ? <IconLock /> : <IconApp />}</span>
      <span className="rn">
        {nameOf(t, m, id)}
        <small>{note ?? (r ? t.panel.restricted : teamName(m, fullApp(m, id)?.teamId))}</small>
      </span>
      <span className="rr" />
    </>
  );
  if (r)
    return (
      <button type="button" className="row" onClick={onRestricted}>
        {inner}
      </button>
    );
  return (
    <Link className="row" to={paths.appLens(id)}>
      {inner}
    </Link>
  );
}

function ObjChip({ id, suffix }: { id: string; suffix?: string }) {
  const { model: m } = useLandscape();
  if (!m) return null;
  const o = m.objects.get(id);
  return (
    <Link className="och" to={paths.lineage(id)}>
      {o && <Sdot s={o.sensitivity} />}
      {objName(m, id)}
      {suffix}
    </Link>
  );
}

export function LensLegend() {
  const { t } = useI18n();
  return (
    <>
      <h3>{t.panel.howToReadLens}</h3>
      <div className="legend-list">
        <div>
          <LineSample type="api" /> {t.panel.apiSolid}
        </div>
        <div>
          <LineSample type="event" /> {t.panel.eventDashed}
        </div>
        <div>
          <LineSample type="batch" /> {t.panel.batchDotted}
        </div>
        <div>
          <Sdot s="internal" /> {t.panel.internal} <Sdot s="personal" /> {t.panel.personal} <Sdot s="sensitive" />{' '}
          {t.panel.sensitive}
        </div>
      </div>
    </>
  );
}

export function RestrictedPanel() {
  const { t } = useI18n();
  return (
    <>
      <div className="lock-big">
        <IconLock size={22} />
      </div>
      <h2>{t.panel.restrictedTitle}</h2>
      <p>{t.panel.restrictedText}</p>
    </>
  );
}

function WhereToLook({ onRestricted }: { onRestricted: () => void }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  if (!m) return null;
  const flows = m.flows.map(fullFlow).filter((f): f is NonNullable<typeof f> => !!f);
  const attn = flows.filter((f) => f.plannedChange?.summary || f.status !== 'live');
  const low = m.appIds
    .map((id) => fullApp(m, id))
    .filter((a): a is NonNullable<typeof a> => !!a && a.completeness < 0.6)
    .sort((a, b) => a.completeness - b.completeness);
  return (
    <>
      <h2>{t.panel.whereToLook}</h2>
      <p>{t.panel.glance}</p>
      <div className="stats">
        <div className="stat">
          <b>{m.appIds.length}</b>
          <span>{t.panel.applications}</span>
        </div>
        <div className="stat">
          <b>{m.flows.length}</b>
          <span>{t.panel.flows}</span>
        </div>
        <div className="stat">
          <b>{m.domains.length}</b>
          <span>{t.panel.domains}</span>
        </div>
      </div>
      <h3>{t.panel.needsAttention(attn.length)}</h3>
      <div className="attn">
        {attn.map((f) => (
          <FlowRow key={f.id} f={f} />
        ))}
      </div>
      {attn
        .filter((f) => f.plannedChange?.summary)
        .map((f) => (
          <div key={f.id} className="callout">
            <IconWarn />
            <span>
              <b>{f.name}</b>: {f.plannedChange!.summary}
            </span>
          </div>
        ))}
      <h3>{t.panel.followData}</h3>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
        {m.objectIds.map((o) => (
          <ObjChip key={o} id={o} />
        ))}
      </div>
      {low.length > 0 && (
        <>
          <h3>{t.panel.incomplete}</h3>
          <div className="rows">
            {low.map((a) => (
              <AppRow
                key={a.id}
                id={a.id}
                note={t.panel.documented(pct(a.completeness), teamName(m, a.teamId))}
                onRestricted={onRestricted}
              />
            ))}
          </div>
        </>
      )}
      <LensLegend />
    </>
  );
}

function AppPanel({ id }: { id: string }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  if (!m) return null;
  const a = fullApp(m, id);
  if (!a) return <RestrictedPanel />;
  const p = pct(a.completeness);
  const ins = m.flowsIn.get(id) ?? [];
  const outs = m.flowsOut.get(id) ?? [];
  const ch = flowsOf(m, id)
    .map(fullFlow)
    .filter((f): f is NonNullable<typeof f> => !!f?.plannedChange?.summary);
  return (
    <>
      <div className="pn-head">
        <span className="kind">{t.kind[a.kind]}</span>
        {a.teamId && (
          <Link className="tchip" to={paths.team(a.teamId)}>
            {teamName(m, a.teamId)}
          </Link>
        )}
      </div>
      <h2>{a.name}</h2>
      <div className="pn-sub">{[a.tech, m.domain.get(a.domainId)?.name].filter(Boolean).join(' · ')}</div>
      <div
        className={'meter' + (a.completeness < 0.65 ? ' low' : '')}
        style={{ display: 'flex', gap: 10, alignItems: 'center', marginTop: 14, fontSize: 13 }}
      >
        <span style={{ color: 'var(--fg-muted)' }}>{t.panel.recordComplete}</span>
        <span
          className="bar2"
          role="progressbar"
          aria-valuenow={p}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label={t.panel.recordComplete}
        >
          <i style={{ width: p + '%' }} />
        </span>
        <b>{p} %</b>
      </div>
      {ch.map((f) => (
        <div key={f.id} className="callout">
          <IconWarn />
          <span>
            {f.plannedChange!.summary} <b>({f.name})</b>
          </span>
        </div>
      ))}
      <h3>{t.panel.receives(ins.length)}</h3>
      <div className="rows">
        {ins.length ? ins.map((f) => <FlowRow key={f.id} f={f} perspective="in" />) : <p>{t.panel.noIncoming}</p>}
      </div>
      <h3>{t.panel.sends(outs.length)}</h3>
      <div className="rows">
        {outs.length ? outs.map((f) => <FlowRow key={f.id} f={f} perspective="out" />) : <p>{t.panel.noOutgoing}</p>}
      </div>
      {a.sourceOf.length > 0 && (
        <>
          <h3>{t.panel.sourceFor}</h3>
          <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
            {a.sourceOf.map((o) => (
              <ObjChip key={o} id={o} suffix={' · ' + t.panel.seeLineage} />
            ))}
          </div>
        </>
      )}
      <div className="pn-actions">
        <Link className="btn primary" to={paths.app(id)}>
          {t.panel.openAppRecord}
        </Link>
      </div>
    </>
  );
}

export function FlowDetails({ id }: { id: string }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  if (!m) return null;
  const f = fullFlow(m.flow.get(id));
  if (!f) return null;
  return (
    <>
      <dl className="kv">
        <dt>{t.lens.schedule}</dt>
        <dd>{f.schedule || '—'}</dd>
        {f.volume && (
          <>
            <dt>{t.lens.volume}</dt>
            <dd>{f.volume}</dd>
          </>
        )}
        <dt>{t.lens.dataCarried}</dt>
        <dd>
          {f.dataObjectIds.map((o) => (
            <ObjChip key={o} id={o} />
          ))}
        </dd>
        <dt>{t.lens.sensitivity}</dt>
        <dd>
          <Sdot s={f.sensitivity} />
          {t.sens[f.sensitivity]}
        </dd>
        <dt>{t.lens.owner}</dt>
        <dd>{teamName(m, f.ownerTeamId) || '—'}</dd>
        <dt>{t.lens.status}</dt>
        <dd>
          {t.status[f.status]} <StatusTag f={f} />
        </dd>
      </dl>
      {f.plannedChange?.summary && (
        <div className="callout">
          <IconWarn />
          <span>
            <b>{t.lens.plannedChange}</b> {f.plannedChange.summary}
          </span>
        </div>
      )}
    </>
  );
}

function FlowPanel({ id, onRestricted }: { id: string; onRestricted: () => void }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  if (!m) return null;
  const lf = m.flow.get(id);
  if (!lf) return null;
  const f = fullFlow(lf);
  const fromTeam = teamName(m, fullApp(m, lf.fromId)?.teamId);
  const toApp = fullApp(m, lf.toId);
  return (
    <>
      <div className="pn-head">
        <span className="kb flow">{t.search.badge.flow}</span>
        <span
          className={`tchip t-${lf.type}`}
          style={{ background: 'none', color: 'var(--tc)', display: 'inline-flex', gap: 5, alignItems: 'center' }}
        >
          <TypeIcon type={lf.type} />
          {t.type[lf.type]}
        </span>
      </div>
      <h2>{f ? f.name : t.common.restrictedFlow}</h2>
      <div className="rows" style={{ marginTop: 10 }}>
        <AppRow id={lf.fromId} note={t.panel.fromDot + (fromTeam || t.panel.restricted)} onRestricted={onRestricted} />
        <AppRow
          id={lf.toId}
          note={t.panel.toDot + (toApp ? teamName(m, toApp.teamId) : t.panel.restricted)}
          onRestricted={onRestricted}
        />
      </div>
      {f ? (
        <>
          <h3>{t.panel.details}</h3>
          <FlowDetails id={id} />
          <div className="pn-actions">
            <Link className="btn primary" to={paths.flow(id)}>
              {t.panel.openFlowRecord}
            </Link>
          </div>
        </>
      ) : (
        <p>{t.panel.restrictedFlowText}</p>
      )}
    </>
  );
}

function ObjectPanel({ id, onRestricted }: { id: string; onRestricted: () => void }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  if (!m) return null;
  const o = m.objects.get(id);
  if (!o) return <p>{t.lens.notFound}</p>;
  const Lg = lineage(m, id);
  const chain = Lg ? Lg.carry.filter((f) => !Lg.off.includes(f)) : [];
  return (
    <>
      <div className="pn-head">
        <span className="kb data">{t.search.badge.object}</span>
        <span className="b">
          <Sdot s={o.sensitivity} />
          {t.sens[o.sensitivity]}
        </span>
      </div>
      <h2>{o.name}</h2>
      {o.description && <p>{o.description}</p>}
      {Lg ? (
        <>
          <p>{t.panel.lineageText(Lg.depth.size, chain.length)}</p>
          <h3>{t.lens.sourceOfTruth}</h3>
          <div className="rows">
            <AppRow id={Lg.src} onRestricted={onRestricted} />
          </div>
          <h3>{t.panel.lineageFlows(chain.length)}</h3>
          <div className="rows">
            {chain.map((f) => (
              <FlowRow key={f.id} f={f} />
            ))}
          </div>
          {Lg.off.length > 0 && (
            <>
              <h3>{t.panel.alsoCarried(Lg.off.length)}</h3>
              <p>{t.panel.alsoCarriedText(o.name)}</p>
              <div className="rows">
                {Lg.off.map((f) => (
                  <FlowRow key={f.id} f={f} />
                ))}
              </div>
            </>
          )}
        </>
      ) : (
        <p>{t.lens.noLineage}</p>
      )}
      <DataObjectPages id={id} />
    </>
  );
}

function TeamPanel({ id, onRestricted }: { id: string; onRestricted: () => void }) {
  const { t } = useI18n();
  const { model: m } = useLandscape();
  if (!m) return null;
  const team = m.teams.get(id);
  const order = appOrder(m);
  const apps = m.appIds.filter((a) => fullApp(m, a)?.teamId === id).sort(order);
  const owned = m.flows.filter((f) => fullFlow(f)?.ownerTeamId === id);
  return (
    <>
      <div className="pn-head">
        <span className="kb team">{t.search.badge.team}</span>
      </div>
      <h2>{team?.name ?? id}</h2>
      {team?.description && <p>{team.description}</p>}
      {team?.contact && (
        <p>
          {t.panel.contact}: {team.contact}
        </p>
      )}
      <h3>{t.panel.teamApps(apps.length)}</h3>
      <div className="rows">
        {apps.map((a) => (
          <AppRow key={a} id={a} note={m.domain.get(m.apps.get(a)!.domainId)?.name} onRestricted={onRestricted} />
        ))}
      </div>
      <h3>{t.panel.teamFlows(owned.length)}</h3>
      <div className="rows">
        {owned.map((f) => (
          <FlowRow key={f.id} f={f} />
        ))}
      </div>
    </>
  );
}

/** Side panel content for the overview (cards) and the lens views. */
export function Panel({
  view,
  restricted,
  onRestricted,
  children,
}: {
  view: View;
  restricted: boolean;
  onRestricted: () => void;
  children?: ReactNode;
}) {
  let body: ReactNode;
  if (restricted) body = <RestrictedPanel />;
  else if (children) body = children;
  else if (!view) body = <WhereToLook onRestricted={onRestricted} />;
  else if (view.kind === 'app') body = <AppPanel id={view.id} />;
  else if (view.kind === 'flow') body = <FlowPanel id={view.id} onRestricted={onRestricted} />;
  else if (view.kind === 'object') body = <ObjectPanel id={view.id} onRestricted={onRestricted} />;
  else body = <TeamPanel id={view.id} onRestricted={onRestricted} />;
  return <div className="pn-in">{body}</div>;
}
