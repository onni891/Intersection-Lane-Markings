// React's KeyboardEvent is aliased so the bare name stays the DOM type, which the
// document-level hotkey handler below uses.
import { ChangeEvent, Component, ErrorInfo, KeyboardEvent as ReactKeyboardEvent, MouseEvent as ReactMouseEvent, ReactNode, useEffect, useState } from "react";
import { createPortal } from "react-dom";
import { Panel as VanillaPanel, Scrollable } from "cs2/ui";
import {
  useToolState,
  cmdToggleSegment,
  cmdSetLineStyle,
  cmdSetSegmentStyle,
  cmdDeleteLine,
  cmdSetHoveredLine,
  cmdSetHoveredSegment,
  cmdSetHoveredArea,
  cmdClearHoveredArea,
  cmdSetLineCurvature,
  cmdToggleVanillaMarkings,
  cmdActivateTool,
  cmdSetCurrentStyle,
  cmdSetCurrentAreaStyle,
  cmdToggleAreaMode,
  cmdSetLineMode,
  cmdSetEditLine,
  cmdSetEditArea,
  cmdSplitLine,
  cmdSetAreaStyle,
  cmdToggleAreaVisible,
  cmdDeleteArea,
  cmdResetNode,
  cmdSetEditTarget,
  cmdSetSnapMask,
  SNAP,
  cmdSetCurrentCrosswalkStyle,
  cmdSetCurrentCrosswalkWidth,
  cmdSetCrosswalkStyle,
  cmdSetCrosswalkWidth,
  cmdDeleteCrosswalk,
  cmdSetEditCrosswalk,
  cmdSetHoveredCrosswalk,
  cmdClearHoveredCrosswalk,
  cmdUndo,
  cmdRedo,
  CROSSWALK_STYLES,
  CrosswalkVM,
  cmdDeleteFreePoint,
  cmdRemoveUnusedPoints,
  cmdSetHoveredPoint,
  cmdClearHoveredPoint,
  TOOL_STATE,
  LINE_MODE,
  EDIT_TARGET,
  LineVM,
  SegmentVM,
  AreaVM,
  FreePointVM,
} from "../hooks/useToolState";
import {
  usePinnedStyles,
  cmdTogglePinLineStyle,
  cmdTogglePinAreaStyle,
} from "../hooks/usePinnedStyles";
import { registerSegmentAnchor, setAnchorExpanded, segKey, areaKey } from "../hooks/positionRegistry";
import { useConfirm, useKeyedConfirm, Confirm } from "../hooks/useConfirm";
import { ChevronRight, Eye, EyeOff, Trash, Cycle, SnapAll, SnapExisting, SnapAngle, SnapSide, SnapGuides, SnapLength, ToolLines, ToolAreas, ToolCrosswalks, ToolPoints, Undo, Redo, Gear } from "../components/icons";
import { LineStylePreview, AreaStylePreview, isG87LineStyle } from "../components/stylePreviews";
import { Dropdown, DropdownOption } from "../components/Dropdown";
import { TooltipProvider, Tooltip } from "../components/Tooltip";
import { useT } from "../i18n";
import type { StringKey } from "../i18n";
import { tokens as T } from "../styles/tokens";
import {
  Panel,
  WindowPosition,
  WindowBody,
  WindowFixed,
  PanelStickyChrome,
  PanelTitle,
  StatusRow,
  StatusDot,
  ToggleRow,
  IconToggleBtn,
  FoldoutHeader,
  NodeIdText,
  PanelHint,
  PanelList,
  ModeRow,
  ModeBtn,
  DraftBox,
  DraftHint,
  SnapRow,
  SnapLabel,
  ToolStrip,
  ToolBtn,
  ToolLabel,
  StripSide,
  StripSideRow,
  StripIconBtn,
  FieldRow,
  FieldLabel,
  SectionTitle,
  HintsBox,
  HintRow,
  HintKey,
  LineRowOuter,
  LineHeader,
  LineChevron,
  LineTitle,
  SwatchWrap,
  G87Mark,
  LineSegCount,
  LineBody,
  StyleRow,
  CurvRow,
  CurvLabel,
  CurvInput,
  CurvUnit,
  CurvResetBtn,
  CurvStepBtn,
  PopoverRoot,
  PopoverBtn,
  PopoverMarker,
  PopoverDropdownWrap,
  SegmentRow,
  SegmentInfo,
  SegmentLen,
  SegmentIndicator,
  Btn,
  ConfirmRow,
} from "./panel.styles";

// An exception inside the panel must not reach the game's React root, where it would take
// down the whole HUD. The error is logged and a small error panel with a retry button is
// shown instead.
class PanelErrorBoundary extends Component<{ children: ReactNode }, { error: Error | null }> {
  state = { error: null as Error | null };
  static getDerivedStateFromError(error: Error) { return { error }; }
  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error("ILM panel crashed:", error, info);
  }
  render() {
    if (this.state.error) {
      return <PanelErrorFallback error={this.state.error} onRetry={() => this.setState({ error: null })} />;
    }
    return this.props.children;
  }
}

const PanelErrorFallback = ({ error, onRetry }: { error: Error; onRetry: () => void }) => {
  const t = useT();
  return (
    <Panel style={{ borderColor: T.colorDanger }}>
      <PanelTitle>{t("panel.error.title")}</PanelTitle>
      <PanelHint>{error.message}</PanelHint>
      <Btn onClick={onRetry}>{t("panel.error.retry")}</Btn>
    </Panel>
  );
};

// MarkingStyle values from C#, in dropdown order.
const STYLE_VALUES = [0, 1, 5, 8, 4, 10, 11, 13, 12, 2, 3, 6, 7, 14, 15, 9] as const;
type StyleValue = typeof STYLE_VALUES[number];

// STYLE_VALUES groups related looks for the dropdown (vanilla white, vanilla yellow, G87,
// curb). The enum values themselves only ever get appended, so their numeric order means
// nothing for display.
const STYLE_KEYS: Record<number, StringKey> = {
  0: "style.solid",
  1: "style.dashed",
  2: "style.g87Solid",
  3: "style.g87Dashed",
  4: "style.doubleSolid",
  5: "style.dashedDense",
  6: "style.g87Yellow",
  7: "style.g87YellowDashed",
  8: "style.dashedLong",
  9: "style.curb",
  10: "style.yellowSolid",
  11: "style.yellowDashed",
  12: "style.yellowDoubleSolid",
  13: "style.yellowSolidDashed",
  14: "style.wide30",
  15: "style.wide60",
};

const styleLabel = (t: ReturnType<typeof useT>, style: number): string =>
  t(STYLE_KEYS[style] ?? "style.unknown");

// Area fill styles; ids match kStyleSurfaceNames in MarkingAreaEmissionSystem. Ids are
// saved with each area, so they only get appended and their order means nothing for
// display. 7-13 and 16 are unused slots and stay hidden; 15 and 17+ are the vanilla surfaces
// built by VanillaSurfaceLateClone.
const AREA_STYLE_VALUES = [0, 14, 1, 2, 3, 4, 5, 6, 15, 17, 18, 19, 20, 21, 22] as const;
const AREA_STYLE_ID_SET: ReadonlySet<number> = new Set(AREA_STYLE_VALUES);

const areaStyleLabel = (t: ReturnType<typeof useT>, styleId: number): string => {
  const key = `areaStyle.${styleId}` as StringKey;
  return AREA_STYLE_ID_SET.has(styleId) ? t(key) : t("style.unknown");
};

// Option lists for every style picker (panel rows and popovers). Pinned favourites
// (usePinnedStyles) come first, both groups in catalogue order.
const sortPinnedFirst = <V,>(opts: DropdownOption<V>[]): DropdownOption<V>[] =>
  [...opts.filter((o) => o.pinned), ...opts.filter((o) => !o.pinned)];

