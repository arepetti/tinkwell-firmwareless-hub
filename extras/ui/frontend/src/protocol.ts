export type UiThemeMode = "dark" | "light";

export type UiFontSize = "small" | "medium" | "large";

export interface UiTheme {
  title: string;
  theme: UiThemeMode;
  accent: string;
  fontSize: UiFontSize;
  locale: string;
}

export interface UiMapEntry {
  label?: string;
  color?: string;
  icon?: string;
}

export type UiControlType =
  | "gauge"
  | "indicator"
  | "text"
  | "value"
  | "button"
  | "toggle"
  | "slider";

export type UiLayoutType = "grid" | "hstack" | "vstack";

export type JsonPrimitive = string | number | boolean | null;

export type JsonValue = JsonPrimitive | JsonValue[] | { [key: string]: JsonValue };

export interface UiControlNode {
  kind: "control";
  type: UiControlType;
  id: string;
  name: string;
  properties: Record<string, unknown>;
  map?: Record<string, UiMapEntry>;
}

export interface UiLayoutNode {
  kind: "layout";
  type: UiLayoutType;
  id: string;
  name: string;
  properties: Record<string, unknown>;
  children: UiElement[];
}

export type UiElement = UiControlNode | UiLayoutNode;

export interface UiPage {
  name: string;
  label: string;
  icon?: string;
  visible: boolean;
  order: number;
  children: UiElement[];
}

export interface UiGroup {
  name: string;
  label: string;
  icon?: string;
  visible: boolean;
  order: number;
  pages: UiPage[];
}

export interface UiWidget {
  name: string;
  label: string;
  icon?: string;
  order: number;
  children: UiElement[];
}

export interface UiTree {
  theme: UiTheme;
  groups: UiGroup[];
  widgets: UiWidget[];
}

export interface WsUpdateMessage {
  type: "update";
  controlId: string;
  property: string;
  value: unknown;
}

export interface WsTreeMessage {
  type: "tree";
  tree: UiTree;
}

export interface WsSetMessage {
  type: "set";
  controlId: string;
  setting: string;
  value: JsonValue;
}

export interface WsActionMessage {
  type: "action";
  controlId: string;
  command: string;
}

export type WsInboundMessage = WsUpdateMessage | WsTreeMessage;

export type WsOutboundMessage = WsSetMessage | WsActionMessage;
