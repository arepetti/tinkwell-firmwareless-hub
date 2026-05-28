import { useUiActions } from "../../hooks/useUiTree";
import type { UiControlNode } from "../../protocol";

function readBool(v: unknown, fallback: boolean): boolean {
  if (typeof v === "boolean") return v;
  if (typeof v === "string") {
    const s = v.toLowerCase();
    if (s === "true" || s === "1" || s === "on") return true;
    if (s === "false" || s === "0" || s === "off") return false;
  }
  if (typeof v === "number") return v !== 0;
  return fallback;
}

export function Toggle({ node }: { node: UiControlNode }) {
  const { sendSet } = useUiActions();
  const p = node.properties;
  const setting = typeof p.setting === "string" ? p.setting : undefined;
  const on = readBool(p.value, false);
  const label = typeof p.label === "string" ? p.label : node.name;

  if (setting === undefined) {
    return (
      <div className="tw-muted tw-control-wrap" style={{ minHeight: 48 }}>
        {label}
      </div>
    );
  }

  return (
    <label
      className="tw-control-wrap tw-surface"
      style={{
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        gap: "0.75rem",
        padding: "0.5rem 0.75rem",
        minHeight: 48,
        cursor: "pointer",
        userSelect: "none",
      }}
    >
      <span style={{ fontWeight: 600 }}>{label}</span>
      <span
        style={{
          position: "relative",
          width: 52,
          height: 32,
          borderRadius: 999,
          background: on
            ? "color-mix(in srgb, var(--tw-accent) 55%, transparent)"
            : "color-mix(in srgb, var(--tw-text) 18%, transparent)",
          transition: "background-color 0.2s ease",
        }}
      >
        <input
          type="checkbox"
          checked={on}
          onChange={(e) => {
            sendSet(node.id, setting, e.target.checked);
          }}
          style={{
            position: "absolute",
            inset: 0,
            opacity: 0,
            width: "100%",
            height: "100%",
            margin: 0,
            cursor: "pointer",
          }}
        />
        <span
          aria-hidden
          style={{
            position: "absolute",
            top: 4,
            left: on ? 24 : 4,
            width: 24,
            height: 24,
            borderRadius: 999,
            background: "var(--tw-surface)",
            transition: "left 0.2s ease",
            pointerEvents: "none",
          }}
        />
      </span>
    </label>
  );
}
