/** One `KEY=value` line of a .env file. */
export interface EnvLine {
  name: string;
  value: string;
  /** The section it sits under, from a heading comment above it; null when none. */
  group: string | null;
  /** The name looks like it holds a credential. */
  secret: boolean;
}

const NAME = /^[A-Za-z_][A-Za-z0-9_]*$/;
const SECRET_NAME = /(PASS|SECRET|TOKEN|KEY|PRIVATE|CREDENTIAL|AUTH|SALT|DSN)/i;
// `# ---- ASP.NET ----`, `### Database ###`, `# == Mail ==`
const DECORATED = /^#+\s*[-=#*]{2,}\s*(.*?)\s*[-=#*]*\s*$/;

/** Parses .env the way compose does for plain values: `export ` prefixes, quotes, inline ` #` comments.
 *  Groups come from heading comments: a decorated one (`# ---- Mail ----`) lasts until the next heading of either kind; a short
 *  comment of its own, after a blank line and right above a variable (`# Discord Bot Configuration`), until a blank line. */
export function parseEnv(text: string): EnvLine[] {
  const lines = text.replace(/\r\n?/g, '\n').split('\n');
  const result: EnvLine[] = [];
  let section: string | null = null; // from the last decorated heading
  let group: string | null = null;

  lines.forEach((raw, i) => {
    const line = raw.trim();
    if (line === '') { group = section; return; }
    if (line.startsWith('#')) {
      const decorated = DECORATED.exec(line);
      if (decorated?.[1]) { group = section = decorated[1]; return; }
      const text = line.replace(/^#+\s*/, '');
      const afterBlank = i === 0 || lines[i - 1].trim() === '';
      const beforeVariable = i + 1 < lines.length && /^\s*(export\s+)?[A-Za-z_]\w*\s*=/.test(lines[i + 1]);
      if (afterBlank && beforeVariable && text.length > 0 && text.length <= 40 && !text.endsWith('.')) { group = text; section = null; }
      return;
    }
    const eq = line.indexOf('=');
    if (eq <= 0) return;
    const name = line.slice(0, eq).replace(/^export\s+/, '').trim();
    if (!NAME.test(name)) return;
    result.push({ name, value: unquote(line.slice(eq + 1).trim()), group, secret: SECRET_NAME.test(name) });
  });
  return result;
}

function unquote(value: string): string {
  if (value.length >= 2 && value.startsWith("'") && value.endsWith("'")) return value.slice(1, -1);
  if (value.length >= 2 && value.startsWith('"') && value.endsWith('"'))
    return value.slice(1, -1).replace(/\\(["\\$nr])/g, (_, c: string) => ({ n: '\n', r: '\r' })[c] ?? c);
  const comment = value.search(/\s#/);
  return (comment >= 0 ? value.slice(0, comment) : value).trim();
}

/** The .env without the lines defining <paramref name="names"/>; everything else (comments, others) stays. */
export function removeFromEnv(text: string, names: ReadonlySet<string>): string {
  const newline = text.includes('\r\n') ? '\r\n' : '\n';
  return text
    .split(/\r?\n/)
    .filter((line) => {
      const m = /^\s*(?:export\s+)?([A-Za-z_]\w*)\s*=/.exec(line);
      return !m || !names.has(m[1]);
    })
    .join(newline);
}
