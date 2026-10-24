import type { BacktestObservation } from './types';

/** How many recent observations a live run keeps; earlier ones are on the chart once the run finishes. */
export const LIVE_OBSERVATION_LIMIT = 1500;

export type LoadObservations = (
  after: number,
) => Promise<{ items: readonly BacktestObservation[]; next: number | null }>;

/**
 * Live observations of one run (stage 3 plan, S3-9): messages may arrive twice or with gaps, for example across a
 * reconnect. The buffer keeps each sequence once, in order, and fills a gap from the stored observations before it
 * shows anything after the gap, so the view never skips a candle.
 */
export class ProgressBuffer {
  private readonly items: BacktestObservation[] = [];
  private readonly waiting = new Map<number, BacktestObservation>();
  private through = 0;
  private filling: Promise<void> | null = null;

  constructor(private readonly load: LoadObservations) {}

  get observations(): readonly BacktestObservation[] {
    return this.items;
  }

  get lastSequence(): number {
    return this.through;
  }

  /** Starts from the stored cursor: the last observations before it are read, later ones arrive as messages. */
  async resume(cursor: number, history = 200): Promise<void> {
    if (cursor > this.through) {
      this.through = Math.max(this.through, cursor - history);
      await this.fill(cursor);
    }
  }

  /** Accepts a message; returns once everything up to it is in order. */
  async receive(observation: BacktestObservation): Promise<void> {
    if (observation.sequence <= this.through) {
      return;
    }

    this.waiting.set(observation.sequence, observation);
    await this.fill(observation.sequence - 1);
    this.drain();
  }

  private async fill(target: number): Promise<void> {
    while (this.filling) {
      await this.filling;
    }

    if (target <= this.through) {
      return;
    }

    this.filling = this.read(target);

    try {
      await this.filling;
    } finally {
      this.filling = null;
    }
  }

  private async read(target: number): Promise<void> {
    let after: number | null = this.through;

    while (after !== null && this.through < target) {
      const page = await this.load(after);

      for (const item of page.items) {
        this.waiting.set(item.sequence, item);
      }

      this.drain();
      after = page.next === null || page.items.length === 0 ? null : this.through;
    }
  }

  private drain(): void {
    let next = this.waiting.get(this.through + 1);

    while (next) {
      this.waiting.delete(next.sequence);
      this.items.push(next);
      this.through = next.sequence;
      next = this.waiting.get(this.through + 1);
    }

    for (const sequence of this.waiting.keys()) {
      if (sequence <= this.through) {
        this.waiting.delete(sequence);
      }
    }

    if (this.items.length > LIVE_OBSERVATION_LIMIT) {
      this.items.splice(0, this.items.length - LIVE_OBSERVATION_LIMIT);
    }
  }
}
