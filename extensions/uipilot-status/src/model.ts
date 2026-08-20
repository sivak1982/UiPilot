export interface SessionDto {
  name: string;
  kind: string;
  isActive: boolean;
  pid: number;
  processName: string;
  mainWindowTitle: string | null;
  uiFramework: string | null;
  launchedByCli: boolean;
  canRestart: boolean;
}

export interface AppDto {
  pid: number;
  processName: string;
  mainWindowTitle: string | null;
  protocolVersion: string;
  startedUtc: string;
  uiFramework: string | null;
}

export interface OperationDto {
  operationId: string;
  name: string;
  category: string;
  session: string | null;
  startedAt: string;
  completedAt: string | null;
  durationMs: number | null;
  outcome: string;
  errorCode: string | null;
  messageSummary: string | null;
}

export interface StatusDto {
  activeSession: string | null;
  sessions: SessionDto[];
  apps: AppDto[];
  operations: {
    current: OperationDto[];
    recent: OperationDto[];
  };
}

export interface TreeNodeModel {
  kind: "group" | "connection" | "session" | "operation" | "empty";
  label: string;
  description?: string;
  tooltip?: string;
  children?: TreeNodeModel[];
}

function object(value: unknown, name: string): Record<string, unknown> {
  if (typeof value !== "object" || value === null || Array.isArray(value)) {
    throw new Error(`${name} must be an object.`);
  }
  return value as Record<string, unknown>;
}

function string(value: unknown, name: string): string {
  if (typeof value !== "string") throw new Error(`${name} must be a string.`);
  return value;
}

function nullableString(value: unknown, name: string): string | null {
  if (value === null) return null;
  return string(value, name);
}

function boolean(value: unknown, name: string): boolean {
  if (typeof value !== "boolean") throw new Error(`${name} must be a boolean.`);
  return value;
}

function number(value: unknown, name: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error(`${name} must be a finite number.`);
  }
  return value;
}

function array<T>(value: unknown, name: string, parse: (item: unknown, index: number) => T): T[] {
  if (!Array.isArray(value)) throw new Error(`${name} must be an array.`);
  return value.map(parse);
}

function parseSession(value: unknown, index: number): SessionDto {
  const item = object(value, `sessions[${index}]`);
  return {
    name: string(item.name, "session.name"),
    kind: string(item.kind, "session.kind"),
    isActive: boolean(item.isActive, "session.isActive"),
    pid: number(item.pid, "session.pid"),
    processName: string(item.processName, "session.processName"),
    mainWindowTitle: nullableString(item.mainWindowTitle, "session.mainWindowTitle"),
    uiFramework: nullableString(item.uiFramework, "session.uiFramework"),
    launchedByCli: boolean(item.launchedByCli, "session.launchedByCli"),
    canRestart: boolean(item.canRestart, "session.canRestart"),
  };
}

function parseApp(value: unknown, index: number): AppDto {
  const item = object(value, `apps[${index}]`);
  return {
    pid: number(item.pid, "app.pid"),
    processName: string(item.processName, "app.processName"),
    mainWindowTitle: nullableString(item.mainWindowTitle, "app.mainWindowTitle"),
    protocolVersion: string(item.protocolVersion, "app.protocolVersion"),
    startedUtc: string(item.startedUtc, "app.startedUtc"),
    uiFramework: nullableString(item.uiFramework, "app.uiFramework"),
  };
}

export function validateOperationEvent(value: unknown): OperationDto {
  const item = object(value, "operation");
  return {
    operationId: string(item.operationId, "operation.operationId"),
    name: string(item.name, "operation.name"),
    category: string(item.category, "operation.category"),
    session: nullableString(item.session ?? null, "operation.session"),
    startedAt: string(item.startedAt, "operation.startedAt"),
    completedAt: nullableString(item.completedAt ?? null, "operation.completedAt"),
    durationMs: item.durationMs == null ? null : number(item.durationMs, "operation.durationMs"),
    outcome: string(item.outcome, "operation.outcome"),
    errorCode: nullableString(item.errorCode ?? null, "operation.errorCode"),
    messageSummary: nullableString(item.messageSummary ?? null, "operation.messageSummary"),
  };
}

