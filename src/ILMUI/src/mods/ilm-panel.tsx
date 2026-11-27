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
  cmdUndo,
  cmdRedo,
  cmdDeleteFreePoint,
  cmdRemoveUnusedPoints,
  cmdSetHoveredPoint,
  cmdClearHoveredPoint,
  cmdCopyMarkings,
  cmdPasteMarkings,
  cmdTurnPaste,
  cmdSaveTemplate,
  cmdPasteTemplate,
  cmdDeleteTemplate,
  cmdSetAutoMarkings,
  cmdSetAutoMarkingStyle,
  cmdSetAutoTurnsOnly,
  cmdSetAutoMode,
  cmdKeepAutoLines,
  PASTE_RESULT,
  AUTO_MODE,
  TemplateVM,
  ToolStateVM,
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
import {
  ChevronRight,
  Eye,
  EyeOff,
  Trash,
  Cycle,
  SnapAll,
  SnapExisting,
  SnapAngle,
  SnapSide,
  SnapGuides,
  SnapLength,
  ToolLines,
  ToolAreas,
  ToolPoints,
  Undo,
  Redo,
  Pencil,
  Cursor,
  SnapDots,
  Anywhere,
  Gear,
  Copy,
  Paste,
  Save,
} from "../components/icons";
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
  PanelTitle,
  ToggleRow,
  IconToggleBtn,
  NodeIdText,
  PanelHint,
  PanelList,
  FieldRow,
  FieldLabel,
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
  TabBar,
  TabBtn,
  Options,
  OptionRow,
  OptionLabel,
  OptionControl,
  Segmented,
  SegBtn,
  SnapBtn,
  SnapDivider,
  HelpCard,
  HelpTitle,
  HelpRow,
  HelpKey,
  ListHeader,
  CountChip,
  EmptyText,
  Footer,
  FooterBtn,
  ActionRow,
  ActionBtn,
  InfoText,
  TextInput,
  InputRow,
  TemplateRow,
  TemplateText,
  TemplateName,
  TemplateMeta,
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

// Hotkey list, in the settings foldout and on the "select a junction" card.
const HotkeysList = () => {
  const t = useT();
  return (
    <HintsBox>
      <HintRow><HintKey>Ctrl+M</HintKey><span>{t("hotkeys.toggle")}</span></HintRow>
      <HintRow><HintKey>Ctrl+Z</HintKey><span>{t("hotkeys.undo")}</span></HintRow>
      <HintRow><HintKey>Y</HintKey><span>{t("hotkeys.cycleLine")}</span></HintRow>
      <HintRow><HintKey>U</HintKey><span>{t("hotkeys.cycleArea")}</span></HintRow>
      <HintRow><HintKey>A</HintKey><span>{t("hotkeys.areaMode")}</span></HintRow>
      <HintRow><HintKey>F</HintKey><span>{t("hotkeys.freeDraw")}</span></HintRow>
      <HintRow><HintKey>{t("hotkeys.esc")}</HintKey><span>{t("hotkeys.esc.desc")}</span></HintRow>
    </HintsBox>
  );
};

// The game's own panel (cs2/ui): vanilla title bar, close button and blurred body, so the
// tool window looks like the rest of the CS2 interface. Closing deactivates the tool.
// `fixed` stays above the scrolling body (the tabs); `footer` sits under it.
const ILMWindow = ({ fixed, footer, children }: { fixed?: ReactNode; footer?: ReactNode; children: ReactNode }) => {
  const t = useT();
  return (
    <WindowPosition>
      <VanillaPanel header={t("panel.appTitle")} onClose={() => cmdActivateTool()} footer={footer}>
        {fixed && <WindowFixed>{fixed}</WindowFixed>}
        <Scrollable vertical trackVisibility="scrollable" style={{ maxHeight: T.panelScrollMaxHeight }}>
          <WindowBody>{children}</WindowBody>
        </Scrollable>
      </VanillaPanel>
    </WindowPosition>
  );
};

type PanelTab = "lines" | "areas" | "points" | "advanced";

type IconC = (p: { size?: number; color?: string }) => JSX.Element;