const makeLineStyleOptions = (t: ReturnType<typeof useT>, pinned: number[]): DropdownOption<StyleValue>[] =>
  sortPinnedFirst(
    STYLE_VALUES.map((s) => ({
      value: s,
      label: styleLabel(t, s),
      preview: <LineStylePreview style={s} width={28} height={8} />,
      pinned: pinned.includes(s),
    })),
  );

const makeAreaStyleOptions = (t: ReturnType<typeof useT>, pinned: number[]): DropdownOption<number>[] =>
  sortPinnedFirst(
    AREA_STYLE_VALUES.map((s) => ({
      value: s,
      label: areaStyleLabel(t, s),
      preview: <AreaStylePreview styleId={s} size={12} />,
      pinned: pinned.includes(s),
    })),
  );

// Mounted into GameTopRight by moduleRegistry.
export const ILMPanel = () => (
  <PanelErrorBoundary>
    <TooltipProvider>
      <ILMPanelInner />
    </TooltipProvider>
  </PanelErrorBoundary>
);

// Hover expansion shared by the in-world popovers. Collapsed, a popover is a small marker so
// a line with many segments doesn't fill the view with buttons; hovering shows its buttons.
// `hover` and `unhover` drive the in-world highlight.
const usePopoverHover = (anchorKey: string, hover: () => void, unhover: () => void) => {
  const [expanded, setExpanded] = useState(false);
  // The style menu is portalled to document.body, so while it is open the cursor is outside
  // PopoverRoot. Collapsing then would unmount the dropdown mid-pick.
  const [styleOpen, setStyleOpen] = useState(false);
  const deleteConfirm = useConfirm();
  const showButtons = expanded || styleOpen;

  // Lets the registry keep the popover at full size while the buttons are shown. The
  // flag is cleared on unmount when the ref callback unregisters the anchor.
  useEffect(() => {
    setAnchorExpanded(anchorKey, showButtons);
  }, [anchorKey, showButtons]);

  return {
    showButtons,
    deleteConfirm,
    onMouseEnter: () => {
      setExpanded(true);
      hover();
    },
    onMouseLeave: () => {
      deleteConfirm.disarm();
      // Moving into the portalled style menu also leaves the root. Keep the buttons
      // while the menu is open; onStyleOpenChange collapses them when it closes.
      if (styleOpen) return;
      setExpanded(false);
      unhover();
    },
    onStyleOpenChange: (open: boolean) => {
      setStyleOpen(open);
      // The cursor may be over the portalled menu, outside the root, so no mouseleave will
      // come. Collapse here; hovering again re-expands.
      if (!open) {
        setExpanded(false);
        unhover();
      }
    },
  };
};

const ARMED_DELETE_STYLE = {
  background: T.colorDangerSoft,
  borderColor: T.colorDanger,
  color: T.colorDanger,
};

// Two-press delete in a popover: one button, red while armed like the panel's confirm row.
const PopoverDeleteButton = ({
  confirm,
  label,
  onDelete,
}: {
  confirm: Confirm;
  label: string;
  onDelete: () => void;
}) => {
  const t = useT();
  return (
    <Tooltip content={confirm.armed ? t("line.delete.confirm.btn") : label}>
      <PopoverBtn
        $active={confirm.armed}
        style={confirm.armed ? ARMED_DELETE_STYLE : undefined}
        onClick={() => confirm.press(onDelete)}
      >
        <Trash size={14} color={confirm.armed ? T.colorDanger : undefined} />
      </PopoverBtn>
    </Tooltip>
  );
};

// In-world popover at a segment's midpoint. The collapsed marker is white when the segment
// is visible and red when hidden; the buttons are the visibility toggle, style picker and a
// two-press delete of the whole line.
//
// Position is not React state: the root registers with positionRegistry, which writes
// left/top/transform on each GetScreenPoints push, hides the popover while its segment is
// off-screen and scales it with camera distance.
const SegmentPopover = ({ seg }: { seg: SegmentVM }) => {
  const t = useT();
  const pinned = usePinnedStyles();
  const key = segKey(seg.lineIndex, seg.segmentIndex);
  const popover = usePopoverHover(
    key,
    // Highlight just this segment, not the whole line.
    () => cmdSetHoveredSegment(seg.lineIndex, seg.segmentIndex),
    () => cmdSetHoveredSegment(-1, -1),
  );

  // Portalled to document.body: inside GameTopRight, `position: fixed` would be relative to
  // the slot rather than the viewport (the slot probably has a transform), while the screen
  // points from Camera.WorldToScreenPoint are viewport coordinates.
  return createPortal(
    <PopoverRoot
      ref={(el: HTMLElement | null) => registerSegmentAnchor(key, el)}
      onMouseEnter={popover.onMouseEnter}
      onMouseLeave={popover.onMouseLeave}
    >
      {!popover.showButtons ? (
        <PopoverMarker $hidden={!seg.visible} />
      ) : (
        <>
          <Tooltip
            content={seg.visible ? t("segment.hide.tooltip") : t("segment.show.tooltip")}
          >
            <PopoverBtn
              // Highlighted while the segment is hidden.
              $active={!seg.visible}
              onClick={() => cmdToggleSegment(seg.lineIndex, seg.segmentIndex)}
            >
              {seg.visible ? <Eye size={14} /> : <EyeOff size={14} />}
            </PopoverBtn>
          </Tooltip>
          <PopoverDropdownWrap>
            <Dropdown
              value={seg.style as StyleValue}
              options={makeLineStyleOptions(t, pinned.lineStyles)}
              onChange={(s) => cmdSetSegmentStyle(seg.lineIndex, seg.segmentIndex, s)}
              onTogglePin={cmdTogglePinLineStyle}
              onOpenChange={popover.onStyleOpenChange}
            />
          </PopoverDropdownWrap>
          <PopoverDeleteButton
            confirm={popover.deleteConfirm}
            label={t("line.delete")}
            onDelete={() => cmdDeleteLine(seg.lineIndex)}
          />
        </>
      )}
    </PopoverRoot>,
    document.body,
  );
};

// In-world popover at an area's centroid (sent on GetScreenPoints under an `area:` key).
// Works like SegmentPopover, but the collapsed face is the area's fill swatch.
const AreaPopover = ({ area }: { area: AreaVM }) => {
  const t = useT();
  const pinned = usePinnedStyles();
  const key = areaKey(area.areaIndex);
  const popover = usePopoverHover(
    key,
    () => cmdSetHoveredArea(area.areaIndex),
    () => cmdClearHoveredArea(area.areaIndex),
  );

  return createPortal(
    <PopoverRoot
      ref={(el: HTMLElement | null) => registerSegmentAnchor(key, el)}
      onMouseEnter={popover.onMouseEnter}
      onMouseLeave={popover.onMouseLeave}
    >
      {!popover.showButtons ? (
        area.visible ? (
          <AreaStylePreview styleId={area.styleId} size={12} />
        ) : (
          <PopoverMarker $hidden />
        )
      ) : (
        <>
          <Tooltip content={area.visible ? t("area.hide.tooltip") : t("area.show.tooltip")}>
            <PopoverBtn
              $active={!area.visible}
              onClick={() => cmdToggleAreaVisible(area.areaIndex)}
            >
              {area.visible ? <Eye size={14} /> : <EyeOff size={14} />}
            </PopoverBtn>
          </Tooltip>
          <PopoverDropdownWrap>
            <Dropdown
              value={area.styleId}
              options={makeAreaStyleOptions(t, pinned.areaStyles)}
              onChange={(s) => cmdSetAreaStyle(area.areaIndex, s)}
              onTogglePin={cmdTogglePinAreaStyle}
              onOpenChange={popover.onStyleOpenChange}
            />
          </PopoverDropdownWrap>
          <PopoverDeleteButton
            confirm={popover.deleteConfirm}
            label={t("area.delete")}
            onDelete={() => cmdDeleteArea(area.areaIndex)}
          />
        </>
      )}
    </PopoverRoot>,
    document.body,
  );
};

