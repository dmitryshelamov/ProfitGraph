// A local stand-in for Bybit's public REST API in end-to-end tests: deterministic Spot candles and instruments, so no
// test ever reaches the exchange. Usage: node e2e/bybit-stub.ts <port>
import { createServer, type ServerResponse } from 'node:http';

const port = Number(process.argv[2] ?? '5296');
const listedAt = Date.UTC(2025, 11, 1);
const minutes: Record<string, number> = {
  '1': 1,
  '3': 3,
  '5': 5,
  '15': 15,
  '30': 30,
  '60': 60,
  '120': 120,
  '240': 240,
  '360': 360,
  '720': 720,
  D: 1440,
};
const instruments = [
  { symbol: 'BTCUSDT', baseCoin: 'BTC', quoteCoin: 'USDT' },
  { symbol: 'ETHUSDT', baseCoin: 'ETH', quoteCoin: 'USDT' },
];

/** A saw-tooth around 100 with a 12-candle period, so a fast SMA crosses a slow one again and again. */
function price(index: number): number {
  const phase = ((index % 12) + 12) % 12;

  return 100 + (phase < 6 ? phase : 12 - phase) * 2;
}

function send(response: ServerResponse, body: unknown, status = 200) {
  response.writeHead(status, { 'Content-Type': 'application/json' });
  response.end(JSON.stringify(body));
}

function klines(url: URL) {
  const interval = minutes[url.searchParams.get('interval') ?? ''];
  const symbol = url.searchParams.get('symbol') ?? '';
  const start = Number(url.searchParams.get('start') ?? '0');
  const end = Number(url.searchParams.get('end') ?? String(Date.now()));
  const limit = Number(url.searchParams.get('limit') ?? '200');

  if (!interval || !instruments.some((instrument) => instrument.symbol === symbol)) {
    return { retCode: 10001, retMsg: 'Invalid request', result: {} };
  }

  const step = interval * 60_000;
  const closedBefore = Math.floor(Date.now() / step) * step;
  const rows: string[][] = [];

  // Bybit pages backwards: the newest candles inside [start, end] come first.
  for (
    let time = Math.floor(end / step) * step;
    time >= Math.max(start, listedAt) && rows.length < limit;
    time -= step
  ) {
    if (time + step > closedBefore) {
      continue;
    }

    const index = (time - listedAt) / step;
    const open = price(index - 1);
    const close = price(index);
    rows.push([
      String(time),
      String(open),
      String(Math.max(open, close) + 1),
      String(Math.min(open, close) - 1),
      close.toString(),
      '10',
      '1000',
    ]);
  }

  return {
    retCode: 0,
    retMsg: 'OK',
    result: { category: 'spot', symbol, list: rows },
    time: Date.now(),
  };
}

function instrumentsInfo(url: URL) {
  const symbol = url.searchParams.get('symbol');

  return {
    retCode: 0,
    retMsg: 'OK',
    result: {
      category: 'spot',
      list: instruments
        .filter((instrument) => !symbol || instrument.symbol === symbol)
        .map((instrument) => ({
          ...instrument,
          status: 'Trading',
          lotSizeFilter: {
            basePrecision: '0.000001',
            quotePrecision: '0.00000001',
            minOrderAmt: '5',
            minOrderQty: '0.000048',
          },
          priceFilter: { tickSize: '0.01' },
        })),
    },
    time: Date.now(),
  };
}

createServer((request, response) => {
  const url = new URL(request.url ?? '/', `http://127.0.0.1:${port}`);

  if (url.pathname === '/health') {
    send(response, { ok: true });
  } else if (url.pathname === '/v5/market/kline') {
    send(response, klines(url));
  } else if (url.pathname === '/v5/market/instruments-info') {
    send(response, instrumentsInfo(url));
  } else {
    send(response, { retCode: 10001, retMsg: `Not stubbed: ${url.pathname}`, result: {} }, 404);
  }
}).listen(port, '127.0.0.1');
