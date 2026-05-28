import type { UiControlNode } from "../../protocol";

function readNum(v: unknown, fallback: number): number {
  if (typeof v === "number" && Number.isFinite(v)) return v;
  if (typeof v === "string") {
    const n = Number.parseFloat(v);
    if (Number.isFinite(n)) return n;
  }
  return fallback;
}

function readStr(v: unknown, fallback: string): string {
  return typeof v === "string" ? v : fallback;
}

export function Value({ node }: { node: UiControlNode }) {
  const p = node.properties;
  const value = readNum(p.value, 0);
  const unit = readStr(p.unit, "");
  const label = readStr(p.label, node.name);
  const decimals =
    typeof p.decimals === "number" && Number.isFinite(p.decimals)
      ? Math.max(0, Math.min(8, Math.floor(p.decimals)))
      : undefined;

  const shown = decimals !== undefined ? value.toFixed(decimals) : String(value);

  return (
    <div className="tw-surface tw-card tw-control-wrap">
      <div className="tw-muted" style={{ marginBottom: "0.25rem" }}>
        {label}
      </div>
      <div style={{ fontSize: "2rem", fontWeight: 700, lineHeight: 1.1 }}>
        {shown}
        {unit ? (
          <span style={{ fontSize: "1rem", fontWeight: 600, marginLeft: "0.25rem", opacity: 0.85 }}>
            {unit}
          </span>
        ) : null}
      </div>
    </div>
  );
}
