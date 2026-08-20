import * as vscode from "vscode";
import { TreeNodeModel } from "./model";

export class UiPilotTreeProvider implements vscode.TreeDataProvider<TreeNodeModel> {
  private readonly changed = new vscode.EventEmitter<TreeNodeModel | undefined>();
  private roots: TreeNodeModel[] = [];

  readonly onDidChangeTreeData = this.changed.event;

  setRoots(roots: TreeNodeModel[]): void {
    this.roots = roots;
    this.changed.fire(undefined);
  }

  getTreeItem(node: TreeNodeModel): vscode.TreeItem {
    const collapsible = node.children
      ? vscode.TreeItemCollapsibleState.Expanded
      : vscode.TreeItemCollapsibleState.None;
    const item = new vscode.TreeItem(node.label, collapsible);
    item.description = node.description;
    item.tooltip = node.tooltip;
    item.contextValue = `uipilotStatus.${node.kind}`;
    item.iconPath = iconFor(node.kind);
    return item;
  }

  getChildren(node?: TreeNodeModel): TreeNodeModel[] {
    return node?.children ?? this.roots;
  }

  dispose(): void {
    this.changed.dispose();
  }
}

function iconFor(kind: TreeNodeModel["kind"]): vscode.ThemeIcon {
  switch (kind) {
    case "connection":
      return new vscode.ThemeIcon("radio-tower");
    case "session":
      return new vscode.ThemeIcon("window");
    case "operation":
      return new vscode.ThemeIcon("pulse");
    case "empty":
      return new vscode.ThemeIcon("circle-outline");
    default:
      return new vscode.ThemeIcon("list-tree");
  }
}
