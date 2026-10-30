import { validateSettings } from '../admin/SettingsPage';
import { en } from '../i18n/en';
import { graphKey } from '../explorer/graph/engine';
import { leavingNodes } from '../explorer/lens/Lens';
import type { LNode } from '../explorer/lens/layouts';
import { documentTitle } from './settings';

describe('instance settings', () => {
  it('builds the browser tab title from the instance name', () => {
    expect(documentTitle('Alizé map')).toBe('Alizé map');
    expect(documentTitle('Alizé map', 'Billing')).toBe('Billing · Alizé map');
    expect(documentTitle('  ', 'Billing')).toBe('Billing · Noosphere');
  });
  it('requires an instance name and limits the lengths', () => {
    const s = { instanceName: '', organisationName: 'x'.repeat(81), defaultLanguage: 'en' as const, version: 0 };
    expect(validateSettings(en, s)).toEqual({
      instanceName: en.record.errors.required,
      organisationName: en.record.errors.tooLong(80),
    });
    expect(validateSettings(en, { ...s, instanceName: 'Alizé', organisationName: '' })).toEqual({});
  });
});

describe('explorer polish', () => {
  const node = (key: string): LNode => ({ key, id: key, x: 0, y: 0, w: 10, h: 10, role: 'node' });
  it('keeps the nodes that leave the lens for their fade-out', () => {
    const prev = ['a', 'b', 'c'].map((k) => ({ n: node(k), dx: 5, dy: 6 }));
    expect(leavingNodes(prev, [node('b'), node('d')]).map((x) => x.n.key)).toEqual(['a', 'c']);
    expect(
      leavingNodes(
        prev,
        prev.map((x) => x.n),
      ),
    ).toEqual([]);
  });
  it('identifies a graph by its applications and links, whatever their order', () => {
    const a = graphKey({ ids: ['x', 'y'], links: [{ key: 'x|y' }] } as Parameters<typeof graphKey>[0]);
    const b = graphKey({ ids: ['y', 'x'], links: [{ key: 'x|y' }] } as Parameters<typeof graphKey>[0]);
    const c = graphKey({ ids: ['x'], links: [] } as unknown as Parameters<typeof graphKey>[0]);
    expect(a).toBe(b);
    expect(a).not.toBe(c);
  });
});
