import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
} from "react";
import type { JsonValue, UiElement, UiTree, WsInboundMessage } from "../protocol";

export interface UiActions {
  sendSet: (controlId: string, setting: string, value: JsonValue) => void;
  sendAction: (controlId: string, command: string) => void;
}

export const UiActionsContext = createContext<UiActions | null>(null);

export function useUiActions(): UiActions {
  const ctx = useContext(UiActionsContext);
  if (ctx === null) {
    throw new Error("useUiActions must be used within UiActionsContext.Provider");
  }
  return ctx;
}

function patchElement(el: UiElement, controlId: string, property: string, value: unknown): UiElement {
  if (el.kind === "control" && el.id === controlId) {
    return {
      ...el,
      properties: { ...el.properties, [property]: value },
    };
  }
  if (el.kind === "layout") {
    return {
      ...el,
      children: el.children.map((c) => patchElement(c, controlId, property, value)),
    };
  }
  return el;
}

function patchTree(tree: UiTree, controlId: string, property: string, value: unknown): UiTree {
  return {
    ...tree,
    groups: tree.groups.map((g) => ({
      ...g,
      pages: g.pages.map((p) => ({
        ...p,
        children: p.children.map((c) => patchElement(c, controlId, property, value)),
      })),
    })),
    widgets: tree.widgets.map((w) => ({
      ...w,
      children: w.children.map((c) => patchElement(c, controlId, property, value)),
    })),
  };
}

function wsUrl(): string {
  const proto = window.location.protocol === "https:" ? "wss:" : "ws:";
  return `${proto}//${window.location.host}/ws`;
}

export function useUiTree(): {
  tree: UiTree | null;
  sendSet: UiActions["sendSet"];
  sendAction: UiActions["sendAction"];
  connected: boolean;
} {
  const [tree, setTree] = useState<UiTree | null>(null);
  const [connected, setConnected] = useState(false);
  const socketRef = useRef<WebSocket | null>(null);

  const sendRaw = useCallback((payload: object) => {
    const ws = socketRef.current;
    if (ws !== null && ws.readyState === WebSocket.OPEN) {
      ws.send(JSON.stringify(payload));
    }
  }, []);

  const sendSet = useCallback(
    (controlId: string, setting: string, value: JsonValue) => {
      sendRaw({ type: "set", controlId, setting, value });
    },
    [sendRaw],
  );

  const sendAction = useCallback(
    (controlId: string, command: string) => {
      sendRaw({ type: "action", controlId, command });
    },
    [sendRaw],
  );

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const res = await fetch("/api/ui/tree");
        if (!res.ok) return;
        const data = (await res.json()) as UiTree;
        if (!cancelled) setTree(data);
      } catch {
        /* ignore */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    const socket = new WebSocket(wsUrl());
    socketRef.current = socket;

    socket.onopen = () => setConnected(true);
    socket.onclose = () => {
      setConnected(false);
      socketRef.current = null;
    };

    socket.onmessage = (ev: MessageEvent<string>) => {
      try {
        const msg = JSON.parse(ev.data) as WsInboundMessage;
        if (msg.type === "tree") {
          setTree(msg.tree);
          return;
        }
        if (msg.type === "update") {
          setTree((prev) =>
            prev === null ? prev : patchTree(prev, msg.controlId, msg.property, msg.value),
          );
        }
      } catch {
        /* ignore */
      }
    };

    return () => {
      socket.close();
      if (socketRef.current === socket) {
        socketRef.current = null;
      }
    };
  }, []);

  return { tree, sendSet, sendAction, connected };
}
