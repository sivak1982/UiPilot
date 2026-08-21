import { AddressInfo } from "node:net";
import http from "node:http";
import { afterEach, describe, expect, it } from "vitest";
import { WebSocket, WebSocketServer } from "ws";
import { UiPilotStatusClient } from "../src/client";
import { LOOPBACK_HOST, parseConfig } from "../src/config";
import { OperationDto, StatusDto } from "../src/model";

const token = "test-token";

const runningOperation: OperationDto = {
  operationId: "op-1",
  name: "click",
  category: "ui",
  session: "desktop",
  startedAt: "2026-08-20T12:00:00Z",
  completedAt: null,
  durationMs: null,
  outcome: "running",
  errorCode: null,
  messageSummary: null,
};

function statusSnapshot(overrides: Partial<StatusDto> = {}): StatusDto {
  return {
    activeSession: "desktop",
    sessions: [{
      name: "desktop",
      kind: "pilot",
      isActive: true,
      pid: 42,
      processName: "Target",
      mainWindowTitle: "Target App",
      uiFramework: "WPF",
      launchedByCli: true,
      canRestart: true,
    }],
    apps: [],
    operations: { current: [], recent: [] },
    ...overrides,
  };
}

interface MockServer {
  port: number;
  inbound: string[];
  send(payload: unknown): void;
  setHttpStatus(status: StatusDto | null): void;
  waitForSocket(): Promise<void>;
  close(): Promise<void>;
}

function listen(server: http.Server): Promise<number> {
  return new Promise((resolve, reject) => {
    server.listen(0, LOOPBACK_HOST, () => resolve((server.address() as AddressInfo).port));
    server.on("error", reject);
  });
}

async function startMock(options: { httpBody?: StatusDto | null } = {}): Promise<MockServer> {
  let httpBody: StatusDto | null = options.httpBody === undefined ? statusSnapshot() : options.httpBody;
  const inbound: string[] = [];
  const sockets = new Set<WebSocket>();
  const server = http.createServer((request, response) => {
    if (request.method === "GET" && request.url === "/v1/status") {
      if (request.headers.authorization !== `Bearer ${token}`) {
        response.statusCode = 401;
        response.end();
        return;
      }
      if (httpBody == null) {
        response.statusCode = 503;
        response.end();
        return;
      }
      response.setHeader("content-type", "application/json");
      response.end(JSON.stringify(httpBody));
      return;
    }
    response.statusCode = 404;
    response.end();
  });

  let resolveSocket: (() => void) | undefined;
  let socketReady = new Promise<void>((resolve) => { resolveSocket = resolve; });
  const wss = new WebSocketServer({ noServer: true });
  server.on("upgrade", (request, socket, head) => {
    if (request.url !== "/v1/events" || request.headers.authorization !== `Bearer ${token}`) {
      socket.destroy();
      return;
    }
    wss.handleUpgrade(request, socket, head, (ws) => wss.emit("connection", ws, request));
  });
  wss.on("connection", (ws) => {
    sockets.add(ws);
    ws.on("message", (data) => inbound.push(data.toString()));
    ws.on("close", () => sockets.delete(ws));
    resolveSocket?.();
  });

  const port = await listen(server);
  return {
    port,
    inbound,
    send(payload) {
      const text = JSON.stringify(payload);
      for (const socket of sockets) socket.send(text);
    },
    setHttpStatus(status) {
      httpBody = status;
    },
    waitForSocket() {
      return socketReady;
    },
    close() {
      for (const socket of sockets) socket.close();
      return new Promise((resolve, reject) => {
        wss.close((error) => {
          if (error) reject(error);
          else server.close((closeError) => closeError ? reject(closeError) : resolve());
        });
      });
    },
  };
}

function waitUntil(predicate: () => boolean, timeoutMs = 2000): Promise<void> {
  return new Promise((resolve, reject) => {
    const started = Date.now();
    const timer = setInterval(() => {
      if (predicate()) {
        clearInterval(timer);
        resolve();
      } else if (Date.now() - started > timeoutMs) {
        clearInterval(timer);
        reject(new Error("Timed out waiting for client state."));
      }
    }, 10);
  });
}

