import { Button } from "./controls/Button";
import { Gauge } from "./controls/Gauge";
import { Indicator } from "./controls/Indicator";
import { Slider } from "./controls/Slider";
import { Text } from "./controls/Text";
import { Toggle } from "./controls/Toggle";
import { Value } from "./controls/Value";
import type { UiControlNode } from "../protocol";

export function ControlRenderer({ node }: { node: UiControlNode }) {
  switch (node.type) {
    case "gauge":
      return <Gauge node={node} />;
    case "indicator":
      return <Indicator node={node} />;
    case "text":
      return <Text node={node} />;
    case "value":
      return <Value node={node} />;
    case "button":
      return <Button node={node} />;
    case "toggle":
      return <Toggle node={node} />;
    case "slider":
      return <Slider node={node} />;
    default:
      return (
        <div className="tw-muted tw-control-wrap" style={{ minHeight: 48 }}>
          Unsupported control: {String(node.type)}
        </div>
      );
  }
}
