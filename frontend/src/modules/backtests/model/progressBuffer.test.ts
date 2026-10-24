import { LIVE_OBSERVATION_LIMIT, type LoadObservations, ProgressBuffer } from './progressBuffer';
import type { BacktestObservation } from './types';

function observation(sequence: number): BacktestObservation {
  return { sequence } as BacktestObservation;
}

/** Stored observations 1..count, served in pages of three like the API's cursor. */
function stored(count: number, calls: number[] = []): LoadObservations {
  return (after) => {
    calls.push(after);
    const items = Array.from({ length: Math.max(0, Math.min(3, count - after)) }, (_, index) =>
      observation(after + index + 1),
    );
    const last = items.at(-1)?.sequence ?? after;

    return Promise.resolve({ items, next: last < count ? last : null });
  };
}

const sequences = (buffer: ProgressBuffer) => buffer.observations.map((item) => item.sequence);

describe('progress buffer', () => {
  it('keeps messages in order and each sequence once', async () => {
    const buffer = new ProgressBuffer(stored(0));

    await buffer.receive(observation(1));
    await buffer.receive(observation(2));
    await buffer.receive(observation(2));
    await buffer.receive(observation(1));

    expect(sequences(buffer)).toEqual([1, 2]);
    expect(buffer.lastSequence).toBe(2);
  });

  it('fills a gap from the stored observations before showing what follows it', async () => {
    const calls: number[] = [];
    const buffer = new ProgressBuffer(stored(9, calls));
    await buffer.receive(observation(1));

    await buffer.receive(observation(8));

    expect(sequences(buffer)).toEqual([1, 2, 3, 4, 5, 6, 7, 8]);
    expect(calls).toEqual([1, 4]);
  });

  it('resumes from the subscription cursor with the latest stored history only', async () => {
    const calls: number[] = [];
    const buffer = new ProgressBuffer(stored(10, calls));

    await buffer.resume(10, 4);
    await buffer.receive(observation(10));
    await buffer.receive(observation(11));

    expect(sequences(buffer)).toEqual([7, 8, 9, 10, 11]);
    expect(calls).toEqual([6, 9]);
  });

  it('merges a message that a gap fill already delivered', async () => {
    const buffer = new ProgressBuffer(stored(5));

    await Promise.all([
      buffer.receive(observation(5)),
      buffer.receive(observation(3)),
      buffer.receive(observation(4)),
    ]);

    expect(sequences(buffer)).toEqual([1, 2, 3, 4, 5]);
  });

  it('keeps a bounded window of the latest observations', async () => {
    const buffer = new ProgressBuffer(stored(0));

    for (let sequence = 1; sequence <= LIVE_OBSERVATION_LIMIT + 5; sequence++) {
      await buffer.receive(observation(sequence));
    }

    expect(buffer.observations).toHaveLength(LIVE_OBSERVATION_LIMIT);
    expect(buffer.observations[0]?.sequence).toBe(6);
  });
});
