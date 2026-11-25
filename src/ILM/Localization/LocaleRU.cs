using System.Collections.Generic;
using Colossal;

namespace ILM.Localization
{
    public class LocaleRU : IDictionarySource
    {
        private readonly ILMSetting _setting;
        public LocaleRU(ILMSetting setting) { _setting = setting; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { _setting.GetSettingsLocaleID(), "Intersection & Lane Markings" },
                { _setting.GetOptionTabLocaleID(ILMSetting.kSection), "Основное" },

                { _setting.GetOptionGroupLocaleID(ILMSetting.kEdgeGroup), "Краевая линия у бордюра" },
                { _setting.GetOptionGroupLocaleID(ILMSetting.kParkingGroup), "Разметка параллельной парковки" },
                { _setting.GetOptionGroupLocaleID(ILMSetting.kSegmentGroup), "Редактор разметки — разрезание на сегменты" },
                { _setting.GetOptionGroupLocaleID(ILMSetting.kSegmentDevGroup), "Разрезание сегментов — тонкая настройка" },
                { _setting.GetOptionGroupLocaleID(ILMSetting.kKeybindGroup), "Горячие клавиши" },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.EdgeLineEnabled)), "Краевая линия на городских дорогах" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.EdgeLineEnabled)),
                    "Добавляет краевую линию у бордюра обычным городским дорогам (полосы 3 м) — так же, как на шоссе. Изменения вступают в силу после перезапуска игры." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.EdgeLineStyle)), "Стиль краевой линии" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.EdgeLineStyle)),
                    "Стиль меша только автоматической краевой линии — линии, нарисованные инструментом разметки, сохраняют собственные стили. Варианты «G87» требуют мод [G87] Road Markings; без него используется ванильный стиль. Изменения вступают в силу после перезапуска игры." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.YellowLeftLineEnabled)), "Жёлтая левая краевая (США)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.YellowLeftLineEnabled)),
                    "В городах с североамериканской темой односторонние и разделённые дороги получают жёлтую линию вдоль левого края проезжей части (со стороны медианы), как принято в США; белая краевая остаётся у бордюра. Города с европейской темой не затрагиваются. Изменения вступают в силу после перезапуска игры." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ParkingMarkingsEnabled)), "Размечать зоны параллельной парковки" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ParkingMarkingsEnabled)),
                    "Рисует линию вдоль зон параллельной уличной парковки с поперечной чертой на концах квартала. Дороги без сублейна Parking Lane 2 (односторонние трёхполосные, асимметричные варианты) остаются без разметки — то же покрытие, что и в v1.1. Изменения вступают в силу после перезапуска игры." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ParkingLineStyle)), "Стиль линии парковки" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ParkingLineStyle)),
                    "Продольная линия вдоль парковочной зоны. Варианты «G87» требуют мод [G87] Road Markings. Изменения вступают в силу после перезапуска игры." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ParkingEndStyle)), "Стиль концевой черты парковки" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ParkingEndStyle)),
                    "Короткая поперечная черта в начале и конце парковочного квартала. «Нет» отключает черты. Варианты «G87» требуют мод [G87] Road Markings. Изменения вступают в силу после перезапуска игры." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.SegmentMinLengthM)), "Минимальная длина сегмента (м)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.SegmentMinLengthM)),
                    "Пересекаясь, нарисованные линии режутся на сегменты (каждый можно скрыть или перекрасить). Сегменты короче этого значения сливаются с соседним. Меньше — более дробные сегменты на плотной разметке; больше — меньше «щепок» от линий, которые лишь слегка задевают друг друга. По умолчанию: 1,0 м. Применяется к перекрёстку при следующей правке его линий, а ко всему — после перезагрузки сохранения." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.SegmentAnchorDeadZoneM)), "Мёртвая зона у точек-якорей (м)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.SegmentAnchorDeadZoneM)),
                    "Пересечения ближе этого расстояния к концу линии не режут её. Линии, выходящие из одной точки-якоря, первые метр-два идут внахлёст — без мёртвой зоны этот нахлёст порождает фантомные микросегменты. Меньше — резы разрешены ближе к точкам; больше — спокойнее возле якорей. По умолчанию: 2,0 м. Применяется к перекрёстку при следующей правке его линий, а ко всему — после перезагрузки сохранения." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.SegmentMinCrossingAngleDeg)), "Минимальный угол пересечения (°)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.SegmentMinCrossingAngleDeg)),
                    "Линии, встречающиеся под углом меньше этого, считаются скользящим касанием, а не пересечением — без реза. При 0° режет любое касание, и почти параллельные линии могут дать пачку микросегментов. По умолчанию: 8°. Применяется к перекрёстку при следующей правке его линий, а ко всему — после перезагрузки сохранения." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.SegmentHitClusterM)), "Радиус склейки пересечений (м)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.SegmentHitClusterM)),
                    "Пологое пересечение распознаётся как несколько почти совпадающих точек; точки в этом радиусе склеиваются в один рез. Меньше — больше таких почти-дублей выживает отдельными резами. По умолчанию: 1,5 м. Применяется к перекрёстку при следующей правке его линий, а ко всему — после перезагрузки сохранения." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ActivateMarkingTool)), "Активировать инструмент разметки" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ActivateMarkingTool)),
                    "Включает/выключает инструмент настройки разметки перекрёстков. То же, что горячая клавиша ниже, но работает всегда (кнопку не может перехватить другой мод)." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.ToggleMarkingToolBinding)), "Инструмент разметки (клавиша)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.ToggleMarkingToolBinding)),
                    "Включает или выключает инструмент настройки разметки перекрёстков. По умолчанию Ctrl+M. Если клавиша не срабатывает (перехвачена другим модом), используйте кнопку выше." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.CycleMarkingStyleBinding)), "Стиль линии по кругу (клавиша)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.CycleMarkingStyleBinding)),
                    "При активном инструменте листает стили: сплошная → пунктир → … Выбранный стиль применяется к СЛЕДУЮЩЕЙ линии. По умолчанию Y. Цвет точек-якорей отражает текущий стиль." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.EnterAreaModeBinding)), "Режим области (клавиша)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.EnterAreaModeBinding)),
                    "При выбранном узле запускает режим полигональной области: кликайте по опорным точкам, чтобы построить заливку. Повторное нажатие или Esc — отмена. По умолчанию A." },

                { _setting.GetOptionLabelLocaleID(nameof(ILMSetting.CycleAreaStyleBinding)), "Стиль области по кругу (клавиша)" },
                { _setting.GetOptionDescLocaleID(nameof(ILMSetting.CycleAreaStyleBinding)),
                    "При активном инструменте листает стиль заливки для СЛЕДУЮЩЕЙ замкнутой области (бетон → вафельная разметка → белая штриховка → жёлтая штриховка → велополоса → автобусная полоса → сначала). По умолчанию U. Стили G87 без установленного мода G87 заменяются бетоном." },

                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteSolid), "Белая сплошная" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteSolidThick), "Белая сплошная (толстая)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteDashed), "Белый пунктир" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.YellowSolid), "Жёлтая сплошная" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteSolid_G87), "Белая сплошная (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.WhiteDashed_G87), "Белый пунктир (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.EdgeLineStyleEnum.YellowSolid_G87), "Жёлтая сплошная (G87)" },

                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteDashedDense), "Белый пунктир (частый)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteDashed), "Белый пунктир" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteSolid), "Белая сплошная" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.YellowDashed), "Жёлтый пунктир" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.YellowSolid), "Жёлтая сплошная" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteSolid_G87), "Белая сплошная (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.WhiteDashed_G87), "Белый пунктир (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.YellowSolid_G87), "Жёлтая сплошная (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.YellowDashed_G87), "Жёлтый пунктир (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.BlueSolid_G87), "Синяя сплошная (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingLineStyleEnum.BlueDashed_G87), "Синий пунктир (G87)" },

                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.None), "Нет" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.WhiteSolid), "Белая сплошная" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.WhiteSolidThick), "Белая сплошная (толстая)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.WhiteTerminal_G87), "Белая концевая линия (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.YellowTerminal_G87), "Жёлтая концевая линия (G87)" },
                { _setting.GetEnumValueLocaleID(ILMSetting.ParkingEndStyleEnum.BlueSolid_G87), "Синяя сплошная (G87)" },
            };
        }

        public void Unload() { }
    }
}
