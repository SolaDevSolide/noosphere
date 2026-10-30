import { render } from '@testing-library/react';
import { Markdown } from './Markdown';

describe('Markdown', () => {
  it('renders paragraphs, lists and inline marks without raw HTML', () => {
    const { container } = render(
      <Markdown
        text={
          'Hello **bold** and `code`\n\n- one\n- two\n\n<script>x</script> [site](https://example.com) [bad](javascript:alert(1))'
        }
      />,
    );
    expect(container.querySelector('strong')?.textContent).toBe('bold');
    expect(container.querySelectorAll('li')).toHaveLength(2);
    expect(container.querySelector('script')).toBeNull();
    const links = container.querySelectorAll('a');
    expect(links).toHaveLength(1);
    expect(links[0].getAttribute('href')).toBe('https://example.com');
    expect(links[0].getAttribute('rel')).toContain('noopener');
  });
});
