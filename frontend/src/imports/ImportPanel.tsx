import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import { isRestrictedApp, type ImportOptions, type ImportPlan } from '../api/types';
import { useAuth } from '../auth/auth';
import { recordHref } from '../docpages/model';
import { useLandscape } from '../explorer/landscape';
import { useI18n } from '../i18n';
import { useCountdown } from '../links/bits';
import {
  IconApi,
  IconApp,
  IconBook,
  IconCheck,
  IconChevron,
  IconClock,
  IconDoc,
  IconDownload,
  IconEvent,
  IconEye,
  IconInfo,
  IconLock,
  IconShare,
  IconTable,
  IconUpload,
  IconWarn,
  IconX,
} from '../ui/icons';
import { useModal } from '../ui/modal';
import { NativeImport } from './NativeImport';
import { ACCEPT, leaveOut, MAX_UPLOAD_BYTES, planCsv, saveText, SOURCES, takeBack, tally, type SourceId } from './plan';
import { PlanPreview } from './PlanPreview';

const ICONS: Record<SourceId, ReactNode> = {
  backstage: <IconApp size={17} />,
  openapi: <IconApi size={17} />,
  asyncapi: <IconEvent size={17} />,
  adr: <IconBook size={17} />,
  mermaid: <IconShare size={17} />,
  excel: <IconTable size={17} />,
  native: <IconDoc size={17} />,
};

type Phase = 'source' | 'planning' | 'preview' | 'done';
type Stale = { kind: 'expired' | 'changed' | 'rejected'; detail?: string };

const kb = (n: number) => (n / 1024).toFixed(n < 10 * 1024 ? 1 : 0);

function Steps({ at }: { at: 0 | 1 | 2 | 3 }) {
  const { t } = useI18n();
  const S = t.imports.steps;
  return (
    <ol className="isteps" aria-label={S.stepsLabel}>
      {[S.source, S.dry, S.confirm].map((label, i) => (
        <li
          key={label}
          className={i < at ? 'done' : i === at ? 'now' : ''}
          aria-current={i === at ? 'step' : undefined}
        >
          <span className="isd">{i < at ? <IconCheck size={13} /> : i + 1}</span>
          {label}
        </li>
      ))}
    </ol>
  );
}

function PreviewSkeleton() {
  return (
    <div className="ip-body" aria-busy="true">
      <div className="ip-tiles">
        {[0, 1, 2, 3, 4].map((i) => (
          <span key={i} className="sk block" style={{ height: 64 }} />
        ))}
      </div>
      <span className="sk line" style={{ width: '55%', marginTop: 16 }} />
      {[0, 1, 2, 3].map((i) => (
        <span key={i} className="sk block" style={{ height: 44, marginTop: 10 }} />
      ))}
    </div>
  );
}

function ConfirmDialog({
  plan,
  skipped,
  title,
  busy,
  onCancel,
  onApply,
}: {
  plan: ImportPlan;
  skipped: ReadonlySet<number>;
  title: string;
  busy: boolean;
  onCancel: () => void;
  onApply: () => void;
}) {
  const { t } = useI18n();
  const T = t.imports;
  const box = useRef<HTMLDivElement>(null);
  useModal(box, () => !busy && onCancel());
  const n = tally(plan, skipped);
  return (
    <div
      className="dlg-back"
      role="presentation"
      onMouseDown={(e) => e.target === e.currentTarget && !busy && onCancel()}
    >
      <div
        ref={box}
        className="dlg"
        role="dialog"
        aria-modal="true"
        aria-labelledby="ic-h"
        aria-describedby="ic-s"
        tabIndex={-1}
      >
        <h2 className="big" id="ic-h">
          {T.confirmH}
        </h2>
        <p className="dsub" id="ic-s">
          {title}
        </p>
        <ul className="ip-tiles three">
          <li className="stat c">
            <b>{n.create}</b>
            <span>{T.created}</span>
          </li>
          <li className="stat u">
            <b>{n.update}</b>
            <span>{T.updated}</span>
          </li>
          <li className="stat d">
            <b>0</b>
            <span>{T.deleted}</span>
          </li>
        </ul>
        <ul className="ip-sure">
          {n.left > 0 && (
            <li className="info">
              <IconInfo size={16} />
              {T.leftOutCount(n.left)}
            </li>
          )}
          <li className="ok">
            <IconCheck size={16} />
            {T.sureRevision}
          </li>
          <li className="ok">
            <IconCheck size={16} />
            {T.sureLogged}
          </li>
          <li className="warn">
            <IconWarn size={16} />
            {T.sureStale}
          </li>
        </ul>
        <div className="form-actions">
          <button type="button" className="btn" onClick={onCancel} disabled={busy}>
            {T.cancel}
          </button>
          <button type="button" className="btn primary" onClick={onApply} disabled={busy} data-autofocus>
            {busy ? T.applying : T.importNow(n.changes)}
          </button>
        </div>
      </div>
    </div>
  );
}