// Hotkey list, on the Advanced tab and the "select a node" card.
const HotkeysList = () => {
  const t = useT();
  return (
    <HintsBox>
      <HintRow><HintKey>Ctrl+M</HintKey><span>{t("hotkeys.toggle")}</span></HintRow>
      <HintRow><HintKey>Y</HintKey><span>{t("hotkeys.cycleLine")}</span></HintRow>
      <HintRow><HintKey>A</HintKey><span>{t("hotkeys.areaMode")}</span></HintRow>
      <HintRow><HintKey>U</HintKey><span>{t("hotkeys.cycleArea")}</span></HintRow>
      <HintRow><HintKey>F</HintKey><span>{t("hotkeys.freeDraw")}</span></HintRow>
      <HintRow><HintKey>Ctrl+Z</HintKey><span>{t("hotkeys.undo")}</span></HintRow>
      <HintRow><HintKey>{t("hotkeys.rmb")}</HintKey><span>{t("hotkeys.rmb.desc")}</span></HintRow>
      <HintRow><HintKey>{t("hotkeys.esc")}</HintKey><span>{t("hotkeys.esc.desc")}</span></HintRow>
    </HintsBox>
  );
};

// The game's own panel (cs2/ui): vanilla title bar, close button and blurred body, so the
// tool window looks like the rest of the CS2 interface. Closing deactivates the tool.
// `fixed` stays put above the scrolling body (tabs and status); the body scrolls with the
// game's own Scrollable, which draws the vanilla scrollbar.
const ILMWindow = ({ fixed, children }: { fixed?: ReactNode; children: ReactNode }) => {
  const t = useT();
  return (
    <WindowPosition>
      <VanillaPanel header={t("panel.appTitle")} onClose={() => cmdActivateTool()}>
        {fixed && <WindowFixed>{fixed}</WindowFixed>}
        <Scrollable vertical trackVisibility="scrollable" style={{ maxHeight: T.panelScrollMaxHeight }}>
          <WindowBody>{children}</WindowBody>
        </Scrollable>
      </VanillaPanel>
    </WindowPosition>
  );
};

type PanelTab = "lines" | "areas" | "crosswalks" | "points" | "advanced";

// The next step for the user in the current tool state.
const toolStatus = (
  t: ReturnType<typeof useT>,
  state: { toolState: number; areaVertexCount: number; freeDraw: boolean; lineMode: number },
  lineOpen: boolean,
): string => {
  if (state.toolState === TOOL_STATE.AreaSelecting) {
    return t("status.area", { n: state.areaVertexCount });
  }
  if (state.lineMode === LINE_MODE.Edit) {
    return lineOpen ? t("status.edit.open") : t("status.edit.pick");
  }
  if (state.freeDraw) {
    return state.toolState === TOOL_STATE.SourceSelected ? t("status.free.next") : t("status.free.first");
  }
  if (state.toolState === TOOL_STATE.SourceSelected) {
    return t("status.line.second");
  }
  return t("status.line.first");
};