const TABS: { id: PanelTab; key: StringKey; tip: StringKey; Icon: IconC }[] = [
  { id: "lines", key: "tab.lines", tip: "tab.lines.tooltip", Icon: ToolLines },
  { id: "areas", key: "tab.areas", tip: "tab.areas.tooltip", Icon: ToolAreas },
  { id: "points", key: "tab.points", tip: "tab.points.tooltip", Icon: ToolPoints },
  { id: "advanced", key: "tab.advanced", tip: "tab.advanced.tooltip", Icon: Gear },
];

// One row of the option block: a label on the left, the control on the right.
const Option = ({ label, tip, children }: { label: string; tip?: string; children: ReactNode }) => (
  <OptionRow>
    {tip ? (
      <Tooltip content={tip}>
        <OptionLabel>{label}</OptionLabel>
      </Tooltip>
    ) : (
      <OptionLabel>{label}</OptionLabel>
    )}
    <OptionControl>{children}</OptionControl>
  </OptionRow>
);

// A joined pair of buttons where exactly one is on.
const Choice = ({
  options,
}: {
  options: { active: boolean; label: string; tip: string; Icon: IconC; onClick: () => void }[];
}) => (
  <Segmented>
    {options.map(({ active, label, tip, Icon, onClick }) => (
      <Tooltip key={label} content={tip} grow>
        <SegBtn $active={active} onClick={onClick}>
          <Icon size={13} />
          <span>{label}</span>
        </SegBtn>
      </Tooltip>
    ))}
  </Segmented>
);

// What to do next, and the mouse controls of the current mode.
const Help = ({ title, rows }: { title: string; rows: [StringKey, StringKey][] }) => {
  const t = useT();
  return (
    <HelpCard>
      <HelpTitle>{title}</HelpTitle>
      {rows.map(([key, text]) => (
        <HelpRow key={text}>
          <HelpKey>{t(key)}</HelpKey>
          <span>{t(text)}</span>
        </HelpRow>
      ))}
    </HelpCard>
  );
};

const ListTitle = ({ label, count }: { label: string; count?: number }) => (
  <ListHeader>
    <span>{label}</span>
    {count !== undefined && <CountChip>{count}</CountChip>}
  </ListHeader>
);