function Expiry({ plan, onOver }: { plan: ImportPlan; onOver: () => void }) {
  const { t } = useI18n();
  const { left, over } = useCountdown(plan.expiresAt);
  useEffect(() => {
    if (over) onOver();
  }, [over, onOver]);
  if (over) return null;
  return (
    <span className="ip-exp">
      <IconClock size={14} />
      {t.imports.expiresIn(left)}
    </span>
  );
}

/**
 * The import card: choose the source and the files, run the dry run, look at the plan and leave lines out, confirm,
 * and see the result. The exchange format keeps its own import.
 */
export function ImportPanel() {
  const { t } = useI18n();
  const T = t.imports;
  const { isAdmin } = useAuth();
  const { model: m, reload } = useLandscape();
  const input = useRef<HTMLInputElement>(null);
  const [source, setSource] = useState<SourceId>('backstage');
  const [files, setFiles] = useState<File[]>([]);
  const [over, setOver] = useState(false);
  const [opts, setOpts] = useState<Pick<ImportOptions, 'applicationId' | 'domainId' | 'teamId'>>({});
  const [phase, setPhase] = useState<Phase>('source');
  const [plan, setPlan] = useState<ImportPlan | null>(null);
  const [skipped, setSkipped] = useState<Set<number>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState<Stale | null>(null);
  const [confirming, setConfirming] = useState(false);
  const [applying, setApplying] = useState(false);
  const [showWritten, setShowWritten] = useState(false);

  const native = source === 'native';
  const name = T.sources[source].name;
  const total = files.reduce((s, f) => s + f.size, 0);

  const message = (e: unknown) =>
    e instanceof ApiError
      ? e.status === 0
        ? t.common.networkError
        : e.status === 413
          ? T.tooBig
          : e.detail || e.message
      : t.common.genericError;

  const addFiles = (list: FileList | null | undefined) => {
    // Copied now: the input is cleared right after, which empties its live FileList.
    const added = [...(list ?? [])];
    if (!added.length) return;
    setError(null);
    setFiles((cur) => {
      const names = new Set(cur.map((f) => f.name));
      return [...cur, ...added.filter((f) => !names.has(f.name))];
    });
  };

  const runDry = async () => {
    if (!files.length) return;
    if (total > MAX_UPLOAD_BYTES) {
      setError(T.tooBig);
      return;
    }
    setPhase('planning');
    setError(null);
    setStale(null);
    setShowWritten(false);
    try {
      const p = await api.planImport(files, {
        format: native ? undefined : (source as ImportOptions['format']),
        ...opts,
      });
      setPlan(p);
      setSkipped(new Set());
      setPhase('preview');
    } catch (e) {
      setError(message(e));
      setPhase('source');
    }
  };

  const apply = async () => {
    if (!plan) return;
    setApplying(true);
    try {
      const p = await api.applyImport(
        plan.id,
        [...skipped].sort((a, b) => a - b),
      );
      setPlan(p);
      setConfirming(false);
      setPhase('done');
      reload();
    } catch (e) {
      setConfirming(false);
      if (e instanceof ApiError && e.status === 410) setStale({ kind: 'expired' });
      else if (e instanceof ApiError && e.status === 409)
        setStale({
          kind: /catalogue changed|changed since|no longer/i.test(e.detail ?? '') ? 'changed' : 'rejected',
          detail: e.detail,
        });
      else setError(message(e));
    } finally {
      setApplying(false);
    }
  };

  const restart = () => {
    setPhase('source');
    setPlan(null);
    setStale(null);
    setError(null);
    setFiles([]);
  };

  const expire = useCallback(() => setStale((s) => s ?? { kind: 'expired' }), []);

  const toggle = (index: number, keep: boolean) =>
    plan && setSkipped((s) => (keep ? takeBack(plan.entries, s, index) : leaveOut(plan.entries, s, index)));

  const filesText = (p: ImportPlan) =>
    p.files.length <= 1 ? (p.files[0]?.name ?? '') : T.moreFiles(p.files[0].name, p.files.length - 1);
  const title = plan ? `${filesText(plan)} · ${name}` : name;

  if (!isAdmin)
    return (
      <section className="box" aria-labelledby="imp-h">
        <h2 className="big" id="imp-h">
          {T.importH}
        </h2>
        <div className="banner info" role="note">
          <IconLock size={16} />
          <div className="bt">
            <b>{T.adminsOnlyH}</b> {T.adminsOnlyText}
            <div className="page-actions ip-acts">
              <a className="btn sm" href={api.importTemplateUrl} download>
                <IconDownload size={14} />
                {T.template}
              </a>
            </div>
          </div>
        </div>
      </section>
    );

  // ---------- preview, result ----------
  if (plan && (phase === 'preview' || phase === 'done')) {
    const n = tally(plan, phase === 'done' ? new Set(plan.skipped) : skipped);
    const done = phase === 'done';
    const locked = done || !plan.canApply || !!stale;
    const report = () =>
      saveText(
        planCsv(plan, done ? new Set(plan.skipped) : skipped, T.csv),
        `noosphere-import-${plan.id.slice(0, 8)}.csv`,
      );
    return (
      <section className="box tb ip" aria-labelledby="imp-h">
        <div className="tb-h">
          <div>
            <h2 className="big" id="imp-h">
              {T.previewH(name)}
            </h2>
            <p>{T.previewSub(filesText(plan), plan.entries.length)}</p>
          </div>
          {!done && (
            <button type="button" className="btn sm" onClick={() => setPhase('source')}>
              <IconUpload size={14} />
              {T.changeFile}
            </button>
          )}
        </div>
        <div className="ip-body">
          <Steps at={done ? 3 : 1} />
          {done && (
            <div className="banner ok" role="status">
              <IconCheck />
              <div className="bt">
                <b>{T.done(n.create, n.update, n.unchanged, n.left)}</b> {T.doneText}
                <div className="page-actions ip-acts">
                  <button
                    type="button"
                    className="btn sm"
                    aria-expanded={showWritten}
                    onClick={() => setShowWritten((x) => !x)}
                  >
                    <IconEye size={14} />
                    {showWritten ? T.hideChanges : T.seeChanges}
                  </button>
                  <button type="button" className="btn sm" onClick={report}>
                    <IconDownload size={14} />
                    {T.report}
                  </button>
                  <button type="button" className="btn sm" onClick={restart}>
                    {T.newImport}
                  </button>
                </div>
              </div>
            </div>
          )}
          {done && showWritten && (
            <div className="ip-written">
              <h3>{T.written}</h3>
              <ul>
                {plan.entries
                  .filter((e) => e.action !== 'unchanged' && !plan.skipped.includes(e.index))
                  .map((e) => (
                    <li key={e.index}>
                      <span className={'pchip ' + (e.action === 'create' ? 'ok' : 'info')}>{T.action[e.action]}</span>
                      <RouterLink to={recordHref(e.kind, e.id)}>{e.name}</RouterLink>
                      <small>{t.review.kind[e.kind]}</small>
                    </li>
                  ))}
              </ul>
            </div>
          )}
          {!done && stale && (
            <div className="banner warn" role="alert">
              <IconWarn />
              <div className="bt">
                <b>{stale.kind === 'expired' ? T.expiredH : stale.kind === 'changed' ? T.changedH : T.rejectedH}</b>{' '}
                {stale.kind === 'expired' ? T.expiredText : stale.kind === 'changed' ? T.changedText : stale.detail}
                <div className="page-actions ip-acts">
                  <button type="button" className="btn sm primary" onClick={() => void runDry()}>
                    {T.again}
                  </button>
                </div>
              </div>
            </div>
          )}
          {!done && !stale && plan.counts.errors > 0 && (
            <div className="banner danger" role="alert">
              <IconWarn />
              <span className="bt">
                <b>{T.hasErrors(plan.counts.errors)}</b> {T.hasErrorsText}
              </span>
            </div>
          )}
          {!done && error && (
            <div className="banner danger" role="alert">
              <IconWarn />
              <span className="bt">{error}</span>
            </div>
          )}
        </div>
        {!done && <PlanPreview plan={plan} m={m} skipped={skipped} locked={locked} onToggle={toggle} />}
        {!done && (
          <div className="tb-f">
            <span className="ip-foot">
              {plan.canApply && !stale ? (
                <>
                  {T.footNote}
                  <Expiry plan={plan} onOver={expire} />
                </>
              ) : (
                !stale && plan.counts.errors > 0 && T.hasErrorsText
              )}
            </span>
            <span className="page-actions">
              <button type="button" className="btn" onClick={report}>
                <IconDownload size={14} />
                {T.report}
              </button>
              <button
                type="button"
                className="btn primary"
                disabled={locked || n.changes === 0}
                title={n.changes === 0 && plan.canApply ? T.nothingKept : undefined}
                onClick={() => setConfirming(true)}
              >
                {T.importN(n.changes)}
              </button>
            </span>
          </div>
        )}
        {confirming && (
          <ConfirmDialog
            plan={plan}
            skipped={skipped}
            title={title}
            busy={applying}
            onCancel={() => setConfirming(false)}
            onApply={() => void apply()}
          />
        )}
      </section>
    );
  }

  // ---------- source ----------
  const apps = m
    ? m.appIds.flatMap((id) => {
        const a = m.apps.get(id);
        return a && !isRestrictedApp(a) ? [{ id: a.id, name: a.name }] : [];
      })
    : [];
  const teams = m ? [...m.teams.values()].sort((a, b) => a.name.localeCompare(b.name)) : [];
  const select = (
    id: string,
    label: string,
    hint: string,
    key: keyof typeof opts,
    items: { id: string; name: string }[],
    empty: string,
  ) => (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {m ? (
        <select
          id={id}
          className="select"
          value={opts[key] ?? ''}
          aria-describedby={id + '-h'}
          onChange={(e) => setOpts((o) => ({ ...o, [key]: e.target.value || undefined }))}
        >
          <option value="">{empty}</option>
          {items.map((x) => (
            <option key={x.id} value={x.id}>
              {x.name}
            </option>
          ))}
        </select>
      ) : (
        <span className="sk block" style={{ height: 40 }} aria-hidden="true" />
      )}
      <small id={id + '-h'}>{hint}</small>
    </div>
  );
  const planning = phase === 'planning';
  return (
    <section className="box tb ip" aria-labelledby="imp-h" aria-busy={planning}>
      <div className="tb-h">
        <div>
          <h2 className="big" id="imp-h">
            {planning ? T.previewH(name) : T.importH}
          </h2>
          <p>{planning ? T.checking : T.importText}</p>
        </div>
      </div>
      {planning ? (
        <>
          <div className="ip-body">
            <Steps at={1} />
          </div>
          <PreviewSkeleton />
        </>
      ) : (
        <div className="ip-body ip-src-step">
          <Steps at={0} />
          <div className="rcards ip-srcs" role="radiogroup" aria-label={T.sourceLabel}>
            {SOURCES.map((s) => {
              return (
                <label key={s} className="rcard">
                  <input
                    type="radio"
                    name="ip-source"
                    value={s}
                    checked={source === s}
                    onChange={() => {
                      setSource(s);
                      setError(null);
                    }}
                  />
                  <b>
                    <span className="ip-si" aria-hidden="true">
                      {ICONS[s]}
                    </span>
                    {T.sources[s].name}
                  </b>
                  <small>{T.sources[s].hint}</small>
                </label>
              );
            })}
          </div>
          <div className="banner info" role="note">
            <IconInfo />
            <span className="bt">
              <b>{name}</b> {T.sources[source].info}
            </span>
          </div>
          {native ? (
            <NativeImport />
          ) : (
            <>
              {(source === 'openapi' || source === 'asyncapi' || source === 'adr') && (
                <div className="ip-grid">
                  {select(
                    'ip-app',
                    source === 'adr' ? T.providerAdrLabel : T.providerLabel,
                    source === 'adr' ? T.providerAdrHint : T.providerHint,
                    'applicationId',
                    apps,
                    T.fromFile,
                  )}
                  {source === 'openapi' && (
                    <p className="ip-static">
                      <IconCheck size={15} />
                      {T.noConsumers}
                    </p>
                  )}
                </div>
              )}
              {source === 'excel' && (
                <div>
                  <a className="btn sm" href={api.importTemplateUrl} download>
                    <IconDownload size={14} />
                    {T.template}
                  </a>
                </div>
              )}
              <div
                className={'drop' + (over ? ' over' : '')}
                onDragOver={(e) => {
                  e.preventDefault();
                  setOver(true);
                }}
                onDragLeave={() => setOver(false)}
                onDrop={(e) => {
                  e.preventDefault();
                  setOver(false);
                  addFiles(e.dataTransfer.files);
                }}
              >
                <IconUpload size={20} />
                <button type="button" className="btn sm" onClick={() => input.current?.click()}>
                  {T.choose}
                </button>
                <span className="muted">{T.drop}</span>
                <input
                  ref={input}
                  type="file"
                  multiple
                  className="sr"
                  accept={ACCEPT[source]}
                  aria-label={T.filesLabel}
                  onChange={(e) => {
                    addFiles(e.target.files);
                    e.target.value = '';
                  }}
                />
              </div>
              {files.length > 0 && (
                <ul className="ip-files" aria-label={T.filesLabel}>
                  {files.map((f) => (
                    <li key={f.name}>
                      <IconDoc size={18} />
                      <span className="ip-fn">
                        <b>{f.name}</b>
                        <small>{T.size(kb(f.size))}</small>
                      </span>
                      <button
                        type="button"
                        className="iconbtn"
                        aria-label={T.remove(f.name)}
                        onClick={() => setFiles((cur) => cur.filter((x) => x !== f))}
                      >
                        <IconX size={14} />
                      </button>
                    </li>
                  ))}
                </ul>
              )}
              <details className="ip-more">
                <summary>
                  <IconChevron size={14} />
                  {T.more}
                </summary>
                <div className="ip-grid">
                  {select('ip-domain', T.domainLabel, T.domainHint, 'domainId', m?.domains ?? [], T.none)}
                  {select('ip-team', T.teamLabel, T.teamHint, 'teamId', teams, T.none)}
                </div>
              </details>
              {error && (
                <div className="banner danger" role="alert">
                  <IconWarn />
                  <span className="bt">
                    <b>{T.loadError}</b> {error}
                  </span>
                </div>
              )}
              <div className="ip-go">
                <span className="muted2">{T.limits}</span>
                <button type="button" className="btn primary" disabled={!files.length} onClick={() => void runDry()}>
                  <IconEye size={15} />
                  {T.dryRun}
                </button>
              </div>
            </>
          )}
        </div>
      )}
    </section>
  );
}
