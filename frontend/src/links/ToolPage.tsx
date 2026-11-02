import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Link as RouterLink, useParams } from 'react-router-dom';
import { api, ApiError } from '../api/client';
import type {
  ApiKey,
  ApiKeyScope,
  KeyLifetime,
  LinkExchange,
  Webhook,
  WebhookDelivery,
  WebhookEvent,
} from '../api/types';
import { ActivitySkeleton, clock, logTime } from '../agents/bits';
import { useDemo } from '../demo/context';
import { useI18n } from '../i18n';
import type { Dict } from '../i18n/en';
import { PageShell } from '../records/common';
import {
  IconBack,
  IconCheck,
  IconEdit,
  IconInfo,
  IconKey,
  IconLock,
  IconNo,
  IconRotate,
  IconSend,
  IconTrash,
  IconWarn,
  IconWebhook,
} from '../ui/icons';
import { useToast } from '../ui/Toast';
import { useGo } from '../ui/useGo';
import { longDate, Switch } from './bits';
import { AdminOnly, usePeopleNames } from './LinksPage';
import {
  errorText,
  EventChoices,
  LifetimeSelect,
  ScopeChoices,
  scopeList,
  ShownOnceDialog,
  type Shown,
} from './ToolDialogs';
import { HookChip, KeyStateChip, ToolTile, useTools } from './ToolsSection';
import type { Tool } from './tools';

const PAGE = 50;
/** Attempts per delivery: the first one and five retries. */
export const MAX_ATTEMPTS = 6;

/* ---------- exchange lines ---------- */

const METHOD: Record<string, string> = { catalog: 'GET', deployments: 'POST' };

/** A line of a tool's exchange log in plain words. */
export function toolExchangeSentence(
  t: Dict,
  x: LinkExchange,
  tool: string,
): { who: string | null; text: string; reason?: string } {
  const e = t.links.tools.ex;
  if (x.reason === 'webhook-paused') return { who: null, text: e.paused };
  if (x.result === 'refused')
    return { who: null, text: e.refused, reason: x.reason ? (e.reason[x.reason] ?? x.reason) : '—' };
  if (x.operation === 'webhook') return { who: null, text: x.result === 'ok' ? e.hookOk : e.hookFailed };
  const text = x.operation === 'catalog' ? e.in.catalog : x.operation === 'deployments' ? e.in.deployments : e.in.other;
  return { who: tool, text };
}

export function toolExchangeChip(t: Dict, x: LinkExchange): { cls: string; label: string; k: string } {
  const c = t.links.tools.ex.chip;
  if (x.reason === 'webhook-paused') return { cls: 'danger', label: c.paused, k: 'refused' };
  if (x.result === 'refused') return { cls: 'danger', label: c.refused, k: 'refused' };
  if (x.result === 'failed') return { cls: 'warn', label: c.failed, k: 'quota' };
  if (x.operation === 'webhook') return { cls: 'ok', label: c.delivered, k: 'read' };
  return { cls: 'ok', label: c.done, k: 'read' };
}

/** The technical line: method and path, status, reason and the server's detail. */
export function toolExchangeTech(x: LinkExchange, hookUrl?: string | null): string {
  const parts = [
    x.operation === 'webhook'
      ? `POST ${hookUrl ?? 'webhook'}`
      : `${METHOD[x.operation] ?? 'POST'} /api/integrations/${x.operation}`,
  ];
  if (x.status != null) parts.push(String(x.status) + (x.reason ? ` ${x.reason}` : ''));
  else if (x.reason) parts.push(x.reason);
  if (x.detail) parts.push(x.detail);
  return parts.join(' · ');
}

