import type { ReactNode } from "react";
import type { UiLayoutNode } from "../../protocol";

function readColumns(props: Record<string, unknown>): string {
  const c = props.columns;
  if (typeof c === "number" && Number.isFinite(c) && c > 0) {
    return `repeat(${Math.floor(c)}, minmax(0, 1fr))`;
  }
  if (typeof c === "string" && c.trim().length > 0) {
    return c;
  }
  return "repeat(auto-fill, minmax(140px, 1fr))";
}

export function Grid({ node, children }: { node: UiLayoutNode; children: ReactNode }) {
  const columns = readColumns(node.properties);
  const gap =
    typeof node.properties.gap === "string" || typeof node.properties.gap === "number"
      ? String(node.properties.gap)
      : "var(--tw-spacing)";

  return (
    <div
      className="tw-control-wrap"
      style={{
        display: "grid",
        gridTemplateColumns: columns,
        gap,
        alignItems: "stretch",
      }}
    >
      {children}
    </div>
  );
}
