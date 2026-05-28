import type { UiControlNode } from "../../protocol";

function valueKey(value: unknown): string {
  if (typeof value === "boolean") return value ? "true" : "false";
  if (typeof value === "number" && Number.isFinite(value)) return String(value);
  if (typeof value === "string") return value;
  return "";
}

export function Indicator({ node }: { node: UiControlNode }) {
  const value = node.properties.value;
  const vkey = valueKey(value);
  const entry = node.map?.[vkey] ?? node.map?.[String(value)];
  const label = entry?.label ?? (vkey || "—");
  const color = entry?.color;
  const icon = entry?.icon;

  return (
    <span
      className="tw-control-wrap"
      style={{
        display: "inline-flex",
        alignItems: "center",
        gap: "0.35rem",
        minHeight: 48,
        padding: "0 0.75rem",
        borderRadius: 999,
        fontWeight: 600,
        fontSize: "0.9rem",
        background: color ?? "color-mix(in srgb, var(--tw-accent) 22%, transparent)",
        color: "var(--tw-text)",
        border: `1px solid color-mix(in srgb, var(--tw-text) 14%, transparent)`,
      }}
      title={node.name}
    >
      {icon ? <span aria-hidden>{icon}</span> : null}
      <span>{label}</span>
    </span>
  );
}
