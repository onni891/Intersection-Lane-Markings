import { useMemo } from "react";
import { bindValue, useValue, trigger } from "cs2/api";
import { BINDING_GROUP } from "../bindingGroup";

// Mirrors PanelStateVM published by the C# `ILMUISystem`. The binding is typed
// through GenericUIWriter, so field names are the contract. C# pushes a new object only when
// a hash of the marking buffers changes. Screen-space popover anchors travel on a separate
// per-frame binding (positionRegistry.ts).

export interface SegmentVM {
  lineIndex: number;
  segmentIndex: number;   // dense per-line index in buffer order, hidden segments included
  tStart: number;
  tEnd: number;
  visible: boolean;
  // Starts as the line's style; the user can override single segments in the popover.
  style: number;
  lengthM: number;        // arc length in metres along the segment's Bezier
}

export interface LineVM {
  lineIndex: number;
  style: number;          // MarkingStyle enum value
  // Curvature in integer percent: 0 is a straight chord, 50 the default arc (pull factor
  // 0.4), 100 the roundest (pull factor 0.8).
  curv: number;
  // Shaped by dragging its handles; the curvature value then doesn't apply.
  handles: boolean;
  // Made by auto markings.
  auto: boolean;
  segments: SegmentVM[];
}

// A closed polygon area on the selected node. Lines crossing the area cut it into pieces;
// pieceCount and visiblePieces count those.
export interface AreaVM {
  areaIndex: number;
  styleId: number;        // index into the C# kStyleSurfaceNames catalogue
  visible: boolean;
  vertexCount: number;
  pieceCount: number;
  visiblePieces: number;
}

// A user-placed free point on the selected node (Points tab).
export interface FreePointVM {
  id: number;
  onJunction: boolean; // inside the junction rather than on one of its roads
  lineCount: number;
  areaCount: number;
}

// Copied markings or a saved template (index -1 for the clipboard).
export interface TemplateVM {
  index: number;
  name: string;
  roads: number;
  isRoad: boolean;
  lines: number;
  areas: number;
  // Same kind and road count as the selection: can be pasted here.
  fits: boolean;
}

// Mirrors PasteResult in C#.
export const PASTE_RESULT = { Ok: 0, NothingToPaste: 1, RoadCountDiffers: 2, KindDiffers: 3, NoRoads: 4 } as const;

// Mirrors AutoMarkingMode in C#.
export const AUTO_MODE = { Default: 0, On: 1, Off: 2 } as const;

// Mirrors MarkingNodeToolSystem.EditTarget: what edit mode works on, set by the panel tab.
export const EDIT_TARGET = {
  Lines: 0,
  Areas: 1,
  Points: 2,
} as const;

// Mirrors MarkingNodeToolSystem.State.
export const TOOL_STATE = {
  Default: 0,
  NodeSelected: 1,
  SourceSelected: 2,
  AreaSelecting: 3,
} as const;

// Mirrors MarkingNodeToolSystem.LineMode.
export const LINE_MODE = {
  Points: 0,
  Free: 1,
  Edit: 2,
} as const;

