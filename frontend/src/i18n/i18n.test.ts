import { en } from './en';
import { fr } from './fr';

function shape(o: unknown, path = ''): string[] {
  if (o && typeof o === 'object' && !Array.isArray(o))
    return Object.entries(o).flatMap(([k, v]) => shape(v, path + '.' + k));
  return [
    path +
      ':' +
      (typeof o === 'function'
        ? 'fn' + (o as (...a: unknown[]) => unknown).length
        : Array.isArray(o)
          ? 'arr' + o.length
          : typeof o),
  ];
}

describe('dictionaries', () => {
  it('have the same keys and argument counts in English and French', () => {
    expect(shape(fr)).toEqual(shape(en));
  });
  it('translate the strings that differ', () => {
    expect(fr.login.title).not.toBe(en.login.title);
    expect(fr.graph.heading).not.toBe(en.graph.heading);
  });
});
