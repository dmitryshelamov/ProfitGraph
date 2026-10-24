/** One page of items in sequence order after a cursor; `next` is null at the end. */
export type LoadSequence<T> = (
  after: number,
) => Promise<{ items: readonly T[]; next: number | null }>;

/**
 * Items that arrive as messages, maybe twice or with gaps, for example across a reconnect. The buffer keeps each
 * sequence once, in order, and fills a gap from storage before it shows anything after the gap. Only the latest
 * `limit` items are kept.
 */
export class SequenceBuffer<T extends { readonly sequence: number }> {
  private readonly items: T[] = [];
  private readonly waiting = new Map<number, T>();
  private through = 0;
  private filling: Promise<void> | null = null;

  constructor(
    private readonly load: LoadSequence<T>,
    private readonly limit: number,
  ) {}

  get values(): readonly T[] {
    return this.items;
  }

  get lastSequence(): number {
    return this.through;
  }

  /** Starts from the stored cursor: the last `history` items before it are read, later ones arrive as messages. */
  async resume(cursor: number, history: number): Promise<void> {
    if (cursor > this.through) {
      this.through = Math.max(this.through, cursor - history);
      await this.fill(cursor);
    }
  }

  /** Accepts a message; returns once everything up to it is in order. */
  async receive(item: T): Promise<void> {
    if (item.sequence <= this.through) {
      return;
    }

    this.waiting.set(item.sequence, item);
    await this.fill(item.sequence - 1);
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

    if (this.items.length > this.limit) {
      this.items.splice(0, this.items.length - this.limit);
    }
  }
}