export interface ToolStateVM {
  isActive: boolean;
  // TOOL_STATE value. In AreaSelecting the panel shows the contour progress and a cancel
  // button.
  toolState: number;
  // Vertices collected so far in the area contour being drawn (AreaSelecting only).
  areaVertexCount: number;
  // Fill style for the next area the user closes (cycled by U or set in the panel).
  currentAreaStyle: number;
  selectedNodeIndex: number; // -1 when no node is selected
  currentStyle: number;    // style for the next line drawn
  // Line the user clicked in the world (near its Bezier, not on a dot). lastClickedTick is
  // bumped on every click, even on the same line, and the panel expands the matching row
  // when it changes.
  lastClickedLine: number;
  lastClickedTick: number;
  // Line under the cursor in the world, -1 for none. The panel highlights its row, the
  // counterpart of cmdSetHoveredLine.
  hoveredLineInGame: number;
  // Area whose piece is under the cursor in the world. Lines and dots win when both match.
  hoveredAreaInGame: number;
  // The selected node carries MarkingOverride{All}: its vanilla markings are hidden
  // regardless of user lines.
  vanillaHidden: boolean;
  // Free draw: clicks on bare road place free points and continue the line (F hotkey).
  freeDraw: boolean;
  // LINE_MODE value: snap to points, free draw, or edit (handles and point dragging).
  lineMode: number;
  // Area open in edit mode, -1 for none. The panel expands its row.
  editArea: number;
  // The selection is a road stretch (mid-road editing) rather than a junction.
  selectedIsRoad: boolean;
  // User-placed free points on the selected node.
  freePointCount: number;
  // EDIT_TARGET value.
  editTarget: number;
  // SNAP bits that are on.
  snapMask: number;
  // Free point id under the cursor in the world, -1 for none.
  hoveredPointInGame: number;
  points: FreePointVM[];
  canUndo: boolean;
  canRedo: boolean;
  // Advanced tab (see ViewModels.cs).
  clipboard: TemplateVM | null;
  templates: TemplateVM[];
  pasteResult: number;
  pasteTick: number;
  pastedLines: number;
  pastedAreas: number;
  canTurn: boolean;
  autoGlobal: boolean;
  autoStyle: number;
  autoTurnsOnly: boolean;
  autoMode: number;
  autoLineCount: number;
  lines: LineVM[];
  areas: AreaVM[];
}

const EMPTY: ToolStateVM = {
  isActive: false,
  toolState: 0,
  areaVertexCount: 0,
  currentAreaStyle: 0,
  selectedNodeIndex: -1,
  currentStyle: 0,
  lastClickedLine: -1,
  lastClickedTick: 0,
  hoveredLineInGame: -1,
  hoveredAreaInGame: -1,
  vanillaHidden: false,
  freeDraw: false,
  lineMode: 0,
  editArea: -1,
  selectedIsRoad: false,
  freePointCount: 0,
  editTarget: 0,
  snapMask: 31,
  hoveredPointInGame: -1,
  points: [],
  canUndo: false,
  canRedo: false,
  clipboard: null,
  templates: [],
  pasteResult: 0,
  pasteTick: 0,
  pastedLines: 0,
  pastedAreas: 0,
  canTurn: false,
  autoGlobal: false,
  autoStyle: 1,
  autoTurnsOnly: true,
  autoMode: 0,
  autoLineCount: 0,
  lines: [],
  areas: [],
};

const STATE_BINDING = bindValue<ToolStateVM>(BINDING_GROUP, "GetPanelState", EMPTY);

// GenericUIWriter always writes every field and C# initialises the arrays, so only a null or
// undefined push needs guarding.
const normalizeToolState = (state: ToolStateVM | null | undefined): ToolStateVM =>
  state
    ? {
        ...state,
        lines: Array.isArray(state.lines) ? state.lines : [],
        areas: Array.isArray(state.areas) ? state.areas : [],
        points: Array.isArray(state.points) ? state.points : [],
        templates: Array.isArray(state.templates) ? state.templates : [],
      }
    : EMPTY;

// useValue returns the same object until C# pushes a new one, so memoizing on it keeps the
// result stable between pushes.
export const useToolState = (): ToolStateVM => {
  const state = useValue(STATE_BINDING);
  return useMemo(() => normalizeToolState(state), [state]);
};

// Commands to C#.

export const cmdToggleSegment = (lineIndex: number, segmentIndex: number) => {
  trigger(BINDING_GROUP, "ToggleSegment", lineIndex, segmentIndex);
};

export const cmdSetLineStyle = (lineIndex: number, style: number) => {
  trigger(BINDING_GROUP, "SetLineStyle", lineIndex, style);
};

