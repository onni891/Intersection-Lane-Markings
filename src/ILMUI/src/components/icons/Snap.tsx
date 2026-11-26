import { IconBase, IconProps } from "./Icon";

// Snap bar icons, after the game's own snap options.

// All options: an asterisk.
export const SnapAll = (props: IconProps) => (
  <IconBase {...props}>
    <line x1="8" y1="2" x2="8" y2="14" />
    <line x1="2.8" y1="5" x2="13.2" y2="11" />
    <line x1="2.8" y1="11" x2="13.2" y2="5" />
  </IconBase>
);

// Existing geometry: a curve with a point on it. The fill is explicit: cohtml draws
// currentColor in SVG as black.
export const SnapExisting = (props: IconProps) => (
  <IconBase {...props}>
    <path d="M2 13 C 5 4, 11 4, 14 13" />
    <circle cx="8" cy="6.3" r="1.8" fill={props.color ?? "rgba(232, 234, 237, 0.92)"} />
  </IconBase>
);

// 90 degrees: a right angle with its corner mark.
export const SnapAngle = (props: IconProps) => (
  <IconBase {...props}>
    <line x1="3" y1="13" x2="13" y2="13" />
    <line x1="8" y1="13" x2="8" y2="3" />
    <polyline points="8,10 11,10 11,13" />
  </IconBase>
);

// Road side: a point pushed against the kerb.
export const SnapSide = (props: IconProps) => (
  <IconBase {...props}>
    <line x1="2" y1="8" x2="8" y2="8" />
    <polyline points="6,6 8,8 6,10" />
    <rect x="10" y="4" width="4" height="8" />
  </IconBase>
);

// Guide lines: a crosshair.
export const SnapGuides = (props: IconProps) => (
  <IconBase {...props}>
    <circle cx="8" cy="8" r="4" />
    <line x1="8" y1="1" x2="8" y2="15" />
    <line x1="1" y1="8" x2="15" y2="8" />
  </IconBase>
);

// Length: a ruler.
export const SnapLength = (props: IconProps) => (
  <IconBase {...props}>
    <rect x="1.5" y="5" width="13" height="6" />
    <line x1="4.5" y1="5" x2="4.5" y2="8" />
    <line x1="8" y1="5" x2="8" y2="8.5" />
    <line x1="11.5" y1="5" x2="11.5" y2="8" />
  </IconBase>
);
