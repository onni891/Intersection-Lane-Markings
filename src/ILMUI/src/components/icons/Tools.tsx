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

// Draw: a pencil.
export const Pencil = (props: IconProps) => (
  <IconBase {...props}>
    <path d="M10.5 2.5 L13.5 5.5 L5.5 13.5 L2 14 L2.5 10.5 Z" />
    <line x1="9" y1="4" x2="12" y2="7" />
  </IconBase>
);

// Edit: a cursor arrow.
export const Cursor = (props: IconProps) => (
  <IconBase {...props}>
    <path d="M3 2 L13 7.5 L8.5 8.8 L11 13.5 L9.2 14.4 L6.8 9.7 L3.5 12.5 Z" />
  </IconBase>
);

// Draw between the road's dots: two dots on a lane line.
export const SnapDots = (props: IconProps) => (
  <IconBase {...props}>
    <line x1="2" y1="12" x2="14" y2="4" strokeDasharray="2 2" />
    <circle cx="3.5" cy="11" r="1.8" fill={fillOf(props)} />
    <circle cx="12.5" cy="5" r="1.8" fill={fillOf(props)} />
  </IconBase>
);

// Draw anywhere: a crosshair.
export const Anywhere = (props: IconProps) => (
  <IconBase {...props}>
    <circle cx="8" cy="8" r="4.5" />
    <path d="M8 1.5 V5 M8 11 V14.5 M1.5 8 H5 M11 8 H14.5" />
  </IconBase>
);
