import { useEffect } from "react";
import { Shell } from "./components/Shell";
import { UiActionsContext, useUiTree } from "./hooks/useUiTree";
import { applyTheme } from "./theme";

export default function App() {
  const { tree, sendSet, sendAction, connected } = useUiTree();

  useEffect(() => {
    if (tree?.theme) {
      applyTheme(tree.theme);
    }
  }, [tree?.theme]);

  return (
    <UiActionsContext.Provider value={{ sendSet, sendAction }}>
      <Shell tree={tree} connected={connected} />
    </UiActionsContext.Provider>
  );
}