// Per-segment style override; the line's own style stays unchanged.
export const cmdSetSegmentStyle = (lineIndex: number, segmentIndex: number, style: number) => {
  trigger(BINDING_GROUP, "SetSegmentStyle", lineIndex, segmentIndex, style);
};

export const cmdDeleteLine = (lineIndex: number) => {
  trigger(BINDING_GROUP, "DeleteLine", lineIndex);
};

// percent is 0..100; C# maps it onto the Bezier pull factor range 0..0.8.
export const cmdSetLineCurvature = (lineIndex: number, percent: number) => {
  trigger(BINDING_GROUP, "SetLineCurvature", lineIndex, percent);
};

// Toggles the "hide vanilla markings" override on the selected node. Works with no lines
// drawn.
export const cmdToggleVanillaMarkings = () => {
  trigger(BINDING_GROUP, "ToggleVanillaMarkings");
};

// Toggles the marking tool, same as Ctrl+M. Used by the toolbar button in GameTopLeft.
export const cmdActivateTool = () => {
  trigger(BINDING_GROUP, "ActivateTool");
};

// Style for the next line drawn; the panel counterpart of the Y hotkey.
export const cmdSetCurrentStyle = (style: number) => {
  trigger(BINDING_GROUP, "SetCurrentStyle", style);
};

// Fill style for the next area closed; the panel counterpart of the U hotkey.
export const cmdSetCurrentAreaStyle = (styleId: number) => {
  trigger(BINDING_GROUP, "SetCurrentAreaStyle", styleId);
};

// Switches between line drawing (NodeSelected) and area drawing (AreaSelecting), like the
// A hotkey. Leaving area mode drops an unfinished contour.
export const cmdToggleAreaMode = () => {
  trigger(BINDING_GROUP, "ToggleAreaMode");
};

// The line expanded in the panel; the tool shows its drag handles on the road. -1 for none.
export const cmdSetEditLine = (lineIndex: number) => {
  trigger(BINDING_GROUP, "SetEditLine", lineIndex);
};

// Splits the line at its middle through a new point that can be dragged.
export const cmdSplitLine = (lineIndex: number) => {
  trigger(BINDING_GROUP, "SplitLine", lineIndex);
};

// Free draw on/off, like the F hotkey.
export const cmdToggleFreeDraw = () => {
  trigger(BINDING_GROUP, "ToggleFreeDraw");
};

// The area row expanded in edit mode; the tool shows its vertices and handles. -1 for none.
export const cmdSetEditArea = (areaIndex: number) => {
  trigger(BINDING_GROUP, "SetEditArea", areaIndex);
};

// The mode switch: a LINE_MODE value.
export const cmdSetLineMode = (mode: number) => {
  trigger(BINDING_GROUP, "SetLineMode", mode);
};

export const cmdSetAreaStyle =(areaIndex: number, styleId: number) => {
  trigger(BINDING_GROUP, "SetAreaStyle", areaIndex, styleId);
};

export const cmdToggleAreaVisible = (areaIndex: number) => {
  trigger(BINDING_GROUP, "ToggleAreaVisible", areaIndex);
};

// The area's pieces and vanilla Area entities go away on the next tick.
export const cmdDeleteArea = (areaIndex: number) => {
  trigger(BINDING_GROUP, "DeleteArea", areaIndex);
};

// Removes all lines and areas from the selected node and clears the vanilla-hide override.
export const cmdResetNode = () => {
  trigger(BINDING_GROUP, "ResetNode");
};

// Line row hovered in the panel; MarkingOverlaySystem draws that line thicker and brighter
// on the road. Pass -1 on leave.
export const cmdSetHoveredLine = (lineIndex: number) => {
  trigger(BINDING_GROUP, "SetHoveredLine", lineIndex);
};

// Like cmdSetHoveredLine, but highlights a single segment. Pass (-1, -1) on leave.
export const cmdSetHoveredSegment = (lineIndex: number, segmentIndex: number) => {
  trigger(BINDING_GROUP, "SetHoveredSegment", lineIndex, segmentIndex);
};

