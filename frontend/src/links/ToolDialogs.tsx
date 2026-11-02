import { useRef, useState, type FormEvent } from 'react';
import { api, ApiError } from '../api/client';
import {
  API_KEY_SCOPES,
  KEY_LIFETIMES,
  WEBHOOK_EVENTS,
  type ApiKey,
  type ApiKeyScope,
  type KeyLifetime,
  type WebhookEvent,
} from '../api/types';
import { copyText } from '../agents/bits';
import { useI18n } from '../i18n';
import { IconCopy, IconKey, IconWarn, IconWebhook } from '../ui/icons';
import { useModal } from '../ui/modal';
import { useToast } from '../ui/Toast';
import { longDate } from './bits';
import { keySnippets } from './tools';

/** A secret shown once: the value, a copy button, and nothing that keeps it after the dialog closes. */
function SecretValue({ label, value, copyLabel }: { label: string; value: string; copyLabel: string }) {
  const { t } = useI18n();
  const toast = useToast();
  const o = t.links.tools.once;
  return (
    <div className="pcode-w tl-secret">
      <span className="lbl2">{label}</span>
      <div className="pcode">
        <span className="mono">{value}</span>
        <button
          type="button"
          className="btn sm ghost"
          aria-label={copyLabel}
          onClick={() => void copyText(value).then((ok) => toast(ok ? o.copied : o.copyFailed))}
        >
          <IconCopy />
          {o.copy}
        </button>
      </div>
    </div>
  );
}

type SnippetTab = 'curl' | 'github' | 'gitlab';

function Snippets({ scopes }: { scopes: string[] }) {
  const { t } = useI18n();
  const o = t.links.tools.once;
  const [tab, setTab] = useState<SnippetTab>('curl');
  const snips = keySnippets(location.origin, scopes, o);
  const tabs: [SnippetTab, string][] = [
    ['curl', 'curl'],
    ['github', 'GitHub Actions'],
    ['gitlab', 'GitLab CI'],
  ];
  const move = (dir: number) => {
    const i = tabs.findIndex(([k]) => k === tab);
    const next = tabs[(i + dir + tabs.length) % tabs.length][0];
    setTab(next);
    document.getElementById(`tl-snip-${next}`)?.focus();
  };
  return (
    <div className="tl-snips">
      <div className="seg sm wrap2 tl-tabs" role="tablist" aria-label={o.snippets}>
        {tabs.map(([k, label]) => (
          <button
            key={k}
            id={`tl-snip-${k}`}
            type="button"
            role="tab"
            aria-selected={tab === k}
            aria-controls="tl-snip-panel"
            tabIndex={tab === k ? 0 : -1}
            onClick={() => setTab(k)}
            onKeyDown={(e) => {
              if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
                e.preventDefault();
                move(e.key === 'ArrowRight' ? 1 : -1);
              }
            }}
          >
            {label}
          </button>
        ))}
      </div>
      <pre
        id="tl-snip-panel"
        role="tabpanel"
        aria-labelledby={`tl-snip-${tab}`}
        tabIndex={0}
        className="codeblock tl-code"
      >
        {snips[tab]}
      </pre>
    </div>
  );
}

export interface Shown {
  title: string;
  sub?: string;
  /** A key just created or rotated. */
  key?: { value: string; apiKey: ApiKey; rotatedUntil?: string | null };
  /** A webhook signing secret just created or regenerated. */
  secret?: string;
}

/** What is shown only once (an API key, a webhook secret), with examples for a key. */
export function ShownOnceDialog({ shown, onDone }: { shown: Shown; onDone: () => void }) {
  const { t, lang } = useI18n();
  const o = t.links.tools.once;
  const box = useRef<HTMLDivElement>(null);
  useModal(box, onDone);
  return (
    <div className="dlg-back" role="presentation">
      <div ref={box} className="dlg wide" role="dialog" aria-modal="true" aria-labelledby="so-h" tabIndex={-1}>
        <h2 id="so-h">{shown.title}</h2>
        {shown.sub && <p className="dsub">{shown.sub}</p>}
        <div className="banner warn" role="note">
          <IconWarn />
          <div className="bt">
            <b>{o.warn}</b> {shown.key ? o.warnKey : o.warnSecret}
          </div>
        </div>
        {shown.key && (
          <>
            {shown.key.rotatedUntil && (
              <p className="muted2 tl-p">
                {o.rotated(longDate(shown.key.rotatedUntil, lang) + ' ' + clockOf(shown.key.rotatedUntil, lang))}
              </p>
            )}
            <SecretValue label={o.key} value={shown.key.value} copyLabel={o.copyKey} />
            <Snippets scopes={shown.key.apiKey.scopes} />
          </>
        )}
        {shown.secret && <SecretValue label={o.secret} value={shown.secret} copyLabel={o.copySecret} />}
        <div className="form-actions tl-done">
          {shown.key && <span className="muted2">{o.prefix}</span>}
          <button type="button" className="btn primary" data-autofocus onClick={onDone}>
            {o.done}
          </button>
        </div>
      </div>
    </div>
  );
}

