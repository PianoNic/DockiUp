import { formatTimestamp, renderLogLine, stripAnsi } from './log-format';

describe('renderLogLine', () => {
  const names = ['web', 'db'];

  it('escapes log text', () => {
    expect(renderLogLine('<script>alert(1)</script> & "x"', names)).toBe('&lt;script&gt;alert(1)&lt;/script&gt; &amp; &quot;x&quot;');
  });

  it('shows the Docker timestamp in local time and colours the container prefix by its slot', () => {
    const html = renderLogLine('2026-10-02T06:31:00.643102243Z [db] ready', names);
    expect(html).toContain(`<span class="l-ts">${formatTimestamp('2026-10-02T06:31:00.643102243Z')}</span>`);
    expect(html).toContain('<span class="l-c1">[db]</span> ready');
    expect(html).not.toContain('2026-10-02T06');
  });

  it('renders ANSI colours and drops other escape sequences', () => {
    const html = renderLogLine('\x1b[32mok\x1b[0m done\x1b[K', names);
    expect(html).toBe('<span style="color:#c3e88d">ok</span> done');
    expect(renderLogLine('\x1b[38;2;10;20;30mrgb\x1b[0m', names)).toBe('<span style="color:rgb(10,20,30)">rgb</span>');
  });

  it('colours the first log level word when the line has no ANSI colours', () => {
    expect(renderLogLine('fail: Microsoft.Hosting', names)).toBe('<span class="l-err">fail</span>: Microsoft.Hosting');
    expect(renderLogLine('2026-10-02 LOG:  checkpoint', names)).toContain('<span class="l-info">LOG</span>:');
    expect(renderLogLine('[WARN] disk low', names)).toContain('<span class="l-warn">WARN</span>');
    expect(renderLogLine('informational text', names)).toBe('informational text');
    expect(renderLogLine('no error here, see the log file', names)).toBe('no error here, see the log file');
    expect(renderLogLine('time=1 level=error msg=x', names)).toContain('level=<span class="l-err">error</span>');
  });

  it('strips escape sequences for search and download', () => {
    expect(stripAnsi('\x1b[1;31merror\x1b[0m')).toBe('error');
  });
});
