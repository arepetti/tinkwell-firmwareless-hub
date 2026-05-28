import { useMemo } from "react";
import type { ReactNode } from "react";
import type { UiElement, UiWidget } from "../protocol";

export function OverviewPage({
  widgets,
  renderElement,
}: {
  widgets: UiWidget[];
  renderElement: (el: UiElement, key: string) => ReactNode;
}) {
  const sorted = useMemo(
    () => [...widgets].sort((a, b) => a.order - b.order),
    [widgets],
  );

  return (
    <div className="tw-widget-grid">
      {sorted.map((w) => (
        <div key={w.name} className="tw-surface tw-card">
          <div style={{ fontWeight: 700, marginBottom: "0.5rem", display: "flex", alignItems: "center", gap: "0.35rem" }}>
            {w.icon ? <span aria-hidden>{w.icon}</span> : null}
            <span>{w.label}</span>
          </div>
          <div style={{ display: "flex", flexDirection: "column", gap: "var(--tw-spacing)" }}>
            {w.children.map((c, i) => renderElement(c, `${w.name}-${i}`))}
          </div>
        </div>
      ))}
    </div>
  );
}