export type StatusEvent =
  | { type: "hello"; status: StatusDto }
  | { type: "status"; status: StatusDto }
  | { type: "sessions"; activeSession: string | null; sessions: SessionDto[] }
  | { type: "operation"; operation: OperationDto };

export function emptyStatus(): StatusDto {
  return {
    activeSession: null,
    sessions: [],
    apps: [],
    operations: { current: [], recent: [] },
  };
}

export function parseEventEnvelope(value: unknown): StatusEvent {
  const root = object(value, "event");
  const type = root.type;
  if (type === "hello" || type === "status") {
    return { type, status: validateStatusDto(root.snapshot ?? root.status) };
  }
  if (type === "sessions") {
    const payload = object(root.sessions ?? root, "sessions");
    return {
      type: "sessions",
      activeSession: nullableString(payload.activeSession ?? null, "sessions.activeSession"),
      sessions: array(payload.sessions, "sessions.sessions", parseSession),
    };
  }
  if (type === "operation") {
    return { type: "operation", operation: validateOperationEvent(root.operation ?? root) };
  }
  if (type === undefined && typeof root.operationId === "string") {
    return { type: "operation", operation: validateOperationEvent(root) };
  }
  throw new Error(`Unsupported event type: ${String(type)}.`);
}

export function applyStatusEvent(status: StatusDto | undefined, event: StatusEvent): StatusDto {
  if (event.type === "hello" || event.type === "status") return event.status;
  if (event.type === "sessions") {
    const current = status ?? emptyStatus();
    return { ...current, activeSession: event.activeSession, sessions: event.sessions };
  }
  return applyOperationEvent(status ?? emptyStatus(), event.operation);
}

export function validateStatusDto(value: unknown): StatusDto {
  const root = object(value, "status");
  const operations = object(root.operations, "status.operations");
  return {
    activeSession: nullableString(root.activeSession ?? null, "status.activeSession"),
    sessions: array(root.sessions, "status.sessions", parseSession),
    apps: array(root.apps, "status.apps", parseApp),
    operations: {
      current: array(operations.current, "operations.current", validateOperationEvent),
      recent: array(operations.recent, "operations.recent", validateOperationEvent),
    },
  };
}

export function applyOperationEvent(status: StatusDto, event: OperationDto, recentLimit = 100): StatusDto {
  const current = status.operations.current.filter((item) => item.operationId !== event.operationId);
  let recent = status.operations.recent.filter((item) => item.operationId !== event.operationId);
  if (event.outcome === "running") {
    current.push(event);
  } else {
    recent = [...recent, event].slice(-recentLimit);
  }
  return { ...status, operations: { current, recent } };
}

function operationNode(operation: OperationDto): TreeNodeModel {
  const context = [operation.category, operation.session].filter(Boolean).join(" · ");
  const detail = operation.errorCode ?? (operation.durationMs == null ? undefined : `${operation.durationMs} ms`);
  return {
    kind: "operation",
    label: operation.name,
    description: [operation.outcome, detail].filter(Boolean).join(" · "),
    tooltip: [context, operation.messageSummary].filter(Boolean).join("\n"),
  };
}

function group(label: string, children: TreeNodeModel[]): TreeNodeModel {
  return {
    kind: "group",
    label,
    description: String(children.length),
    children: children.length ? children : [{ kind: "empty", label: "None" }],
  };
}

export function buildTreeModel(connectionLabel: string, status: StatusDto | undefined): TreeNodeModel[] {
  const connection: TreeNodeModel = { kind: "connection", label: connectionLabel };
  if (!status) return [connection];

  const sessions = status.sessions.map<TreeNodeModel>((session) => ({
    kind: "session",
    label: session.name,
    description: `${session.isActive ? "active · " : ""}${session.processName} (${session.pid})`,
    tooltip: [session.kind, session.uiFramework, session.mainWindowTitle].filter(Boolean).join("\n"),
  }));
  return [
    connection,
    group("Sessions", sessions),
    group("Current Operations", status.operations.current.map(operationNode)),
    group("Recent Operations", [...status.operations.recent].reverse().map(operationNode)),
  ];
}
