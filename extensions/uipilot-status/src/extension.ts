import * as vscode from "vscode";
import { UiPilotStatusClient } from "./client";
import { DEFAULT_PORT, LOOPBACK_HOST, parseConfig } from "./config";
import { applyOperationEvent, buildTreeModel, StatusDto } from "./model";
import { ConnectionState, statusPresentation } from "./statusText";
import { UiPilotTreeProvider } from "./treeProvider";

export function activate(context: vscode.ExtensionContext): void {
  const output = vscode.window.createOutputChannel("UiPilot Status");
  const tree = new UiPilotTreeProvider();
  const statusBar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 50);
  statusBar.command = "uipilotStatus.showOutput";
  statusBar.show();

  let state: ConnectionState = "disconnected";
  let stateDetail: string | undefined;
  let status: StatusDto | undefined;

  const render = () => {
    const current = status?.operations.current ?? [];
    const presentation = statusPresentation(state, {
      currentOperationCount: current.length,
      sessionCount: status?.sessions.length ?? 0,
      currentOperationName: current[0]?.name,
      detail: stateDetail,
    });
    statusBar.text = presentation.text;
    statusBar.tooltip = presentation.tooltip;
    const connectionLabel = state === "connected"
      ? `Connected to ${LOOPBACK_HOST}`
      : `${capitalize(state)}${stateDetail ? `: ${stateDetail}` : ""}`;
    tree.setRoots(buildTreeModel(connectionLabel, status));
  };

  const client = new UiPilotStatusClient(
    () => {
      const configuration = vscode.workspace.getConfiguration("uipilotStatus");
      return parseConfig({
        host: configuration.get("host", LOOPBACK_HOST),
        port: configuration.get("port", DEFAULT_PORT),
        token: configuration.get("token", ""),
      });
    },
    {
      onState(nextState, detail) {
        state = nextState;
        stateDetail = detail;
        render();
      },
      onStatus(nextStatus) {
        status = nextStatus;
        render();
      },
      onOperation(operation) {
        if (!status) return;
        status = applyOperationEvent(status, operation);
        render();
      },
      log(message) {
        output.appendLine(`${new Date().toISOString()} ${message}`);
      },
    },
  );

  context.subscriptions.push(
    output,
    tree,
    statusBar,
    vscode.window.registerTreeDataProvider("uipilotStatus.sessions", tree),
    vscode.commands.registerCommand("uipilotStatus.refresh", async () => {
      try {
        await client.refresh();
      } catch (error) {
        void vscode.window.showErrorMessage(error instanceof Error ? error.message : String(error));
      }
    }),
    vscode.commands.registerCommand("uipilotStatus.reconnect", () => client.reconnect()),
    vscode.commands.registerCommand("uipilotStatus.showOutput", () => output.show(true)),
    vscode.workspace.onDidChangeConfiguration((event) => {
      if (event.affectsConfiguration("uipilotStatus")) client.reconnect();
    }),
    { dispose: () => client.dispose() },
  );

  render();
  client.connect();
}

export function deactivate(): void {}

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
