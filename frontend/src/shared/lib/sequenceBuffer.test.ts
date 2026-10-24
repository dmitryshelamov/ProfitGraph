import { SequenceBuffer } from './sequenceBuffer';

const item = (sequence: number) => ({ sequence });

describe('sequence buffer', () => {
  it('keeps each sequence once and in order, filling a gap from storage before later items', async () => {
    const load = vi.fn(async (after: number) => ({
      items: [after + 1, after + 2].filter((sequence) => sequence <= 3).map(item),
      next: after + 2 < 3 ? after + 2 : null,
    }));
    const buffer = new SequenceBuffer(load, 100);

    await buffer.receive(item(4));
    await buffer.receive(item(4));
    await buffer.receive(item(2));

    expect(buffer.values.map((value) => value.sequence)).toEqual([1, 2, 3, 4]);
    expect(buffer.lastSequence).toBe(4);
  });

  it('resumes from a cursor with a bounded history and keeps only the latest items', async () => {
    const load = vi.fn(async (after: number) => ({
      items: Array.from({ length: 10 - after }, (_, index) => item(after + index + 1)),
      next: null,
    }));
    const buffer = new SequenceBuffer(load, 3);

    await buffer.resume(10, 5);

    expect(load).toHaveBeenCalledWith(5);
    expect(buffer.values.map((value) => value.sequence)).toEqual([8, 9, 10]);
  });
});