function ToolExchanges({ tool, reloadKey }: { tool: Tool; reloadKey: string }) {
  const { t, lang } = useI18n();
  const p = t.links.tools.page;
  const keyIds = tool.keys.map((k) => k.id).slice(0, 19);
  const hookIds = tool.webhooks.map((w) => w.id).slice(0, 20 - keyIds.length);
  const ids = [...keyIds, '|', ...hookIds].join(',');
  const [cursors, setCursors] = useState<(string | null)[]>([null]);
  const [items, setItems] = useState<LinkExchange[] | null>(null);
  const [next, setNext] = useState<string | null>(null);
  const [error, setError] = useState(false);
  const cursor = cursors[cursors.length - 1];
  useEffect(() => setCursors([null]), [ids, reloadKey]);
  useEffect(() => {
    let alive = true;
    setItems(null);
    setError(false);
    if (keyIds.length + hookIds.length === 0) {
      setItems([]);
      return;
    }
    api
      .toolExchanges(keyIds, hookIds, cursor, PAGE)
      .then((r) => {
        if (!alive) return;
        setItems(r.items);
        setNext(r.nextCursor ?? null);
      })
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
    // The ids are compared through `ids`.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ids, cursor, reloadKey]);
  return (
    <section className="box tb" aria-labelledby="tx-h">
      <div className="tb-h">
        <div>
          <h2 id="tx-h">{p.exchanges}</h2>
          <p>{p.exchangesHint}</p>
        </div>
      </div>
      {error ? (
        <p className="log-empty">{t.common.genericError}</p>
      ) : !items ? (
        <ActivitySkeleton n={4} />
      ) : items.length === 0 ? (
        <p className="log-empty">{p.noLines}</p>
      ) : (
        <ul className="alog">
          {items.map((x) => {
            const s = toolExchangeSentence(t, x, tool.name);
            const chip = toolExchangeChip(t, x);
            return (
              <li key={x.id} data-k={chip.k}>
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
                  <span className="rs mono">
                    {toolExchangeTech(x, tool.webhooks.find((w) => w.id === x.webhookId)?.url)}
                  </span>
                </span>
                <span className={'pchip ' + chip.cls}>{chip.label}</span>
              </li>
            );
          })}
        </ul>
      )}
      <div className="tb-f">
        <span>{items && items.length > 0 ? p.lines(items.length) : ' '}</span>
        <span className="pager">
          <button
            type="button"
            className="btn sm"
            disabled={cursors.length < 2}
            onClick={() => setCursors((c) => (c.length > 1 ? c.slice(0, -1) : c))}
          >
            {p.newer}
          </button>
          <button
            type="button"
            className="btn sm"
            disabled={!next}
            onClick={() => next && setCursors((c) => [...c, next])}
          >
            {t.links.detail.older}
          </button>
        </span>
      </div>
    </section>
  );
}

/* ---------- webhook ---------- */

function DeliveryResult({ d }: { d: WebhookDelivery }) {
  const { t, lang } = useI18n();
  const p = t.links.tools.page;
  const status = d.responseStatus != null ? String(d.responseStatus) : p.noAnswer;
  const sub: string[] = [];
  if (d.state === 'delivered') {
    if (d.attempts > 1) sub.push(p.thenDelivered(d.attempts));
  } else {
    if (d.state === 'pending') {
      sub.push(p.attempt(d.attempts, MAX_ATTEMPTS));
      if (d.nextAttemptAt) sub.push(p.nextAt(clock(d.nextAttemptAt, lang)));
    } else sub.push(d.test ? p.attempt(d.attempts, 1) : p.gaveUp);
  }
  return (
    <span className="tl-res">
      <span className={'pchip ' + (d.state === 'delivered' ? 'ok' : d.state === 'pending' ? 'warn' : 'danger')}>
        {status}
      </span>
      {sub.length > 0 && <span className="muted2"> {sub.join(' · ')}</span>}
      {d.error && d.state !== 'delivered' && <span className="am2 tl-err">{d.error}</span>}
    </span>
  );
}

function Deliveries({ hook, reloadKey, onRetried }: { hook: Webhook; reloadKey: string; onRetried: () => void }) {
  const { t, lang } = useI18n();
  const toast = useToast();
  const p = t.links.tools.page;
  const [items, setItems] = useState<WebhookDelivery[] | null>(null);
  const [next, setNext] = useState<string | null>(null);
  const [error, setError] = useState(false);
  const [busy, setBusy] = useState<number | null>(null);
  useEffect(() => {
    let alive = true;
    setError(false);
    api
      .webhookDeliveries(hook.id, null, 10)
      .then((r) => {
        if (!alive) return;
        setItems(r.items);
        setNext(r.nextCursor ?? null);
      })
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, [hook.id, reloadKey]);
  const more = async () => {
    if (!next) return;
    try {
      const r = await api.webhookDeliveries(hook.id, next, 20);
      setItems((xs) => [...(xs ?? []), ...r.items]);
      setNext(r.nextCursor ?? null);
    } catch {
      toast(t.common.genericError);
    }
  };
  const retry = async (d: WebhookDelivery) => {
    setBusy(d.id);
    try {
      const r = await api.retryDelivery(hook.id, d.id);
      setItems((xs) => xs?.map((x) => (x.id === r.id ? r : x)) ?? null);
      onRetried();
    } catch (e) {
      toast(errorText(e, t.common.genericError, t.common.networkError));
    } finally {
      setBusy(null);
    }
  };
  const canRetry = hook.enabled && !hook.pausedAt;
  const event = (d: WebhookDelivery) => (d.test ? p.testTag : d.eventType);
  return (
    <div className="tl-deliv">
      {error ? (
        <p className="log-empty">{t.common.genericError}</p>
      ) : !items ? (
        <ActivitySkeleton n={3} />
      ) : items.length === 0 ? (
        <p className="log-empty">{p.noDeliveries}</p>
      ) : (
        <table className="table rtable atable tl-dtable">
          <thead>
            <tr>
              <th scope="col">{p.deliveries}</th>
              <th scope="col">{p.colEvent}</th>
              <th scope="col">{p.colResult}</th>
              <th scope="col">
                <span className="sr">{t.links.list.actions}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((d) => (
              <tr key={d.id}>
                <td data-l={p.colWhen} className="lead">
                  <span title={new Date(d.createdAt).toLocaleString(lang === 'fr' ? 'fr-FR' : 'en-GB')}>
                    {logTime(t, lang, d.lastAttemptAt ?? d.createdAt)}
                  </span>
                </td>
                <td data-l={p.colEvent}>
                  <span className="mono">{event(d)}</span>
                </td>
                <td data-l={p.colResult}>
                  <DeliveryResult d={d} />
                </td>
                <td className="acts-td">
                  {d.state !== 'delivered' && canRetry && (
                    <button type="button" className="btn sm" disabled={busy !== null} onClick={() => void retry(d)}>
                      {p.retry}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {next && (
        <div className="tl-more">
          <button type="button" className="btn sm" onClick={() => void more()}>
            {p.older}
          </button>
        </div>
      )}
    </div>
  );
}

function HookForm({
  initial,
  submitLabel,
  onSubmit,
  onCancel,
}: {
  initial: { url: string; events: WebhookEvent[] };
  submitLabel: string;
  onSubmit: (v: { url: string; events: WebhookEvent[] }) => Promise<string | null>;
  onCancel?: () => void;
}) {
  const { t } = useI18n();
  const c = t.links.tools.create;
  const p = t.links.tools.page;
  const [url, setUrl] = useState(initial.url);
  const [events, setEvents] = useState<WebhookEvent[]>(initial.events);
  const [err, setErr] = useState<{ url?: string; events?: string; form?: string }>({});
  const [busy, setBusy] = useState(false);
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const errs: typeof err = {};
    if (!/^https?:\/\/[^/\s]+/i.test(url.trim())) errs.url = c.urlRequired;
    if (events.length === 0) errs.events = c.eventsRequired;
    setErr(errs);
    if (errs.url || errs.events) return;
    setBusy(true);
    const urlError = await onSubmit({ url: url.trim(), events });
    setBusy(false);
    if (urlError) setErr({ url: urlError });
  };
  return (
    <form className="tl-form" onSubmit={(e) => void submit(e)} noValidate>
      <div className="field">
        <label htmlFor="hf-url">{p.address}</label>
        <input
          id="hf-url"
          className="input mono"
          type="url"
          inputMode="url"
          value={url}
          aria-invalid={!!err.url || undefined}
          aria-describedby={err.url ? 'hf-url-e' : undefined}
          onChange={(e) => setUrl(e.target.value)}
        />
        {err.url && (
          <span className="err" id="hf-url-e">
            <IconWarn size={13} />
            {err.url}
          </span>
        )}
      </div>
      <span className="lbl2 tl-l" id="hf-ev">
        {p.events}
      </span>
      <EventChoices value={events} onChange={setEvents} labelId="hf-ev" />
      {err.events && (
        <span className="lk-err" role="alert">
          <IconWarn size={13} />
          {err.events}
        </span>
      )}
      <div className="set-f">
        {onCancel && (
          <button type="button" className="btn" onClick={onCancel}>
            {p.cancel}
          </button>
        )}
        <button type="submit" className="btn primary" disabled={busy}>
          {busy ? t.common.saving : submitLabel}
        </button>
      </div>
    </form>
  );
}

function WebhookBox({
  tool,
  hook,
  onChanged,
  onShown,
}: {
  tool: Tool;
  hook: Webhook;
  onChanged: () => void;
  onShown: (s: Shown) => void;
}) {
  const { t, lang } = useI18n();
  const toast = useToast();
  const p = t.links.tools.page;
  const o = t.links.tools.once;
  const [editing, setEditing] = useState(false);
  const [confirm, setConfirm] = useState<null | 'secret' | 'delete'>(null);
  const [busy, setBusy] = useState<null | 'test' | 'switch' | 'secret' | 'delete'>(null);
  const [test, setTest] = useState<WebhookDelivery | null>(null);
  const [dKey, setDKey] = useState(0);
  const fail = (e: unknown) => toast(errorText(e, t.common.genericError, t.common.networkError));
  const update = async (patch: { url?: string; events?: string[]; enabled?: boolean }) =>
    api.updateWebhook(hook.id, {
      name: hook.name,
      url: patch.url ?? hook.url,
      events: patch.events ?? hook.events,
      enabled: patch.enabled,
      version: hook.version,
    });
  const run = async (what: 'test' | 'switch' | 'secret' | 'delete', call: () => Promise<void>) => {
    setBusy(what);
    try {
      await call();
    } catch (e) {
      fail(e);
    } finally {
      setBusy(null);
    }
  };
  const status = hook.pausedAt ? 'paused' : null;
  const secretPreview = 'whsec_' + '•'.repeat(16);
  return (
    <section className="box tb" aria-labelledby="wh-h">
      <div className="tb-h tl-hh">
        <div>
          <h2 id="wh-h">{p.hookTitle}</h2>
          <p>{p.hookIntro}</p>
        </div>
        <span className="tl-hacts">
          <HookChip w={hook} />
          <button
            type="button"
            className="btn sm"
            disabled={busy !== null || !!status}
            onClick={() =>
              void run('test', async () => {
                setTest(await api.testWebhook(hook.id));
                setDKey((k) => k + 1);
                onChanged();
              })
            }
          >
            <IconSend size={13} />
            {busy === 'test' ? p.testing : p.test}
          </button>
          <Switch
            label={p.hookSwitch}
            checked={hook.enabled && !hook.pausedAt}
            disabled={busy !== null}
            onChange={(v) =>
              void run('switch', async () => {
                await update({ enabled: v });
                toast(p.saved);
                onChanged();
              })
            }
          />
        </span>
      </div>
      <div className="tl-body">
        {hook.pausedAt && (
          <div className="banner danger" role="status">
            <IconWarn />
            <div className="bt">
              <b>{p.paused(longDate(hook.pausedAt, lang) + ' ' + clock(hook.pausedAt, lang))}</b> {p.pausedText}
              <div className="page-actions" style={{ marginTop: 10 }}>
                <button
                  type="button"
                  className="btn"
                  disabled={busy !== null}
                  onClick={() =>
                    void run('switch', async () => {
                      await update({ enabled: true });
                      toast(p.saved);
                      onChanged();
                    })
                  }
                >
                  {p.resume}
                </button>
              </div>
            </div>
          </div>
        )}
        {!hook.pausedAt && hook.failingSince && (
          <div className="banner warn" role="status">
            <IconWarn />
            <div className="bt">
              <b>{p.failing(longDate(hook.failingSince, lang) + ' ' + clock(hook.failingSince, lang))}</b>{' '}
              {p.failingText}
            </div>
          </div>
        )}
        {editing ? (
          <HookForm
            initial={{ url: hook.url, events: hook.events as WebhookEvent[] }}
            submitLabel={p.save}
            onCancel={() => setEditing(false)}
            onSubmit={async (v) => {
              try {
                await update({ url: v.url, events: v.events });
                toast(p.saved);
                setEditing(false);
                onChanged();
                return null;
              } catch (e) {
                const fe = e instanceof ApiError ? e.fieldErrors : {};
                if (fe.url?.[0]) return fe.url[0];
                fail(e);
                return null;
              }
            }}
          />
        ) : (
          <dl className="setgrid one lk-dl tl-dl">
            <div>
              <dt>{p.address}</dt>
              <dd className="tl-addr">
                <span className="mono">{hook.url}</span>
                <button type="button" className="btn sm ghost" onClick={() => setEditing(true)}>
                  <IconEdit size={13} />
                  {p.edit}
                </button>
              </dd>
            </div>
            <div>
              <dt>{p.events}</dt>
              <dd>
                <span className="chip-row">
                  {hook.events.map((ev) => (
                    <span key={ev} className="pchip mono">
                      {ev}
                    </span>
                  ))}
                </span>
              </dd>
            </div>
            <div>
              <dt>{p.secret}</dt>
              <dd className="tl-addr">
                <span className="mono">{secretPreview}</span>
                <button
                  type="button"
                  className="btn sm"
                  aria-expanded={confirm === 'secret'}
                  disabled={busy !== null}
                  onClick={() => setConfirm('secret')}
                >
                  <IconRotate size={13} />
                  {p.regenerate}
                </button>
              </dd>
            </div>
          </dl>
        )}
        {confirm === 'secret' && (
          <div className="inl-confirm" role="alert">
            <span>{p.regenerateQ}</span>
            <button
              type="button"
              className="btn sm danger-solid"
              disabled={busy !== null}
              onClick={() =>
                void run('secret', async () => {
                  const r = await api.regenerateWebhookSecret(hook.id);
                  setConfirm(null);
                  onShown({ title: o.secretTitle, sub: tool.name, secret: r.secret });
                  onChanged();
                })
              }
            >
              {p.regenerate}
            </button>
            <button type="button" className="btn sm" onClick={() => setConfirm(null)}>
              {p.cancel}
            </button>
          </div>
        )}
        {!editing && (
          <div className="tl-sig">
            <small className="muted2">{p.secretHint}</small>
            <pre className="codeblock tl-code">
              {
                'X-Noosphere-Signature: t=<unix seconds>,v1=<hex>\nX-Noosphere-Delivery: <event id>\n# v1 = HMAC-SHA256(secret, t + "." + body)'
              }
            </pre>
          </div>
        )}
        {test && (
          <div className={'banner ' + (test.state === 'delivered' ? 'ok' : 'danger')} role="status">
            {test.state === 'delivered' ? <IconCheck /> : <IconWarn />}
            <div className="bt">
              <b>{test.state === 'delivered' ? p.testOk(test.responseStatus ?? 200) : p.testFail}</b>{' '}
              {test.state === 'delivered' ? p.testOkText : test.error}
            </div>
          </div>
        )}
      </div>
      <Deliveries hook={hook} reloadKey={`${dKey}-${hook.version}`} onRetried={onChanged} />
      <div className="tb-f tl-foot">
        <span>{p.retries}</span>
        {confirm === 'delete' ? (
          <span className="tl-inl" role="alert">
            {p.deleteHookQ}{' '}
            <button
              type="button"
              className="btn sm danger-solid"
              disabled={busy !== null}
              onClick={() =>
                void run('delete', async () => {
                  await api.deleteWebhook(hook.id);
                  toast(p.hookDeleted);
                  onChanged();
                })
              }
            >
              {p.deleteHook}
            </button>{' '}
            <button type="button" className="btn sm" onClick={() => setConfirm(null)}>
              {p.cancel}
            </button>
          </span>
        ) : (
          <button type="button" className="btn sm ghost" onClick={() => setConfirm('delete')}>
            <IconTrash size={13} />
            {p.deleteHook}
          </button>
        )}
      </div>
    </section>
  );
}

function NoWebhookBox({
  tool,
  onChanged,
  onShown,
}: {
  tool: Tool;
  onChanged: () => void;
  onShown: (s: Shown) => void;
}) {
  const { t } = useI18n();
  const p = t.links.tools.page;
  const o = t.links.tools.once;
  const toast = useToast();
  const [open, setOpen] = useState(false);
  return (
    <section className="box" aria-labelledby="nwh-h">
      <h2 id="nwh-h">{p.noHookTitle}</h2>
      <p className="muted2">{p.noHookText}</p>
      {open ? (
        <HookForm
          initial={{ url: 'https://', events: ['catalog.changed'] }}
          submitLabel={p.addHook}
          onCancel={() => setOpen(false)}
          onSubmit={async (v) => {
            try {
              const r = await api.createWebhook({ name: tool.name, url: v.url, events: v.events });
              setOpen(false);
              onShown({ title: o.hookTitle, sub: tool.name, secret: r.secret });
              onChanged();
              return null;
            } catch (e) {
              const fe = e instanceof ApiError ? e.fieldErrors : {};
              if (fe.url?.[0]) return fe.url[0];
              toast(errorText(e, t.common.genericError, t.common.networkError));
              return null;
            }
          }}
        />
      ) : (
        <div className="set-f tl-start">
          <button type="button" className="btn" onClick={() => setOpen(true)}>
            <IconWebhook />
            {p.addHook}
          </button>
        </div>
      )}
    </section>
  );
}

/* ---------- API key ---------- */

function KeyBox({ tool, onChanged, onShown }: { tool: Tool; onChanged: () => void; onShown: (s: Shown) => void }) {
  const { t, lang } = useI18n();
  const toast = useToast();
  const p = t.links.tools.page;
  const o = t.links.tools.once;
  const k = tool.key;
  const [busy, setBusy] = useState<null | 'rotate' | 'revoke' | 'create'>(null);
  const [revoking, setRevoking] = useState(false);
  const [creating, setCreating] = useState(false);
  const [scopes, setScopes] = useState<ApiKeyScope[]>(['deployments:write']);
  const [days, setDays] = useState<KeyLifetime>(365);
  const fail = (e: unknown) => toast(errorText(e, t.common.genericError, t.common.networkError));
  const run = async (what: 'rotate' | 'revoke' | 'create', call: () => Promise<void>) => {
    setBusy(what);
    try {
      await call();
    } catch (e) {
      fail(e);
    } finally {
      setBusy(null);
    }
  };
  const created = (title: string, r: { key: string; apiKey: ApiKey }, rotatedUntil?: string) =>
    onShown({
      title,
      sub: o.sub(tool.name, scopeList(r.apiKey.scopes), r.apiKey.expiresAt ? longDate(r.apiKey.expiresAt, lang) : '—'),
      key: { value: r.key, apiKey: r.apiKey, rotatedUntil },
    });
  if (!k)
    return (
      <section className="box" aria-labelledby="nk-h">
        <h2 id="nk-h">{p.noKeyTitle}</h2>
        <p className="muted2">{p.noKeyText}</p>
        {creating ? (
          <form
            className="tl-form"
            noValidate
            onSubmit={(e) => {
              e.preventDefault();
              if (scopes.length === 0) return;
              void run('create', async () => {
                const r = await api.createApiKey(tool.name, scopes, days);
                setCreating(false);
                created(o.keyTitle, r);
                onChanged();
              });
            }}
          >
            <span className="lbl2 tl-l" id="nk-sc">
              {t.links.tools.create.scopes}
            </span>
            <ScopeChoices value={scopes} onChange={setScopes} labelId="nk-sc" />
            <div className="field" style={{ marginTop: 12 }}>
              <label htmlFor="nk-exp">{t.links.tools.create.expires}</label>
              <LifetimeSelect id="nk-exp" value={days} onChange={setDays} />
            </div>
            <div className="set-f">
              <button type="button" className="btn" onClick={() => setCreating(false)}>
                {p.cancel}
              </button>
              <button type="submit" className="btn primary" disabled={busy !== null || scopes.length === 0}>
                <IconKey />
                {busy === 'create' ? t.links.tools.create.creating : t.links.tools.create.submitKey}
              </button>
            </div>
          </form>
        ) : (
          <div className="set-f tl-start">
            <button type="button" className="btn" onClick={() => setCreating(true)}>
              <IconKey />
              {p.createKey}
            </button>
          </div>
        )}
      </section>
    );
  return (
    <section className="box" aria-labelledby="ak-h">
      <h2 id="ak-h">{p.keyTitle}</h2>
      <dl className="setgrid one lk-dl tl-kv">
        <div>
          <dt>{p.key}</dt>
          <dd className="mono">{k.prefix}…</dd>
        </div>
        <div>
          <dt>{p.scopes}</dt>
          <dd>
            <span className="chip-row">
              {k.scopes.map((s) => (
                <span key={s} className="pchip mono">
                  {s}
                </span>
              ))}
            </span>
          </dd>
        </div>
        <div>
          <dt>{p.expires}</dt>
          <dd>
            {k.expiresAt ? longDate(k.expiresAt, lang) : '—'} <KeyStateChip k={k} />
          </dd>
        </div>
        <div>
          <dt>{p.lastUse}</dt>
          <dd>{k.lastUsedAt ? `${longDate(k.lastUsedAt, lang)} ${clock(k.lastUsedAt, lang)}` : t.links.tools.never}</dd>
        </div>
        <div>
          <dt>{p.rate}</dt>
          <dd>{p.rateValue}</dd>
        </div>
      </dl>
      {tool.previous?.expiresAt && (
        <p className="muted2 tl-p">
          <IconInfo size={14} />
          {p.previous(
            tool.previous.prefix,
            `${longDate(tool.previous.expiresAt, lang)} ${clock(tool.previous.expiresAt, lang)}`,
          )}
        </p>
      )}
      {revoking ? (
        <div className="inl-confirm" role="alert">
          <span>{p.revokeQ}</span>
          <button
            type="button"
            className="btn sm danger-solid"
            disabled={busy !== null}
            onClick={() =>
              void run('revoke', async () => {
                await api.revokeApiKey(k.id);
                setRevoking(false);
                toast(p.revoked);
                onChanged();
              })
            }
          >
            {p.revoke}
          </button>
          <button type="button" className="btn sm" onClick={() => setRevoking(false)}>
            {p.cancel}
          </button>
        </div>
      ) : (
        <div className="tl-kacts">
          <button
            type="button"
            className="btn sm"
            disabled={busy !== null}
            onClick={() =>
              void run('rotate', async () => {
                const r = await api.rotateApiKey(k.id);
                created(o.rotatedTitle, r, new Date(Date.now() + 24 * 3600_000).toISOString());
                onChanged();
              })
            }
          >
            <IconRotate size={13} />
            {busy === 'rotate' ? p.rotating : p.rotate}
          </button>
          <button
            type="button"
            className="btn sm danger"
            aria-expanded={revoking}
            disabled={busy !== null}
            onClick={() => setRevoking(true)}
          >
            <IconNo size={13} />
            {p.revoke}
          </button>
        </div>
      )}
      <p className="muted2 tl-p">{p.rotateHint}</p>
    </section>
  );
}

function SharedToolBox() {
  const { t } = useI18n();
  const p = t.links.tools.page;
  return (
    <section className="box" aria-labelledby="ts-h">
      <h2 id="ts-h">{p.sharedTitle}</h2>
      <ul className="can">
        <li className="ok">
          <IconCheck />
          <span>
            <b>{p.sharedRecords}</b>
            <small>{p.sharedRecordsText}</small>
          </span>
        </li>
        <li className="lock">
          <IconLock size={16} />
          <span>
            <b>{p.sharedHook}</b>
            <small>{p.sharedHookText}</small>
          </span>
        </li>
      </ul>
    </section>
  );
}

/* ---------- page ---------- */

function ToolSkeleton() {
  return (
    <div aria-busy="true">
      <span className="sk line" style={{ width: 60 }} />
      <span className="sk title" style={{ width: '40%', marginTop: 12 }} />
      <span className="sk line" style={{ width: '30%', marginTop: 8 }} />
      <div className="cgrid" style={{ marginTop: 20 }}>
        <div>
          <section className="box">
            <span className="sk line" style={{ width: 100 }} />
            <span className="sk block" style={{ height: 260, marginTop: 16 }} />
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

function ToolDetail() {
  const { t, lang } = useI18n();
  const toast = useToast();
  const { name = '' } = useParams();
  const { tick } = useDemo();
  const nameOf = usePeopleNames();
  const [reload, setReload] = useState(0);
  const { tools, error } = useTools(reload + tick);
  const [shown, setShown] = useState<Shown | null>(null);
  const [removing, setRemoving] = useState(false);
  const [busy, setBusy] = useState(false);
  const p = t.links.tools.page;
  const tool = tools?.find((x) => x.name === name) ?? null;
  const changed = () => setReload((x) => x + 1);
  // A tool with only a webhook disappears once the webhook is removed: back to the list.
  const nav = useGo();
  const seen = useRef(false);
  useEffect(() => {
    if (tool) seen.current = true;
    else if (tools && seen.current) nav('/admin/links');
  }, [tool, tools, nav]);

  const remove = async (x: Tool) => {
    setBusy(true);
    try {
      for (const k of x.keys.filter((k) => k.state === 'active')) await api.revokeApiKey(k.id);
      for (const w of x.webhooks) await api.deleteWebhook(w.id);
      toast(p.removed(x.name));
      setRemoving(false);
    } catch (e) {
      toast(errorText(e, t.common.genericError, t.common.networkError));
    } finally {
      setBusy(false);
      changed();
    }
  };

  return (
    <PageShell wide title={name || t.links.title}>
      {error || (tools && !tool) ? (
        <>
          <RouterLink className="backlink" to="/admin/links">
            <IconBack size={14} /> {t.links.back}
          </RouterLink>
          <div className="banner danger" role="alert">
            <IconWarn />
            <div className="bt">
              <b>{error ? t.links.tools.loadError : p.notFound}</b>
              <div className="page-actions" style={{ marginTop: 10 }}>
                <RouterLink className="btn" to="/admin/links">
                  {t.links.back}
                </RouterLink>
                {error && (
                  <button type="button" className="btn" onClick={changed}>
                    {t.common.retry}
                  </button>
                )}
              </div>
            </div>
          </div>
        </>
      ) : !tool ? (
        <ToolSkeleton />
      ) : (
        <>
          <RouterLink className="backlink" to="/admin/links">
            <IconBack size={14} /> {t.links.back}
          </RouterLink>
          <div className="page-h">
            <div className="ph-t lk-ph">
              <ToolTile tool={tool} big />
              <span>
                <h1>{tool.name}</h1>
                <p>{p.by(nameOf(tool.createdBy), longDate(tool.createdAt, lang))}</p>
              </span>
            </div>
            {!tool.removed && (
              <div className="page-actions">
                <button
                  type="button"
                  className="btn danger"
                  aria-expanded={removing}
                  disabled={busy}
                  onClick={() => setRemoving(true)}
                >
                  <IconTrash />
                  {p.remove}
                </button>
              </div>
            )}
          </div>
          {removing && !tool.removed && (
            <div className="inl-confirm lk-cut" role="alert">
              <span>
                <b>{p.removeQ(tool.name)}</b> {p.removeText}
              </span>
              <button type="button" className="btn sm danger-solid" disabled={busy} onClick={() => void remove(tool)}>
                {busy ? t.common.saving : p.remove}
              </button>
              <button type="button" className="btn sm" onClick={() => setRemoving(false)}>
                {p.cancel}
              </button>
            </div>
          )}
          {tool.removed && (
            <div className="banner" role="note">
              <IconInfo />
              <span className="bt">{p.removedBanner}</span>
            </div>
          )}
          <div className="cgrid lk-grid">
            <div>
              {tool.webhook ? (
                <WebhookBox tool={tool} hook={tool.webhook} onChanged={changed} onShown={setShown} />
              ) : (
                !tool.removed && <NoWebhookBox tool={tool} onChanged={changed} onShown={setShown} />
              )}
              <ToolExchanges tool={tool} reloadKey={String(reload)} />
            </div>
            <div>
              <KeyBox tool={tool} onChanged={changed} onShown={setShown} />
              <SharedToolBox />
            </div>
          </div>
        </>
      )}
      {shown && <ShownOnceDialog shown={shown} onDone={() => setShown(null)} />}
    </PageShell>
  );
}

/** One connected tool: its webhook and deliveries, its API key, and its exchanges. */
export function ToolPage() {
  return (
    <AdminOnly>
      <ToolDetail />
    </AdminOnly>
  );
}
