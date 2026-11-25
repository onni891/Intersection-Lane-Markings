using System.Collections.Generic;
using Colossal;

namespace ILM.Localization
{
    public class LocaleEN : IDictionarySource
    {
        private readonly ILMSetting _setting;
        public LocaleEN(ILMSetting setting) { _setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { _setting.GetSettingsLocaleID(), "Intersection & Lane Markings" },
                { _setting.GetOptionTabLocaleID(ILMSetting.kSection), "Main" },

                { _setting.GetOptionGroupLocaleID(ILMSetting.kEdgeGroup), "Curb-side edge line" },
                { _setting.GetOptionGroupLocaleID(ILMSetting.kParkingGroup), "Parallel parking markings" },
                { _setting.GetOptionGroupLocaleID(ILMSetting.kSegmentGroup), "Marking editor — segment splitting" },
                { _setting.GetOptionGroupLocaleID(ILMSetting.kSegmentDevGroup), "Segment splitting — fine tuning" },
                { _setting.GetOptionGroupLocaleID(ILMSetting.kKeybindGroup), "Keybinds" },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.EdgeLineEnabled)), "Edge line on city roads" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.EdgeLineEnabled)),
                    "Adds the curb-side edge line to ordinary city roads (3 m car lanes), the way highway roads have it. Changes take effect after the game is restarted." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.EdgeLineStyle)), "Edge line style" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.EdgeLineStyle)),
                    "The mesh style used for the automatic curb-side edge line only — lines drawn with the marking tool keep their own styles. \"G87\" options require the [G87] Road Markings mod; if it isn't installed they fall back to vanilla. Changes take effect after the game is restarted." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.YellowLeftLineEnabled)), "Yellow left edge line (US)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.YellowLeftLineEnabled)),
                    "In North American-theme cities, one-way and divided roads get a yellow line along the left (median-side) edge of the carriageway, the way US roads mark it — the white edge line stays on the curb side. European-theme cities are unaffected. Changes take effect after the game is restarted." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ParkingMarkingsEnabled)), "Mark parallel parking zones" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ParkingMarkingsEnabled)),
                    "Draws a line along parallel street-parking zones with a cross tick at each end of the block. Roads without a Parking Lane 2 sublane (oneway 3-lane, asymmetric variants) remain unmarked — same coverage as v1.1. Changes take effect after the game is restarted." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ParkingLineStyle)), "Parking line style" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ParkingLineStyle)),
                    "The longitudinal line drawn along the parking zone. \"G87\" options require the [G87] Road Markings mod. Changes take effect after the game is restarted." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ParkingEndStyle)), "Parking end-tick style" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ParkingEndStyle)),
                    "The short perpendicular tick at the start and end of a parking block. \"None\" disables the ticks. \"G87\" options require the [G87] Road Markings mod. Changes take effect after the game is restarted." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.SegmentMinLengthM)), "Minimum segment length (m)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.SegmentMinLengthM)),
                    "When drawn lines cross, they are split into segments (each can be hidden or restyled). Segments shorter than this merge into their neighbour. Lower = finer segments on densely packed markings; higher = fewer slivers from lines that merely graze each other. Default: 1.0 m. Applies to a junction the next time its lines are edited, and everywhere after reloading the save." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.SegmentAnchorDeadZoneM)), "Dead zone around anchor dots (m)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.SegmentAnchorDeadZoneM)),
                    "Crossings closer than this to a line's endpoint don't split the line. Lines that leave the same anchor dot overlap for the first metre or two — without the dead zone that overlap spawns phantom micro-segments. Lower = splits allowed closer to the dots; higher = calmer behaviour around anchors. Default: 2.0 m. Applies to a junction the next time its lines are edited, and everywhere after reloading the save." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.SegmentMinCrossingAngleDeg)), "Minimum crossing angle (°)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.SegmentMinCrossingAngleDeg)),
                    "Two lines meeting at less than this angle count as a graze, not a crossing — no split. At 0° every touch splits, and near-parallel lines can produce clusters of micro-segments. Default: 8°. Applies to a junction the next time its lines are edited, and everywhere after reloading the save." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.SegmentHitClusterM)), "Crossing cluster radius (m)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.SegmentHitClusterM)),
                    "A shallow crossing is reported as several near-identical intersection points; points within this radius collapse into a single split. Lower = more of those near-duplicates survive as separate splits. Default: 1.5 m. Applies to a junction the next time its lines are edited, and everywhere after reloading the save." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ActivateMarkingTool)), "Activate marking tool" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ActivateMarkingTool)),
                    "Toggles the per-node marking customisation tool. Same as the keyboard shortcut below, but always works (button cannot be intercepted by other mods)." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ToggleMarkingToolBinding)), "Toggle marking tool (hotkey)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ToggleMarkingToolBinding)),
                    "Activates or deactivates the per-node marking customisation tool. Default Ctrl+M. If the hotkey doesn't work (other mod intercepts), use the button above instead." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.CycleMarkingStyleBinding)), "Cycle marking style (hotkey)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.CycleMarkingStyleBinding)),
                    "While the marking tool is active, cycles through Solid → Dashed → … The chosen style is used for the NEXT line you draw. Default Y. The colour of the endpoint dots reflects the current style." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.EnterAreaModeBinding)), "Start area polygon (hotkey)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.EnterAreaModeBinding)),
                    "With a node selected, starts the polygon-area mode: click anchor dots to build a filled region. Press the same key again or Esc to cancel. Default A." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.CycleAreaStyleBinding)), "Cycle area style (hotkey)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.CycleAreaStyleBinding)),
                    "While the marking tool is active, cycles the fill style for the NEXT area you close (Solid → Junction Box → White Stripes → Yellow Stripes → Green Bike → Red Bus → back). Default U. G87 styles fall back to Solid when G87 isn't installed." },

                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteSolid), "White solid" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteSolidThick), "White solid (thick)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteDashed), "White dashed" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.YellowSolid), "Yellow solid" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteSolid_G87), "White solid (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteDashed_G87), "White dashed (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.YellowSolid_G87), "Yellow solid (G87)" },

                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteDashedDense), "White dashed (dense)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteDashed), "White dashed" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteSolid), "White solid" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.YellowDashed), "Yellow dashed" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.YellowSolid), "Yellow solid" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteSolid_G87), "White solid (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteDashed_G87), "White dashed (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.YellowSolid_G87), "Yellow solid (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.YellowDashed_G87), "Yellow dashed (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.BlueSolid_G87), "Blue solid (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.BlueDashed_G87), "Blue dashed (G87)" },

                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.None), "None" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.WhiteSolid), "White solid" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.WhiteSolidThick), "White solid (thick)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.WhiteTerminal_G87), "White terminal line (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.YellowTerminal_G87), "Yellow terminal line (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.BlueSolid_G87), "Blue solid (G87)" },
            };
        }

        public void Unload() { }
    }
}
