import type { CSSProperties } from "react";
import type { UiControlNode } from "../../protocol";

function readStr(v: unknown, fallback: string): string {
  return typeof v === "string" ? v : fallback;
}

export function Text({ node }: { node: UiControlNode }) {
  const text = readStr(node.properties.text, "");
  const variant = readStr(node.properties.variant, "body");

  const styles: Record<string, CSSProperties> = {
    heading: { fontSize: "1.35rem", fontWeight: 700, lineHeight: 1.25 },
    body: { fontSize: "1rem", fontWeight: 400 },
    caption: { fontSize: "0.8rem", opacity: 0.85 },
  };

  const s = styles[variant] ?? styles.body;

  return (
    <p className="tw-control-wrap" style={{ ...s, margin: 0, minHeight: variant === "caption" ? 40 : 48 }}>
      {text}
    </p>
  );
}
