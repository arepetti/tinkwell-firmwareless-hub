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

export function Gauge({ node }: { node: UiControlNode }) {
  const p = node.properties;
  const min = readNum(p.min, 0);
  const max = readNum(p.max, 100);
  const value = readNum(p.value, min);
  const unit = readStr(p.unit, "");
  const label = readStr(p.label, node.name);
  const style = readStr(p.style, "radial");

  const t = max > min ? (value - min) / (max - min) : 0;
  const clamped = Math.min(1, Math.max(0, t));

  if (style === "linear") {
    return (
      <div className="tw-surface tw-card tw-control-wrap">
        <div className="tw-muted" style={{ marginBottom: "0.35rem" }}>
          {label}
        </div>
        <div
          style={{
            height: 12,
            borderRadius: 6,
            background: "color-mix(in srgb, var(--tw-text) 12%, transparent)",
            overflow: "hidden",
          }}
        >
          <div
            style={{
              width: `${clamped * 100}%`,
              height: "100%",
              background: "var(--tw-accent)",
              transition: "width 0.2s ease",
            }}
          />
        </div>
        <div style={{ marginTop: "0.35rem", fontWeight: 600 }}>
          {value}
          {unit ? ` ${unit}` : ""}
        </div>
      </div>
    );
  }

  const r = 36;
  const cx = 50;
  const cy = 50;
  const startAngle = -135;
  const sweep = 270;
  const endAngle = startAngle + sweep * clamped;

  function polar(cx0: number, cy0: number, radius: number, angleDeg: number) {
    const rad = (angleDeg * Math.PI) / 180;
    return { x: cx0 + radius * Math.cos(rad), y: cy0 + radius * Math.sin(rad) };
  }

  function arcPath(from: number, to: number): string {
    const p1 = polar(cx, cy, r, from);
    const p2 = polar(cx, cy, r, to);
    const large = to - from > 180 ? 1 : 0;
    return `M ${p1.x} ${p1.y} A ${r} ${r} 0 ${large} 1 ${p2.x} ${p2.y}`;
  }

  const track = arcPath(startAngle, startAngle + sweep);
  const fill = arcPath(startAngle, endAngle);

  return (
    <div className="tw-surface tw-card tw-control-wrap">
      <div className="tw-muted" style={{ marginBottom: "0.35rem", textAlign: "center" }}>
        {label}
      </div>
      <svg viewBox="0 0 100 62" width="100%" height={140} style={{ display: "block" }}>
        <path
          d={track}
          fill="none"
          stroke="color-mix(in srgb, var(--tw-text) 18%, transparent)"
          strokeWidth={8}
          strokeLinecap="round"
        />
        <path
          d={fill}
          fill="none"
          stroke="var(--tw-accent)"
          strokeWidth={8}
          strokeLinecap="round"
          style={{ transition: "d 0.2s ease" }}
        />
        <text
          x={cx}
          y={cy + 8}
          textAnchor="middle"
          fill="currentColor"
          fontSize="12"
          fontWeight="600"
        >
          {value}
          {unit ? ` ${unit}` : ""}
        </text>
      </svg>
    </div>
  );
}