const ILMPanelInner = () => {
  const state = useToolState();
  const t = useT();
  const pinned = usePinnedStyles();
  // Expanded line row, -1 for none.
  const [expandedLine, setExpandedLine] = useState<number>(-1);
  // Expanded area row, independent of expandedLine.
  const [expandedArea, setExpandedArea] = useState<number>(-1);
  // Line armed for deletion by the Delete key; a second press in time deletes it.
  // DeleteLineButton shows its confirm row while this matches its line.
  const keyDelete = useKeyedConfirm<number>();
  // Folded lists keep only the header (with count) and the row that is expanded or hovered
  // in the world, so a busy junction doesn't turn the panel into a wall. Kept across node
  // switches on purpose.
  // Chosen tab. Area drawing is the C# AreaSelecting state, so the Areas tab follows it:
  // while drawing an area the Areas tab shows, and when drawing ends it falls back to Lines.
  const [tab, setTab] = useState<PanelTab>("lines");

  // The expanded rows react to changes pushed by C#. They are adjusted while rendering, on
  // the render that first sees the change, instead of in effects that would commit a frame
  // with the stale row first. The seen values start out null so the first render applies
  // them too.
  const lineListKey = `${state.selectedNodeIndex}:${state.lines.length}`;
  const areaListKey = `${state.selectedNodeIndex}:${state.areas.length}`;
  const [seenLineListKey, setSeenLineListKey] = useState<string | null>(null);
  const [seenAreaListKey, setSeenAreaListKey] = useState<string | null>(null);
  const [seenClickTick, setSeenClickTick] = useState<number | null>(null);

  if (lineListKey !== seenLineListKey || state.lastClickedTick !== seenClickTick) {
    let next = expandedLine;
    // Another node or line count: a node with a single line opens with that line expanded,
    // and a row past the end of the list is dropped.
    if (lineListKey !== seenLineListKey) {
      const [prevNode, prevLen] = (seenLineListKey ?? "-1:0").split(":").map(Number);
      setSeenLineListKey(lineListKey);
      // A line was just drawn on this node: open it (new lines are appended).
      if (prevNode === state.selectedNodeIndex && state.lines.length > prevLen) next = state.lines.length - 1;
      else if (state.lines.length === 1) next = 0;
      else if (next >= state.lines.length) next = -1;
    }
    // A click on a line in the world expands its row, a click that hit no line collapses.
    // The tick changes on every click, so clicking the same line again still counts.
    if (state.lastClickedTick !== seenClickTick) {
      setSeenClickTick(state.lastClickedTick);
      if (state.lastClickedLine >= 0 && state.lastClickedLine < state.lines.length) {
        next = state.lastClickedLine;
      } else if (state.lastClickedTick > 0 && state.lastClickedLine === -1) {
        next = -1;
      }
    }
    if (next !== expandedLine) setExpandedLine(next);
  }

  // Edit mode on the Areas tab: an area opened (or closed) in the world expands (or collapses)
  // its row. Areas can only be opened from the Areas tab, lines only from the Lines tab.
  const [seenEditArea, setSeenEditArea] = useState<number | null>(null);
  if (state.editArea !== seenEditArea) {
    setSeenEditArea(state.editArea);
    if (seenEditArea !== null && tab === "areas" && state.lineMode === LINE_MODE.Edit) {
      setExpandedArea(state.editArea);
    }
  }

  // Drop the expanded area row when the list shrinks below it.
  if (areaListKey !== seenAreaListKey) {
    const [prevNode, prevLen] = (seenAreaListKey ?? "-1:0").split(":").map(Number);
    setSeenAreaListKey(areaListKey);
    // An area was just closed on this node: open it.
    if (prevNode === state.selectedNodeIndex && state.areas.length > prevLen) setExpandedArea(state.areas.length - 1);
    else if (expandedArea >= state.areas.length) setExpandedArea(-1);
  }

  // The open line or area gets drag handles on the road: lines from the Lines tab, areas from
  // the Areas tab in edit mode. The tool ignores both outside edit mode.
  const drawingArea = state.toolState === TOOL_STATE.AreaSelecting;
  const handleLine = state.isActive && !drawingArea && tab === "lines" ? expandedLine : -1;
  useEffect(() => {
    cmdSetEditLine(handleLine);
  }, [handleLine, state.selectedNodeIndex]);
  const handleArea = state.isActive && !drawingArea && tab === "areas" && state.lineMode === LINE_MODE.Edit ? expandedArea : -1;
  useEffect(() => {
    cmdSetEditArea(handleArea);
  }, [handleArea, state.selectedNodeIndex]);

  // The tab decides what edit mode works on, so a click while editing lines never opens an
  // area and the other way round. The Points tab edits free points only.
  const editTarget =
    drawingArea || (tab === "areas" && state.lineMode === LINE_MODE.Edit)
      ? EDIT_TARGET.Areas
      : tab === "points"
        ? EDIT_TARGET.Points
        : tab === "crosswalks"
          ? EDIT_TARGET.Crosswalks
          : EDIT_TARGET.Lines;
  useEffect(() => {
    if (state.isActive) cmdSetEditTarget(editTarget);
  }, [editTarget, state.isActive, state.selectedNodeIndex]);

  // Crosswalks: the expanded row follows a crosswalk placed or clicked in the world, and in
  // edit mode the expanded one gets its handles on the road.
  const [expandedCw, setExpandedCw] = useState<number>(-1);
  const [seenCwTick, setSeenCwTick] = useState<number | null>(null);
  if (state.lastCrosswalkTick !== seenCwTick) {
    setSeenCwTick(state.lastCrosswalkTick);
    if (seenCwTick !== null) setExpandedCw(state.lastCrosswalk);
  }
  if (expandedCw >= state.crosswalks.length && expandedCw !== -1) setExpandedCw(-1);
  const handleCw = state.isActive && tab === "crosswalks" && state.lineMode === LINE_MODE.Edit ? expandedCw : -1;
  useEffect(() => {
    cmdSetEditCrosswalk(handleCw);
  }, [handleCw, state.selectedNodeIndex]);

  // The draw sub-mode for lines (snap to dots or free), kept while editing so Draw returns
  // to it.
  const [lineDrawMode, setLineDrawMode] = useState<number>(LINE_MODE.Points);
  if (state.lineMode !== LINE_MODE.Edit && state.lineMode !== lineDrawMode) setLineDrawMode(state.lineMode);

  // Panel shortcuts while a node is selected. Keys typed into text fields and combos with
  // Ctrl/Meta/Alt are left alone; those belong to the input or the game.
  useEffect(() => {
    if (!state.isActive || state.selectedNodeIndex < 0) return;

    const handler = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      const tag = (e.target as HTMLElement | null)?.tagName?.toUpperCase();
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;

      const lineCount = state.lines.length;

      // Tab / Shift+Tab: cycle the expanded line.
      if (e.key === "Tab" && lineCount > 0) {
        e.preventDefault();
        const cur = expandedLine < 0 ? -1 : expandedLine;
        const step = e.shiftKey ? -1 : 1;
        const next = ((cur + step) % lineCount + lineCount) % lineCount;
        setExpandedLine(next);
        keyDelete.disarm();
        return;
      }

      // Esc: cancel a pending delete first, then collapse.
      if (e.key === "Escape") {
        if (keyDelete.armed !== null) {
          keyDelete.disarm();
        } else if (expandedLine >= 0) {
          setExpandedLine(-1);
        }
        return;
      }

      // Enter: expand the first line when nothing is expanded.
      if (e.key === "Enter") {
        if (expandedLine < 0 && lineCount > 0) setExpandedLine(0);
        return;
      }

      // Delete: the first press arms, the second deletes, as with the button.
      if (e.key === "Delete" && expandedLine >= 0) {
        keyDelete.press(expandedLine, () => cmdDeleteLine(expandedLine));
        return;
      }

      // 1..5: pick one of the first five dropdown styles for the expanded line.
      if (expandedLine >= 0 && /^[1-5]$/.test(e.key)) {
        const idx = parseInt(e.key, 10) - 1;
        if (idx >= 0 && idx < STYLE_VALUES.length) {
          cmdSetLineStyle(expandedLine, STYLE_VALUES[idx]);
        }
        return;
      }
    };

    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [state.isActive, state.selectedNodeIndex, state.lines.length, expandedLine, keyDelete]);

  if (!state.isActive) return null;

  const inAreaMode = state.toolState === TOOL_STATE.AreaSelecting;
  const editMode = state.lineMode === LINE_MODE.Edit;
  // The Areas tab is either drawing (the C# AreaSelecting state) or editing areas (edit mode).
  // Area drawing that ended on its own outside edit mode falls back to the Lines tab.
  const shownTab: PanelTab = inAreaMode ? "areas" : tab === "areas" && !editMode ? "lines" : tab;

  // Tabs drive the C# drawing state: Areas enters area drawing (unless editing), the others
  // leave it.
  const openTab = (next: PanelTab) => {
    setTab(next);
    if (next === "areas" && !inAreaMode && !editMode) cmdToggleAreaMode();
    if (next !== "areas" && inAreaMode) cmdToggleAreaMode();
  };
  // The Draw / Edit switch under the tool strip. It is one mode for every tool, so switching
  // tools keeps you drawing or editing. Drawing areas is the C# area mode.
  const setDraw = () => {
    if (editMode) cmdSetLineMode(lineDrawMode);
    if (tab === "areas" && !inAreaMode) cmdToggleAreaMode();
  };
  const setEdit = () => {
    if (inAreaMode) cmdToggleAreaMode();
    cmdSetLineMode(LINE_MODE.Edit);
  };

  // No node selected yet: a short "how to start" card, so activating the tool gives
  // visible feedback before the first node click.
  if (state.selectedNodeIndex < 0) {
    return (
      <ILMWindow
        fixed={
          <StatusRow>
            <StatusDot />
            <span>{t("panel.hint.selectNode")}</span>
          </StatusRow>
        }
      >
        <HotkeysList />
      </ILMWindow>
    );
  }

  const popoverLine =
    expandedLine >= 0 && expandedLine < state.lines.length ? state.lines[expandedLine] : null;
  const popoverArea = state.areas.find((a) => a.areaIndex === expandedArea) ?? null;

  const lineStyleOptions = makeLineStyleOptions(t, pinned.lineStyles);
  const statusText =
    shownTab === "advanced"
      ? t("status.advanced")
      : shownTab === "points"
        ? t("status.points")
      : shownTab === "crosswalks"
        ? editMode
          ? expandedCw >= 0 ? t("status.cw.open") : t("status.cw.pick")
          : t("status.cw.place")
      : shownTab === "areas" && editMode && !inAreaMode
        ? popoverArea ? t("status.edit.area") : t("area.edit.pick")
        : toolStatus(t, state, popoverLine !== null);
  const areaStyleOptions = makeAreaStyleOptions(t, pinned.areaStyles);
  const crosswalkStyleOptions = makeCrosswalkStyleOptions(t);

  // Tool strip: one button per kind of marking, then undo / redo and the advanced gear.
  // Draw / Edit below it applies to the chosen tool; Points is always editing.
  const tools: { id: PanelTab; key: StringKey; tip: StringKey; Icon: (p: { size?: number }) => JSX.Element }[] = [
    { id: "lines", key: "tab.lines", tip: "tool.lines.tooltip", Icon: ToolLines },
    { id: "areas", key: "tab.areas", tip: "tool.areas.tooltip", Icon: ToolAreas },
    { id: "crosswalks", key: "tab.crosswalks", tip: "tool.crosswalks.tooltip", Icon: ToolCrosswalks },
    { id: "points", key: "tab.points", tip: "tool.points.tooltip", Icon: ToolPoints },
  ];
  const showDrawEdit = shownTab !== "points" && shownTab !== "advanced";
  const drawing = shownTab === "areas" ? inAreaMode : !editMode;
  const fixed = (
    <>
      <ToolStrip>
        {tools.map(({ id, key, tip, Icon }) => (
          <Tooltip key={id} content={t(tip)}>
            <ToolBtn $active={shownTab === id} onClick={() => openTab(id)}>
              <Icon size={18} />
              <ToolLabel>{t(key)}</ToolLabel>
            </ToolBtn>
          </Tooltip>
        ))}
        <StripSide>
          <StripSideRow>
            <Tooltip content={t("undo.tooltip")}>
              <StripIconBtn $disabled={!state.canUndo} onClick={() => state.canUndo && cmdUndo()}>
                <Undo size={13} />
              </StripIconBtn>
            </Tooltip>
            <Tooltip content={t("redo.tooltip")}>
              <StripIconBtn $disabled={!state.canRedo} onClick={() => state.canRedo && cmdRedo()}>
                <Redo size={13} />
              </StripIconBtn>
            </Tooltip>
          </StripSideRow>
          <StripSideRow>
            <Tooltip content={t("tab.advanced")}>
              <StripIconBtn $active={shownTab === "advanced"} onClick={() => openTab(shownTab === "advanced" ? "lines" : "advanced")}>
                <Gear size={13} />
              </StripIconBtn>
            </Tooltip>
          </StripSideRow>
        </StripSide>
      </ToolStrip>
      {showDrawEdit && (
        <ModeRow>
          <Tooltip content={t("action.draw.tooltip")}>
            <ModeBtn $active={drawing} onClick={setDraw}>{t("action.draw")}</ModeBtn>
          </Tooltip>
          <Tooltip content={t("action.edit.tooltip")}>
            <ModeBtn $active={!drawing && editMode} onClick={setEdit}>{t("mode.edit")}</ModeBtn>
          </Tooltip>
        </ModeRow>
      )}
      <StatusRow>
        <StatusDot />
        <span>{statusText}</span>
      </StatusRow>
    </>
  );

  return (
    <>
      <ILMWindow fixed={fixed}>
        {shownTab === "lines" && (
          <>
            {/* Drawing: snap to the road's dots, or place points anywhere. */}
            {!editMode ? (
              <ModeRow>
                <Tooltip content={t("mode.lines.tooltip")}>
                  <ModeBtn $active={state.lineMode === LINE_MODE.Points} onClick={() => cmdSetLineMode(LINE_MODE.Points)}>
                    {t("mode.points")}
                  </ModeBtn>
                </Tooltip>
                <Tooltip content={t("mode.free.tooltip")}>
                  <ModeBtn $active={state.lineMode === LINE_MODE.Free} onClick={() => cmdSetLineMode(LINE_MODE.Free)}>
                    {t("mode.free")}
                  </ModeBtn>
                </Tooltip>
              </ModeRow>
            ) : (
              <PanelHint>{t("lines.edit.hint")}</PanelHint>
            )}
            {state.lineMode !== LINE_MODE.Points && <SnapBar mask={state.snapMask} />}
            {/* Edit mode draws nothing new, so the style for the next line doesn't apply. */}
            {!editMode && (
              <FieldRow>
                <Tooltip content={t("next.lineStyle.tooltip")}>
                  <FieldLabel>{t("next.lineStyle")}</FieldLabel>
                </Tooltip>
                <Dropdown
                  value={state.currentStyle as StyleValue}
                  options={lineStyleOptions}
                  onChange={(s) => cmdSetCurrentStyle(s)}
                  onTogglePin={cmdTogglePinLineStyle}
                />
              </FieldRow>
            )}

            <SectionTitle>{`${t("section.lines")} · ${state.lines.length}`}</SectionTitle>
            {state.lines.length === 0 ? (
              <PanelHint>{t("lines.empty")}</PanelHint>
            ) : (
              <PanelList>
                {state.lines.map((line) => (
                  <LineRow
                    key={line.lineIndex}
                    line={line}
                    isExpanded={expandedLine === line.lineIndex}
                    isGameHovered={state.hoveredLineInGame === line.lineIndex}
                    isPendingDelete={keyDelete.armed === line.lineIndex}
                    editMode={editMode}
                    onToggleExpand={() =>
                      setExpandedLine(expandedLine === line.lineIndex ? -1 : line.lineIndex)
                    }
                    onCancelPendingDelete={keyDelete.disarm}
                  />
                ))}
              </PanelList>
            )}
          </>
        )}

        {shownTab === "areas" && (
          <>
            <SnapBar mask={state.snapMask} />
            {inAreaMode ? (
              <>
                <FieldRow>
                  <Tooltip content={t("next.areaStyle.tooltip")}>
                    <FieldLabel>{t("next.areaStyle")}</FieldLabel>
                  </Tooltip>
                  <Dropdown
                    value={state.currentAreaStyle}
                    options={areaStyleOptions}
                    onChange={(s) => cmdSetCurrentAreaStyle(s)}
                    onTogglePin={cmdTogglePinAreaStyle}
                  />
                </FieldRow>
                <DraftBox>
                  <DraftHint>{t("area.draft.hint.add")}</DraftHint>
                  <DraftHint>{t("area.draft.hint.undo")}</DraftHint>
                  <DraftHint>{t("area.draft.hint.close")}</DraftHint>
                </DraftBox>
              </>
            ) : (
              <PanelHint>{t("area.edit.hint")}</PanelHint>
            )}

            <SectionTitle>{`${t("section.areas")} · ${state.areas.length}`}</SectionTitle>
            {state.areas.length === 0 ? (
              <PanelHint>{t("areas.empty")}</PanelHint>
            ) : (
              <PanelList>
                {state.areas.map((area) => (
                  <AreaRow
                    key={area.areaIndex}
                    area={area}
                    isExpanded={expandedArea === area.areaIndex}
                    isGameHovered={state.hoveredAreaInGame === area.areaIndex}
                    areaStyleOptions={areaStyleOptions}
                    onToggleExpand={() =>
                      setExpandedArea(expandedArea === area.areaIndex ? -1 : area.areaIndex)
                    }
                  />
                ))}
              </PanelList>
            )}
          </>
        )}

        {shownTab === "crosswalks" && (
          <>
            {!editMode ? (
              <>
                <FieldRow>
                  <FieldLabel>{t("cw.next.style")}</FieldLabel>
                  <Dropdown
                    value={state.currentCrosswalkStyle}
                    options={crosswalkStyleOptions}
                    onChange={(s) => cmdSetCurrentCrosswalkStyle(s)}
                  />
                </FieldRow>
                <WidthStepper
                  label={t("cw.next.width")}
                  widthCm={state.currentCrosswalkWidthCm}
                  onChange={cmdSetCurrentCrosswalkWidth}
                />
                <PanelHint>{t("cw.place.hint")}</PanelHint>
              </>
            ) : (
              <PanelHint>{t("cw.edit.hint")}</PanelHint>
            )}
            <SnapBar mask={state.snapMask} />
            <SectionTitle>{`${t("section.crosswalks")} · ${state.crosswalks.length}`}</SectionTitle>
            {state.crosswalks.length === 0 ? (
              <PanelHint>{t("cw.empty")}</PanelHint>
            ) : (
              <PanelList>
                {state.crosswalks.map((cw) => (
                  <CrosswalkRow
                    key={cw.index}
                    cw={cw}
                    options={crosswalkStyleOptions}
                    isExpanded={expandedCw === cw.index}
                    isGameHovered={state.hoveredCrosswalkInGame === cw.index}
                    onToggleExpand={() => setExpandedCw(expandedCw === cw.index ? -1 : cw.index)}
                  />
                ))}
              </PanelList>
            )}
          </>
        )}

        {shownTab === "points" && (
          <>
            <PanelHint>{t("points.hint")}</PanelHint>
            <SnapBar mask={state.snapMask} />
            <SectionTitle>{`${t("section.points")} · ${state.points.length}`}</SectionTitle>
            {state.points.length === 0 ? (
              <PanelHint>{t("points.empty")}</PanelHint>
            ) : (
              <>
                <PanelList>
                  {state.points.map((point) => (
                    <PointRow
                      key={point.id}
                      point={point}
                      isGameHovered={state.hoveredPointInGame === point.id}
                    />
                  ))}
                </PanelList>
                {state.points.some((p) => p.lineCount === 0 && p.areaCount === 0 && p.crosswalkCount === 0) && (
                  <Tooltip content={t("points.removeUnused.tooltip")}>
                    <Btn $full onClick={cmdRemoveUnusedPoints}>
                      <Trash size={12} />
                      <span>{t("points.removeUnused")}</span>
                    </Btn>
                  </Tooltip>
                )}
              </>
            )}
          </>
        )}

        {shownTab === "advanced" && (
          <>
            <SectionTitle>{state.selectedIsRoad ? t("section.road") : t("section.node")}</SectionTitle>
            <ToggleRow>
              <FieldLabel>{t("node.vanilla.label")}</FieldLabel>
              <Tooltip content={t("vanilla.tooltip")}>
                <IconToggleBtn $active={state.vanillaHidden} onClick={cmdToggleVanillaMarkings}>
                  {state.vanillaHidden ? <EyeOff size={14} /> : <Eye size={14} />}
                </IconToggleBtn>
              </Tooltip>
            </ToggleRow>
            {(state.lines.length > 0 || state.areas.length > 0 || state.vanillaHidden || state.freePointCount > 0) && (
              <ResetNodeButton />
            )}
            <NodeIdText>
              {state.selectedIsRoad
                ? t("panel.title.road", { n: state.selectedNodeIndex })
                : t("panel.title", { n: state.selectedNodeIndex })}
            </NodeIdText>

            <SectionTitle>{t("hotkeys.title")}</SectionTitle>
            <HotkeysList />

            <SectionTitle>{t("section.more")}</SectionTitle>
            <PanelHint>{t("advanced.optionsHint")}</PanelHint>
          </>
        )}
      </ILMWindow>
      {shownTab === "lines" && popoverLine?.segments.map((seg) => (
        <SegmentPopover
          key={`pop-${seg.lineIndex}-${seg.segmentIndex}`}
          seg={seg}
        />
      ))}
      {shownTab === "areas" && popoverArea && (
        <AreaPopover key={`pop-area-${popoverArea.areaIndex}`} area={popoverArea} />
      )}
    </>
  );
};

