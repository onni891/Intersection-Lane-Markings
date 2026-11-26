// Typed i18n dictionary. en-US defines the key set, and LocaleDict makes a key missing from
// another locale a compile error rather than a silent fallback at runtime.
//
// Placeholders are written as {name} and filled from t()'s params:
// t("panel.title", { n: 5 }). A new language is a translated copy of the en-US block added
// to STRINGS.

const enUS = {
  // Toolbar
  "toolbar.toggle.label":     "Intersection & Lane Markings",
  "toolbar.toggle.tooltip":   "Toggle ILM marking tool (Ctrl+M)",

  // Panel chrome
  "panel.appTitle":           "Intersection & Lane Markings",
  "panel.title":              "Node #{n}",
  "panel.close.tooltip":      "Close the marking tool",
  "panel.hint.selectNode":    "Click a junction, or the middle of a road, to edit its markings.",
  "panel.error.title":        "Panel error",
  "panel.error.retry":        "Retry",

  // Tool status (MarkingNodeToolSystem.State)
  "status.line.first":        "Click a point to start a line",
  "status.line.second":       "Click a second point to finish",
  "status.area":              "Outline the area — {n} points",
  "status.free.first":        "Click anywhere on the road to start drawing",
  "status.free.next":         "Click to add a point · Esc to finish",
  "status.edit.pick":         "Click a line to shape it",
  "status.edit.open":         "Drag the blue handles or white points · Esc to put the line down",

  // Section headers
  "section.drawing":          "Drawing",
  "section.node":             "Junction",
  "section.road":             "Road",
  "panel.title.road":         "Road #{n}",

  // Node block
  "node.vanilla.label":       "Game markings",

  // Tabs and sections
  "tab.lines":                "Lines",
  "tab.areas":                "Areas",
  "tab.points":               "Points",
  "tab.crosswalks":           "Crossings",
  "tool.lines.tooltip":       "Lines: draw between the road's dots or freely, or edit their curves.",
  "tool.areas.tooltip":       "Areas: outline fills such as hatching, bike lanes or grass, or edit their shape.",
  "tool.crosswalks.tooltip":  "Crosswalks: one click puts a crosswalk across a road; edit its ends, width and position.",
  "tool.points.tooltip":      "Points: all the points you placed. Move or remove them without touching anything else.",
  "undo.tooltip":             "Undo (Ctrl+Z)",
  "redo.tooltip":             "Redo (Ctrl+Y)",
  "action.draw":              "Draw",
  "action.draw.tooltip":      "Create new markings with the chosen tool.",
  "action.edit.tooltip":      "Change markings of the chosen tool: click one to show its handles. Only that kind can be picked.",
  "lines.edit.hint":          "Click a line to show its curve handles. Drag the handles or a white point; right-click a white point to remove it.",
  "status.cw.place":          "Click a road to put a crosswalk across it · RMB on a crosswalk removes it",
  "status.cw.pick":           "Click a crosswalk to change it",
  "status.cw.open":           "Drag the ends, the blue side handles (width) or the middle (slide) · RMB removes",
  "section.crosswalks":       "Crosswalks",
  "cw.next.style":            "Style",
  "cw.next.width":            "Width",
  "cw.place.hint":            "Click a road near the junction: the crosswalk goes across it from kerb to kerb. Right-click a crosswalk to remove it.",
  "cw.edit.hint":             "Click a crosswalk to open it. Drag its white end points to move them, the blue side handles to change the width, and the green middle handle to slide it along the road. Right-click removes it.",
  "cw.empty":                 "No crosswalks yet. Switch to Draw and click a road.",
  "cw.title":                 "Crosswalk #{n}",
  "cw.style":                 "Style",
  "cw.width":                 "Width",
  "cw.width.step":            "0.5 m (Shift: 0.1 m)",
  "cw.delete":                "Delete crosswalk",
  "cw.style.0":               "Zebra",
  "cw.style.1":               "Ladder (zebra with borders)",
  "cw.style.2":               "Two lines",
  "cw.style.3":               "Zebra, thin bars",
  "point.crosswalks":         "{n} crosswalk(s)",
  "hotkeys.undo":             "undo / redo (Ctrl+Y)",
  "snap.label":               "Snapping",
  "snap.all.on":              "Turn all snapping on",
  "snap.all.off":             "Turn all snapping off",
  "snap.existing":            "Snap to existing geometry: dots, kerb corners, line crossings and your lines",
  "snap.angle":               "Snap to 90°: along or across the road, or square to the previous segment",
  "snap.side":                "Snap to the side of the road (the kerb line)",
  "snap.guides":              "Snap to guide lines: lane lines continued straight, and lines along and across the road through your points",
  "snap.length":              "Snap the length from the previous point to 0.5 m steps",
  "tab.advanced":             "Advanced",
  "status.advanced":          "Settings for this selection",
  "status.points":            "Drag a white point to move it · RMB removes it",
  "section.points":           "Your points",
  "points.hint":              "Every point you placed with free draw or as an area corner. Only points can be picked here, so lines and areas stay put.",
  "points.empty":             "No points yet. Place some with Free draw on the Lines tab.",
  "points.removeUnused":      "Remove unused points",
  "points.removeUnused.tooltip": "Removes every point that no line or area uses.",
  "point.title":              "Point #{n}",
  "point.onJunction":         "junction",
  "point.onRoad":             "road",
  "point.lines":              "{n} line(s)",
  "point.areas":              "{n} area(s)",
  "point.unused":             "unused",
  "point.delete.tooltip":     "Remove this point. Its lines go with it; areas only lose this corner.",
  "section.draw":             "Draw",
  "section.more":             "More options",
  "mode.points":              "Snap to points",
  "lines.empty":              "No lines yet. Click a point on the road to start one.",
  "areas.empty":              "No areas yet. Click points to outline one.",
  "advanced.optionsHint":     "Extra point rows, lane centre points and keybinds are in Options → Intersection & Lane Markings.",

  // Mode switch
  "mode.lines":               "Lines",
  "mode.area":                "Area",
  "mode.lines.tooltip":       "Draw marking lines between endpoint dots",
  "mode.area.tooltip":        "Fill a polygon between anchor dots (safety islands, hatched zones)",
  "mode.free":                "Free draw",
  "mode.free.tooltip":        "Click anywhere on the junction or its roads to place points and draw through them (hotkey F). Right-click a white point to remove it.",
  "mode.edit":                "Edit",
  "mode.edit.tooltip":        "Shape lines you have drawn: click a line to show its curve handles, then drag the handles or a white point. On this tab only lines can be picked, so areas are never hit by mistake (edit areas on the Areas tab). Right-click a white point to remove it.",

  // Area draft (AreaSelecting)
  "area.draft.hint.add":      "LMB anywhere on the road — add a corner (near a dot it snaps)",
  "area.draft.hint.undo":     "RMB — remove the last corner",
  "area.draft.hint.close":    "Click the first corner again to close (needs 3+). Shape it afterwards in Edit.",
  "area.mode.draw":           "Draw",
  "area.mode.draw.tooltip":   "Outline a new area: click on the road to place corners, like drawing a line.",
  "area.mode.edit.tooltip":   "Change areas you have drawn: click an area to open it, drag its corners and blue handles, + adds a corner, right-click removes one.",
  "area.edit.pick":           "Click an area (or open a row below) to change its shape.",
  "area.edit.hint":           "Drag the corners and blue handles on the road. Green + adds a corner, right-click a corner removes it.",
  "status.edit.area":         "Drag corners or blue handles · + adds a corner · RMB removes one",
  "area.draft.cancel":        "Cancel area",

  // Next-object style pickers
  "next.lineStyle":           "Line style",
  "next.lineStyle.tooltip":   "Style applied to the NEXT line you draw (hotkey Y cycles it)",
  "next.areaStyle":           "Fill",
  "next.areaStyle.tooltip":   "Fill applied to the NEXT area you close (hotkey U cycles it). Asphalt needs the separate 'G87 Vanilla Asphalt Pavement' mod (G87 VA); missing fills fall back to concrete. Pin favourites with the star to keep them on top.",

  // Sections
  "section.lines":            "Lines",
  "section.areas":            "Areas",

  // Area rows
  "area.title":               "Area #{n}",
  "area.pieces":              "{visible}/{total}",
  "area.meta.vertices":       "{n} pts",
  "area.hide.tooltip":        "Hide area",
  "area.show.tooltip":        "Show area",
  "area.delete":              "Delete area",
  "area.style":               "Fill style",

  // Area fill styles (indexes match the C# kStyleSurfaceNames)
  "areaStyle.0":              "Concrete",
  "areaStyle.1":              "Junction box",
  "areaStyle.2":              "White stripes",
  "areaStyle.3":              "White stripes, sparse",
  "areaStyle.4":              "Yellow stripes",
  "areaStyle.5":              "Green bike lane",
  "areaStyle.6":              "Red bus lane",
  "areaStyle.14":             "Asphalt (G87 VA)",
  "areaStyle.15":             "Grass",
  "areaStyle.17":             "Grass, dark",
  "areaStyle.18":             "Sand",
  "areaStyle.19":             "Pavement",
  "areaStyle.20":             "Tiles 1",
  "areaStyle.21":             "Tiles 2",
  "areaStyle.22":             "Tiles 3",

  // Full node reset
  "node.reset":               "Reset junction",
  "node.reset.tooltip":       "Delete every line and area on this node and bring back the game's own markings.",

  // Hotkey hints footer
  "hotkeys.title":            "Hotkeys",
  "hotkeys.toggle":           "toggle tool",
  "hotkeys.cycleLine":        "next line style",
  "hotkeys.cycleArea":        "next area fill",
  "hotkeys.areaMode":         "area mode",
  "hotkeys.freeDraw":         "free draw",
  "hotkeys.rmb":              "RMB",
  "hotkeys.rmb.desc":         "remove a point / undo",
  "hotkeys.esc":              "Esc",
  "hotkeys.esc.desc":         "step back",

  // Vanilla markings toggle
  "vanilla.hide":             "Hide vanilla markings",
  "vanilla.show":             "Show vanilla markings",
  "vanilla.tooltip":          "Suppress the game's own markings on this intersection. Nodes with drawn lines hide them automatically; this switch works without any lines.",

  // Line row
  "line.title":               "Line #{n}",
  "line.segCount":            "{visible}/{total}",
  "line.delete":              "Delete line",
  "line.delete.confirm":      "Delete line #{n}? This cannot be undone.",
  "line.delete.cancel":       "Cancel",
  "line.delete.confirm.btn":  "Delete",
  "line.curvature":           "Curvature",
  "line.curvature.tooltip":   "Bend of the line: 0% = straight, 50% = default arc, 100% = maximum roundness",
  "line.curvature.reset":     "Reset to the default 50%",
  "line.curvature.step":      "Step 1 · Shift: 10 · Ctrl: 5",
  "line.handles.hint":        "Drag the blue handles on the road to shape the curve.",
  "line.addPoint":            "Add point",
  "line.addPoint.tooltip":    "Split the line in the middle with a new point you can drag (white). Right-click it to remove.",
  "line.editHint":            "Switch to Edit to shape this line with handles.",

  // Segment row
  "segment.label":            "Segment {n}",
  "segment.length":           "{m}m",
  "segment.hide.tooltip":     "Hide segment",
  "segment.show.tooltip":     "Show segment",

  // Styles
  "style.solid":              "Solid",
  "style.dashed":             "Dashed",
  "style.g87Solid":           "G87 Solid",
  "style.g87Dashed":          "G87 Dashed",
  "style.doubleSolid":        "Double Solid",
  "style.dashedDense":        "Dashed, short",
  "style.dashedLong":         "Dashed, long",
  "style.g87Yellow":          "G87 Yellow",
  "style.g87YellowDashed":    "G87 Yellow Dashed",
  "style.curb":               "Curb",
  "style.yellowSolid":        "Yellow solid",
  "style.yellowDashed":       "Yellow dashed",
  "style.yellowDoubleSolid":  "Yellow double solid",
  "style.yellowSolidDashed":  "Yellow solid + dashed",
  "style.wide30":             "Wide line 30 cm (stop line)",
  "style.wide60":             "Wide line 60 cm",
  "style.unknown":            "?",
} as const;

// Shape every other locale must have: all of the English keys. To add a language, add a
// dictionary of this type and list it in STRINGS; CS2 locales without one fall back to en-US.
export type LocaleDict = { readonly [K in keyof typeof enUS]: string };

export const STRINGS = {
  "en-US": enUS,
} as const;

export type Locale = keyof typeof STRINGS;
export type StringKey = keyof typeof enUS;

export const DEFAULT_LOCALE: Locale = "en-US";

// Maps a CS2 locale code (en-US, de-DE, ...) to a shipped locale: exact match first, then
// any locale with the same base language, then en-US.
export const resolveLocale = (raw: string | null | undefined): Locale => {
  if (!raw) return DEFAULT_LOCALE;
  if (raw in STRINGS) return raw as Locale;
  const base = raw.split("-")[0];
  for (const code of Object.keys(STRINGS) as Locale[]) {
    if (code.startsWith(base + "-")) return code;
  }
  return DEFAULT_LOCALE;
};
