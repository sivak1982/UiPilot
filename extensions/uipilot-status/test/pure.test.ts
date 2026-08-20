import { describe, expect, it } from "vitest";
import { backoffDelay } from "../src/backoff";
import { DEFAULT_PORT, LOOPBACK_HOST, parseConfig } from "../src/config";
import {
  applyOperationEvent,
  applyStatusEvent,
  buildTreeModel,
  OperationDto,
  parseEventEnvelope,
  validateOperationEvent,
  validateStatusDto,
} from "../src/model";
import { statusPresentation } from "../src/statusText";

const runningOperation: OperationDto = {
  operationId: "op-1",
  name: "find_elements",
  category: "ui",
  session: "desktop",
  startedAt: "2026-08-20T12:00:00Z",
  completedAt: null,
  durationMs: null,
  outcome: "running",
  errorCode: null,
  messageSummary: null,
};

const statusPayload = {
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
  operations: {
    current: [runningOperation],
    recent: [],
  },
};

describe("loopback configuration", () => {
  it("builds only loopback status URLs without query tokens", () => {
    const config = parseConfig({ host: LOOPBACK_HOST, port: DEFAULT_PORT, token: "a b" });
    expect(config).toEqual({
      host: LOOPBACK_HOST,
      port: DEFAULT_PORT,
      token: "a b",
      httpBaseUrl: "http://127.0.0.1:17831",
      eventsUrl: "ws://127.0.0.1:17831/v1/events",
    });
    expect(config.eventsUrl).not.toContain("token");
  });

  it("rejects non-loopback hosts, invalid ports, and blank tokens", () => {
    expect(() => parseConfig({ host: "localhost", port: DEFAULT_PORT, token: "token" })).toThrow("127.0.0.1");
    expect(() => parseConfig({ host: LOOPBACK_HOST, port: 0, token: "token" })).toThrow("port");
    expect(() => parseConfig({ host: LOOPBACK_HOST, port: DEFAULT_PORT, token: " " })).toThrow("token");
  });
});

describe("DTO validation and tree model", () => {
  it("validates the status DTO and creates all tree groups", () => {
    const status = validateStatusDto(statusPayload);
    const roots = buildTreeModel("Connected", status);
    expect(roots.map((node) => node.label)).toEqual([
      "Connected",
      "Sessions",
      "Current Operations",
      "Recent Operations",
    ]);
    expect(roots[1].children?.[0].label).toBe("desktop");
    expect(roots[2].children?.[0].label).toBe("find_elements");
  });

  it("rejects malformed DTOs", () => {
    expect(() => validateStatusDto({ ...statusPayload, sessions: "invalid" })).toThrow("array");
    expect(() => validateOperationEvent({ ...runningOperation, operationId: 10 })).toThrow("string");
  });

  it("moves completed events from current to recent", () => {
    const status = validateStatusDto(statusPayload);
    const completed = { ...runningOperation, outcome: "succeeded", completedAt: "2026-08-20T12:00:01Z", durationMs: 1000 };
    const updated = applyOperationEvent(status, completed);
    expect(updated.operations.current).toHaveLength(0);
    expect(updated.operations.recent).toEqual([completed]);
  });
});

describe("event envelopes", () => {
  it("parses hello and status snapshots", () => {
    const hello = parseEventEnvelope({ type: "hello", snapshot: statusPayload });
    const status = parseEventEnvelope({ type: "status", snapshot: statusPayload });
    expect(hello).toEqual({ type: "hello", status: validateStatusDto(statusPayload) });
    expect(status.type).toBe("status");
    expect(applyStatusEvent(undefined, hello).sessions[0].name).toBe("desktop");
  });

  it("parses session list updates without replacing operations", () => {
    const current = validateStatusDto(statusPayload);
    const event = parseEventEnvelope({
      type: "sessions",
      sessions: {
        activeSession: "client",
        sessions: [{
          name: "client",
          kind: "pilot",
          isActive: true,
          pid: 7,
          processName: "Client",
          mainWindowTitle: null,
          uiFramework: "Avalonia",
          launchedByCli: false,
          canRestart: false,
        }],
      },
    });
    const updated = applyStatusEvent(current, event);
    expect(updated.activeSession).toBe("client");
    expect(updated.sessions).toHaveLength(1);
    expect(updated.sessions[0].name).toBe("client");
    expect(updated.operations.current).toEqual(current.operations.current);
  });

  it("parses operation envelopes and legacy bare operations", () => {
    const enveloped = parseEventEnvelope({ type: "operation", operation: runningOperation });
    const legacy = parseEventEnvelope(runningOperation);
    expect(enveloped).toEqual({ type: "operation", operation: runningOperation });
    expect(legacy).toEqual({ type: "operation", operation: runningOperation });
    const completed = {
      ...runningOperation,
      outcome: "succeeded",
      completedAt: "2026-08-20T12:00:01Z",
      durationMs: 12,
    };
    const updated = applyStatusEvent(validateStatusDto(statusPayload), { type: "operation", operation: completed });
    expect(updated.operations.current).toHaveLength(0);
    expect(updated.operations.recent[0].outcome).toBe("succeeded");
  });

  it("rejects unknown envelope types", () => {
    expect(() => parseEventEnvelope({ type: "control", snapshot: statusPayload })).toThrow("Unsupported event type");
  });
});

describe("status text", () => {
  it("shows the current operation or session count when connected", () => {
    expect(statusPresentation("connected", {
      currentOperationCount: 1,
      currentOperationName: "click",
      sessionCount: 2,
    }).text).toContain("click");
    expect(statusPresentation("connected", { sessionCount: 3 }).text).toContain("3 sessions");
    expect(statusPresentation("error", { detail: "Unauthorized" }).tooltip).toBe("Unauthorized");
  });
});

describe("bounded exponential backoff", () => {
  it("grows exponentially and caps at the maximum", () => {
    expect([0, 1, 2, 20].map((attempt) => backoffDelay(attempt))).toEqual([
      500,
      1000,
      2000,
      30_000,
    ]);
  });

  it("rejects negative attempts", () => {
    expect(() => backoffDelay(-1)).toThrow("non-negative");
  });
});