const LineRow = ({
  line,
  isExpanded,
  isGameHovered,
  isPendingDelete,
  editMode,
  onToggleExpand,
  onCancelPendingDelete,
}: {
  line: LineVM;
  isExpanded: boolean;
  isGameHovered: boolean;
  isPendingDelete: boolean;
  // Handles and Add point belong to edit mode; the draw modes only point there.
  editMode: boolean;
  onToggleExpand: () => void;
  onCancelPendingDelete: () => void;
}) => {
  const t = useT();
  const visibleCount = line.segments.filter((s) => s.visible).length;
  return (
    <LineRowOuter
      $expanded={isExpanded}
      $gameHovered={isGameHovered}
      onMouseEnter={() => cmdSetHoveredLine(line.lineIndex)}
      onMouseLeave={() => cmdSetHoveredLine(-1)}
    >
      <LineHeader onClick={onToggleExpand}>
        <LineChevron $open={isExpanded}>
          <ChevronRight size={10} />
        </LineChevron>
        {/* 1-based for humans; commands keep the raw index. */}
        <LineTitle>{t("line.title", { n: line.lineIndex + 1 })}</LineTitle>
        <Tooltip content={styleLabel(t, line.style)}>
          <SwatchWrap>
            <LineStylePreview style={line.style} width={28} height={8} />
            {isG87LineStyle(line.style) && <G87Mark>G87</G87Mark>}
          </SwatchWrap>
        </Tooltip>
        <LineSegCount>
          {t("line.segCount", { visible: visibleCount, total: line.segments.length })}
        </LineSegCount>
      </LineHeader>
      <LineBody $open={isExpanded}>
        <StyleSelector line={line} />
        <CurvatureInput line={line} />
        {editMode ? (
          <>
            <PanelHint>{t("line.handles.hint")}</PanelHint>
            <Tooltip content={t("line.addPoint.tooltip")}>
              <Btn $full onClick={() => cmdSplitLine(line.lineIndex)}>
                <span>{t("line.addPoint")}</span>
              </Btn>
            </Tooltip>
          </>
        ) : (
          <PanelHint>{t("line.editHint")}</PanelHint>
        )}
        {line.segments.map((seg) => (
          <SegmentRowComponent key={`${seg.lineIndex}-${seg.segmentIndex}`} seg={seg} />
        ))}
        <DeleteLineButton
          lineIndex={line.lineIndex}
          keyboardConfirming={isPendingDelete}
          onKeyboardCancel={onCancelPendingDelete}
        />
      </LineBody>
    </LineRowOuter>
  );
};

