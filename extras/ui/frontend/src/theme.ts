import type { UiFontSize, UiTheme, UiThemeMode } from "./protocol";

const fontSizeMap: Record<UiFontSize, string> = {
  small: "0.875rem",
  medium: "1rem",
  large: "1.125rem",
};

function normalizeMode(theme: string): UiThemeMode {
  return theme === "light" ? "light" : "dark";
}

function normalizeFontSize(size: string): UiFontSize {
  if (size === "small" || size === "medium" || size === "large") {
    return size;
  }
  return "medium";
}

function palette(mode: UiThemeMode): { bg: string; surface: string; text: string } {
  if (mode === "light") {
    return {
      bg: "#f4f4f5",
      surface: "#ffffff",
      text: "#18181b",
    };
  }
  return {
    bg: "#09090b",
    surface: "#18181b",
    text: "#fafafa",
  };
}

export function applyTheme(theme: UiTheme): void {
  const root = document.documentElement.style;
  const mode = normalizeMode(theme.theme);
  const fs = fontSizeMap[normalizeFontSize(theme.fontSize)];
  const { bg, surface, text } = palette(mode);

  root.setProperty("--tw-accent", theme.accent);
  root.setProperty("--tw-bg", bg);
  root.setProperty("--tw-surface", surface);
  root.setProperty("--tw-text", text);
  root.setProperty("--tw-font-size", fs);
  root.setProperty("--tw-spacing", "0.75rem");
  root.setProperty("color-scheme", mode === "light" ? "light" : "dark");
}
