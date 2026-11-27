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
  "status.free.next":         "Click to add the next point",
  "status.edit.pick":         "Click a line to shape it",
  "status.edit.open":         "Shape the line on the road",

  "panel.title.road":         "Road #{n}",

  // Node block
  "node.vanilla.label":       "Game markings",

  // Tabs: what you work on
  "tab.lines":                "Lines",
  "tab.areas":                "Areas",
  "tab.points":               "Points",
  "tab.lines.tooltip":        "Lane lines, stop lines and guide lines",
  "tab.areas.tooltip":        "Filled areas: hatching, bike and bus lanes, grass, islands",
  "tab.points.tooltip":       "Every point you placed. Move or remove them without touching anything else.",
  "tab.advanced":             "Advanced",
  "tab.advanced.tooltip":     "Copy and paste, templates, auto markings and settings for this junction",

  // Option rows (label on the left, buttons on the right, like the game's tool options)
  "opt.mode":                 "Mode",
  "opt.draw":                 "Draw",
  "opt.draw.tooltip":         "Make new markings",
  "opt.edit":                 "Edit",
  "opt.edit.tooltip":         "Change markings you already made: click one on the road to get its handles",
  "opt.place":                "Place on",
  "opt.place.dots":           "Dots",
  "opt.place.dots.tooltip":   "Lines go between the road's lane dots",
  "opt.place.free":           "Anywhere",
  "opt.place.free.tooltip":   "Click anywhere on the junction or its roads; each click adds a point the line runs through (hotkey F)",
  "opt.style":                "Style",
  "opt.style.tooltip":        "Style of the next line you draw (hotkey Y)",
  "opt.fill":                 "Fill",
  "opt.fill.tooltip":         "Fill of the next area you close (hotkey U). Asphalt needs the 'G87 Vanilla Asphalt Pavement' mod; missing fills fall back to concrete.",
  "opt.snap":                 "Snapping",

  // Help card: the mouse controls for the current mode
  "help.lmb":                 "LMB",
  "help.rmb":                 "RMB",
  "help.esc":                 "Esc",
  "help.lines.dots.lmb":      "click two dots to draw a line between them",
  "help.lines.dots.esc":      "drop the first dot",
  "help.lines.free.lmb":      "each click adds a point to the line",
  "help.lines.free.rmb":      "remove a white point",
  "help.lines.free.esc":      "finish the line",
  "help.lines.edit.lmb":      "pick a line, then drag its blue handles or white points",
  "help.lines.edit.rmb":      "remove a white point",
  "help.lines.edit.esc":      "put the line down",
  "help.areas.draw.lmb":      "add a corner; click the first corner to close",
  "help.areas.draw.rmb":      "remove the last corner",
  "help.areas.edit.lmb":      "pick an area, then drag corners or blue handles; green + adds a corner",
  "help.areas.edit.rmb":      "remove a corner",
  "help.points.lmb":          "drag a point to move it",
  "help.points.rmb":          "remove a point (its lines go with it)",

  // Advanced tab
  "adv.copy.title":           "Copy & templates",
  "adv.copy":                 "Copy",
  "adv.copy.tooltip":         "Copy every line, area and point here, to paste on another junction with the same number of roads",
  "adv.paste":                "Paste",
  "adv.paste.tooltip":        "Replace the markings here with the copied ones, turned to fit (Undo brings the old ones back)",
  "adv.paste.noFit":          "Needs a junction with the same number of roads (or a road, for markings copied from a road)",
  "adv.turn":                 "Turn",
  "adv.turn.tooltip":         "Lay the last paste down again, turned one road further",
  "adv.clipboard":            "Copied: {what}",
  "adv.clipboard.empty":      "Nothing copied yet.",
  "adv.paste.ok":             "Pasted {lines} line(s) and {areas} area(s). Not facing the right way? Press Turn.",
  "adv.paste.nothing":        "There is nothing to paste.",
  "adv.paste.roads":          "This junction has a different number of roads.",
  "adv.paste.kind":           "Markings from a road go on a road, and from a junction on a junction.",
  "adv.paste.noRoads":        "This junction has no roads to line the markings up with.",
  "adv.template.placeholder": "Template name",
  "adv.template.save":        "Save",
  "adv.template.save.tooltip": "Save the markings here as a template, kept between games",
  "adv.template.empty":       "No templates yet. Mark a junction, name it above and press Save.",
  "adv.template.delete":      "Delete template",
  "adv.kind.road":            "road",
  "adv.kind.junction":        "{n}-way junction",
  "adv.count.lines":          "{n} line(s)",
  "adv.count.areas":          "{n} area(s)",
  "adv.auto.title":           "Auto markings (test)",
  "adv.auto.global":          "Everywhere",
  "adv.auto.global.tooltip":  "Turn auto markings on or off for every junction. Junctions set to On or Off below keep their own setting.",
  "adv.auto.global.on":       "Guide lines on every junction that is not set to Off",
  "adv.auto.global.off":      "No guide lines, except on junctions set to On",
  "adv.auto.here":            "This junction",
  "adv.auto.here.tooltip":    "Default follows Everywhere; On and Off always apply here, whatever Everywhere says.",
  "adv.auto.here.default":    "Default",
  "adv.on":                   "On",
  "adv.off":                  "Off",
  "adv.auto.style":           "Style",
  "adv.auto.lanes":           "Lanes",
  "adv.auto.lanes.tooltip":   "Guide only turning lanes, or every lane through the junction",
  "adv.auto.lanes.turns":     "Turns",
  "adv.auto.lanes.all":       "All",
  "adv.auto.hint":            "Guide lines along the lanes the game built through the junction, so they follow the Traffic mod's lane connections. They are redrawn when the lanes change and leave the game's own markings in place.",
  "adv.auto.keep":            "Keep the {n} auto line(s) as my own",
  "adv.auto.keep.tooltip":    "Turn them into normal lines: they stay when the lanes change, and auto markings are set to Off here.",
  "hotkeys.title":            "Hotkeys",

  // Status line: the next step
  "status.points":            "Move or remove the points you placed",
  "status.area.pick":         "Click an area to change its shape",
  "status.area.open":         "Shape the area on the road",

  // List headers and footer
  "section.points":           "Points",
  "undo":                     "Undo",
  "redo":                     "Redo",
  "undo.tooltip":             "Undo (Ctrl+Z)",
  "redo.tooltip":             "Redo (Ctrl+Y)",
  "hotkeys.undo":             "undo (Ctrl+Y redo)",
  "settings.title":           "Junction settings",
  "settings.title.road":      "Road settings",

  // Snapping
  "snap.all.on":              "Turn all snapping on",
  "snap.all.off":             "Turn all snapping off",
  "snap.existing":            "Snap to existing geometry: dots, kerb corners, line crossings and your lines",
  "snap.angle":               "Snap to 90°: along or across the road, or square to the previous segment",
  "snap.side":                "Snap to the side of the road (the kerb line)",
  "snap.guides":              "Snap to guide lines: lane lines continued straight, and lines along and across the road through your points",
  "snap.length":              "Snap the length from the previous point to 0.5 m steps",
  "points.empty":             "No points yet. Draw a line with Place on → Anywhere to make some.",

  // Points list
  "points.removeUnused":      "Remove unused points",
  "points.removeUnused.tooltip": "Removes every point that no line or area uses.",
  "point.title":              "Point #{n}",
  "point.onJunction":         "junction",
  "point.onRoad":             "road",
  "point.lines":              "{n} line(s)",
  "point.areas":              "{n} area(s)",
  "point.unused":             "unused",
  "point.delete.tooltip":     "Remove this point. Its lines go with it; areas only lose this corner.",
  "lines.empty":              "No lines yet. Click two dots on the road to draw one.",
  "areas.empty":              "No areas yet. Choose Draw and click on the road to outline one.",
  "advanced.optionsHint":     "Extra point rows, lane centre points and keybinds are in Options → Intersection & Lane Markings.",

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
  "hotkeys.toggle":           "toggle tool",
  "hotkeys.cycleLine":        "next line style",
  "hotkeys.cycleArea":        "next area fill",
  "hotkeys.areaMode":         "area mode",
  "hotkeys.freeDraw":         "draw anywhere",
  "hotkeys.esc":              "Esc",
  "hotkeys.esc.desc":         "step back",

  // Vanilla markings toggle
  "vanilla.tooltip":          "Suppress the game's own markings on this intersection. Nodes with drawn lines hide them automatically; this switch works without any lines.",

  // Line row
  "line.title":               "Line #{n}",
  "line.segCount":            "{visible}/{total}",
  "line.delete":              "Delete line",
  "line.delete.cancel":       "Cancel",
  "line.delete.confirm.btn":  "Delete",
  "line.curvature":           "Curvature",
  "line.curvature.tooltip":   "Bend of the line: 0% = straight, 50% = default arc, 100% = maximum roundness",
  "line.curvature.reset":     "Reset to the default 50%",
  "line.curvature.step":      "Step 1 · Shift: 10 · Ctrl: 5",
  "line.addPoint":            "Add point",
  "line.addPoint.tooltip":    "Split the line in the middle with a new point you can drag (white). Right-click it to remove.",

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
