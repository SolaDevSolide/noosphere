import { render, screen } from '@testing-library/react';
import { ApiError } from '../api/client';
import type { AccessLogEntry, FieldDiff, Person } from '../api/types';
import { EMPTY_FILTERS, appMeetsSens, anyFilter, filterCount } from '../domain/model';
import { I18nProvider } from '../i18n';
import { en } from '../i18n/en';
import { fr } from '../i18n/fr';
import { secretErrors } from '../mocks/access';
import { DiffView, displayValue } from '../review/diff';
import { memberModel } from '../test/fixture';
import { isFullExport, whyText } from './AccessPage';
import { joinNames, listFor } from './RestrictDialog';
import { fieldKey, isFieldRefusal, isSecretMessage, secretFields, translateMessage } from './secret';

const SECRET_MSG =
  'This looks like a secret (GitHub token). Noosphere is documentation, not a vault: store it in your secret manager and write where it lives instead.';

describe('secret refusals', () => {
  it('recognises and translates the refusal, and leaves other messages alone', () => {
    expect(isSecretMessage(SECRET_MSG)).toBe(true);
    expect(isSecretMessage('Name is required.')).toBe(false);
    expect(translateMessage(fr, SECRET_MSG)).toContain('jeton GitHub');
    expect(translateMessage(en, 'Name is required.')).toBe('Name is required.');
  });

  it('names fields as the forms do', () => {
    expect(fieldKey('$.ProtectedNotes')).toBe('protectedNotes');
    expect(fieldKey('changes[0].after.description')).toBe('description');
    expect(fieldKey('body')).toBe('body');
  });

  it('lists the fields refused as secrets', () => {
    const e = new ApiError(422, 'Unprocessable content', undefined, {
      description: [SECRET_MSG],
      name: ['Name is required.'],
    });
    expect(isFieldRefusal(e)).toBe(true);
    expect(secretFields(e)).toEqual(['description']);
    expect(isFieldRefusal(new ApiError(500, 'boom'))).toBe(false);
  });

  it('the dev mock refuses tokens and accepts placeholders', () => {
    const token = 'gh' + 'p_' + 'a1B2c3D4'.repeat(4) + 'zZ9x';
    expect(Object.keys(secretErrors({ description: 'token ' + token }))).toEqual(['description']);
    expect(secretErrors({ protectedNotes: 'password: <in Vault>' })).toEqual({});
  });
});

describe('restrict dialog helpers', () => {
  const people: Person[] = [
    { username: 'claire', displayName: 'Claire Petit', role: 'guest', teamIds: [] },
    { username: 'bob', displayName: 'Bob Durand', role: 'member', teamIds: ['team-finance'] },
  ];
  it('keeps teams and people for a restricted record, only guests for a public one', () => {
    const list = { teamIds: ['team-finance'], usernames: ['bob', 'claire'] };
    expect(listFor('restricted', list, people)).toEqual(list);
    expect(listFor('everyone', list, people)).toEqual({ teamIds: [], usernames: ['claire'] });
  });
  it('joins names in the reader language', () => {
    expect(joinNames('en', ['A', 'B', 'C'])).toBe('A, B, and C');
    expect(joinNames('fr', ['A', 'B'])).toBe('A et B');
  });
});

describe('sensitivity filter', () => {
  it('counts as a filter and keeps only applications at least that sensitive', () => {
    const f = { ...EMPTY_FILTERS, appSens: 'personal' as const };
    expect(filterCount(f)).toBe(1);
    expect(anyFilter(f)).toBe(true);
    const app = (s: 'internal' | 'sensitive') =>
      ({ id: 'x', name: 'X', domainId: 'd', restricted: false, sensitivity: s }) as never;
    expect(appMeetsSens(app('sensitive'), 'personal')).toBe(true);
    expect(appMeetsSens(app('internal'), 'personal')).toBe(false);
    expect(appMeetsSens({ id: 'r', domainId: 'd', restricted: true }, 'internal')).toBe(false);
    expect(appMeetsSens(app('internal'), null)).toBe(true);
  });
});

describe('masked protected notes', () => {
  it('shows a lock instead of the notes and says who can see them', () => {
    const d: FieldDiff[] = [
      {
        field: 'protectedNotes',
        label: 'Protected notes',
        kind: 'markdown',
        before: '••• protected',
        after: '••• protected',
      },
    ] as FieldDiff[];
    render(
      <I18nProvider initial="en">
        <DiffView diff={d} m={memberModel()} leftTitle="v3" rightTitle="v4" notesOwner="Team Finance" />
      </I18nProvider>,
    );
    expect(screen.getAllByText(/protected/).length).toBeGreaterThan(1);
    expect(screen.getByText(/Only Team Finance and administrators can see what changed/)).toBeInTheDocument();
  });
  it('names teams and people of an access list', () => {
    const m = memberModel();
    const team = [...m.teams.values()][0];
    expect(displayValue(en, m, 'access', 'team:' + team.id)).toBe(team.name);
    expect(displayValue(en, m, 'access', 'user:claire')).toBe('claire');
  });
});

describe('access log lines', () => {
  const line = (p: Partial<AccessLogEntry>): AccessLogEntry => ({
    id: 1,
    at: '2026-10-30T10:42:00Z',
    user: { username: 'alice', displayName: 'Alice Martin' },
    kind: 'application',
    entityId: 'billing',
    title: 'Billing',
    what: 'record',
    detail: null,
    admin: true,
    why: 'admin',
    whyTeam: null,
    owningTeam: null,
    ...p,
  });
  it('tells full exports from visible ones', () => {
    expect(isFullExport(line({ what: 'export', detail: 'scope full, json, 80 items' }))).toBe(true);
    expect(isFullExport(line({ what: 'export', detail: 'Yearly audit' }))).toBe(true);
    expect(isFullExport(line({ what: 'export', detail: 'scope visible, yaml, 70 items' }))).toBe(false);
    expect(isFullExport(line({}))).toBe(false);
  });
  it('explains why someone could open a record', () => {
    expect(whyText(en, line({}))).toBe('Administrator');
    expect(whyText(en, line({ why: 'listedTeam', whyTeam: { id: 't', name: 'Team Finance' } }))).toBe(
      'Team Finance is on the list',
    );
    expect(whyText(en, line({ what: 'export', detail: 'Yearly audit' }))).toBe('« Yearly audit »');
  });
});
