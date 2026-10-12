import { parseEnv, removeFromEnv } from './env-parse';

describe('parseEnv', () => {
  const env = [
    '# Tessaly on the VPS: secrets and per-install values.',
    '',
    '# ---- ASP.NET ----',
    '# Encrypts the API keys of providers added in the admin console.',
    'DataProtection__Key=abc123',
    '',
    '# Discord Bot Configuration',
    'DISCORD_BOT_TOKEN="tok\\"en"',
    "export DISCORD_USER_ID='5662'",
    '',
    'LOG_LEVEL=INFO # default',
    'not a variable',
  ].join('\r\n');

  it('reads names, values and quotes', () => {
    const lines = parseEnv(env);
    expect(lines.map((l) => l.name)).toEqual(['DataProtection__Key', 'DISCORD_BOT_TOKEN', 'DISCORD_USER_ID', 'LOG_LEVEL']);
    expect(lines[1].value).toBe('tok"en');
    expect(lines[2].value).toBe('5662');
    expect(lines[3].value).toBe('INFO');
  });

  it('takes groups from heading comments, not from descriptive ones', () => {
    expect(parseEnv(env).map((l) => l.group)).toEqual(['ASP.NET', 'Discord Bot Configuration', 'Discord Bot Configuration', null]);
  });

  it('flags names that look like credentials', () => {
    expect(parseEnv(env).map((l) => l.secret)).toEqual([true, true, false, false]);
  });
});

describe('removeFromEnv', () => {
  it('drops only the named variables and keeps line endings', () => {
    expect(removeFromEnv('# c\r\nA=1\r\nexport B=2\r\nC=3', new Set(['A', 'B']))).toBe('# c\r\nC=3');
  });
});
