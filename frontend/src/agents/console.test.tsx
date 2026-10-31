import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, vi } from 'vitest';
import { api, ApiError } from '../api/client';
import type { AgentConnection, AgentSettings } from '../api/types';
import { en } from '../i18n/en';
import { ME, renderPage } from '../test/render';
import { ConsolePage } from './ConsolePage';

afterEach(() => vi.restoreAllMocks());

const c = en.agents.console;

const settings: AgentSettings = {
  enabled: true,
  ceiling: 'public',
  quotaPerHour: 600,
  whoConnects: 'anyMember',
  dynamicRegistration: true,
  anonymousLlms: false,
  mcpUrl: 'http://127.0.0.1:5094/mcp',
  version: 0,
  updatedAt: null,
};

const conn: AgentConnection = {
  id: 'c-1',
  clientId: 'nsclient_1',
  clientName: 'Claude Code',
  clientUri: null,
  person: { username: 'bob', displayName: 'Bob Durand' },
  scopes: ['docs:read', 'docs:draft'],
  state: 'active',
  createdAt: '2026-10-30T08:00:00Z',
  lastUsedAt: '2026-10-30T09:00:00Z',
  revokedAt: null,
  pausedUntil: null,
  approvedAt: '2026-10-30T08:00:00Z',
  callsLastHour: 3,
  openDrafts: 0,
  lastActivity: null,
  demo: false,
};

function open(me = ME.alice) {
  vi.spyOn(api, 'agents').mockResolvedValue([conn]);
  vi.spyOn(api, 'agentSettings').mockResolvedValue(settings);
  vi.spyOn(api, 'agentActivity').mockResolvedValue({ items: [], nextCursor: null });
  vi.spyOn(api, 'agentCapabilities').mockResolvedValue({ ceiling: 'public', items: [] } as never);
  return renderPage(<ConsolePage />, { path: '/admin/agents', route: '/admin/agents', me });
}

describe('ConsolePage', () => {
  it('is for administrators only', async () => {
    open(ME.bob);
    expect(await screen.findByText(en.common.noAccess)).toBeInTheDocument();
    expect(api.agents).not.toHaveBeenCalled();
  });

  it('stops every agent after a confirmation', async () => {
    const user = userEvent.setup();
    const stop = vi
      .spyOn(api, 'stopAllAgents')
      .mockResolvedValue({ revokedConnections: 1, revokedTokens: 2, at: '2026-10-30T10:00:00Z' });
    open();
    await user.click(await screen.findByRole('button', { name: c.stopAll }));
    expect(stop).not.toHaveBeenCalled();
    expect(screen.getByText(c.stopConfirm(1))).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: c.stopYes }));
    await waitFor(() => expect(stop).toHaveBeenCalledOnce());
    expect(await screen.findByRole('button', { name: c.done })).toBeInTheDocument();
  });

  it('checks the quota and shows a conflict when settings changed meanwhile', async () => {
    const user = userEvent.setup();
    const update = vi.spyOn(api, 'updateAgentSettings').mockRejectedValue(new ApiError(409, 'Conflict'));
    open();
    await user.click(await screen.findByRole('button', { name: c.change }));
    const quota = document.getElementById('quota') as HTMLInputElement;
    await user.clear(quota);
    await user.type(quota, '0');
    await user.click(screen.getByRole('button', { name: c.save }));
    expect(screen.getByText(c.quotaError(1, 100000))).toBeInTheDocument();
    expect(update).not.toHaveBeenCalled();
    await user.clear(quota);
    await user.type(quota, '1200');
    await user.click(screen.getByRole('button', { name: c.save }));
    await waitFor(() =>
      expect(update).toHaveBeenCalledWith(expect.objectContaining({ quotaPerHour: 1200, version: 0 })),
    );
    expect(await screen.findByText(c.conflict)).toBeInTheDocument();
  });
});
