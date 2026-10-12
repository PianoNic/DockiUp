import { lineSeparatorFor } from './code-editor';

describe('lineSeparatorFor', () => {
  it('keeps CRLF when every line break is CRLF', () => {
    expect(lineSeparatorFor('a=1\r\nb=2\r\n')).toBe('\r\n');
  });

  it('uses the default for LF files', () => {
    expect(lineSeparatorFor('a=1\nb=2\n')).toBeNull();
  });

  it('uses the default for mixed files, so every line break splits a line', () => {
    expect(lineSeparatorFor('# header\r\n\nA=1\nB=2\r\n')).toBeNull();
    expect(lineSeparatorFor('\nA=1\r\n')).toBeNull();
  });
});
