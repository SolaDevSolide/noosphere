import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../i18n';
import { DocMarkdown, parseDoc, safeHref, type Resolve } from './DocMarkdown';

const resolve: Resolve = (_kind, id) =>
  id === 'missing'
    ? { exists: false, restricted: false, title: id, href: null }
    : { exists: true, restricted: false, title: id.toUpperCase(), href: `/x/${id}` };

function show(body: string) {
  return render(
    <I18nProvider initial="en">
      <MemoryRouter>
        <DocMarkdown doc={parseDoc(body)} resolve={resolve} />
      </MemoryRouter>
    </I18nProvider>,
  ).container;
}

describe('DocMarkdown', () => {
  it('renders wiki links as chips and broken ones in red', () => {
    const c = show('Run [[application:crm]], then [[flow:missing|the old flow]].');
    const chip = c.querySelector('a.wl');
    expect(chip?.getAttribute('href')).toBe('/x/crm');
    expect(chip?.textContent).toBe('CRM');
    const broken = c.querySelector('.wl.broken');
    expect(broken?.textContent).toBe('the old flow');
    expect(broken?.getAttribute('data-tip')).toContain('missing');
  });

  it('never renders raw HTML, unsafe links or images', () => {
    const c = show(
      '<script>alert(1)</script>\n\n<b>bold?</b> [bad](javascript:alert(1)) [ok](https://example.com)\n\n![shot](https://example.com/a.png)',
    );
    expect(c.querySelector('script')).toBeNull();
    expect(c.querySelector('b')).toBeNull();
    expect(c.querySelector('img')).toBeNull();
    expect(c.textContent).toContain('<script>');
    const hrefs = [...c.querySelectorAll('a')].map((a) => a.getAttribute('href'));
    expect(hrefs).not.toContain('javascript:alert(1)');
    expect(hrefs).toContain('https://example.com');
    expect(hrefs).toContain('https://example.com/a.png');
  });

  it('renders callouts, tables and code blocks without links inside code', () => {
    const c = show(
      '> [!WARNING] Careful\n> Not after 05:00.\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```bash\n# note\nls [[application:crm]]\n```',
    );
    expect(c.querySelector('.co.warn')?.textContent).toContain('Careful');
    expect(c.querySelectorAll('.tbl td')).toHaveLength(2);
    const pre = c.querySelector('pre');
    expect(pre?.textContent).toContain('[[application:crm]]');
    expect(pre?.querySelector('.wl')).toBeNull();
  });

  it('builds the table of contents from the main headings', () => {
    const d = parseDoc('# Intro\n\n## Check the file\n\n### Detail\n\n## Check the file');
    expect(d.toc.map((x) => x.text)).toEqual(['Intro', 'Check the file', 'Check the file']);
    expect(new Set(d.toc.map((x) => x.id)).size).toBe(3);
  });

  it('keeps only safe link targets', () => {
    expect(safeHref('https://a.b')).toBe('https://a.b');
    expect(safeHref('/pages/x')).toBe('/pages/x');
    expect(safeHref('//evil.example')).toBeNull();
    expect(safeHref('data:text/html,x')).toBeNull();
  });
});
