import { useState } from "react";
import { useUiActions } from "../../hooks/useUiTree";
import type { JsonValue, UiControlNode } from "../../protocol";

function readStr(v: unknown, fallback: string): string {
  return typeof v === "string" ? v : fallback;
}

export function Button({ node }: { node: UiControlNode }) {
  const { sendSet, sendAction } = useUiActions();
  const p = node.properties;
  const label = readStr(p.label, node.name);
  const setting = typeof p.setting === "string" ? p.setting : undefined;
  const input = readStr(p.input, "button");
  const command = typeof p.command === "string" ? p.command : undefined;
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState("0");

  const needsNumberModal = setting !== undefined && input === "number";

  function handlePrimaryClick() {
    if (command !== undefined) {
      sendAction(node.id, command);
      return;
    }
    if (needsNumberModal) {
      const cur = p.value;
      if (typeof cur === "number") setDraft(String(cur));
      else if (typeof cur === "string") setDraft(cur);
      setOpen(true);
      return;
    }
  }

  function confirmNumber() {
    if (setting === undefined) return;
    const n = Number.parseFloat(draft);
    const value: JsonValue = Number.isFinite(n) ? n : draft;
    sendSet(node.id, setting, value);
    setOpen(false);
  }

  return (
    <>
      <button
        type="button"
        className="tw-surface tw-control-wrap"
        style={{
          width: "100%",
          fontWeight: 600,
          background: "color-mix(in srgb, var(--tw-accent) 28%, transparent)",
        }}
        onClick={handlePrimaryClick}
      >
        {label}
      </button>
      {open && needsNumberModal ? (
        <div
          className="tw-modal-backdrop"
          role="presentation"
          onClick={() => setOpen(false)}
        >
          <div
            className="tw-surface tw-modal"
            role="dialog"
            aria-modal="true"
            onClick={(e) => e.stopPropagation()}
          >
            <div style={{ fontWeight: 600, marginBottom: "0.5rem" }}>{label}</div>
            <input
              className="tw-input"
              type="number"
              inputMode="decimal"
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
            />
            <div className="tw-modal-actions">
              <button type="button" className="tw-surface" onClick={() => setOpen(false)}>
                Cancel
              </button>
              <button
                type="button"
                className="tw-surface"
                style={{ background: "color-mix(in srgb, var(--tw-accent) 35%, transparent)" }}
                onClick={confirmNumber}
              >
                OK
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </>
  );
}
