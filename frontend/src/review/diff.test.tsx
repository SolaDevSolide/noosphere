import { render, screen } from '@testing-library/react';
import type { FieldDiff } from '../api/types';
import { I18nProvider } from '../i18n';
import { memberModel } from '../test/fixture';
import { DiffView, displayValue, listChanges, wordSegments } from './diff';
import { en } from '../i18n/en';

describe('wordSegments', () => {
  it('marks added and removed words and keeps the unchanged text', () => {
    const s = wordSegments('Sends invoices by email.', 'Sends invoices by email and post.');
    expect(s.map((x) => x.op)).toEqual(['same', 'add', 'same']);
    expect(s.map((x) => x.text).join('')).toBe('Sends invoices by email and post.');
    expect(s[1].text.trim()).toBe('and post');
  });

  it('reports a replaced word as removed then added', () => {
    const s = wordSegments('runs on SAP', 'runs on Oracle');
    expect(s).toEqual([
      { text: 'runs on ', op: 'same' },
      { text: 'SAP', op: 'del' },
      { text: 'Oracle', op: 'add' },
    ]);
  });

  it('does not highlight whitespace alone, and keeps the new line breaks', () => {
    const s = wordSegments('One line.', 'One line.\n\nNew paragraph.');
    expect(s.filter((x) => x.op !== 'same').every((x) => x.text.trim().length > 0)).toBe(true);
    expect(s.map((x) => x.text).join('')).toBe('One line.\n\nNew paragraph.');
  });
});

describe('listChanges', () => {
  it('finds removed and added items', () => {
    expect(listChanges(['a', 'b'], ['b', 'c'])).toMatchObject({ removed: ['a'], added: ['c'] });
    expect(listChanges(null, ['x'])).toMatchObject({ removed: [], added: ['x'] });
  });
});

describe('displayValue', () => {
  const m = memberModel();
  it('shows names instead of ids and translates enum values', () => {
    expect(displayValue(en, m, 'teamId', 'team-finance')).toBe('Team Finance');
    expect(displayValue(en, m, 'domainId', 'finance')).toBe('Finance');
    expect(displayValue(en, m, 'lifecycle', 'retiring')).toBe('Retiring');
    expect(displayValue(en, m, 'tech', '.NET')).toBe('.NET');
    expect(displayValue(en, m, 'plannedChange', { summary: 'v4', date: '2026-11-04' })).toBe('v4 (2026-11-04)');
    expect(displayValue(en, m, 'tech', null)).toBe('');
  });
});

describe('DiffView', () => {
  const m = memberModel();
  const diff: FieldDiff[] = [
    { field: 'tech', label: 'Technology', kind: 'text', before: '.NET', after: '.NET 10' },
    { field: 'teamId', label: 'Team', kind: 'ref', before: 'team-crm', after: 'team-finance' },
    {
      field: 'ownerTeamId',
      label: 'Owner',
      kind: 'ref',
      before: 'x',
      after: 'y',
      beforeTitle: 'Old owner',
      afterTitle: 'New owner',
    },
    { field: 'dataObjectIds', label: 'Data', kind: 'list', before: ['customer'], after: ['customer', 'invoice'] },
    {
      field: 'description',
      label: 'Description',
      kind: 'markdown',
      before: 'Pays claims.',
      after: 'Pays claims fast.',
    },
  ];
  const view = (d: FieldDiff[], conflicts: string[] = []) =>
    render(
      <I18nProvider initial="en">
        <DiffView diff={d} m={m} leftTitle="Published · v3" rightTitle="Proposed" conflicts={conflicts} />
      </I18nProvider>,
    );

  it('compares short fields side by side, with removed values on the left and added ones on the right', () => {
    const { container } = view(diff);
    const [left, right] = [...container.querySelectorAll('.dside')];
    expect(left.querySelector('h3')?.textContent).toBe('Published · v3');
    expect(right.querySelector('h3')?.textContent).toBe('Proposed');
    expect(left.querySelectorAll('del.d')[0].textContent).toContain('.NET');
    expect(right.querySelectorAll('ins.d')[0].textContent).toContain('.NET 10');
    expect(left.textContent).toContain('Team CRM');
    expect(right.textContent).toContain('Team Finance');
    expect(left.textContent).toContain('Old owner');
    expect(right.textContent).toContain('New owner');
    // a list highlights only the item added
    const added = [...right.querySelectorAll('ins.d')].map((x) => x.textContent);
    expect(added.some((x) => x?.includes('Invoice'))).toBe(true);
    expect(added.some((x) => x?.includes('Customer'))).toBe(false);
  });

  it('labels the marks for screen readers', () => {
    view(diff);
    expect(screen.getAllByText('added:', { exact: false }).length).toBeGreaterThan(0);
    expect(screen.getAllByText('removed:', { exact: false }).length).toBeGreaterThan(0);
  });

  it('shows Markdown fields as an inline word diff across the full width', () => {
    const { container } = view(diff);
    const long = container.querySelector('.dlong')!;
    expect(long.querySelector('h3')?.textContent).toContain('Description');
    expect(long.querySelector('ins.d')?.textContent).toContain('fast');
    expect(long.querySelector('del.d')).toBeNull();
    expect(container.querySelectorAll('.dside dt')).toHaveLength(8);
  });

  it('flags the fields changed on both sides', () => {
    view(diff.slice(0, 1), ['tech']);
    expect(screen.getByText('Changed on both sides')).toBeInTheDocument();
  });

  it('says so when nothing changed', () => {
    view([]);
    expect(screen.getByText('No field changed.')).toBeInTheDocument();
  });
});

describe('wordSegments around line breaks', () => {
  it('keeps the whitespace of an added block outside the highlight', () => {
    const s = wordSegments('Intro.', 'Intro.\n\n- one\n- two');
    const added = s.filter((x) => x.op === 'add');
    expect(added).toHaveLength(1);
    expect(added[0].text.startsWith('-')).toBe(true);
    expect(s.map((x) => x.text).join('')).toBe('Intro.\n\n- one\n- two');
  });
  it('keeps struck words apart from their neighbours', () => {
    const s = wordSegments('pays claims fast', 'pays claims');
    expect(s.map((x) => x.text).join('')).toContain('claims fast');
  });
});
