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
  "style.unknown":            "?",
} as const;

type LocaleDict = { readonly [K in keyof typeof enUS]: string };

const ruRU: LocaleDict = {
  // Toolbar
  "toolbar.toggle.label":     "Дорожная разметка",
  "toolbar.toggle.tooltip":   "Включить инструмент разметки ILM (Ctrl+M)",

  // Panel chrome
  "panel.appTitle":           "Разметка перекрёстков и полос",
  "panel.title":              "Узел #{n}",
  "panel.close.tooltip":      "Закрыть инструмент разметки",
  "panel.hint.selectNode":    "Кликните по перекрёстку или середине дороги, чтобы редактировать разметку.",
  "panel.error.title":        "Ошибка панели",
  "panel.error.retry":        "Повторить",

  // Tool status (MarkingNodeToolSystem.State)
  "status.line.first":        "Кликните первую точку линии",
  "status.line.second":       "Кликните вторую точку — линия завершится",
  "status.area":              "Режим области — точек: {n}",
  "status.free.first":        "Кликните в любом месте дороги, чтобы начать",
  "status.free.next":         "Клик — добавить точку · Esc — закончить",
  "status.edit.pick":         "Кликните линию, чтобы изменить её",
  "status.edit.open":         "Тяните голубые ручки или белые точки · Esc — отпустить линию",

  // Section headers
  "section.drawing":          "Рисование",
  "section.node":             "Перекрёсток",
  "section.road":             "Дорога",
  "panel.title.road":         "Дорога #{n}",

  // Node block
  "node.vanilla.label":       "Ванильная разметка",

  // Tabs and sections
  "tab.lines":                "Линии",
  "tab.areas":                "Области",
  "tab.points":               "Точки",
  "snap.label":               "Привязка",
  "snap.all.on":              "Включить всю привязку",
  "snap.all.off":             "Выключить всю привязку",
  "snap.existing":            "Привязка к существующей геометрии: точки, углы бордюров, пересечения и ваши линии",
  "snap.angle":               "Привязка к 90°: вдоль или поперёк дороги либо под прямым углом к предыдущему отрезку",
  "snap.side":                "Привязка к краю дороги (линии бордюра)",
  "snap.guides":              "Привязка к направляющим: продолжения линий полос и линии вдоль и поперёк дороги через ваши точки",
  "snap.length":              "Длина от предыдущей точки шагами по 0,5 м",
  "tab.advanced":             "Ещё",
  "status.advanced":          "Настройки выбранного объекта",
  "status.points":            "Тяните белую точку, чтобы переместить · ПКМ — удалить",
  "section.points":           "Ваши точки",
  "points.hint":              "Все точки, поставленные свободным рисованием или как углы областей. Здесь выбираются только точки — линии и области не задеваются.",
  "points.empty":             "Точек пока нет. Поставьте их свободным рисованием на вкладке «Линии».",
  "points.removeUnused":      "Удалить неиспользуемые точки",
  "points.removeUnused.tooltip": "Удаляет все точки, которые не использует ни одна линия или область.",
  "point.title":              "Точка #{n}",
  "point.onJunction":         "перекрёсток",
  "point.onRoad":             "дорога",
  "point.lines":              "линий: {n}",
  "point.areas":              "областей: {n}",
  "point.unused":             "не используется",
  "point.delete.tooltip":     "Удалить точку. Её линии удаляются вместе с ней, области лишь теряют этот угол.",
  "section.draw":             "Рисование",
  "section.more":             "Другие настройки",
  "mode.points":              "По точкам",
  "lines.empty":              "Линий пока нет. Кликните точку на дороге, чтобы начать.",
  "areas.empty":              "Областей пока нет. Кликайте точки, чтобы обвести область.",
  "advanced.optionsHint":     "Доп. ряды точек, точки по центру полос и клавиши — в Настройки → Intersection & Lane Markings.",

  // Mode switch
  "mode.lines":               "Линии",
  "mode.area":                "Область",
  "mode.lines.tooltip":       "Рисование линий разметки между точками",
  "mode.area.tooltip":        "Заливка полигона по опорным точкам (островки безопасности, штриховка)",
  "mode.free":                "Свободно",
  "mode.free.tooltip":        "Кликайте в любом месте перекрёстка или его дорог, чтобы ставить точки и рисовать через них (клавиша F). Правый клик по белой точке удаляет её.",
  "mode.edit":                "Правка",
  "mode.edit.tooltip":        "Правка нарисованных линий: кликните линию, чтобы показать ручки кривой, затем тяните ручки или белые точки. На этой вкладке выбираются только линии, области не задеваются (их правят на вкладке «Области»). Правый клик по белой точке удаляет её.",

  // Area draft (AreaSelecting)
  "area.draft.hint.add":      "ЛКМ в любом месте дороги — добавить угол (рядом с точкой — притягивается)",
  "area.draft.hint.undo":     "ПКМ — убрать последний угол",
  "area.draft.hint.close":    "Клик по первому углу — замкнуть (нужно 3+). Форму можно изменить в «Правке».",
  "area.mode.draw":           "Рисовать",
  "area.mode.draw.tooltip":   "Обвести новую область: кликайте по дороге, чтобы ставить углы, как при рисовании линии.",
  "area.mode.edit.tooltip":   "Правка нарисованных областей: кликните область, тяните углы и голубые ручки, + добавляет угол, правый клик удаляет.",
  "area.edit.pick":           "Кликните область (или раскройте строку ниже), чтобы изменить её форму.",
  "area.edit.hint":           "Тяните углы и голубые ручки на дороге. Зелёный + добавляет угол, правый клик по углу удаляет его.",
  "status.edit.area":         "Тяните углы или голубые ручки · + — новый угол · ПКМ — удалить",
  "area.draft.cancel":        "Отменить область",

  // Next-object style pickers
  "next.lineStyle":           "Стиль новой линии",
  "next.lineStyle.tooltip":   "Стиль СЛЕДУЮЩЕЙ линии, которую вы проведёте (хоткей Y листает по кругу)",
  "next.areaStyle":           "Заливка новой области",
  "next.areaStyle.tooltip":   "Заливка СЛЕДУЮЩЕЙ замкнутой области (хоткей U листает по кругу). Асфальту нужен отдельный мод «G87 Vanilla Asphalt Pavement» (G87 VA); недоступные стили откатываются к бетону. Звёздочка закрепляет любимые стили наверху.",

  // Sections
  "section.lines":            "Линии",
  "section.areas":            "Области",

  // Area rows
  "area.title":               "Область #{n}",
  "area.pieces":              "{visible}/{total}",
  "area.meta.vertices":       "{n} тчк",
  "area.hide.tooltip":        "Скрыть область",
  "area.show.tooltip":        "Показать область",
  "area.delete":              "Удалить область",
  "area.style":               "Стиль заливки",

  // Area fill styles (indexes match the C# kStyleSurfaceNames)
  "areaStyle.0":              "Бетон",
  "areaStyle.1":              "Вафельная разметка",
  "areaStyle.2":              "Белая штриховка",
  "areaStyle.3":              "Белая штриховка, редкая",
  "areaStyle.4":              "Жёлтая штриховка",
  "areaStyle.5":              "Зелёная велополоса",
  "areaStyle.6":              "Красная автобусная полоса",
  "areaStyle.14":             "Асфальт (G87 VA)",
  "areaStyle.15":             "Трава",
  "areaStyle.17":             "Трава тёмная",
  "areaStyle.18":             "Песок",
  "areaStyle.19":             "Тротуарная плитка",
  "areaStyle.20":             "Плитка 1",
  "areaStyle.21":             "Плитка 2",
  "areaStyle.22":             "Плитка 3",

  // Full node reset
  "node.reset":               "Сбросить всю разметку",
  "node.reset.tooltip":       "Удаляет все линии и области на этом узле и возвращает штатную разметку игры.",

  // Hotkey hints footer
  "hotkeys.title":            "Горячие клавиши",
  "hotkeys.toggle":           "вкл/выкл инструмент",
  "hotkeys.cycleLine":        "стиль новой линии",
  "hotkeys.cycleArea":        "заливка новой области",
  "hotkeys.areaMode":         "режим области",
  "hotkeys.freeDraw":         "свободное рисование",
  "hotkeys.rmb":              "ПКМ",
  "hotkeys.rmb.desc":         "удалить точку / отмена",
  "hotkeys.esc":              "Esc",
  "hotkeys.esc.desc":         "шаг назад",

  // Vanilla markings toggle
  "vanilla.hide":             "Скрыть ванильную разметку",
  "vanilla.show":             "Показать ванильную разметку",
  "vanilla.tooltip":          "Убирает штатную разметку игры на этом перекрёстке. Узлы с нарисованными линиями скрывают её автоматически; этот переключатель работает и без линий.",

  // Line row
  "line.title":               "Линия #{n}",
  "line.segCount":            "{visible}/{total}",
  "line.delete":              "Удалить линию",
  "line.delete.confirm":      "Удалить линию #{n}? Действие нельзя отменить.",
  "line.delete.cancel":       "Отмена",
  "line.delete.confirm.btn":  "Удалить",
  "line.curvature":           "Кривизна",
  "line.curvature.tooltip":   "Изгиб линии: 0% — прямая, 50% — стандартная дуга, 100% — максимальное скругление",
  "line.curvature.reset":     "Вернуть стандартные 50%",
  "line.curvature.step":      "Шаг 1 · Shift: 10 · Ctrl: 5",
  "line.handles.hint":        "Тяните голубые ручки на дороге, чтобы изогнуть линию.",
  "line.addPoint":            "Добавить точку",
  "line.addPoint.tooltip":    "Разделить линию посередине новой точкой, которую можно перетаскивать (белая). Правый клик удаляет её.",
  "line.editHint":            "Переключитесь в «Правка», чтобы изгибать линию ручками.",

  // Segment row
  "segment.label":            "Сегмент {n}",
  "segment.length":           "{m} м",
  "segment.hide.tooltip":     "Скрыть сегмент",
  "segment.show.tooltip":     "Показать сегмент",

  // Styles
  "style.solid":              "Сплошная",
  "style.dashed":             "Пунктир",
  "style.g87Solid":           "G87 Сплошная",
  "style.g87Dashed":          "G87 Пунктир",
  "style.doubleSolid":        "Двойная сплошная",
  "style.dashedDense":        "Пунктир, короткий",
  "style.dashedLong":         "Пунктир, длинный",
  "style.g87Yellow":          "G87 Жёлтая",
  "style.g87YellowDashed":    "G87 Жёлтый пунктир",
  "style.curb":               "Бордюр",
  "style.yellowSolid":        "Жёлтая сплошная",
  "style.yellowDashed":       "Жёлтый пунктир",
  "style.yellowDoubleSolid":  "Двойная жёлтая",
  "style.yellowSolidDashed":  "Жёлтая сплошная + пунктир",
  "style.unknown":            "?",
};

export const STRINGS = {
  "en-US": enUS,
  "ru-RU": ruRU,
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
