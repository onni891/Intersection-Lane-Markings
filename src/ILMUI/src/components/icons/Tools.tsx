import { IconBase, IconProps } from "./Icon";

// Tool strip icons. Fills use the stroke colour explicitly: cohtml draws currentColor in SVG
// as black.
const fillOf = (props: IconProps) => props.color ?? "rgba(232, 234, 237, 0.92)";

// Lines: a curve between two dots.
export const ToolLines = (props: IconProps) => (
  <IconBase {...props}>
    <path d="M3 13 C 4 6, 12 10, 13 3" />
    <circle cx="3" cy="13" r="1.6" fill={fillOf(props)} />
    <circle cx="13" cy="3" r="1.6" fill={fillOf(props)} />
  </IconBase>
);

// Areas: a filled polygon.
export const ToolAreas = (props: IconProps) => (
  <IconBase {...props}>
    <polygon points="3,4 12,2.5 14,11 6,13.5" />
    <line x1="5" y1="10" x2="9" y2="4" />
    <line x1="8" y1="12" x2="12" y2="6" />
  </IconBase>
);

// Crosswalks: zebra bars.
export const ToolCrosswalks = (props: IconProps) => (
  <IconBase {...props}>
    <rect x="2" y="3" width="2" height="10" fill={fillOf(props)} />
    <rect x="7" y="3" width="2" height="10" fill={fillOf(props)} />
    <rect x="12" y="3" width="2" height="10" fill={fillOf(props)} />
  </IconBase>
);

// Points: a point with a ring.
export const ToolPoints = (props: IconProps) => (
  <IconBase {...props}>
    <circle cx="8" cy="8" r="5" />
    <circle cx="8" cy="8" r="1.8" fill={fillOf(props)} />
  </IconBase>
);

export const Undo = (props: IconProps) => (
  <IconBase {...props}>
    <polyline points="5,3 2,6 5,9" />
    <path d="M2 6 H10 a4 4 0 0 1 0 8 H6" />
  </IconBase>
);

export const Redo = (props: IconProps) => (
  <IconBase {...props}>
    <polyline points="11,3 14,6 11,9" />
    <path d="M14 6 H6 a4 4 0 0 0 0 8 H10" />
  </IconBase>
);

// Advanced settings: a gear.
export const Gear = (props: IconProps) => (
  <IconBase {...props}>
    <circle cx="8" cy="8" r="2.2" />
    <path d="M8 1.5 V3.5 M8 12.5 V14.5 M1.5 8 H3.5 M12.5 8 H14.5 M3.4 3.4 L4.8 4.8 M11.2 11.2 L12.6 12.6 M3.4 12.6 L4.8 11.2 M11.2 4.8 L12.6 3.4" />
    <circle cx="8" cy="8" r="4.6" />
  </IconBase>
);
