import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, vi } from 'vitest';
import { api } from '../api/client';
import type { ApiKey, LinkExchange, Webhook } from '../api/types';
import { en } from '../i18n/en';
import { fr } from '../i18n/fr';
import { ME, renderPage } from '../test/render';
import { LinksPage } from './LinksPage';
import { toolExchangeChip, toolExchangeSentence, ToolPage } from './ToolPage';
import { expiresSoon, groupTools, hookStatus, keySnippets } from './tools';

afterEach(() => vi.restoreAllMocks());

const T = en.links.tools;
const day = 86_400_000;

const key = (x: Partial<ApiKey> = {}): ApiKey => ({
  id: 'k-1',
  name: 'GitLab CI',
  prefix: 'nsk_abcd2345',
  scopes: ['deployments:write'],
  state: 'active',
  createdAt: '2026-10-01T08:00:00Z',
  createdBy: 'alice',
  expiresAt: new Date(Date.now() + 200 * day).toISOString(),
  lastUsedAt: null,
  revokedAt: null,
  revokedBy: null,
  replacedById: null,
  ...x,
});

const hook = (x: Partial<Webhook> = {}): Webhook => ({
  id: 'w-1',
  name: 'ServiceNow',
  url: 'https://itsm.example/hooks',
  events: ['catalog.changed'],
  enabled: true,
  createdAt: '2026-10-02T08:00:00Z',
  createdBy: 'alice',
  lastDeliveryAt: null,
  pending: 0,
  failingSince: null,
  pausedAt: null,
  version: 1,
  ...x,
});

const ex = (x: Partial<LinkExchange>): LinkExchange => ({
  id: 1,
  at: '2026-10-30T08:00:00Z',
  linkId: null,
  direction: 'in',
  operation: 'catalog',
  result: 'ok',
  status: 200,
  reason: null,
  detail: null,
  apiKeyId: 'k-1',
  webhookId: null,
  ...x,
});

describe('tool bits', () => {
  it('groups keys and webhooks by name, keeps a rotated key apart, and puts removed tools last', () => {
    const tools = groupTools(
      [
        key({
          id: 'k-old',
          createdAt: '2026-10-01T08:00:00Z',
          replacedById: 'k-new',
          lastUsedAt: '2026-10-20T08:00:00Z',
        }),
        key({ id: 'k-new', createdAt: '2026-10-10T08:00:00Z' }),
        key({ id: 'k-x', name: 'Archive', state: 'revoked' }),
        key({ id: 'k-s', name: 'ServiceNow', scopes: ['catalog:read'] }),
      ],
      [hook({ lastDeliveryAt: '2026-10-25T08:00:00Z' })],
    );
    expect(tools.map((x) => x.name)).toEqual(['GitLab CI', 'ServiceNow', 'Archive']);
    const ci = tools[0];
    expect([ci.key?.id, ci.previous?.id, ci.webhook]).toEqual(['k-new', 'k-old', null]);
    expect(ci.lastUse).toBe('2026-10-20T08:00:00Z');
    expect(ci.createdAt).toBe('2026-10-01T08:00:00Z');
    expect(tools[1].webhook?.id).toBe('w-1');
    expect(tools[1].lastUse).toBe('2026-10-25T08:00:00Z');
    expect(tools[2].removed).toBe(true);
  });

  it('reminds of keys expiring within 30 days, unless rotated', () => {
    const now = new Date('2026-11-02T08:00:00Z');
    expect(expiresSoon(key({ expiresAt: '2026-11-20T08:00:00Z' }), now)).toBe(true);
    expect(expiresSoon(key({ expiresAt: '2027-01-20T08:00:00Z' }), now)).toBe(false);
    expect(expiresSoon(key({ expiresAt: '2026-11-03T08:00:00Z', replacedById: 'k-2' }), now)).toBe(false);
    expect(expiresSoon(key({ expiresAt: '2026-11-03T08:00:00Z', state: 'revoked' }), now)).toBe(false);
  });

  it('tells the state of a webhook', () => {
    expect(hookStatus(hook())).toBe('on');
    expect(hookStatus(hook({ enabled: false }))).toBe('off');
    expect(hookStatus(hook({ failingSince: '2026-11-01T08:00:00Z' }))).toBe('failing');
    expect(hookStatus(hook({ pausedAt: '2026-11-02T08:00:00Z', enabled: true }))).toBe('paused');
  });

  it('writes examples for the scopes of the key, with this instance address', () => {
    const w = en.links.tools.once;
    const deploy = keySnippets('https://noos.example', ['deployments:write'], w);
    expect(deploy.curl).toContain('POST https://noos.example/api/integrations/deployments');
    expect(deploy.github).toContain('secrets.NOOSPHERE_KEY');
    expect(deploy.gitlab).toContain('$NOOSPHERE_KEY');
    const read = keySnippets('https://noos.example', ['catalog:read'], w);
    expect(read.curl).toContain('/api/integrations/catalog');
    expect(read.curl).not.toContain('deployments');
  });

  it('tells exchanges of a tool in plain words', () => {
    expect(toolExchangeSentence(en, ex({}), 'GitLab CI')).toEqual({ who: 'GitLab CI', text: T.ex.in.catalog });
    expect(toolExchangeSentence(en, ex({ result: 'refused', status: 403, reason: 'scope-missing' }), 'X')).toEqual({
      who: null,
      text: T.ex.refused,
      reason: T.ex.reason['scope-missing'],
    });
    expect(toolExchangeSentence(en, ex({ operation: 'webhook', direction: 'out', result: 'failed' }), 'X').text).toBe(
      T.ex.hookFailed,
    );
    expect(toolExchangeChip(en, ex({ reason: 'webhook-paused', result: 'failed' })).label).toBe(T.ex.chip.paused);
  });

  it('has every tool text in French too', () => {
    const keys = (o: object, p = ''): string[] =>
      Object.entries(o).flatMap(([k, v]) =>
        v && typeof v === 'object' && !Array.isArray(v) ? keys(v as object, `${p}${k}.`) : [`${p}${k}`],
      );
    expect(keys(fr.links.tools).sort()).toEqual(keys(en.links.tools).sort());
  });
});

