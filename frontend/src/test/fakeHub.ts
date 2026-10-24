/** Stands in for the SignalR client: tests set the subscription cursor and emit hub messages themselves. */
interface Cursor {
  lastSequence: number;
  outcome: string | null;
}

type Handler = (message: unknown) => void;

export const hub = {
  handlers: new Map<string, Handler>(),
  cursor: { lastSequence: 0, outcome: null } as Cursor,
  subscriptions: [] as string[],
  emit(name: string, message: unknown) {
    hub.handlers.get(name)?.(message);
  },
  reset() {
    hub.handlers.clear();
    hub.cursor = { lastSequence: 0, outcome: null };
    hub.subscriptions = [];
  },
};

class FakeConnection {
  on(name: string, handler: Handler) {
    hub.handlers.set(name, handler);
  }

  onreconnecting() {}

  onreconnected() {}

  onclose() {}

  start() {
    return Promise.resolve();
  }

  invoke(_method: string, runId: string) {
    hub.subscriptions.push(runId);

    return Promise.resolve(hub.cursor);
  }

  stop() {
    return Promise.resolve();
  }
}

class FakeBuilder {
  withUrl() {
    return this;
  }

  withAutomaticReconnect() {
    return this;
  }

  configureLogging() {
    return this;
  }

  build() {
    return new FakeConnection();
  }
}

export const signalrModule = { HubConnectionBuilder: FakeBuilder, LogLevel: { None: 6 } };