const clockOf = (iso: string, lang: string) =>
  new Intl.DateTimeFormat(lang === 'fr' ? 'fr-FR' : 'en-GB', { hour: '2-digit', minute: '2-digit' }).format(
    new Date(iso),
  );

/** "deployments:write, catalog:read" for the subtitle of a new key. */
export const scopeList = (scopes: string[]) => scopes.join(', ');

export function ScopeChoices({
  value,
  onChange,
  labelId,
}: {
  value: ApiKeyScope[];
  onChange: (v: ApiKeyScope[]) => void;
  labelId: string;
}) {
  const { t } = useI18n();
  return (
    <div className="rcards" role="group" aria-labelledby={labelId}>
      {API_KEY_SCOPES.map((s) => {
        const [name, text] = t.links.tools.scope[s];
        return (
          <label key={s} className="rcard">
            <input
              type="checkbox"
              checked={value.includes(s)}
              onChange={(e) => onChange(API_KEY_SCOPES.filter((x) => (x === s ? e.target.checked : value.includes(x))))}
            />
            <b>
              {name} <span className="mono tl-scope">{s}</span>
            </b>
            <small>{text}</small>
          </label>
        );
      })}
    </div>
  );
}

export function EventChoices({
  value,
  onChange,
  labelId,
}: {
  value: WebhookEvent[];
  onChange: (v: WebhookEvent[]) => void;
  labelId: string;
}) {
  const { t } = useI18n();
  return (
    <div className="rcards" role="group" aria-labelledby={labelId}>
      {WEBHOOK_EVENTS.map((ev) => {
        const [name, text] = t.links.tools.event[ev];
        return (
          <label key={ev} className="rcard">
            <input
              type="checkbox"
              checked={value.includes(ev)}
              onChange={(e) =>
                onChange(WEBHOOK_EVENTS.filter((x) => (x === ev ? e.target.checked : value.includes(x))))
              }
            />
            <b>
              {name} <span className="mono tl-scope">{ev}</span>
            </b>
            <small>{text}</small>
          </label>
        );
      })}
    </div>
  );
}

export function LifetimeSelect({
  id,
  value,
  onChange,
}: {
  id: string;
  value: KeyLifetime;
  onChange: (v: KeyLifetime) => void;
}) {
  const { t } = useI18n();
  return (
    <select id={id} className="select" value={value} onChange={(e) => onChange(Number(e.target.value) as KeyLifetime)}>
      {KEY_LIFETIMES.map((d) => (
        <option key={d} value={d}>
          {t.links.tools.create.lifetime(d)}
        </option>
      ))}
    </select>
  );
}

const fieldError = (e: unknown, field: string) => (e instanceof ApiError ? (e.fieldErrors[field]?.[0] ?? null) : null);

export const errorText = (e: unknown, fallback: string, network: string) =>
  e instanceof ApiError ? (e.status === 0 ? network : e.detail || fallback) : fallback;