function mockTools(keys: ApiKey[], hooks: Webhook[]) {
  vi.spyOn(api, 'apiKeys').mockResolvedValue(keys);
  vi.spyOn(api, 'webhooks').mockResolvedValue(hooks);
  vi.spyOn(api, 'people').mockResolvedValue([
    { username: 'alice', displayName: 'Alice Martin', role: 'admin', teamIds: [] },
  ]);
}

describe('Connected tools on the Links page', () => {
  const open = (keys: ApiKey[], hooks: Webhook[]) => {
    vi.spyOn(api, 'links').mockResolvedValue([]);
    vi.spyOn(api, 'linkCodes').mockResolvedValue([]);
    vi.spyOn(api, 'integrationSettings').mockResolvedValue({
      allowPrivateAddresses: false,
      updatedAt: null,
      updatedBy: null,
    });
    mockTools(keys, hooks);
    return renderPage(<LinksPage />, { path: '/admin/links', route: '/admin/links', me: ME.alice });
  };

  it('lists tools with their key, webhook state and reminders', async () => {
    open(
      [key({ name: 'ServiceNow', expiresAt: new Date(Date.now() + 10 * day).toISOString() })],
      [hook({ failingSince: '2026-11-01T08:00:00Z', pending: 2 })],
    );
    const row = (await screen.findAllByRole('link', { name: 'ServiceNow' }))
      .find((a) => a.closest('tr'))!
      .closest('tr')!;
    expect(within(row).getByText('deployments:write')).toBeInTheDocument();
    expect(within(row).getByText(T.hook.failing(2))).toBeInTheDocument();
    expect(within(row).getByText(/expires in/)).toBeInTheDocument();
    expect(screen.getByText(T.remindKeys(1))).toBeInTheDocument();
    expect(await screen.findByRole('switch', { name: T.privateLabel })).toHaveAttribute('aria-checked', 'false');
  });

  it('connects a tool: the key is shown once, with examples', async () => {
    open([key({ name: 'Taken' })], []);
    const create = vi.spyOn(api, 'createApiKey').mockResolvedValue({
      apiKey: key({ id: 'k-9', name: 'Jenkins' }),
      key: 'nsk_abcd2345_' + 'x'.repeat(40),
    });
    const user = userEvent.setup();
    await screen.findByRole('link', { name: 'Taken' });
    await user.click(screen.getAllByRole('button', { name: en.links.connect })[0]);
    const dialog = screen.getByRole('dialog', { name: T.create.title });
    await user.type(within(dialog).getByLabelText(new RegExp(T.create.name)), 'Taken');
    await user.click(within(dialog).getByRole('button', { name: T.create.submitKey }));
    expect(within(dialog).getByText(T.create.nameTaken)).toBeInTheDocument();
    expect(create).not.toHaveBeenCalled();

    await user.clear(within(dialog).getByLabelText(new RegExp(T.create.name)));
    await user.type(within(dialog).getByLabelText(new RegExp(T.create.name)), 'Jenkins');
    await user.selectOptions(within(dialog).getByLabelText(T.create.expires), '90');
    await user.click(within(dialog).getByRole('button', { name: T.create.submitKey }));
    await waitFor(() => expect(create).toHaveBeenCalledWith('Jenkins', ['deployments:write'], 90));
    const once = await screen.findByRole('dialog', { name: T.once.keyTitle });
    expect(within(once).getByText('nsk_abcd2345_' + 'x'.repeat(40))).toBeInTheDocument();
    expect(within(once).getByText(T.once.warn)).toBeInTheDocument();
    await user.click(within(once).getByRole('tab', { name: 'GitLab CI' }));
    expect(within(once).getByRole('tabpanel')).toHaveTextContent('.gitlab-ci.yml');
  });

  it('asks for a scope or a webhook', async () => {
    open([], []);
    const user = userEvent.setup();
    await screen.findByText(T.emptyTitle);
    await user.click(screen.getAllByRole('button', { name: en.links.connect })[0]);
    const dialog = screen.getByRole('dialog', { name: T.create.title });
    await user.type(within(dialog).getByLabelText(new RegExp(T.create.name)), 'Script');
    await user.click(within(dialog).getByRole('checkbox', { name: /Send deployment events/ }));
    await user.click(within(dialog).getByRole('button', { name: T.create.submitHook }));
    expect(within(dialog).getByText(T.create.nothing)).toBeInTheDocument();
  });
});

