import { useEffect, useState } from 'react';
import { api } from '../api/client';
import { isRestrictedApp, type Deployment, type Deployments, type Language } from '../api/types';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { AppTile } from '../links/bits';
import { IconExternal, IconInfo, IconRocket } from '../ui/icons';

const locale = (lang: Language) => (lang === 'fr' ? 'fr-FR' : 'en-GB');
const fmt = (lang: Language, o: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat(locale(lang), o);

/** The day a deployment is shown under: its planned start, else its planned end, else its last change. */
export const dayOf = (d: Deployment) => d.plannedStart ?? d.plannedEnd ?? d.updatedAt;

/** "Tue 4 Nov, 20:00 – 23:00", or "Tue 4 Nov, 20:00 – Wed 5 Nov, 02:00" across days. */
export function whenText(lang: Language, start: string, end?: string | null): string {
  const long = fmt(lang, { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
  const s = long.format(new Date(start));
  if (!end) return s;
  const a = new Date(start);
  const b = new Date(end);
  const same = a.toDateString() === b.toDateString();
  return `${s} – ${same ? fmt(lang, { hour: '2-digit', minute: '2-digit' }).format(b) : long.format(b)}`;
}

/** The chip on the right of a deployment: its phase, or how it ended. */
export function statusChip(t: Dict, d: Deployment): { cls: string; label: string } {
  const p = t.deployments.phase;
  if (d.phase === 'cancelled') return { cls: 'muted', label: p.cancelled };
  if (d.phase === 'closed') {
    const o = d.outcome;
    const cls = !o || o === 'success' ? 'ok' : o === 'success-with-incidents' ? 'warn' : 'danger';
    return { cls, label: o ? `${p.closed} · ${t.deployments.outcome[o]}` : p.closed };
  }
  if (d.phase === 'rollback') return { cls: 'danger', label: p.rollback };
  return { cls: 'warn', label: p[d.phase] };
}

function DeploymentRow({ d, here, names }: { d: Deployment; here: string[]; names: (id: string) => string | null }) {
  const { t, lang } = useI18n();
  const x = t.deployments;
  const day = new Date(dayOf(d));
  const ended = d.phase === 'closed' || d.phase === 'cancelled';
  const production = d.environments.some((e) => e.production);
  const others = d.applicationIds
    .filter((a) => !here.includes(a))
    .map(names)
    .filter((n): n is string => !!n);
  const when = !d.plannedStart
    ? x.notScheduled
    : ended
      ? d.phase === 'closed'
        ? x.deployed(whenText(lang, d.plannedStart))
        : x.planned(fmt(lang, { weekday: 'short', day: 'numeric', month: 'short' }).format(new Date(d.plannedStart)))
      : whenText(lang, d.plannedStart, d.plannedEnd);
  const parts = [when];
  if (!production && d.environments.length) parts.push(d.environments.map((e) => e.name).join(', '));
  if (others.length) parts.push(x.alsoTouches(others));
  const chip = statusChip(t, d);
  const src = d.source;
  return (
    <a className="row dep" href={d.url} target="_blank" rel="noopener noreferrer" title={x.openIn(src.instanceName)}>
      <span className="dtile" aria-hidden="true">
        <b>{day.getDate()}</b>
        <small>{fmt(lang, { month: 'short' }).format(day).replace('.', '')}</small>
      </span>
      <span className="rn">
        {d.title}
        {production && <span className="pchip warn prod">{x.production}</span>}
        <small>{parts.join(' · ')}</small>
        <span className="src">
          <AppTile name={src.instanceName} product={src.product} size="xs" cut={!src.linkActive} />
          {src.instanceName}{' '}
          <span>
            · {src.product}
            {!src.linkActive && ` · ${x.linkCut}`}
          </span>
        </span>
      </span>
      <span className="rr">
        {d.postponed && !ended && <span className="pchip warn">{x.postponed}</span>}
        <span className={'pchip ' + chip.cls}>{chip.label}</span>
        <IconExternal size={14} className="xo" />
      </span>
    </a>
  );
}

function useNames(): (id: string) => string | null {
  const { model } = useLandscape();
  return (id) => {
    const a = model?.apps.get(id);
    return a && !isRestrictedApp(a) ? a.name : null;
  };
}

const sourcesOf = (ds: Deployment[]) => {
  const seen = new Map<string, string>();
  for (const d of ds) seen.set(d.source.linkId, `${d.source.instanceName} (${d.source.product})`);
  return [...seen.values()];
};

function Group({
  title,
  items,
  here,
  names,
}: {
  title: string;
  items: Deployment[];
  here: string[];
  names: (id: string) => string | null;
}) {
  if (items.length === 0) return null;
  return (
    <>
      <h2 className="sub">{title}</h2>
      <div className="rows">
        {items.map((d) => (
          <DeploymentRow key={d.source.linkId + d.deploymentId} d={d} here={here} names={names} />
        ))}
      </div>
    </>
  );
}

/** "Deployments and changes" on an application: the upcoming ones and the last three, from linked applications. */
export function ApplicationDeployments({ appId, deployments }: { appId: string; deployments?: Deployments }) {
  const { t } = useI18n();
  const names = useNames();
  const x = t.deployments;
  if (!deployments) return null;
  const { upcoming, past } = deployments;
  if (upcoming.length + past.length === 0) return null;
  return (
    <section className="box deps" aria-labelledby="dp-h">
      <div className="bx-h">
        <h2 id="dp-h">
          <IconRocket size={15} />
          {x.title}
        </h2>
        <span className="muted2">{x.from}</span>
      </div>
      <Group title={x.upcoming(upcoming.length)} items={upcoming} here={[appId]} names={names} />
      <Group title={x.recent} items={past} here={[appId]} names={names} />
      <p className="pn-foot">
        <IconInfo size={14} />
        {x.sentBy(sourcesOf([...upcoming, ...past]))}
      </p>
    </section>
  );
}

/** Per end of a flow: its upcoming deployments and its last three past ones. */
export function perEnd(all: Deployments, appId: string, recent = 3): Deployment[] {
  const touches = (d: Deployment) => d.applicationIds.includes(appId);
  return [...all.upcoming.filter(touches), ...all.past.filter(touches).slice(0, recent)];
}

/** "Deployments and changes on its ends" on a flow, grouped by source and target application. */
export function FlowDeployments({
  flowId,
  fromId,
  toId,
  initial,
}: {
  flowId: string;
  fromId: string;
  toId: string;
  /** The deployments of the flow's detail: when there are none, nothing more is asked. */
  initial?: Deployments;
}) {
  const { t } = useI18n();
  const names = useNames();
  const x = t.deployments;
  const [data, setData] = useState<Deployments | null>(null);
  const [failed, setFailed] = useState(false);
  const any = !!initial && initial.upcoming.length + initial.past.length > 0;
  useEffect(() => {
    if (!any) return;
    let alive = true;
    setFailed(false);
    api
      .deployments('flow', flowId)
      .then((d) => alive && setData(d))
      .catch(() => alive && setFailed(true));
    return () => {
      alive = false;
    };
  }, [flowId, any]);
  if (!any || failed) return null;
  if (!data)
    return (
      <section className="box deps" aria-busy="true">
        <span className="sk line" style={{ width: 200 }} />
        <span className="sk block" style={{ height: 90, marginTop: 14 }} />
      </section>
    );
  if (data.upcoming.length + data.past.length === 0) return null;
  const ends: [string, string][] = [
    [fromId, x.source(names(fromId) ?? fromId)],
    [toId, x.target(names(toId) ?? toId)],
  ];
  return (
    <section className="box deps" aria-labelledby="dpf-h">
      <div className="bx-h">
        <h2 id="dpf-h">
          <IconRocket size={15} />
          {x.titleFlow}
        </h2>
        <span className="muted2">{x.from}</span>
      </div>
      {ends.map(([id, title]) => (
        <Group key={id} title={title} items={perEnd(data, id)} here={[id]} names={names} />
      ))}
      <p className="pn-foot">
        <IconInfo size={14} />
        {x.flowFoot}
      </p>
    </section>
  );
}

/** The next planned deployment, under a record's title. */
export function DeploymentHint({ deployments }: { deployments?: Deployments }) {
  const { t, lang } = useI18n();
  const next = deployments?.upcoming.find((d) => d.plannedStart);
  if (!next?.plannedStart) return null;
  const when = fmt(lang, { day: 'numeric', month: 'short' }).format(new Date(next.plannedStart));
  return (
    <p className="dep-hint">
      <IconRocket size={14} />
      <a href={next.url} target="_blank" rel="noopener noreferrer">
        {t.deployments.hint(when, next.title)}
      </a>
      {next.environments.some((e) => e.production) && (
        <span className="pchip warn prod">{t.deployments.production}</span>
      )}
      <span className="muted2">{t.deployments.hintFrom(next.source.instanceName)}</span>
    </p>
  );
}