// Same layout as LineRow. The body has the fill style, a visibility toggle and a two-step
// delete.
const AreaRow = ({
  area,
  isExpanded,
  isGameHovered,
  areaStyleOptions,
  onToggleExpand,
}: {
  area: AreaVM;
  isExpanded: boolean;
  isGameHovered: boolean;
  areaStyleOptions: DropdownOption<number>[];
  onToggleExpand: () => void;
}) => {
  const t = useT();
  const deleteConfirm = useConfirm();

  return (
    <LineRowOuter
      $expanded={isExpanded}
      $gameHovered={isGameHovered}
      onMouseEnter={() => cmdSetHoveredArea(area.areaIndex)}
      onMouseLeave={() => cmdClearHoveredArea(area.areaIndex)}
    >
      <LineHeader onClick={onToggleExpand}>
        <LineChevron $open={isExpanded}>
          <ChevronRight size={10} />
        </LineChevron>
        {/* 1-based for humans; commands keep the raw index. */}
        <LineTitle>{t("area.title", { n: area.areaIndex + 1 })}</LineTitle>
        <Tooltip content={areaStyleLabel(t, area.styleId)}>
          <SwatchWrap>
            <AreaStylePreview styleId={area.styleId} size={12} />
          </SwatchWrap>
        </Tooltip>
        <LineSegCount>
          {area.pieceCount > 1
            ? t("area.pieces", { visible: area.visiblePieces, total: area.pieceCount })
            : t("area.meta.vertices", { n: area.vertexCount })}
        </LineSegCount>
      </LineHeader>
      <LineBody $open={isExpanded}>
        <FieldRow style={{ marginTop: 0 }}>
          <FieldLabel>{t("area.style")}</FieldLabel>
          <Dropdown
            value={area.styleId}
            options={areaStyleOptions}
            onChange={(s) => cmdSetAreaStyle(area.areaIndex, s)}
            onTogglePin={cmdTogglePinAreaStyle}
          />
        </FieldRow>
        <Tooltip content={area.visible ? t("area.hide.tooltip") : t("area.show.tooltip")}>
          <Btn $full onClick={() => cmdToggleAreaVisible(area.areaIndex)}>
            {area.visible ? <Eye size={12} /> : <EyeOff size={12} />}
            <span>{area.visible ? t("area.hide.tooltip") : t("area.show.tooltip")}</span>
          </Btn>
        </Tooltip>
        {!deleteConfirm.armed ? (
          <Btn $danger $full onClick={deleteConfirm.arm}>
            <Trash size={12} color={T.colorDanger} />
            <span>{t("area.delete")}</span>
          </Btn>
        ) : (
          <ConfirmDeleteRow
            onCancel={deleteConfirm.disarm}
            onConfirm={() => deleteConfirm.confirm(() => cmdDeleteArea(area.areaIndex))}
          />
        )}
      </LineBody>
    </LineRowOuter>
  );
};

