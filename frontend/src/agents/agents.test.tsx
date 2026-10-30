import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, vi } from 'vitest';
import { api, ApiError } from '../api/client';
import type { ConsentRequest, DemoPersona, Proposal } from '../api/types';
import { canSee } from '../demo/context';
import { recordOfPath, sortPersonas } from '../demo/DemoToolbox';
import { I18nProvider } from '../i18n';
import { en } from '../i18n/en';
import { fr } from '../i18n/fr';
import { AuthorChip } from '../review/bits';
import { ME, renderPage } from '../test/render';
import { capClass, clientHost, logTime, relTime } from './bits';
import { ConsentPage, leave } from './ConsentPage';
import { parseQuota, QUOTA_MAX } from './ConsolePage';
import { changedFields, guideText } from './MyAgentsPage';

afterEach(() => vi.restoreAllMocks());

const text = (en: string) => ({ en, fr: en });

const request = (over: Partial<ConsentRequest> = {}): ConsentRequest => ({
  id: 'req-1',
  clientId: 'nsclient_test',
  clientKind: 'registered',
  clientName: 'Curl agent',
  clientUri: null,
  redirectHost: '127.0.0.1:33418',
  person: { username: 'bob', displayName: 'Bob Durand' },
  scopes: [
    { scope: 'docs:read', grantable: true, text: text('Read the records you can see.') },
    { scope: 'docs:draft', grantable: true, text: text('Write drafts.') },
  ],
  capabilities: [
    { key: 'read', allowed: true, text: text('Reads every record visible to everyone.') },
    { key: 'draft', allowed: true, text: text('Writes drafts in its own workspace.') },
    { key: 'publish', allowed: false, text: text('Cannot publish anything.') },
    { key: 'protectedNotes', allowed: false, text: text('Cannot read protected notes.') },
  ],
  ceiling: 'public',
  approvalRequired: false,
  alreadyConnected: false,
  expiresAt: '2026-10-30T23:26:05Z',
  ...over,
});

describe('agent helpers', () => {
  const now = new Date('2026-10-30T12:00:00');
  it('says when, relative to now', () => {
    expect(relTime(en, 'en', new Date('2026-10-30T11:59:40').toISOString(), now)).toBe(en.agents.justNow);
    expect(relTime(en, 'en', new Date('2026-10-30T11:48:00').toISOString(), now)).toBe(en.agents.minutesAgo(12));
    expect(relTime(en, 'en', new Date('2026-10-30T08:05:00').toISOString(), now)).toContain('08:05');
    expect(relTime(fr, 'fr', new Date('2026-10-29T17:12:00').toISOString(), now)).toBe(fr.agents.yesterday('17:12'));
    expect(logTime(en, 'en', new Date('2026-10-30T10:42:00').toISOString(), now)).toBe('10:42');
    expect(logTime(en, 'en', new Date('2026-10-20T10:42:00').toISOString(), now)).toBe('20 Oct');
  });

  it('sorts capabilities into allowed, locked and refused', () => {
    expect(capClass({ key: 'read', allowed: true })).toBe('ok');
    expect(capClass({ key: 'restricted', allowed: false })).toBe('lock');
    expect(capClass({ key: 'protectedNotes', allowed: false })).toBe('lock');
    expect(capClass({ key: 'publish', allowed: false })).toBe('no');
    expect(capClass({ key: 'quota', allowed: true })).toBe('');
  });

  it('names where an agent comes from', () => {
    expect(clientHost(en, { demo: true, clientUri: null, clientId: 'x' })).toBe(en.agents.demoClient);
    expect(clientHost(en, { demo: false, clientUri: 'https://claude.ai/app', clientId: 'x' })).toBe('claude.ai');
    expect(clientHost(en, { demo: false, clientUri: null, clientId: 'https://code.example/meta.json' })).toBe(
      'code.example',
    );
    expect(clientHost(en, { demo: false, clientUri: null, clientId: 'nsclient_1' })).toBe(en.agents.registeredClient);
  });

  it('writes the connection guides for the MCP address', () => {
    const url = 'https://noosphere.example/mcp';
    expect(guideText('cli', url)).toBe(`claude mcp add --transport http noosphere ${url}`);
    expect(JSON.parse(guideText('json', url))).toEqual({ mcpServers: { noosphere: { type: 'http', url } } });
    expect(JSON.parse(guideText('vsc', url))).toEqual({ servers: { noosphere: { type: 'http', url } } });
  });

  it('accepts only whole quotas in range', () => {
    expect(parseQuota('600')).toBe(600);
    expect(parseQuota(' 1 ')).toBe(1);
    expect(parseQuota(String(QUOTA_MAX))).toBe(QUOTA_MAX);
    expect(parseQuota(String(QUOTA_MAX + 1))).toBeNull();
    expect(parseQuota('0')).toBeNull();
    expect(parseQuota('6.5')).toBeNull();
    expect(parseQuota('')).toBeNull();
  });

  it('lists the changed fields of a proposal once', () => {
    const d = (label: string) => ({ field: label, label, kind: 'text', before: 'a', after: 'b' });
    const p = {
      changes: [{ diff: [d('Technology'), d('Owner')] }, { diff: [d('Technology')] }],
    } as unknown as Proposal;
    expect(changedFields(p)).toEqual(['Technology', 'Owner']);
  });
});

