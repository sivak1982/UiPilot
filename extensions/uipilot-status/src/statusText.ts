export type ConnectionState = "disconnected" | "connecting" | "connected" | "error";

export interface StatusPresentation {
  text: string;
  tooltip: string;
}

export interface StatusPresentationInput {
  currentOperationCount?: number;
  sessionCount?: number;
  currentOperationName?: string;
  detail?: string;
}

export function statusPresentation(
  state: ConnectionState,
  input: StatusPresentationInput = {},
): StatusPresentation {
  const currentOperationCount = input.currentOperationCount ?? 0;
  const sessionCount = input.sessionCount ?? 0;
  switch (state) {
    case "connected": {
      const running = currentOperationCount > 0
        ? currentOperationCount === 1 && input.currentOperationName
          ? input.currentOperationName
          : `${currentOperationCount} running`
        : undefined;
      return {
        text: running
          ? `$(pulse) UiPilot: ${running}`
          : `$(check) UiPilot: ${sessionCount} ${sessionCount === 1 ? "session" : "sessions"}`,
        tooltip: input.detail ?? `Connected · ${sessionCount} sessions · ${currentOperationCount} running`,
      };
    }
    case "connecting":
      return {
        text: "$(sync~spin) UiPilot: Connecting",
        tooltip: input.detail ?? "Connecting to the UiPilot status service.",
      };
    case "error":
      return {
        text: "$(error) UiPilot: Error",
        tooltip: input.detail ?? "UiPilot status connection failed.",
      };
    default:
      return {
        text: "$(debug-disconnect) UiPilot: Disconnected",
        tooltip: input.detail ?? "UiPilot status service is disconnected.",
      };
  }
}
