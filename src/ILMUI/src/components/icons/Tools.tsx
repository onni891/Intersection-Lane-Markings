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

// Advanced: a gear.
export const Gear = (props: IconProps) => (
  <IconBase {...props}>
    <circle cx="8" cy="8" r="2.2" />
    <path d="M8 1.5 V3.5 M8 12.5 V14.5 M1.5 8 H3.5 M12.5 8 H14.5 M3.4 3.4 L4.8 4.8 M11.2 11.2 L12.6 12.6 M3.4 12.6 L4.8 11.2 M11.2 4.8 L12.6 3.4" />
    <circle cx="8" cy="8" r="4.6" />
  </IconBase>
);

// Copy: two sheets.
export const Copy = (props: IconProps) => (
  <IconBase {...props}>
    <rect x="5.5" y="5.5" width="8" height="8.5" rx="1" />
    <path d="M10.5 3.5 V3 a1 1 0 0 0 -1 -1 H3.5 a1 1 0 0 0 -1 1 V10 a1 1 0 0 0 1 1 H4" />
  </IconBase>
);

// Paste: a clipboard.
export const Paste = (props: IconProps) => (
  <IconBase {...props}>
    <rect x="3" y="3" width="10" height="11.5" rx="1" />
    <rect x="6" y="1.5" width="4" height="3" rx="0.8" />
    <line x1="5.5" y1="8" x2="10.5" y2="8" />
    <line x1="5.5" y1="11" x2="9" y2="11" />
  </IconBase>
);

// Save: a floppy disk.
export const Save = (props: IconProps) => (
  <IconBase {...props}>
    <path d="M2.5 2.5 H11 L13.5 5 V13.5 H2.5 Z" />
    <rect x="5" y="2.5" width="5" height="3.5" />
    <rect x="4.5" y="9" width="7" height="4.5" />
  </IconBase>
);