// The snap bar, after the game's own snap options: "All" turns every option on, or all off
// when they are all on already; the others toggle one option each. Kept in the settings.
const SNAP_BUTTONS: { bit: number; key: StringKey; Icon: (p: { size?: number }) => JSX.Element }[] = [
  { bit: SNAP.Existing, key: "snap.existing", Icon: SnapExisting },
  { bit: SNAP.Angle, key: "snap.angle", Icon: SnapAngle },
  { bit: SNAP.RoadSide, key: "snap.side", Icon: SnapSide },
  { bit: SNAP.Guides, key: "snap.guides", Icon: SnapGuides },
  { bit: SNAP.Length, key: "snap.length", Icon: SnapLength },
];

const SnapBar = ({ mask }: { mask: number }) => {
  const t = useT();
  const allOn = (mask & SNAP.All) === SNAP.All;
  return (
    <SnapRow>
      <SnapLabel>{t("snap.label")}</SnapLabel>
      <Tooltip content={allOn ? t("snap.all.off") : t("snap.all.on")}>
        <IconToggleBtn $active={allOn} onClick={() => cmdSetSnapMask(allOn ? 0 : SNAP.All)}>
          <SnapAll size={14} />
        </IconToggleBtn>
      </Tooltip>
      {SNAP_BUTTONS.map(({ bit, key, Icon }) => (
        <Tooltip key={bit} content={t(key)}>
          <IconToggleBtn $active={(mask & bit) !== 0} onClick={() => cmdSetSnapMask(mask ^ bit)}>
            <Icon size={14} />
          </IconToggleBtn>
        </Tooltip>
      ))}
    </SnapRow>
  );
};

// Crosswalk looks (CrosswalkStyle in C#), with a small preview of each.
const CW_KEYS: Record<number, StringKey> = { 0: "cw.style.0", 1: "cw.style.1", 2: "cw.style.2", 3: "cw.style.3" };

const CrosswalkPreview = ({ style }: { style: number }) => {
  const bars = style === 2 ? [] : style === 3 ? [3, 8, 13, 18, 23, 28] : [4, 11, 18, 25];
  const barW = style === 3 ? 2 : 4;
  const borders = style === 1 || style === 2;
  return (
    <svg width={28} height={12} viewBox="0 0 32 14" fill="none">
      {borders && <rect x="0.5" y="0.5" width="31" height="1.6" fill="rgba(240, 251, 255, 0.92)" />}
      {borders && <rect x="0.5" y="11.9" width="31" height="1.6" fill="rgba(240, 251, 255, 0.92)" />}
      {bars.map((x) => (
        <rect key={x} x={x} y={borders ? 3 : 1} width={barW} height={borders ? 8 : 12} fill="rgba(240, 251, 255, 0.92)" />
      ))}
    </svg>
  );
};

const makeCrosswalkStyleOptions = (t: ReturnType<typeof useT>): DropdownOption<number>[] =>
  CROSSWALK_STYLES.map((s) => ({ value: s, label: t(CW_KEYS[s]), preview: <CrosswalkPreview style={s} /> }));

// Width in 0.5 m steps (Shift: 0.1 m), 1 to 12 m.
const WidthStepper = ({ label, widthCm, onChange }: { label: string; widthCm: number; onChange: (cm: number) => void }) => {
  const t = useT();
  const step = (dir: number, e: ReactMouseEvent<HTMLButtonElement>) =>
    onChange(Math.max(100, Math.min(1200, Math.round((widthCm + dir * (e.shiftKey ? 10 : 50)) / 10) * 10)));
  return (
    <CurvRow>
      <CurvLabel>{label}</CurvLabel>
      <Tooltip content={t("cw.width.step")}>
        <CurvStepBtn onClick={(e: ReactMouseEvent<HTMLButtonElement>) => step(-1, e)}>−</CurvStepBtn>
      </Tooltip>
      <CurvUnit>{(widthCm / 100).toFixed(1)}</CurvUnit>
      <Tooltip content={t("cw.width.step")}>
        <CurvStepBtn onClick={(e: ReactMouseEvent<HTMLButtonElement>) => step(1, e)}>+</CurvStepBtn>
      </Tooltip>
      <CurvUnit>m</CurvUnit>
    </CurvRow>
  );
};

const CrosswalkRow = ({
  cw,
  options,
  isExpanded,
  isGameHovered,
  onToggleExpand,
}: {
  cw: CrosswalkVM;
  options: DropdownOption<number>[];
  isExpanded: boolean;
  isGameHovered: boolean;
  onToggleExpand: () => void;
}) => {
  const t = useT();
  const deleteConfirm = useConfirm();
  return (
    <LineRowOuter
      $expanded={isExpanded}
      $gameHovered={isGameHovered}
      onMouseEnter={() => cmdSetHoveredCrosswalk(cw.index)}
      onMouseLeave={() => cmdClearHoveredCrosswalk(cw.index)}
    >
      <LineHeader onClick={onToggleExpand}>
        <LineChevron $open={isExpanded}>
          <ChevronRight size={10} />
        </LineChevron>
        <LineTitle>{t("cw.title", { n: cw.index + 1 })}</LineTitle>
        <SwatchWrap>
          <CrosswalkPreview style={cw.style} />
        </SwatchWrap>
        <LineSegCount>{`${(cw.widthCm / 100).toFixed(1)} × ${(cw.lengthCm / 100).toFixed(1)} m`}</LineSegCount>
      </LineHeader>
      <LineBody $open={isExpanded}>
        <FieldRow style={{ marginTop: 0 }}>
          <FieldLabel>{t("cw.style")}</FieldLabel>
          <Dropdown value={cw.style} options={options} onChange={(s) => cmdSetCrosswalkStyle(cw.index, s)} />
        </FieldRow>
        <WidthStepper label={t("cw.width")} widthCm={cw.widthCm} onChange={(cm) => cmdSetCrosswalkWidth(cw.index, cm)} />
        {!deleteConfirm.armed ? (
          <Btn $danger $full onClick={deleteConfirm.arm}>
            <Trash size={12} color={T.colorDanger} />
            <span>{t("cw.delete")}</span>
          </Btn>
        ) : (
          <ConfirmDeleteRow
            onCancel={deleteConfirm.disarm}
            onConfirm={() => deleteConfirm.confirm(() => cmdDeleteCrosswalk(cw.index))}
          />
        )}
      </LineBody>
    </LineRowOuter>
  );
};

