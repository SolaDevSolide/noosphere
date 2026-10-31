import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, vi } from 'vitest';
import { api } from '../api/client';
import type { Link, LinkCode, LinkExchange } from '../api/types';
import { en } from '../i18n/en';
import { ME, renderPage } from '../test/render';
import { exchangeChip, exchangeMatches, exchangeSentence, initialsOf, tileVariant } from './bits';
import { LinkPage } from './LinkPage';
import { LinksPage } from './LinksPage';
import { PairPage } from './PairPage';

afterEach(() => vi.restoreAllMocks());

const L = en.links;

const link = (x: Partial<Link> = {}): Link => ({
  id: 'l-1',
  product: 'Release pipeline',
  instanceName: 'Alizé CI',
  url: 'https://ci.example',
  peerVersion: '2.3.0',
  state: 'active',
  confirmedHere: true,
  confirmedAt: '2026-10-20T08:00:00Z',
  confirmedBy: 'alice',
  peerConfirmed: true,
  peerCheckedAt: '2026-10-30T08:00:00Z',
  capabilities: ['catalog:read', 'notices:send', 'deployments:receive'],
  fingerprint: 'orbit cedar 0712 lumen',
  pairedAt: '2026-10-20T08:00:00Z',
  pairedBy: 'alice',
  keysRenewedAt: null,
  lastExchangeAt: null,
  cutAt: null,
  cutBy: null,
  demo: false,
  keys: { signing: 'a', encryption: 'b', peerSigning: 'c', peerEncryption: 'd' },
  version: 1,
  ...x,
});

const ex = (x: Partial<LinkExchange>): LinkExchange => ({
  id: 1,
  at: '2026-10-30T08:00:00Z',
  linkId: 'l-1',
  direction: 'in',
  operation: 'catalog',
  result: 'ok',
  status: 200,
  reason: null,
  detail: null,
  ...x,
});

describe('link bits', () => {
  it('builds initials from the announced name, whatever the product', () => {
    expect(initialsOf('MEP Alizé')).toBe('MA');
    expect(initialsOf('Alizé CI')).toBe('AC');
    expect(initialsOf('MEP Alizé · test')).toBe('MT');
    expect(initialsOf('Jenkins')).toBe('JE');
    expect(initialsOf('···')).toBe('?');
    expect(tileVariant('gestion-mep')).toBe(tileVariant('gestion-mep'));
  });

  it('tells each exchange in plain words, with the peer name it announced', () => {
    expect(exchangeSentence(en, ex({}), 'Alizé CI')).toEqual({ who: 'Alizé CI', text: L.ex.in.catalog });
    expect(exchangeSentence(en, ex({ status: 304 }), 'X').text).toBe(L.ex.in.catalogSame);
    expect(exchangeSentence(en, ex({ result: 'refused', status: 401, reason: 'replay' }), 'X')).toEqual({
      who: null,
      text: L.ex.refusedIn,
      reason: 'replay',
    });
    expect(
      exchangeSentence(en, ex({ direction: 'out', operation: 'notices', result: 'failed', status: null }), 'X').text,
    ).toBe(L.ex.failed(L.ex.op.notices));
    expect(exchangeChip(en, ex({ operation: 'deployments' })).label).toBe(L.ex.chip.accepted);
    expect(exchangeChip(en, ex({ result: 'refused' })).cls).toBe('danger');
  });

  it('filters exchanges by kind', () => {
    const refused = ex({ result: 'refused', operation: 'deployments' });
    expect(exchangeMatches(refused, 'refused')).toBe(true);
    expect(exchangeMatches(refused, 'events')).toBe(true);
    expect(exchangeMatches(refused, 'catalog')).toBe(false);
    expect(exchangeMatches(ex({ operation: 'rekey' }), 'keys')).toBe(true);
  });
});

describe('LinksPage', () => {
  const open = (links: Link[], codes: LinkCode[] = [], me = ME.alice) => {
    vi.spyOn(api, 'links').mockResolvedValue(links);
    vi.spyOn(api, 'linkCodes').mockResolvedValue(codes);
    vi.spyOn(api, 'people').mockResolvedValue([
      { username: 'alice', displayName: 'Alice Martin', role: 'admin', teamIds: [] },
    ]);
    return renderPage(<LinksPage />, { path: '/admin/links', route: '/admin/links', me });
  };

  it('is for administrators only', async () => {
    open([], [], ME.bob);
    expect(await screen.findByText(en.common.noAccess)).toBeInTheDocument();
    expect(api.links).not.toHaveBeenCalled();
  });

  it('explains what a link can do when there is none', async () => {
    open([]);
    expect(await screen.findByText(L.empty.title)).toBeInTheDocument();
    for (const [name] of Object.values(L.cap)) expect(screen.getByText(name)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: new RegExp(L.pair) })).toHaveAttribute('href', '/admin/links/pair');
  });

  it('lists links with what each peer announced, and an open code', async () => {
    open(
      [link(), link({ id: 'l-2', instanceName: 'Old one', state: 'cut', cutAt: '2026-10-03T09:00:00Z' })],
      [
        {
          id: 'c-1',
          codeId: '7K4Q',
          state: 'active',
          capabilities: [],
          createdAt: new Date().toISOString(),
          expiresAt: new Date(Date.now() + 600000).toISOString(),
          createdBy: 'alice',
          usedAt: null,
          linkId: null,
          failedAttempts: 0,
          bootstrap: false,
        },
      ],
    );
    const row = (await screen.findByRole('link', { name: 'Alizé CI' })).closest('tr')!;
    expect(within(row).getByText(/Release pipeline 2\.3\.0/)).toBeInTheDocument();
    expect(within(row).getByText('https://ci.example')).toBeInTheDocument();
    expect(within(row).getByText('AC')).toBeInTheDocument();
    expect(within(row).getByText(L.state.peerConfirmed)).toBeInTheDocument();
    expect(screen.getByText(L.list.summary(1, 1, 0, 1))).toBeInTheDocument();
    expect(screen.getByText(L.list.codeOpen)).toBeInTheDocument();
    expect(screen.getByText(/7K4Q… · expires in/)).toBeInTheDocument();
  });
});

