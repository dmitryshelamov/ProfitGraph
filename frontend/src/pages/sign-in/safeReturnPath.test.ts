import { safeReturnPath } from './safeReturnPath';

const origin = 'https://profitgraph.example';

describe('safeReturnPath', () => {
  it('keeps a path of this application with its query and fragment', () => {
    expect(safeReturnPath('/strategies', origin)).toBe('/strategies');
    expect(safeReturnPath('/bots/42?tab=orders#log', origin)).toBe('/bots/42?tab=orders#log');
    expect(safeReturnPath(`${origin}/backtests`, origin)).toBe('/backtests');
  });

  it('falls back to the root without a return address', () => {
    expect(safeReturnPath(undefined, origin)).toBe('/');
    expect(safeReturnPath('', origin)).toBe('/');
  });

  it('rejects protocol-relative addresses', () => {
    expect(safeReturnPath('//evil.example', origin)).toBe('/');
    expect(safeReturnPath('//evil.example/strategies', origin)).toBe('/');
  });

  it('rejects a backslash that browsers read as a second slash', () => {
    expect(safeReturnPath('/\\evil.example', origin)).toBe('/');
    expect(safeReturnPath('\\\\evil.example', origin)).toBe('/');
    expect(safeReturnPath('/\t/evil.example', origin)).toBe('/');
  });

  it('rejects absolute addresses of another origin', () => {
    expect(safeReturnPath('https://evil.example/strategies', origin)).toBe('/');
    expect(safeReturnPath('http://profitgraph.example/strategies', origin)).toBe('/');
  });

  it('rejects other schemes', () => {
    expect(safeReturnPath('javascript:alert(1)', origin)).toBe('/');
    expect(safeReturnPath('data:text/html,<script>alert(1)</script>', origin)).toBe('/');
  });

  it('uses the current origin by default', () => {
    expect(safeReturnPath('/strategies')).toBe('/strategies');
    expect(safeReturnPath('//evil.example')).toBe('/');
  });
});