describe('demo helpers', () => {
  it('finds the record of the current page', () => {
    expect(recordOfPath('/applications/billing')).toEqual({ kind: 'application', id: 'billing' });
    expect(recordOfPath('/data-objects/invoice/edit')).toEqual({ kind: 'dataObject', id: 'invoice' });
    expect(recordOfPath('/applications/new')).toBeNull();
    expect(recordOfPath('/review')).toBeNull();
  });

  it('shows members first, then the administrator, then the guest', () => {
    const p = (username: string, role: DemoPersona['role']) =>
      ({ username, displayName: username, role, teams: [], whatToTry: text(''), current: false }) as DemoPersona;
    const order = sortPersonas([p('gina', 'guest'), p('alice', 'admin'), p('david', 'member'), p('bob', 'member')]);
    expect(order.map((x) => x.username)).toEqual(['bob', 'david', 'alice', 'gina']);
  });

  it('keeps a page only for a persona who can see it', async () => {
    const guest = { ...ME.bob, username: 'gina', role: 'guest' as const, teams: [] };
    expect(await canSee('/admin/agents', ME.bob)).toBe(false);
    expect(await canSee('/admin/agents', ME.alice)).toBe(true);
    expect(await canSee('/my-agents', guest)).toBe(false);
    expect(await canSee('/', guest)).toBe(true);
    vi.spyOn(api, 'application').mockResolvedValueOnce({ id: 'hr', restricted: true } as never);
    expect(await canSee('/applications/hr', ME.david)).toBe(false);
    vi.spyOn(api, 'application').mockRejectedValueOnce(new ApiError(404, 'Not found'));
    expect(await canSee('/applications/gone', ME.david)).toBe(false);
    vi.spyOn(api, 'application').mockResolvedValueOnce({ id: 'billing' } as never);
    expect(await canSee('/applications/billing', ME.david)).toBe(true);
  });
});

describe('agent proposals in review', () => {
  it('says which person the agent wrote for', () => {
    render(
      <I18nProvider initial="en">
        <AuthorChip
          p={{
            authorKind: 'agent',
            author: { username: 'bob', displayName: 'Bob Durand' },
            onBehalfOf: { username: 'bob', displayName: 'Bob Durand' },
            agentName: 'Claude Code',
          }}
        />
      </I18nProvider>,
    );
    expect(screen.getByText('AI agent for Bob Durand')).toBeInTheDocument();
    expect(screen.getByText('Claude Code')).toBeInTheDocument();
  });
});

describe('ConsentPage', () => {
  const open = (r: ConsentRequest) => {
    vi.spyOn(api, 'consentRequest').mockResolvedValue(r);
    return renderPage(<ConsentPage />, { path: '/oauth/consent?request=' + r.id, route: '/oauth/consent', me: ME.bob });
  };

  it('lists what the agent can and cannot do, and allows it', async () => {
    const user = userEvent.setup();
    const go = vi.spyOn(leave, 'to').mockImplementation(() => {});
    const approve = vi
      .spyOn(api, 'approveConsent')
      .mockResolvedValue({ redirectTo: 'http://127.0.0.1:33418/cb?code=x', pendingApproval: false });
    open(request());
    expect(await screen.findByText('Reads every record visible to everyone.')).toBeInTheDocument();
    expect(screen.getByText('Cannot publish anything.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: en.agents.consent.allow }));
    expect(approve).toHaveBeenCalledWith('req-1', undefined);
    await waitFor(() => expect(go).toHaveBeenCalledWith('http://127.0.0.1:33418/cb?code=x'));
  });

  it('narrows the access when drafts are unticked', async () => {
    const user = userEvent.setup();
    vi.spyOn(leave, 'to').mockImplementation(() => {});
    const approve = vi
      .spyOn(api, 'approveConsent')
      .mockResolvedValue({ redirectTo: 'http://x/cb', pendingApproval: false });
    open(request());
    await user.click(await screen.findByRole('checkbox', { name: en.agents.consent.allowDrafts }));
    expect(screen.queryByText('Writes drafts in its own workspace.')).toBeNull();
    await user.click(screen.getByRole('button', { name: en.agents.consent.allow }));
    expect(approve).toHaveBeenCalledWith('req-1', ['docs:read']);
  });

  it('denies and goes back to the agent', async () => {
    const user = userEvent.setup();
    const go = vi.spyOn(leave, 'to').mockImplementation(() => {});
    vi.spyOn(api, 'denyConsent').mockResolvedValue({
      redirectTo: 'http://x/cb?error=access_denied',
      pendingApproval: false,
    });
    open(request());
    await user.click(await screen.findByRole('button', { name: en.agents.consent.deny }));
    await waitFor(() => expect(go).toHaveBeenCalledWith('http://x/cb?error=access_denied'));
  });

  it('waits for an administrator when approval is required', async () => {
    const user = userEvent.setup();
    const go = vi.spyOn(leave, 'to').mockImplementation(() => {});
    vi.spyOn(api, 'approveConsent').mockResolvedValue({ redirectTo: 'http://x/cb?code=y', pendingApproval: true });
    open(request({ approvalRequired: true }));
    await user.click(await screen.findByRole('button', { name: en.agents.consent.allow }));
    expect(await screen.findByText(en.agents.consent.waitTitle('Curl agent'))).toBeInTheDocument();
    expect(go).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: en.agents.consent.continueTo('Curl agent') }));
    expect(go).toHaveBeenCalledWith('http://x/cb?code=y');
  });

  it('explains an expired request', async () => {
    vi.spyOn(api, 'consentRequest').mockRejectedValue(new ApiError(404, 'Not found'));
    renderPage(<ConsentPage />, { path: '/oauth/consent?request=old', route: '/oauth/consent', me: ME.bob });
    expect(await screen.findByText(en.agents.consent.errorExpired)).toBeInTheDocument();
  });
});
