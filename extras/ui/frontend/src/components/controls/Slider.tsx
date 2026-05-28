import { useUiActions } from "../../hooks/useUiTree";
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

export function Slider({ node }: { node: UiControlNode }) {
  const { sendSet } = useUiActions();
  const p = node.properties;
  const setting = typeof p.setting === "string" ? p.setting : undefined;
  const min = readNum(p.min, 0);
  const max = readNum(p.max, 100);
  const step = readNum(p.step, 1);
  const value = readNum(p.value, min);
  const label = readStr(p.label, node.name);
  const unit = readStr(p.unit, "");

  if (setting === undefined) {
    return (
      <div className="tw-muted tw-control-wrap" style={{ minHeight: 48 }}>
        {label}
      </div>
    );
  }

  return (
    <div className="tw-surface tw-card tw-control-wrap">
      <div style={{ display: "flex", justifyContent: "space-between", gap: "0.5rem", marginBottom: "0.35rem" }}>
        <span style={{ fontWeight: 600 }}>{label}</span>
        <span className="tw-muted">
          {value}
          {unit ? ` ${unit}` : ""}
        </span>
      </div>
      <input
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        onInput={(e) => {
          const n = Number.parseFloat(e.currentTarget.value);
          if (Number.isFinite(n)) sendSet(node.id, setting, n);
        }}
      />
    </div>
  );
}