/** A new tool: a name, an API key's scopes and expiry, and optionally a webhook. */
export function ConnectToolDialog({
  taken,
  onClose,
  onCreated,
}: {
  /** Names already used by a tool. */
  taken: string[];
  onClose: () => void;
  onCreated: (name: string, shown: Shown) => void;
}) {
  const { t, lang } = useI18n();
  const c = t.links.tools.create;
  const o = t.links.tools.once;
  const box = useRef<HTMLDivElement>(null);
  useModal(box, onClose);
  const [name, setName] = useState('');
  const [scopes, setScopes] = useState<ApiKeyScope[]>(['deployments:write']);
  const [days, setDays] = useState<KeyLifetime>(365);
  const [withHook, setWithHook] = useState(false);
  const [url, setUrl] = useState('https://');
  const [events, setEvents] = useState<WebhookEvent[]>(['catalog.changed']);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    const n = name.trim();
    const errs: Record<string, string> = {};
    if (!n) errs.name = c.nameRequired;
    else if (taken.includes(n)) errs.name = c.nameTaken;
    if (scopes.length === 0 && !withHook) errs.form = c.nothing;
    if (withHook && !/^https?:\/\/[^/\s]+/i.test(url.trim())) errs.url = c.urlRequired;
    if (withHook && events.length === 0) errs.events = c.eventsRequired;
    setErrors(errs);
    if (Object.keys(errs).length) {
      box.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus();
      return;
    }
    setBusy(true);
    let secret: string | undefined;
    try {
      // The webhook first: its address is what the server may refuse.
      if (withHook) secret = (await api.createWebhook({ name: n, url: url.trim(), events })).secret;
      if (scopes.length) {
        const created = await api.createApiKey(n, scopes, days);
        onCreated(n, {
          title: o.keyTitle,
          sub: o.sub(
            n,
            scopeList(created.apiKey.scopes),
            created.apiKey.expiresAt ? longDate(created.apiKey.expiresAt, lang) : '—',
          ),
          key: { value: created.key, apiKey: created.apiKey },
          secret,
        });
      } else onCreated(n, { title: o.hookTitle, sub: n, secret });
    } catch (x) {
      const fe = { url: fieldError(x, 'url'), name: fieldError(x, 'name') };
      setErrors({
        ...(fe.url ? { url: fe.url } : {}),
        ...(fe.name ? { name: fe.name } : {}),
        ...(!fe.url && !fe.name ? { form: errorText(x, t.common.genericError, t.common.networkError) } : {}),
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="dlg-back" role="presentation" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div ref={box} className="dlg wide" role="dialog" aria-modal="true" aria-labelledby="ct-h" tabIndex={-1}>
        <form onSubmit={(e) => void submit(e)} noValidate>
          <h2 id="ct-h">{c.title}</h2>
          <p className="dsub">{c.sub}</p>
          <div className="field">
            <label htmlFor="ct-name">
              {c.name}
              <span className="req" aria-hidden="true">
                *
              </span>
            </label>
            <input
              id="ct-name"
              className="input"
              value={name}
              maxLength={200}
              required
              data-autofocus
              aria-invalid={!!errors.name || undefined}
              aria-describedby={errors.name ? 'ct-name-e' : undefined}
              onChange={(e) => setName(e.target.value)}
            />
            {errors.name && (
              <span className="err" id="ct-name-e">
                <IconWarn size={13} />
                {errors.name}
              </span>
            )}
          </div>
          <span className="lbl2 tl-l" id="ct-sc">
            {c.scopes}
          </span>
          <ScopeChoices value={scopes} onChange={setScopes} labelId="ct-sc" />
          {scopes.length > 0 && (
            <div className="tl-grid2">
              <div className="field">
                <label htmlFor="ct-exp">{c.expires}</label>
                <LifetimeSelect id="ct-exp" value={days} onChange={setDays} />
                <small>{c.expiresHint}</small>
              </div>
              <div className="field">
                <span className="lbl">{c.rate}</span>
                <span className="tl-static">{c.rateValue}</span>
                <small>{c.rateHint}</small>
              </div>
            </div>
          )}
          <div className="rcards tl-hookopt">
            <label className="rcard">
              <input type="checkbox" checked={withHook} onChange={(e) => setWithHook(e.target.checked)} />
              <b>
                <IconWebhook size={14} /> {c.addHook}
              </b>
              <small>{c.addHookText}</small>
            </label>
          </div>
          {withHook && (
            <div className="tl-hookf">
              <div className="field">
                <label htmlFor="ct-url">{c.url}</label>
                <input
                  id="ct-url"
                  className="input mono"
                  type="url"
                  inputMode="url"
                  value={url}
                  aria-invalid={!!errors.url || undefined}
                  aria-describedby={errors.url ? 'ct-url-e' : undefined}
                  onChange={(e) => setUrl(e.target.value)}
                />
                {errors.url && (
                  <span className="err" id="ct-url-e">
                    <IconWarn size={13} />
                    {errors.url}
                  </span>
                )}
              </div>
              <span className="lbl2 tl-l" id="ct-ev">
                {c.events}
              </span>
              <EventChoices value={events} onChange={setEvents} labelId="ct-ev" />
              {errors.events && (
                <span className="lk-err" role="alert">
                  <IconWarn size={13} />
                  {errors.events}
                </span>
              )}
            </div>
          )}
          {errors.form && (
            <span className="lk-err" role="alert">
              <IconWarn size={13} />
              {errors.form}
            </span>
          )}
          <div className="form-actions tl-actions">
            <button type="button" className="btn" onClick={onClose}>
              {c.cancel}
            </button>
            <button type="submit" className="btn primary" disabled={busy}>
              {scopes.length ? <IconKey /> : <IconWebhook />}
              {busy ? c.creating : scopes.length ? c.submitKey : c.submitHook}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
