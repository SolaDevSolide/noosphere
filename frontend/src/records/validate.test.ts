import { en } from '../i18n/en';
import { validateApp } from './ApplicationRecord';
import { validateFlow } from './FlowRecord';

const flow = {
  name: 'X',
  fromId: 'a',
  toId: 'b',
  type: 'api' as const,
  schedule: '',
  dataObjectIds: [],
  ownerTeamId: '',
  status: 'live' as const,
  volume: '',
  description: '',
  changeSummary: '',
  changeDate: '',
};

describe('record validation', () => {
  it('requires a name and two different ends', () => {
    expect(validateFlow(en, flow)).toEqual({});
    expect(validateFlow(en, { ...flow, name: ' ' }).name).toBe(en.record.errors.required);
    expect(validateFlow(en, { ...flow, toId: 'a' }).toId).toBe(en.record.errors.sameEnds);
    expect(validateFlow(en, { ...flow, changeDate: '2026-11-04' }).changeSummary).toBe(
      en.record.errors.summaryRequired,
    );
  });
  it('limits application fields', () => {
    const app = {
      name: 'Billing',
      domainId: 'finance',
      kind: 'app' as const,
      lifecycle: 'live' as const,
      visibility: 'everyone' as const,
      teamId: '',
      tech: '',
      description: '',
      protectedNotes: '',
    };
    expect(validateApp(en, app)).toEqual({});
    expect(validateApp(en, { ...app, name: 'x'.repeat(121) }).name).toBe(en.record.errors.tooLong(120));
  });
});
