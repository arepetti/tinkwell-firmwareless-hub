import type { ReactNode } from "react";
import { useEffect, useMemo, useState } from "react";
import type { UiElement, UiTree } from "../protocol";
import { ControlRenderer } from "./ControlRenderer";
import { OverviewPage } from "./OverviewPage";
import { Grid } from "./layout/Grid";
import { Stack } from "./layout/Stack";

function useWideLayout(): boolean {
  const [wide, setWide] = useState(
    () => (typeof window !== "undefined" ? window.matchMedia("(min-width: 768px)").matches : true),
  );

  useEffect(() => {
    const mq = window.matchMedia("(min-width: 768px)");
    const onChange = () => setWide(mq.matches);
    mq.addEventListener("change", onChange);
    return () => mq.removeEventListener("change", onChange);
  }, []);

  return wide;
}

function renderUiElement(el: UiElement, key: string): ReactNode {
  if (el.kind === "layout") {
    if (el.type === "grid") {
      return (
        <Grid key={key} node={el}>
          {el.children.map((c, i) => renderUiElement(c, `${el.id}-${i}`))}
        </Grid>
      );
    }
    return (
      <Stack key={key} node={el}>
        {el.children.map((c, i) => renderUiElement(c, `${el.id}-${i}`))}
      </Stack>
    );
  }
  return <ControlRenderer key={key} node={el} />;
}

export function Shell({ tree, connected }: { tree: UiTree | null; connected: boolean }) {
  const wide = useWideLayout();
  const [groupIdx, setGroupIdx] = useState(0);
  const [pageIdx, setPageIdx] = useState(0);

  const visibleGroups = useMemo(() => {
    if (tree === null) return [];
    return [...tree.groups].filter((g) => g.visible).sort((a, b) => a.order - b.order);
  }, [tree]);

  useEffect(() => {
    setGroupIdx(0);
    setPageIdx(0);
  }, [tree]);

  useEffect(() => {
    if (groupIdx >= visibleGroups.length) {
      setGroupIdx(visibleGroups.length === 0 ? 0 : visibleGroups.length - 1);
    }
  }, [groupIdx, visibleGroups.length]);

  const activeGroup = visibleGroups[groupIdx];

  const visiblePages = useMemo(() => {
    if (activeGroup === undefined) return [];
    return [...activeGroup.pages].filter((p) => p.visible).sort((a, b) => a.order - b.order);
  }, [activeGroup]);

  useEffect(() => {
    setPageIdx(0);
  }, [groupIdx, activeGroup?.name]);

  useEffect(() => {
    if (pageIdx >= visiblePages.length) {
      setPageIdx(visiblePages.length === 0 ? 0 : visiblePages.length - 1);
    }
  }, [pageIdx, visiblePages.length]);

  const activePage = visiblePages[pageIdx];
  const isOverview = activePage?.name.toLowerCase() === "overview";

  if (tree === null) {
    return (
      <div className="tw-shell">
        <main className="tw-main">
          <div className="tw-muted">Loading…</div>
        </main>
      </div>
    );
  }

  const title = tree.theme.title;

  return (
    <div className="tw-shell">
      {wide ? (
        <aside className="tw-sidebar" aria-label="Groups">
          {visibleGroups.map((g, i) => (
            <button
              key={g.name}
              type="button"
              className="tw-nav-btn"
              data-active={i === groupIdx ? "true" : "false"}
              onClick={() => setGroupIdx(i)}
            >
              <span aria-hidden>{g.icon ?? "◆"}</span>
              <span>{g.label}</span>
            </button>
          ))}
        </aside>
      ) : null}

      <main className="tw-main">
        <header className="tw-header">
          <div>
            <div style={{ fontWeight: 800, fontSize: "1.15rem" }}>{title}</div>
            {activeGroup !== undefined && activePage !== undefined ? (
              <div className="tw-muted" style={{ fontSize: "0.85rem" }}>
                {activeGroup.label} / {activePage.label}
              </div>
            ) : null}
          </div>
          <div className="tw-status" data-on={connected ? "true" : "false"}>
            {connected ? "Live" : "Offline"}
          </div>
        </header>

        {activeGroup !== undefined && visiblePages.length > 0 ? (
          <div className="tw-page-tabs" role="tablist" aria-label="Pages">
            {visiblePages.map((p, i) => (
              <button
                key={p.name}
                type="button"
                className="tw-page-tab"
                role="tab"
                aria-selected={i === pageIdx}
                data-active={i === pageIdx ? "true" : "false"}
                onClick={() => setPageIdx(i)}
              >
                {p.icon ? `${p.icon} ` : ""}
                {p.label}
              </button>
            ))}
          </div>
        ) : null}

        {activePage !== undefined ? (
          isOverview ? (
            <OverviewPage widgets={tree.widgets} renderElement={renderUiElement} />
          ) : (
            <div
              style={{
                display: "flex",
                flexDirection: "column",
                gap: "var(--tw-spacing)",
              }}
            >
              {activePage.children.map((c, i) => renderUiElement(c, `${activePage.name}-${i}`))}
            </div>
          )
        ) : (
          <div className="tw-muted">No pages available.</div>
        )}
      </main>

      {!wide ? (
        <nav className="tw-bottom-nav" aria-label="Groups">
          {visibleGroups.map((g, i) => (
            <button
              key={g.name}
              type="button"
              className="tw-nav-btn"
              data-active={i === groupIdx ? "true" : "false"}
              onClick={() => setGroupIdx(i)}
            >
              <span aria-hidden>{g.icon ?? "◆"}</span>
              <span>{g.label}</span>
            </button>
          ))}
        </nav>
      ) : null}
    </div>
  );
}