// The next step for the user while working on lines.
const lineStatus = (
  t: ReturnType<typeof useT>,
  state: { toolState: number; freeDraw: boolean; lineMode: number },
  lineOpen: boolean,
): string => {
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
        : EDIT_TARGET.Lines;
  useEffect(() => {
    if (state.isActive) cmdSetEditTarget(editTarget);
  }, [editTarget, state.isActive, state.selectedNodeIndex]);

  // The draw sub-mode for lines (dots or anywhere), kept while editing so Draw returns to it.
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

  // Nothing selected yet: one card that says how to start.
  if (state.selectedNodeIndex < 0) {
    return (
      <ILMWindow>
        <Help title={t("panel.hint.selectNode")} rows={[]} />
        <HotkeysList />
      </ILMWindow>
    );
  }

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
  // Draw / Edit is one mode for Lines and Areas, so switching tabs keeps you drawing or
  // editing. Drawing areas is the C# area mode.
  const setDraw = () => {
    if (editMode) cmdSetLineMode(lineDrawMode);
    if (shownTab === "areas" && !inAreaMode) cmdToggleAreaMode();
  };
  const setEdit = () => {
    if (inAreaMode) cmdToggleAreaMode();
    cmdSetLineMode(LINE_MODE.Edit);
  };

  const popoverLine =
    expandedLine >= 0 && expandedLine < state.lines.length ? state.lines[expandedLine] : null;
  const popoverArea = state.areas.find((a) => a.areaIndex === expandedArea) ?? null;
  const lineStyleOptions = makeLineStyleOptions(t, pinned.lineStyles);
  const areaStyleOptions = makeAreaStyleOptions(t, pinned.areaStyles);

  const drawing = shownTab === "areas" ? inAreaMode : !editMode;
  const freeLines = state.lineMode === LINE_MODE.Free;

  const modeChoice = (
    <Option label={t("opt.mode")}>
      <Choice
        options={[
          { active: drawing, label: t("opt.draw"), tip: t("opt.draw.tooltip"), Icon: Pencil, onClick: setDraw },
          { active: !drawing, label: t("opt.edit"), tip: t("opt.edit.tooltip"), Icon: Cursor, onClick: setEdit },
        ]}
      />
    </Option>
  );

  const fixed = (
    <TabBar>
      {TABS.map(({ id, key, tip, Icon }) => (
        <Tooltip key={id} content={t(tip)} grow>
          <TabBtn $active={shownTab === id} onClick={() => openTab(id)}>
            <Icon size={15} />
            <span>{t(key)}</span>
          </TabBtn>
        </Tooltip>
      ))}
    </TabBar>
  );

  const footer = (
    <Footer>
      <span>
        {state.selectedIsRoad
          ? t("panel.title.road", { n: state.selectedNodeIndex })
          : t("panel.title", { n: state.selectedNodeIndex })}
      </span>
      <Tooltip content={t("undo.tooltip")}>
        <FooterBtn $disabled={!state.canUndo} onClick={() => state.canUndo && cmdUndo()}>
          <Undo size={12} />
          <span>{t("undo")}</span>
        </FooterBtn>
      </Tooltip>
      <Tooltip content={t("redo.tooltip")}>
        <FooterBtn $disabled={!state.canRedo} onClick={() => state.canRedo && cmdRedo()}>
          <Redo size={12} />
          <span>{t("redo")}</span>
        </FooterBtn>
      </Tooltip>
    </Footer>
  );

  return (
    <>
      <ILMWindow fixed={fixed} footer={footer}>
        {shownTab === "lines" && (
          <>
            <Options>
              {modeChoice}
              {!editMode && (
                <Option label={t("opt.place")}>
                  <Choice
                    options={[
                      {
                        active: !freeLines,
                        label: t("opt.place.dots"),
                        tip: t("opt.place.dots.tooltip"),
                        Icon: SnapDots,
                        onClick: () => cmdSetLineMode(LINE_MODE.Points),
                      },
                      {
                        active: freeLines,
                        label: t("opt.place.free"),
                        tip: t("opt.place.free.tooltip"),
                        Icon: Anywhere,
                        onClick: () => cmdSetLineMode(LINE_MODE.Free),
                      },
                    ]}
                  />
                </Option>
              )}
              {!editMode && (
                <Option label={t("opt.style")} tip={t("opt.style.tooltip")}>
                  <Dropdown
                    value={state.currentStyle as StyleValue}
                    options={lineStyleOptions}
                    onChange={(s) => cmdSetCurrentStyle(s)}
                    onTogglePin={cmdTogglePinLineStyle}
                  />
                </Option>
              )}
              {(editMode || freeLines) && <SnapOption mask={state.snapMask} />}
            </Options>
            <Help
              title={lineStatus(t, state, popoverLine !== null)}
              rows={
                editMode
                  ? [["help.lmb", "help.lines.edit.lmb"], ["help.rmb", "help.lines.edit.rmb"], ["help.esc", "help.lines.edit.esc"]]
                  : freeLines
                    ? [["help.lmb", "help.lines.free.lmb"], ["help.rmb", "help.lines.free.rmb"], ["help.esc", "help.lines.free.esc"]]
                    : [["help.lmb", "help.lines.dots.lmb"], ["help.esc", "help.lines.dots.esc"]]
              }
            />

            <ListTitle label={t("section.lines")} count={state.lines.length} />
            {state.lines.length === 0 ? (
              <EmptyText>{t("lines.empty")}</EmptyText>
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
            <Options>
              {modeChoice}
              {inAreaMode && (
                <Option label={t("opt.fill")} tip={t("opt.fill.tooltip")}>
                  <Dropdown
                    value={state.currentAreaStyle}
                    options={areaStyleOptions}
                    onChange={(s) => cmdSetCurrentAreaStyle(s)}
                    onTogglePin={cmdTogglePinAreaStyle}
                  />
                </Option>
              )}
              <SnapOption mask={state.snapMask} />
            </Options>
            {inAreaMode ? (
              <Help
                title={t("status.area", { n: state.areaVertexCount })}
                rows={[["help.lmb", "help.areas.draw.lmb"], ["help.rmb", "help.areas.draw.rmb"]]}
              />
            ) : (
              <Help
                title={popoverArea ? t("status.area.open") : t("status.area.pick")}
                rows={[["help.lmb", "help.areas.edit.lmb"], ["help.rmb", "help.areas.edit.rmb"]]}
              />
            )}

            <ListTitle label={t("section.areas")} count={state.areas.length} />
            {state.areas.length === 0 ? (
              <EmptyText>{t("areas.empty")}</EmptyText>
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

        {shownTab === "points" && (
          <>
            <Options>
              <SnapOption mask={state.snapMask} />
            </Options>
            <Help
              title={t("status.points")}
              rows={[["help.lmb", "help.points.lmb"], ["help.rmb", "help.points.rmb"]]}
            />
            <ListTitle label={t("section.points")} count={state.points.length} />
            {state.points.length === 0 ? (
              <EmptyText>{t("points.empty")}</EmptyText>
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
                {state.points.some((p) => p.lineCount === 0 && p.areaCount === 0) && (
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

        {shownTab === "advanced" && <AdvancedTab state={state} />}
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
            {line.auto && <G87Mark>AUTO</G87Mark>}
          </SwatchWrap>
        </Tooltip>
        <LineSegCount>
          {t("line.segCount", { visible: visibleCount, total: line.segments.length })}
        </LineSegCount>
      </LineHeader>
      <LineBody $open={isExpanded}>
        <StyleSelector line={line} />
        <CurvatureInput line={line} />
        {editMode && (
            <Tooltip content={t("line.addPoint.tooltip")}>
              <Btn $full onClick={() => cmdSplitLine(line.lineIndex)}>
                <span>{t("line.addPoint")}</span>
              </Btn>
            </Tooltip>
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

// "4-way junction · 6 lines · 2 areas"
const templateMeta = (t: ReturnType<typeof useT>, tpl: TemplateVM): string =>
  [
    tpl.isRoad ? t("adv.kind.road") : t("adv.kind.junction", { n: tpl.roads }),
    t("adv.count.lines", { n: tpl.lines }),
    tpl.areas > 0 ? t("adv.count.areas", { n: tpl.areas }) : null,
  ]
    .filter((s) => s !== null)
    .join(" · ");

const PASTE_MESSAGES: Record<number, StringKey> = {
  [PASTE_RESULT.NothingToPaste]: "adv.paste.nothing",
  [PASTE_RESULT.RoadCountDiffers]: "adv.paste.roads",
  [PASTE_RESULT.KindDiffers]: "adv.paste.kind",
  [PASTE_RESULT.NoRoads]: "adv.paste.noRoads",
};

// The Advanced tab: copy / paste, templates, auto markings and this junction's settings.
const AdvancedTab = ({ state }: { state: ToolStateVM }) => {
  const t = useT();
  const pinned = usePinnedStyles();
  const [name, setName] = useState("");
  const clip = state.clipboard;
  const hasMarkings = state.lines.length > 0 || state.areas.length > 0;

  // Shown for the paste that just happened, until the selection changes.
  const [seenPaste, setSeenPaste] = useState(state.pasteTick);
  const [pasteNode, setPasteNode] = useState(state.selectedNodeIndex);
  if (state.selectedNodeIndex !== pasteNode) {
    setPasteNode(state.selectedNodeIndex);
    setSeenPaste(state.pasteTick);
  }
  const showPaste = state.pasteTick !== seenPaste;

  const save = () => {
    cmdSaveTemplate(name);
    setName("");
  };

  return (
    <>
      <ListTitle label={t("adv.copy.title")} />
      <ActionRow>
        <Tooltip content={t("adv.copy.tooltip")} grow>
          <ActionBtn $disabled={!hasMarkings} onClick={() => hasMarkings && cmdCopyMarkings()}>
            <Copy size={13} />
            <span>{t("adv.copy")}</span>
          </ActionBtn>
        </Tooltip>
        <Tooltip content={clip && !clip.fits ? t("adv.paste.noFit") : t("adv.paste.tooltip")} grow>
          <ActionBtn $primary $disabled={!clip?.fits} onClick={() => clip?.fits && cmdPasteMarkings()}>
            <Paste size={13} />
            <span>{t("adv.paste")}</span>
          </ActionBtn>
        </Tooltip>
        <Tooltip content={t("adv.turn.tooltip")} grow>
          <ActionBtn $disabled={!state.canTurn} onClick={() => state.canTurn && cmdTurnPaste()}>
            <Cycle size={13} />
            <span>{t("adv.turn")}</span>
          </ActionBtn>
        </Tooltip>
      </ActionRow>
      <InfoText>{clip ? t("adv.clipboard", { what: templateMeta(t, clip) }) : t("adv.clipboard.empty")}</InfoText>
      {showPaste && (
        <InfoText $tone={state.pasteResult === PASTE_RESULT.Ok ? "ok" : "warn"}>
          {state.pasteResult === PASTE_RESULT.Ok
            ? t("adv.paste.ok", { lines: state.pastedLines, areas: state.pastedAreas })
            : t(PASTE_MESSAGES[state.pasteResult] ?? "adv.paste.nothing")}
        </InfoText>
      )}

      <InputRow>
        <TextInput
          type="text"
          value={name}
          placeholder={t("adv.template.placeholder")}
          onChange={(e: ChangeEvent<HTMLInputElement>) => setName(e.target.value.slice(0, 40))}
          onKeyDown={(e: ReactKeyboardEvent<HTMLInputElement>) => {
            if (e.key === "Enter" && hasMarkings) save();
          }}
        />
        <Tooltip content={t("adv.template.save.tooltip")}>
          <ActionBtn $disabled={!hasMarkings} onClick={() => hasMarkings && save()}>
            <Save size={13} />
            <span>{t("adv.template.save")}</span>
          </ActionBtn>
        </Tooltip>
      </InputRow>
      <div style={{ marginTop: T.space2 }}>
        {state.templates.length === 0 ? (
          <EmptyText>{t("adv.template.empty")}</EmptyText>
        ) : (
          state.templates.map((tpl) => <TemplateItem key={`${tpl.index}-${tpl.name}`} tpl={tpl} />)
        )}
      </div>

      <ListTitle label={t("adv.auto.title")} count={state.autoLineCount} />
      <Options>
        <Option label={t("adv.auto.global")} tip={t("adv.auto.global.tooltip")}>
          <Choice
            options={[
              { active: !state.autoGlobal, label: t("adv.off"), tip: t("adv.auto.global.off"), Icon: EyeOff, onClick: () => cmdSetAutoMarkings(false) },
              { active: state.autoGlobal, label: t("adv.on"), tip: t("adv.auto.global.on"), Icon: Eye, onClick: () => cmdSetAutoMarkings(true) },
            ]}
          />
        </Option>
        {!state.selectedIsRoad && (
          <Option label={t("adv.auto.here")} tip={t("adv.auto.here.tooltip")}>
            <Segmented>
              {[
                { mode: AUTO_MODE.Default, label: t("adv.auto.here.default") },
                { mode: AUTO_MODE.On, label: t("adv.on") },
                { mode: AUTO_MODE.Off, label: t("adv.off") },
              ].map(({ mode, label }) => (
                <SegBtn key={mode} $active={state.autoMode === mode} onClick={() => cmdSetAutoMode(mode)}>
                  {label}
                </SegBtn>
              ))}
            </Segmented>
          </Option>
        )}
        <Option label={t("adv.auto.style")}>
          <Dropdown
            value={state.autoStyle as StyleValue}
            options={makeLineStyleOptions(t, pinned.lineStyles)}
            onChange={(s) => cmdSetAutoMarkingStyle(s)}
            onTogglePin={cmdTogglePinLineStyle}
          />
        </Option>
        <Option label={t("adv.auto.lanes")} tip={t("adv.auto.lanes.tooltip")}>
          <Segmented>
            <SegBtn $active={state.autoTurnsOnly} onClick={() => cmdSetAutoTurnsOnly(true)}>{t("adv.auto.lanes.turns")}</SegBtn>
            <SegBtn $active={!state.autoTurnsOnly} onClick={() => cmdSetAutoTurnsOnly(false)}>{t("adv.auto.lanes.all")}</SegBtn>
          </Segmented>
        </Option>
      </Options>
      <InfoText>{t("adv.auto.hint")}</InfoText>
      {state.autoLineCount > 0 && (
        <Tooltip content={t("adv.auto.keep.tooltip")}>
          <Btn $full onClick={cmdKeepAutoLines}>
            <span>{t("adv.auto.keep", { n: state.autoLineCount })}</span>
          </Btn>
        </Tooltip>
      )}

      <ListTitle label={state.selectedIsRoad ? t("settings.title.road") : t("settings.title")} />
      <ToggleRow>
        <FieldLabel>{t("node.vanilla.label")}</FieldLabel>
        <Tooltip content={t("vanilla.tooltip")}>
          <IconToggleBtn $active={state.vanillaHidden} onClick={cmdToggleVanillaMarkings}>
            {state.vanillaHidden ? <EyeOff size={14} /> : <Eye size={14} />}
          </IconToggleBtn>
        </Tooltip>
      </ToggleRow>
      {(hasMarkings || state.vanillaHidden || state.freePointCount > 0) && <ResetNodeButton />}

      <ListTitle label={t("hotkeys.title")} />
      <HotkeysList />
      <NodeIdText>{t("advanced.optionsHint")}</NodeIdText>
    </>
  );
};

const TemplateItem = ({ tpl }: { tpl: TemplateVM }) => {
  const t = useT();
  const del = useConfirm();
  return (
    <TemplateRow>
      <TemplateText>
        <TemplateName>{tpl.name}</TemplateName>
        <TemplateMeta>{templateMeta(t, tpl)}</TemplateMeta>
      </TemplateText>
      <Tooltip content={tpl.fits ? t("adv.paste.tooltip") : t("adv.paste.noFit")}>
        <IconToggleBtn style={tpl.fits ? undefined : { opacity: 0.4 }} onClick={() => tpl.fits && cmdPasteTemplate(tpl.index)}>
          <Paste size={13} />
        </IconToggleBtn>
      </Tooltip>
      <Tooltip content={del.armed ? t("line.delete.confirm.btn") : t("adv.template.delete")}>
        <IconToggleBtn
          style={del.armed ? ARMED_DELETE_STYLE : undefined}
          onClick={() => del.press(() => cmdDeleteTemplate(tpl.index))}
        >
          <Trash size={12} color={T.colorDanger} />
        </IconToggleBtn>
      </Tooltip>
    </TemplateRow>
  );
};

// Snapping, after the game's own snap buttons: "all" turns every option on, or all off when
// they are all on already; the others toggle one option each. Kept in the settings.
const SNAP_BUTTONS: { bit: number; key: StringKey; Icon: IconC }[] = [
  { bit: SNAP.Existing, key: "snap.existing", Icon: SnapExisting },
  { bit: SNAP.Angle, key: "snap.angle", Icon: SnapAngle },
  { bit: SNAP.RoadSide, key: "snap.side", Icon: SnapSide },
  { bit: SNAP.Guides, key: "snap.guides", Icon: SnapGuides },
  { bit: SNAP.Length, key: "snap.length", Icon: SnapLength },
];

const SnapOption = ({ mask }: { mask: number }) => {
  const t = useT();
  const allOn = (mask & SNAP.All) === SNAP.All;
  return (
    <Option label={t("opt.snap")}>
      <Tooltip content={allOn ? t("snap.all.off") : t("snap.all.on")}>
        <SnapBtn $active={allOn} onClick={() => cmdSetSnapMask(allOn ? 0 : SNAP.All)}>
          <SnapAll size={14} />
        </SnapBtn>
      </Tooltip>
      <SnapDivider />
      {SNAP_BUTTONS.map(({ bit, key, Icon }) => (
        <Tooltip key={bit} content={t(key)}>
          <SnapBtn $active={(mask & bit) !== 0} onClick={() => cmdSetSnapMask(mask ^ bit)}>
            <Icon size={14} />
          </SnapBtn>
        </Tooltip>
      ))}
    </Option>
  );
};

// One free point on the Points tab: where it sits, what uses it, and delete. Hovering the
// row rings the point on the road; a point hovered on the road lights its row.
const PointRow = ({ point, isGameHovered }: { point: FreePointVM; isGameHovered: boolean }) => {
  const t = useT();
  const deleteConfirm = useConfirm();
  const used = point.lineCount > 0 || point.areaCount > 0;
  const usage = used
    ? [
        point.lineCount > 0 ? t("point.lines", { n: point.lineCount }) : null,
        point.areaCount > 0 ? t("point.areas", { n: point.areaCount }) : null,
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
