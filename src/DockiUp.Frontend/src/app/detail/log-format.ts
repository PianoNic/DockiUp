/**
 * Turns raw container log lines into coloured HTML for the dark log surface:
 * - a leading Docker timestamp (RFC 3339, UTC) shown in the browser's locale and time zone, dimmed
 * - the "[container]" prefix in a stable colour per container
 * - ANSI SGR colour codes (16, 256 and true colour) rendered; other escape sequences dropped
 * - without ANSI colours, the first log-level word (fail/error/warn/info/debug, Postgres' LOG/ERROR…) coloured
 * Every piece of log text is HTML-escaped here, so the result is safe to bind as trusted HTML.
 */

const TIMESTAMP = /^(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d+)?Z) /;
const PREFIX = /^\[([^\]\s]+)\] /;
const ESCAPE = /\x1b\[([\d;?]*)([A-Za-z])/g;
// A level word only where loggers put one: "info:", "[WARN]", "LOG:" or "level=error" (not "no error here").
const LEVEL_WORDS = 'fail|fatal|crit(?:ical)?|panic|error|err|emerg|alert|warn(?:ing)?|info|notice|log|dbug|debug|trce|trace';
const LEVEL = new RegExp(String.raw`\b(${LEVEL_WORDS})\b(?=[:\]])|(?<=\blevel=)(${LEVEL_WORDS})\b`, 'i');

const TIME = new Intl.DateTimeFormat(undefined, {
  year: '2-digit', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3,
});

const escapeHtml = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/** Plain text of a line: escape sequences removed (search, download). */
export function stripAnsi(line: string): string {
  return line.replace(ESCAPE, '');
}

export function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : TIME.format(date);
}

/** One line as HTML. `containers` fixes each container's colour slot (same order, same colour every time). */
export function renderLogLine(line: string, containers: readonly string[]): string {
  let rest = line;
  let html = '';

  const ts = TIMESTAMP.exec(rest);
  if (ts) {
    html += `<span class="l-ts">${escapeHtml(formatTimestamp(ts[1]))}</span> `;
    rest = rest.slice(ts[0].length);
  }
  // Only a known container name is a prefix: "[WARN] disk low" is a log level, not a container.
  const prefix = PREFIX.exec(rest);
  if (prefix && containers.includes(prefix[1])) {
    const slot = containers.indexOf(prefix[1]) % 6;
    html += `<span class="l-c${slot}">[${escapeHtml(prefix[1])}]</span> `;
    rest = rest.slice(prefix[0].length);
  }

  ESCAPE.lastIndex = 0;
  return html + (/\x1b\[[\d;]*m/.test(rest) ? renderAnsi(rest) : renderLevel(stripAnsi(rest)));
}

function renderLevel(text: string): string {
  const m = LEVEL.exec(text);
  if (!m) return escapeHtml(text);
  const word = m[1] ?? m[2];
  return escapeHtml(text.slice(0, m.index)) + `<span class="${levelClass(word)}">${escapeHtml(word)}</span>` + escapeHtml(text.slice(m.index + word.length));
}

function levelClass(word: string): string {
  const w = word.toLowerCase();
  if (/^(fail|fatal|crit|critical|panic|error|err|emerg|alert)$/.test(w)) return 'l-err';
  if (w.startsWith('warn')) return 'l-warn';
  if (/^(dbug|debug|trce|trace)$/.test(w)) return 'l-dbg';
  return 'l-info';
}

interface Sgr { fg?: string; bg?: string; bold?: boolean; dim?: boolean; italic?: boolean; underline?: boolean }

function renderAnsi(text: string): string {
  let out = '';
  let state: Sgr = {};
  let last = 0;
  for (const m of text.matchAll(ESCAPE)) {
    out += wrap(text.slice(last, m.index), state);
    last = m.index! + m[0].length;
    if (m[2] === 'm') state = applySgr(state, m[1]);
  }
  return out + wrap(text.slice(last), state);
}

function wrap(text: string, s: Sgr): string {
  if (!text) return '';
  const style = [
    s.fg && `color:${s.fg}`,
    s.bg && `background-color:${s.bg}`,
    s.bold && 'font-weight:700',
    s.dim && 'opacity:.65',
    s.italic && 'font-style:italic',
    s.underline && 'text-decoration:underline',
  ].filter(Boolean).join(';');
  return style ? `<span style="${style}">${escapeHtml(text)}</span>` : escapeHtml(text);
}

function applySgr(state: Sgr, params: string): Sgr {
  const codes = params === '' ? [0] : params.split(';').map((p) => Number(p) || 0);
  let s = { ...state };
  for (let i = 0; i < codes.length; i++) {
    const c = codes[i];
    if (c === 0) s = {};
    else if (c === 1) s.bold = true;
    else if (c === 2) s.dim = true;
    else if (c === 3) s.italic = true;
    else if (c === 4) s.underline = true;
    else if (c === 22) s.bold = s.dim = false;
    else if (c === 23) s.italic = false;
    else if (c === 24) s.underline = false;
    else if (c >= 30 && c <= 37) s.fg = ANSI16[c - 30];
    else if (c >= 90 && c <= 97) s.fg = ANSI16[c - 90 + 8];
    else if (c === 39) s.fg = undefined;
    else if (c >= 40 && c <= 47) s.bg = ANSI16[c - 40];
    else if (c >= 100 && c <= 107) s.bg = ANSI16[c - 100 + 8];
    else if (c === 49) s.bg = undefined;
    else if ((c === 38 || c === 48) && codes[i + 1] === 5) {
      const color = xterm256(codes[i + 2]);
      if (c === 38) s.fg = color; else s.bg = color;
      i += 2;
    } else if ((c === 38 || c === 48) && codes[i + 1] === 2) {
      const color = `rgb(${codes[i + 2] & 255},${codes[i + 3] & 255},${codes[i + 4] & 255})`;
      if (c === 38) s.fg = color; else s.bg = color;
      i += 4;
    }
  }
  return s;
}

// The 16 terminal colours, tuned for the #1d2433 log surface (readable, same family as the editor theme).
const ANSI16 = [
  '#5f6b84', '#ff8a80', '#c3e88d', '#ffcb8b', '#8ab4f8', '#f48fb1', '#80deea', '#dbe2f9',
  '#8a94a8', '#ffb4ab', '#d4f5b0', '#ffe0a3', '#aecbfa', '#ffc1e0', '#b2ebf2', '#ffffff',
];

function xterm256(n: number): string {
  if (n < 16) return ANSI16[n] ?? ANSI16[7];
  if (n >= 232) {
    const v = 8 + (n - 232) * 10;
    return `rgb(${v},${v},${v})`;
  }
  const i = n - 16;
  const level = (x: number) => (x === 0 ? 0 : 55 + x * 40);
  return `rgb(${level(Math.floor(i / 36))},${level(Math.floor(i / 6) % 6)},${level(i % 6)})`;
}