describe("status client against a mock loopback endpoint", () => {
  let mock: MockServer | undefined;
  let client: UiPilotStatusClient | undefined;

  afterEach(async () => {
    client?.dispose();
    client = undefined;
    await mock?.close();
    mock = undefined;
  });

  it("uses Bearer headers, never sends control frames, and applies hello/status/operation envelopes", async () => {
    mock = await startMock({ httpBody: null });
    const snapshots: StatusDto[] = [];
    const operations: OperationDto[] = [];
    const states: string[] = [];
    client = new UiPilotStatusClient(
      () => parseConfig({ host: LOOPBACK_HOST, port: mock!.port, token }),
      {
        onState(state) { states.push(state); },
        onStatus(status) { snapshots.push(status); },
        onOperation(operation) { operations.push(operation); },
        log() {},
      },
      { backoff: { initialMs: 20, maximumMs: 20, factor: 1 } },
    );

    client.connect();
    await mock.waitForSocket();
    mock.send({ type: "hello", snapshot: statusSnapshot() });
    await waitUntil(() => snapshots.length === 1 && states.includes("connected"));

    mock.send({ type: "operation", operation: runningOperation });
    await waitUntil(() => operations.length === 1);

    const twoSessions = statusSnapshot({
      sessions: [
        ...statusSnapshot().sessions,
        {
          name: "client",
          kind: "pilot",
          isActive: false,
          pid: 7,
          processName: "Client",
          mainWindowTitle: null,
          uiFramework: "Avalonia",
          launchedByCli: false,
          canRestart: false,
        },
      ],
    });
    mock.send({
      type: "sessions",
      sessions: {
        activeSession: twoSessions.activeSession,
        sessions: twoSessions.sessions,
      },
    });
    await waitUntil(() => snapshots.at(-1)?.sessions.length === 2);

    mock.setHttpStatus(statusSnapshot({ activeSession: "recovered" }));
    await client.refresh();
    expect(snapshots.at(-1)?.activeSession).toBe("recovered");
    expect(operations[0].name).toBe("click");
    expect(states.filter((state) => state === "connected")).toHaveLength(1);
    expect(mock.inbound).toEqual([]);
  });

  it("keeps a healthy WebSocket connected when manual HTTP refresh fails", async () => {
    mock = await startMock();
    const states: string[] = [];
    client = new UiPilotStatusClient(
      () => parseConfig({ host: LOOPBACK_HOST, port: mock!.port, token }),
      {
        onState(state) { states.push(state); },
        onStatus() {},
        onOperation() {},
        log() {},
      },
    );

    client.connect();
    await mock.waitForSocket();
    await waitUntil(() => states.at(-1) === "connected");
    mock.setHttpStatus(null);

    await expect(client.refresh()).rejects.toThrow("HTTP 503");
    expect(states.at(-1)).toBe("connected");
  });

  it("uses an HTTP recovery snapshot for later partial events", async () => {
    mock = await startMock();
    const snapshots: StatusDto[] = [];
    client = new UiPilotStatusClient(
      () => parseConfig({ host: LOOPBACK_HOST, port: mock!.port, token }),
      {
        onState() {},
        onStatus(status) { snapshots.push(status); },
        onOperation() {},
        log() {},
      },
    );

    client.connect();
    await mock.waitForSocket();
    await waitUntil(() => snapshots.length > 0);
    mock.setHttpStatus(statusSnapshot({
      apps: [{
        pid: 99,
        processName: "Recovered",
        mainWindowTitle: null,
        protocolVersion: "2.0",
        startedUtc: "2026-08-20T12:00:00Z",
        uiFramework: "WPF",
      }],
    }));
    mock.send({ malformed: true });
    await waitUntil(() => snapshots.at(-1)?.apps.length === 1);

    mock.send({
      type: "sessions",
      sessions: {
        activeSession: "desktop",
        sessions: statusSnapshot().sessions,
      },
    });
    await waitUntil(() => snapshots.length >= 3);
    expect(snapshots.at(-1)?.apps[0].processName).toBe("Recovered");
  });

  it("does not retry permanent configuration errors", async () => {
    const logs: string[] = [];
    const states: string[] = [];
    client = new UiPilotStatusClient(
      () => parseConfig({ host: LOOPBACK_HOST, port: 17831, token: "" }),
      {
        onState(state) { states.push(state); },
        onStatus() {},
        onOperation() {},
        log(message) { logs.push(message); },
      },
      { backoff: { initialMs: 10, maximumMs: 10, factor: 1 } },
    );

    client.connect();
    await waitUntil(() => states.includes("error"));
    await new Promise((resolve) => setTimeout(resolve, 30));

    expect(logs.filter((message) => message.startsWith("Connection failed:"))).toHaveLength(1);
    expect(logs.some((message) => message.startsWith("Reconnecting in"))).toBe(false);
  });
});