// Area counterpart of cmdSetHoveredLine: the overlay outlines every piece of the area.
export const cmdSetHoveredArea = (areaIndex: number) => {
  trigger(BINDING_GROUP, "SetHoveredArea", areaIndex);
};

// Passes the row's own index, and C# clears the hover only if that index still holds it:
// cohtml can deliver the next row's mouseenter before this row's mouseleave.
export const cmdClearHoveredArea = (areaIndex: number) => {
  trigger(BINDING_GROUP, "ClearHoveredArea", areaIndex);
};

// What edit mode works on: an EDIT_TARGET value, sent when the panel tab changes.
export const cmdSetEditTarget = (target: number) => {
  trigger(BINDING_GROUP, "SetEditTarget", target);
};

// Removes the free point with its lines; areas only lose that corner.
export const cmdDeleteFreePoint = (id: number) => {
  trigger(BINDING_GROUP, "DeleteFreePoint", id);
};

// Removes every free point no line or area uses.
export const cmdRemoveUnusedPoints = () => {
  trigger(BINDING_GROUP, "RemoveUnusedPoints");
};

// Point row hovered on the Points tab: the overlay rings that point on the road.
export const cmdSetHoveredPoint = (id: number) => {
  trigger(BINDING_GROUP, "SetHoveredPoint", id);
};

// Clears only while that point is still the hovered one (see cmdClearHoveredArea).
export const cmdClearHoveredPoint = (id: number) => {
  trigger(BINDING_GROUP, "ClearHoveredPoint", id);
};

// Mirrors MarkingNodeToolSystem.SnapFlags.
export const SNAP = {
  Existing: 1,
  Angle: 2,
  RoadSide: 4,
  Guides: 8,
  Length: 16,
  All: 31,
} as const;

// The snap bar: which snap options are on (SNAP bits). Kept in the mod settings.
export const cmdSetSnapMask = (mask: number) => {
  trigger(BINDING_GROUP, "SetSnapMask", mask);
};

// Undo / redo on the selected junction or road (also Ctrl+Z / Ctrl+Y).
export const cmdUndo = () => {
  trigger(BINDING_GROUP, "Undo");
};

export const cmdRedo = () => {
  trigger(BINDING_GROUP, "Redo");
};

// Advanced tab: copy / paste and templates. Paste replaces the selection's markings (undo
// brings them back); Turn lays the last paste down rotated one road further.
export const cmdCopyMarkings = () => {
  trigger(BINDING_GROUP, "CopyMarkings");
};

export const cmdPasteMarkings = () => {
  trigger(BINDING_GROUP, "PasteMarkings");
};

export const cmdTurnPaste = () => {
  trigger(BINDING_GROUP, "TurnPaste");
};

export const cmdSaveTemplate = (name: string) => {
  trigger(BINDING_GROUP, "SaveTemplate", name);
};

export const cmdPasteTemplate = (index: number) => {
  trigger(BINDING_GROUP, "PasteTemplate", index);
};

export const cmdDeleteTemplate = (index: number) => {
  trigger(BINDING_GROUP, "DeleteTemplate", index);
};

// Auto markings.
export const cmdSetAutoMarkings = (on: boolean) => {
  trigger(BINDING_GROUP, "SetAutoMarkings", on);
};

export const cmdSetAutoMarkingStyle = (style: number) => {
  trigger(BINDING_GROUP, "SetAutoMarkingStyle", style);
};

export const cmdSetAutoTurnsOnly = (on: boolean) => {
  trigger(BINDING_GROUP, "SetAutoTurnsOnly", on);
};

// This junction's own mode (AUTO_MODE).
export const cmdSetAutoMode = (mode: number) => {
  trigger(BINDING_GROUP, "SetAutoMode", mode);
};

// Turns the junction's auto lines into normal lines and stops auto markings here.
export const cmdKeepAutoLines = () => {
  trigger(BINDING_GROUP, "KeepAutoLines");
};
