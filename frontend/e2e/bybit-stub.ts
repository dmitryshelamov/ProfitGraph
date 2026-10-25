// A local stand-in for Bybit in end-to-end tests: deterministic Spot candles and instruments over REST, and a public
// stream that pushes a fixed ticker, so no test ever reaches the exchange. Usage: node e2e/bybit-stub.ts <port>
import { createHash } from 'node:crypto';
import { createServer, type ServerResponse } from 'node:http';
import type { Duplex } from 'node:stream';

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
/** The last price every ticker of the public stream reports. */
const tickerPrice = '104';
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

/** One unmasked server frame: text (0x1), close (0x8) or pong (0xA). */
function frame(opcode: number, payload: Buffer = Buffer.alloc(0)): Buffer {
  const length = payload.length;
  const header =
    length < 126
      ? Buffer.from([0x80 | opcode, length])
      : Buffer.from([0x80 | opcode, 126, length >> 8, length & 0xff]);

  return Buffer.concat([header, payload]);
}

/**
 * Bybit's public Spot WebSocket, as little as the Worker needs: a `tickers.SYMBOL` subscription receives the fixed
 * price now and every two seconds; kline subscriptions stay silent, so bots run on the history the REST stub serves.
 * The private stream is accepted and stays silent too: e2e bots trade against the Worker's in-memory simulation.
 */
function serveStream(socket: Duplex) {
  const timers: NodeJS.Timeout[] = [];
  let pending = Buffer.alloc(0);
  const ticker = (topic: string) => {
    const message = {
      topic,
      ts: Date.now(),
      type: 'snapshot',
      data: { symbol: topic.slice('tickers.'.length), lastPrice: tickerPrice },
    };
    socket.write(frame(0x1, Buffer.from(JSON.stringify(message))));
  };

  socket.on('data', (chunk: Buffer) => {
    pending = Buffer.concat([pending, chunk]);

    // Client frames are masked; messages here are small, so one frame carries one message.
    while (pending.length >= 2) {
      const opcode = pending[0]! & 0x0f;
      let length = pending[1]! & 0x7f;
      let offset = 2;

      if (length === 126) {
        length = pending.readUInt16BE(2);
        offset = 4;
      } else if (length === 127) {
        socket.destroy();

        return;
      }

      if (pending.length < offset + 4 + length) {
        return;
      }

      const mask = pending.subarray(offset, offset + 4);
      const payload = Buffer.from(pending.subarray(offset + 4, offset + 4 + length));
      payload.forEach((byte, index) => (payload[index] = byte ^ mask[index % 4]!));
      pending = pending.subarray(offset + 4 + length);

      if (opcode === 0x8) {
        socket.end(frame(0x8));

        return;
      }

      if (opcode === 0x9) {
        socket.write(frame(0xa, payload));
      } else if (opcode === 0x1) {
        const message = JSON.parse(payload.toString()) as { op?: string; args?: string[] };

        for (const topic of message.op === 'subscribe' ? (message.args ?? []) : []) {
          if (topic.startsWith('tickers.')) {
            ticker(topic);
            timers.push(setInterval(() => ticker(topic), 2000));
          }
        }
      }
    }
  });
  socket.on('close', () => timers.forEach((timer) => clearInterval(timer)));
  socket.on('error', () => socket.destroy());
}

const server = createServer((request, response) => {
  const url = new URL(request.url ?? '/', `http://127.0.0.1:${port}`);

  if (url.pathname === '/health') {
    send(response, { ok: true });
  } else if (url.pathname === '/v5/market/kline') {
    send(response, klines(url));
  } else if (url.pathname === '/v5/market/instruments-info') {
    send(response, instrumentsInfo(url));
  } else if (url.pathname === '/v5/market/time') {
    send(response, { retCode: 0, retMsg: 'OK', result: {}, time: Date.now() });
  } else if (url.pathname === '/v5/account/fee-rate') {
    // Signed in production; the stub checks nothing, because the e2e key is invented.
    send(response, {
      retCode: 0,
      retMsg: 'OK',
      result: {
        list: [
          { symbol: url.searchParams.get('symbol'), makerFeeRate: '0.001', takerFeeRate: '0.001' },
        ],
      },
    });
  } else {
    send(response, { retCode: 10001, retMsg: `Not stubbed: ${url.pathname}`, result: {} }, 404);
  }
});

server.on('upgrade', (request, socket) => {
  const key = request.headers['sec-websocket-key'];

  if (!key || !['/v5/public/spot', '/v5/private'].includes(request.url ?? '')) {
    socket.destroy();

    return;
  }

  const accept = createHash('sha1')
    .update(`${key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11`)
    .digest('base64');
  socket.write(
    'HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n' +
      `Sec-WebSocket-Accept: ${accept}\r\n\r\n`,
  );
  serveStream(socket);
});
server.listen(port, '127.0.0.1');