// One free point on the Points tab: where it sits, what uses it, and delete. Hovering the
// row rings the point on the road; a point hovered on the road lights its row.
const PointRow = ({ point, isGameHovered }: { point: FreePointVM; isGameHovered: boolean }) => {
  const t = useT();
  const deleteConfirm = useConfirm();
  const used = point.lineCount > 0 || point.areaCount > 0 || point.crosswalkCount > 0;
  const usage = used
    ? [
        point.lineCount > 0 ? t("point.lines", { n: point.lineCount }) : null,
        point.areaCount > 0 ? t("point.areas", { n: point.areaCount }) : null,
        point.crosswalkCount > 0 ? t("point.crosswalks", { n: point.crosswalkCount }) : null,
      ]
        .filter((s) => s !== null)
        .join(" · ")
    : t("point.unused");
  // An unused point goes at once; one with lines or areas asks first.
  const onDelete = () => (used ? deleteConfirm.arm() : cmdDeleteFreePoint(point.id));
  return (
    <LineRowOuter
      $expanded={deleteConfirm.armed}
      $gameHovered={isGameHovered}
      onMouseEnter={() => cmdSetHoveredPoint(point.id)}
      onMouseLeave={() => cmdClearHoveredPoint(point.id)}
    >
      <LineHeader>
        <LineTitle>{t("point.title", { n: point.id + 1 })}</LineTitle>
        <LineSegCount>{`${point.onJunction ? t("point.onJunction") : t("point.onRoad")} · ${usage}`}</LineSegCount>
        <Tooltip content={t("point.delete.tooltip")}>
          <IconToggleBtn onClick={onDelete}>
            <Trash size={12} color={T.colorDanger} />
          </IconToggleBtn>
        </Tooltip>
      </LineHeader>
      {deleteConfirm.armed && (
        <ConfirmDeleteRow
          onCancel={deleteConfirm.disarm}
          onConfirm={() => deleteConfirm.confirm(() => cmdDeleteFreePoint(point.id))}
        />
      )}
    </LineRowOuter>
  );
};

// Removes every line and area on the node and the vanilla-hide override. Node-wide and
// destructive, so it needs the same two-step confirm as deletes.
const ResetNodeButton = () => {
  const t = useT();
  const resetConfirm = useConfirm();

  if (!resetConfirm.armed) {
    return (
      <Tooltip content={t("node.reset.tooltip")}>
        <Btn $danger $full onClick={resetConfirm.arm}>
          <Trash size={12} color={T.colorDanger} />
          <span>{t("node.reset")}</span>
        </Btn>
      </Tooltip>
    );
  }
  return (
    <ConfirmDeleteRow
      onCancel={resetConfirm.disarm}
      onConfirm={() => resetConfirm.confirm(cmdResetNode)}
    />
  );
};

// Second step of a panel delete: the first click swaps the button for this row, which goes
// back after CONFIRM_TIMEOUT_MS. Inline rather than a modal, because overlay positioning in
// cohtml is fragile and the item stays visible above while confirming.
const ConfirmDeleteRow = ({ onCancel, onConfirm }: { onCancel: () => void; onConfirm: () => void }) => {
  const t = useT();
  return (
    <ConfirmRow>
      <Btn onClick={onCancel}>
        <span>{t("line.delete.cancel")}</span>
      </Btn>
      <Btn $danger onClick={onConfirm}>
        <Trash size={12} color={T.colorDanger} />
        <span>{t("line.delete.confirm.btn")}</span>
      </Btn>
    </ConfirmRow>
  );
};

const DeleteLineButton = ({
  lineIndex,
  keyboardConfirming,
  onKeyboardCancel,
}: {
  lineIndex: number;
  keyboardConfirming: boolean;
  onKeyboardCancel: () => void;
}) => {
  const t = useT();
  // Armed either by a click here or by the Delete key through keyboardConfirming. Only the
  // click arm lives here; the parent owns and times out the keyboard one.
  const clickConfirm = useConfirm();

  // Clears both arms. After a delete the next line takes over this row's index, and must
  // not inherit the armed state.
  const disarm = () => {
    clickConfirm.disarm();
    if (keyboardConfirming) onKeyboardCancel();
  };

  if (!clickConfirm.armed && !keyboardConfirming) {
    return (
      <Btn $danger $full onClick={clickConfirm.arm}>
        <Trash size={12} color={T.colorDanger} />
        <span>{t("line.delete")}</span>
      </Btn>
    );
  }

  return (
    <ConfirmDeleteRow
      onCancel={disarm}
      onConfirm={() => {
        disarm();
        cmdDeleteLine(lineIndex);
      }}
    />
  );
};

// Curvature in percent of the C# pull factor range 0..0.8 (50% is the default 0.4 arc).
// A text field, since range sliders don't work in cohtml: digits only, committed on Enter
// or blur and clamped to 0..100. While the user types, the draft owns the field; otherwise
// it shows the C# value.
const CURV_DEFAULT = 50;

const CurvatureInput = ({ line }: { line: LineVM }) => {
  const t = useT();
  const [draft, setDraft] = useState<string | null>(null);

  const commit = () => {
    if (draft === null) return;
    const v = parseInt(draft, 10);
    if (!isNaN(v)) {
      cmdSetLineCurvature(line.lineIndex, Math.max(0, Math.min(100, v)));
    }
    setDraft(null);
  };

  // −/+: ±1, Shift ±10, Ctrl ±5. Steps from an uncommitted draft when there is one, since
  // that is what the user sees.
  const step = (dir: 1 | -1, e: ReactMouseEvent<HTMLButtonElement>) => {
    const mag = e.shiftKey ? 10 : e.ctrlKey ? 5 : 1;
    const parsed = draft !== null ? parseInt(draft, 10) : NaN;
    const base = !isNaN(parsed) ? parsed : line.curv;
    setDraft(null);
    cmdSetLineCurvature(line.lineIndex, Math.max(0, Math.min(100, base + dir * mag)));
  };

  return (
    <CurvRow>
      <Tooltip content={t("line.curvature.tooltip")}>
        <CurvLabel>{t("line.curvature")}</CurvLabel>
      </Tooltip>
      <Tooltip content={t("line.curvature.step")}>
        <CurvStepBtn onClick={(e: ReactMouseEvent<HTMLButtonElement>) => step(-1, e)}>−</CurvStepBtn>
      </Tooltip>
      <CurvInput
        type="text"
        value={draft ?? (line.handles ? "—" : String(line.curv))}
        onChange={(e: ChangeEvent<HTMLInputElement>) =>
          setDraft(e.target.value.replace(/[^0-9]/g, "").slice(0, 3))
        }
        onBlur={commit}
        onKeyDown={(e: ReactKeyboardEvent<HTMLInputElement>) => {
          if (e.key === "Enter") commit();
        }}
      />
      <Tooltip content={t("line.curvature.step")}>
        <CurvStepBtn onClick={(e: ReactMouseEvent<HTMLButtonElement>) => step(1, e)}>+</CurvStepBtn>
      </Tooltip>
      <CurvUnit>%</CurvUnit>
      {(line.curv !== CURV_DEFAULT || line.handles) && (
        <Tooltip content={t("line.curvature.reset")}>
          <CurvResetBtn
            onClick={() => cmdSetLineCurvature(line.lineIndex, CURV_DEFAULT)}
          >
            <Cycle size={12} />
          </CurvResetBtn>
        </Tooltip>
      )}
    </CurvRow>
  );
};

// Options are rebuilt on each render so they follow locale changes.
const StyleSelector = ({ line }: { line: LineVM }) => {
  const t = useT();
  const pinned = usePinnedStyles();
  return (
    <StyleRow>
      <Dropdown
        value={line.style as StyleValue}
        options={makeLineStyleOptions(t, pinned.lineStyles)}
        onChange={(s) => cmdSetLineStyle(line.lineIndex, s)}
        onTogglePin={cmdTogglePinLineStyle}
      />
    </StyleRow>
  );
};

const SegmentRowComponent = ({ seg }: { seg: SegmentVM }) => {
  const t = useT();
  return (
    <SegmentRow
      $hidden={!seg.visible}
      onClick={() => cmdToggleSegment(seg.lineIndex, seg.segmentIndex)}
    >
      {/* 1-based for humans; commands keep the raw index. */}
      <SegmentInfo>{t("segment.label", { n: seg.segmentIndex + 1 })}</SegmentInfo>
      <SegmentLen>{t("segment.length", { m: seg.lengthM.toFixed(1) })}</SegmentLen>
      <SegmentIndicator>
        {seg.visible ? <Eye size={12} /> : <EyeOff size={12} />}
      </SegmentIndicator>
    </SegmentRow>
  );
};
