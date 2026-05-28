import type { CSSProperties, ReactNode } from "react";
import type { UiLayoutNode } from "../../protocol";

export function Stack({ node, children }: { node: UiLayoutNode; children: ReactNode }) {
  const row = node.type === "hstack";
  const gap =
    typeof node.properties.gap === "string" || typeof node.properties.gap === "number"
      ? String(node.properties.gap)
      : "var(--tw-spacing)";
  const align =
    typeof node.properties.align === "string" ? node.properties.align : row ? "center" : "stretch";
  const justify =
    typeof node.properties.justify === "string" ? node.properties.justify : "flex-start";

  return (
    <div
      className="tw-control-wrap"
      style={{
        display: "flex",
        flexDirection: row ? "row" : "column",
        flexWrap: typeof node.properties.wrap === "boolean" && node.properties.wrap ? "wrap" : "nowrap",
        gap,
        alignItems: align as CSSProperties["alignItems"],
        justifyContent: justify as CSSProperties["justifyContent"],
      }}
    >
      {children}
    </div>
  );
}