describe('PairPage', () => {
  it('creates a code with the chosen capabilities and shows it once', async () => {
    vi.spyOn(api, 'people').mockResolvedValue([]);
    vi.spyOn(api, 'createLinkCode').mockResolvedValue({
      id: 'c-9',
      code: 'AB12-CD34-EF56',
      codeId: 'AB12',
      expiresAt: new Date(Date.now() + 900000).toISOString(),
      capabilities: ['catalog:read', 'deployments:receive'],
      pairUrl: 'https://docs.example',
    });
    vi.spyOn(api, 'linkCodes').mockResolvedValue([]);
    renderPage(<PairPage />, { path: '/admin/links/pair', route: '/admin/links/pair', me: ME.alice });
    await userEvent.click(await screen.findByRole('checkbox', { name: new RegExp(L.cap['notices:send'][0]) }));
    await userEvent.click(screen.getByRole('button', { name: L.code.create }));
    expect(api.createLinkCode).toHaveBeenCalledWith(['catalog:read', 'deployments:receive']);
    expect(await screen.findByText('AB12-CD34-EF56')).toBeInTheDocument();
    expect(screen.getByText(L.code.waiting)).toBeInTheDocument();
    await waitFor(() => expect(api.linkCodes).toHaveBeenCalled());
  });

  it('refuses a code with no capability', async () => {
    vi.spyOn(api, 'people').mockResolvedValue([]);
    vi.spyOn(api, 'createLinkCode');
    renderPage(<PairPage />, { path: '/admin/links/pair', route: '/admin/links/pair', me: ME.alice });
    for (const [name] of Object.values(L.cap))
      await userEvent.click(await screen.findByRole('checkbox', { name: new RegExp(name) }));
    await userEvent.click(screen.getByRole('button', { name: L.code.create }));
    expect(await screen.findByText(L.code.noneChosen)).toBeInTheDocument();
    expect(api.createLinkCode).not.toHaveBeenCalled();
  });
});

describe('LinkPage', () => {
  const open = (l: Link, exchanges: LinkExchange[] = []) => {
    vi.spyOn(api, 'link').mockResolvedValue(l);
    vi.spyOn(api, 'people').mockResolvedValue([]);
    vi.spyOn(api, 'linkExchanges').mockResolvedValue({ items: exchanges, nextCursor: null });
    return renderPage(<LinkPage />, { path: '/admin/links/l-1', route: '/admin/links/:id', me: ME.alice });
  };

  it('asks to compare the fingerprint while the link is pending here', async () => {
    const pending = link({ state: 'pending', confirmedHere: false, peerConfirmed: false });
    vi.spyOn(api, 'confirmLink').mockResolvedValue(link());
    open(pending);
    expect(await screen.findByRole('img', { name: /orbit cedar 0712 lumen/ })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: L.confirm.match }));
    expect(api.confirmLink).toHaveBeenCalledWith('l-1', 'orbit cedar 0712 lumen');
    expect(await screen.findByRole('heading', { name: L.detail.exchanges })).toBeInTheDocument();
  });

  it('cuts the link from the fingerprint step when the words differ', async () => {
    vi.spyOn(api, 'cutLink').mockResolvedValue(link({ state: 'cut' }));
    open(link({ state: 'pending', confirmedHere: false }));
    await userEvent.click(await screen.findByRole('button', { name: L.confirm.differ }));
    expect(api.cutLink).toHaveBeenCalledWith('l-1');
  });

  it('shows exchanges, filters refusals, edits capabilities and cuts after a confirmation', async () => {
    vi.spyOn(api, 'setLinkCapabilities').mockResolvedValue(
      link({ capabilities: ['catalog:read', 'deployments:receive'] }),
    );
    vi.spyOn(api, 'cutLink').mockResolvedValue(link({ state: 'cut', cutAt: '2026-10-31T09:00:00Z', cutBy: 'alice' }));
    open(link(), [
      ex({ id: 2 }),
      ex({ id: 1, operation: 'deployments', result: 'refused', status: 401, reason: 'bad-signature' }),
    ]);
    expect(await screen.findByText(L.ex.in.catalog)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /Refused/ }));
    expect(screen.queryByText(L.ex.in.catalog)).not.toBeInTheDocument();
    expect(screen.getByText('bad-signature', { exact: false })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('switch', { name: L.cap['notices:send'][0] }));
    expect(api.setLinkCapabilities).toHaveBeenCalledWith('l-1', ['catalog:read', 'deployments:receive']);

    await userEvent.click(screen.getByRole('button', { name: L.detail.cut }));
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(L.detail.cutQ('Alizé CI'));
    expect(api.cutLink).not.toHaveBeenCalled();
    await userEvent.click(within(alert).getByRole('button', { name: L.detail.cut }));
    expect(api.cutLink).toHaveBeenCalledWith('l-1');
    expect(await screen.findByText(/This link was cut on/)).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: L.cap['catalog:read'][0] })).toHaveAttribute('aria-disabled', 'true');
  });
});