describe('ToolPage', () => {
  const open = (keys: ApiKey[], hooks: Webhook[], name = 'ServiceNow') => {
    mockTools(keys, hooks);
    vi.spyOn(api, 'toolExchanges').mockResolvedValue({
      items: [ex({ result: 'refused', status: 403, reason: 'scope-missing', operation: 'deployments' })],
      nextCursor: null,
    });
    vi.spyOn(api, 'webhookDeliveries').mockResolvedValue({
      items: [
        {
          id: 7,
          eventId: 'e-7',
          eventType: 'catalog.changed',
          state: 'pending',
          attempts: 3,
          createdAt: '2026-11-02T08:00:00Z',
          lastAttemptAt: '2026-11-02T08:10:00Z',
          nextAttemptAt: '2026-11-02T08:40:00Z',
          responseStatus: 503,
          error: 'The receiver answered 503.',
          test: false,
        },
      ],
      nextCursor: null,
    });
    return renderPage(<ToolPage />, {
      path: `/admin/links/tools/${encodeURIComponent(name)}`,
      route: '/admin/links/tools/:name',
      me: ME.alice,
    });
  };

  it('shows the webhook, its deliveries and the key; retries a delivery', async () => {
    open([key({ name: 'ServiceNow', scopes: ['catalog:read'] })], [hook({ failingSince: '2026-11-02T08:00:00Z' })]);
    expect(await screen.findByRole('heading', { name: 'ServiceNow', level: 1 })).toBeInTheDocument();
    expect(screen.getByText('https://itsm.example/hooks')).toBeInTheDocument();
    expect(await screen.findByText('503')).toBeInTheDocument();
    expect(screen.getByText(/attempt 3\/6/)).toBeInTheDocument();
    expect(await screen.findByText(T.ex.reason['scope-missing'])).toBeInTheDocument();
    expect(screen.getByText('nsk_abcd2345…')).toBeInTheDocument();
    const retry = vi.spyOn(api, 'retryDelivery').mockResolvedValue({
      id: 7,
      eventId: 'e-7',
      eventType: 'catalog.changed',
      state: 'delivered',
      attempts: 4,
      createdAt: '2026-11-02T08:00:00Z',
      lastAttemptAt: '2026-11-02T08:12:00Z',
      nextAttemptAt: null,
      responseStatus: 200,
      error: null,
      test: false,
    });
    await userEvent.setup().click(screen.getByRole('button', { name: T.page.retry }));
    await waitFor(() => expect(retry).toHaveBeenCalledWith('w-1', 7));
    expect(await screen.findByText(T.page.thenDelivered(4))).toBeInTheDocument();
  });

  it('rotates the key: the new one is shown once', async () => {
    open([key({ name: 'ServiceNow' })], []);
    const rotate = vi.spyOn(api, 'rotateApiKey').mockResolvedValue({
      apiKey: key({ id: 'k-2', name: 'ServiceNow', prefix: 'nsk_zzzz2345' }),
      key: 'nsk_zzzz2345_' + 'y'.repeat(40),
    });
    const user = userEvent.setup();
    await user.click(await screen.findByRole('button', { name: T.page.rotate }));
    await waitFor(() => expect(rotate).toHaveBeenCalledWith('k-1'));
    const once = await screen.findByRole('dialog', { name: T.once.rotatedTitle });
    expect(within(once).getByText('nsk_zzzz2345_' + 'y'.repeat(40))).toBeInTheDocument();
    expect(within(once).getByText(/The previous key keeps working until/)).toBeInTheDocument();
    expect(screen.getByText(T.page.noHookTitle)).toBeInTheDocument();
  });

  it('resumes a paused webhook', async () => {
    open([], [hook({ pausedAt: '2026-11-02T08:00:00Z', failingSince: '2026-11-01T08:00:00Z' })]);
    const update = vi.spyOn(api, 'updateWebhook').mockResolvedValue(hook());
    const user = userEvent.setup();
    expect(await screen.findByText(/Paused on/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: T.page.retry })).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: T.page.resume }));
    await waitFor(() =>
      expect(update).toHaveBeenCalledWith('w-1', expect.objectContaining({ enabled: true, version: 1 })),
    );
  });

  it('says when the tool does not exist', async () => {
    open([], [], 'Nope');
    expect(await screen.findByText(T.page.notFound)).toBeInTheDocument();
  });
});
