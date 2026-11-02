import { useEffect, useState } from 'react';
import { Link as RouterLink } from 'react-router-dom';
import { api } from '../api/client';
import type { ApiKey, IntegrationSettings, Webhook } from '../api/types';
import { relTime, shortDate } from '../agents/bits';
import { useI18n } from '../i18n';
import { IconClock, IconDoc, IconKey, IconPlug, IconWarn, IconWebhook } from '../ui/icons';
import { useToast } from '../ui/Toast';
import { Switch } from './bits';
import { daysUntil, expiresSoon, groupTools, hookStatus, toolPath, type Tool } from './tools';

/** The keys and webhooks of the tools, loaded together. */
export function useTools(reload: number) {
  const [data, setData] = useState<{ keys: ApiKey[]; webhooks: Webhook[] } | null>(null);
  const [error, setError] = useState(false);
  useEffect(() => {
    let alive = true;
    setError(false);
    Promise.all([api.apiKeys(), api.webhooks()])
      .then(([keys, webhooks]) => alive && setData({ keys, webhooks }))
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, [reload]);
  return { tools: data ? groupTools(data.keys, data.webhooks) : null, error };
}

export function ToolTile({ tool, big }: { tool: Pick<Tool, 'webhook' | 'removed'>; big?: boolean }) {
  return (
    <span className={'atile ini tl-tile' + (big ? ' big2' : '') + (tool.removed ? ' c' : '')} aria-hidden="true">
      {tool.webhook ? <IconWebhook size={big ? 22 : 17} /> : <IconKey size={big ? 22 : 17} />}
    </span>
  );
}

export function KeyStateChip({ k }: { k: ApiKey }) {
  const { t } = useI18n();
  const l = t.links.tools;
  if (k.state === 'revoked') return <span className="pchip muted">{l.revoked}</span>;
  if (k.state === 'expired') return <span className="pchip muted">{l.expired}</span>;
  if (expiresSoon(k) && k.expiresAt)
    return (
      <span className="pchip warn">
        <IconClock size={11} />
        {l.expiresIn(daysUntil(k.expiresAt))}
      </span>
    );
  return null;
}

export function HookChip({ w }: { w: Webhook }) {
  const { t } = useI18n();
  const h = t.links.tools.hook;
  switch (hookStatus(w)) {
    case 'paused':
      return (
        <span className="pchip danger">
          <IconWarn size={11} />
          {h.paused}
        </span>
      );
    case 'off':
      return <span className="pchip muted">{h.off}</span>;
    case 'failing':
      return (
        <span className="pchip warn">
          <IconWarn size={11} />
          {h.failing(w.pending)}
        </span>
      );
    default:
      return (
        <span className="pchip ok">
          <span className="st-dot" />
          {h.on}
        </span>
      );
  }
}

function ToolRow({ tool, nameOf }: { tool: Tool; nameOf: (u: string) => string }) {
  const { t, lang } = useI18n();
  const l = t.links.tools;
  const to = toolPath(tool.name);
  const shownKey = tool.key ?? tool.keys[0] ?? null;
  return (
    <tr className={tool.removed ? 'cutrow' : ''}>
      <td className="lead">
        <span className="lk-app">
          <ToolTile tool={tool} />
          <span>
            <RouterLink className="an2 rowpick" to={to}>
              {tool.name}
            </RouterLink>
            <span className="am2">
              {l.by(nameOf(tool.createdBy), shortDate(tool.createdAt, lang))}
              {tool.removed && <span className="pchip muted lk-demo">{l.removed}</span>}
            </span>
          </span>
        </span>
      </td>
      <td data-l={l.colKey}>
        {shownKey ? (
          <span className="tl-key">
            <span className="chip-row">
              {shownKey.scopes.map((s) => (
                <span key={s} className="pchip mono">
                  {s}
                </span>
              ))}
              <KeyStateChip k={shownKey} />
            </span>
            <span className="am2 mono">{shownKey.prefix}…</span>
          </span>
        ) : (
          <span className="none">{l.none}</span>
        )}
      </td>
      <td data-l={l.colHook}>
        {tool.webhook ? <HookChip w={tool.webhook} /> : <span className="none">{l.none}</span>}
      </td>
      <td data-l={l.colLast}>
        {tool.lastUse ? relTime(t, lang, tool.lastUse) : <span className="none">{l.never}</span>}
      </td>
      <td className="acts-td">
        <span className="acts2">
          <RouterLink className="btn sm" to={to} aria-label={`${l.open} ${tool.name}`}>
            {l.open}
          </RouterLink>
        </span>
      </td>
    </tr>
  );
}

export function IntegrationsDocLink() {
  const { t } = useI18n();
  const l = t.links.tools;
  return (
    <a className="lk-doc" href={l.docUrl} target="_blank" rel="noopener noreferrer">
      <IconDoc size={14} /> {l.docTitle}
      <span className="mono">{l.docPath}</span>
    </a>
  );
}

/** Keys expiring within 30 days and paused webhooks: what an administrator must act on. */
export function ToolReminders({ tools }: { tools: Tool[] }) {
  const { t } = useI18n();
  const l = t.links.tools;
  const soon = tools.filter((x) => x.key && expiresSoon(x.key));
  const paused = tools.filter((x) => x.webhook?.pausedAt);
  return (
    <>
      {paused.length > 0 && (
        <div className="banner danger" role="status">
          <IconWarn />
          <div className="bt">
            <b>{l.remindPaused(paused.map((x) => x.name).join(', '))}</b> {l.remindPausedText}{' '}
            {paused.map((x, i) => (
              <span key={x.name}>
                {i > 0 && ', '}
                <RouterLink to={toolPath(x.name)}>{x.name}</RouterLink>
              </span>
            ))}
          </div>
        </div>
      )}
      {soon.length > 0 && (
        <div className="banner warn" role="status">
          <IconClock size={16} />
          <div className="bt">
            <b>{l.remindKeys(soon.length)}</b> {l.remindKeysText}{' '}
            {soon.map((x, i) => (
              <span key={x.name}>
                {i > 0 && ', '}
                <RouterLink to={toolPath(x.name)}>{x.name}</RouterLink>
              </span>
            ))}
          </div>
        </div>
      )}
    </>
  );
}

function ToolsSkeleton() {
  return (
    <section className="box" aria-busy="true">
      <span className="sk line" style={{ width: 160 }} />
      <span className="sk line" style={{ width: '60%', marginTop: 10 }} />
      <span className="sk block" style={{ height: 120, marginTop: 16 }} />
    </section>
  );
}

/** The second list of the Links page: tools with an API key and/or a webhook. */
export function ToolsSection({
  tools,
  error,
  onRetry,
  onConnect,
  nameOf,
}: {
  tools: Tool[] | null;
  error: boolean;
  onRetry: () => void;
  onConnect: () => void;
  nameOf: (u: string) => string;
}) {
  const { t } = useI18n();
  const l = t.links.tools;
  if (error)
    return (
      <div className="banner danger" role="alert">
        <IconWarn />
        <div className="bt">
          <b>{l.loadError}</b>
          <div className="page-actions" style={{ marginTop: 10 }}>
            <button type="button" className="btn" onClick={onRetry}>
              {t.common.retry}
            </button>
          </div>
        </div>
      </div>
    );
  if (!tools) return <ToolsSkeleton />;
  return (
    <section className="box tb" aria-labelledby="tl-h">
      <div className="tb-h">
        <div>
          <h2 id="tl-h">
            {l.title} <span className="tl-new">{l.newBadge}</span>
          </h2>
          <p>{l.intro}</p>
        </div>
      </div>
      {tools.length === 0 ? (
        <div className="tl-empty">
          <span className="atile big" aria-hidden="true">
            <IconPlug size={22} />
          </span>
          <b>{l.emptyTitle}</b>
          <p>{l.emptyText}</p>
          <button type="button" className="btn" onClick={onConnect}>
            <IconKey />
            {t.links.connect}
          </button>
        </div>
      ) : (
        <table className="table rtable atable lk-table tl-table">
          <thead>
            <tr>
              <th scope="col">{l.colTool}</th>
              <th scope="col">{l.colKey}</th>
              <th scope="col">{l.colHook}</th>
              <th scope="col">{l.colLast}</th>
              <th scope="col">
                <span className="sr">{t.links.list.actions}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {tools.map((x) => (
              <ToolRow key={x.name} tool={x} nameOf={nameOf} />
            ))}
          </tbody>
        </table>
      )}
      <div className="tb-f tl-foot">
        <span>{l.foot}</span>
        <IntegrationsDocLink />
      </div>
    </section>
  );
}

/** The instance-wide setting of webhooks: may they reach private addresses? */
export function WebhookAddressSettings({ nameOf }: { nameOf: (u: string) => string }) {
  const { t, lang } = useI18n();
  const toast = useToast();
  const l = t.links.tools;
  const [s, setS] = useState<IntegrationSettings | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  useEffect(() => {
    let alive = true;
    api
      .integrationSettings()
      .then((x) => alive && setS(x))
      .catch(() => alive && setError(true));
    return () => {
      alive = false;
    };
  }, []);
  const change = async (v: boolean) => {
    setBusy(true);
    try {
      setS(await api.setIntegrationSettings(v));
      toast(v ? l.privateOn : l.privateOff);
    } catch {
      toast(t.common.genericError);
    } finally {
      setBusy(false);
    }
  };
  if (error) return null;
  return (
    <section className="box setbox" aria-labelledby="wa-h">
      <div className="set-hd">
        <h2 id="wa-h">{l.settingsTitle}</h2>
      </div>
      {!s ? (
        <div aria-busy="true">
          <span className="sk line" style={{ width: '70%' }} />
          <span className="sk line" style={{ width: '50%', marginTop: 8 }} />
        </div>
      ) : (
        <div className="swrow">
          <b>{l.privateLabel}</b>
          <small>
            {l.privateText}
            {s.updatedBy && s.updatedAt && <> {l.privateBy(nameOf(s.updatedBy), relTime(t, lang, s.updatedAt))}</>}
          </small>
          <Switch
            label={l.privateLabel}
            checked={s.allowPrivateAddresses}
            disabled={busy}
            onChange={(v) => void change(v)}
          />
        </div>
      )}
    </section>
  );
}
