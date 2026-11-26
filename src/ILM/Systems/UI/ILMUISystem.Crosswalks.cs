using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Utilities;

namespace ILM.Systems.UI
{
    // The Crosswalks tab and the undo / redo buttons.
    public partial class ILMUISystem
    {
        private int _uiHoveredCrosswalk = -1;
        public int UIHoveredCrosswalk => _uiHoveredCrosswalk;

        private void RegisterCrosswalkTriggers()
        {
            CreateTrigger<int>("SetCurrentCrosswalkStyle", s => _tool.SetCurrentCrosswalkStyle(s));
            CreateTrigger<int>("SetCurrentCrosswalkWidth", cm => _tool.SetCurrentCrosswalkWidth(cm / 100f));
            CreateTrigger<int, int>("SetCrosswalkStyle", OnSetCrosswalkStyle);
            CreateTrigger<int, int>("SetCrosswalkWidth", OnSetCrosswalkWidth);
            CreateTrigger<int>("DeleteCrosswalk", OnDeleteCrosswalk);
            CreateTrigger<int>("SetEditCrosswalk", i => _tool.SetEditCrosswalk(i));
            CreateTrigger<int>("SetHoveredCrosswalk", i => _uiHoveredCrosswalk = i);
            CreateTrigger<int>("ClearHoveredCrosswalk", i => { if (_uiHoveredCrosswalk == i) _uiHoveredCrosswalk = -1; });
            CreateTrigger("Undo", () => _tool.Undo());
            CreateTrigger("Redo", () => _tool.Redo());
        }

        private void HashCrosswalks(ref Fnv1a h, Entity node)
        {
            h.Add((int)_tool.CurrentCrosswalkStyle);
            h.Add(_tool.CurrentCrosswalkWidth);
            h.Add(_tool.EditCrosswalk);
            h.Add(_tool.HoveredCrosswalkInGame);
            h.Add(_tool.LastCreatedCrosswalk);
            h.Add(_tool.LastCreatedCrosswalkTick);
            h.Add(_tool.CanUndo);
            h.Add(_tool.CanRedo);
            if (node == Entity.Null || !EntityManager.HasBuffer<MarkingCrosswalk>(node)) { h.Add(0); return; }
            var cws = EntityManager.GetBuffer<MarkingCrosswalk>(node, isReadOnly: true);
            h.Add(cws.Length);
            for (int i = 0; i < cws.Length; i++) { h.Add(cws[i].style); h.Add(cws[i].width); }
        }

        private CrosswalkVM[] BuildCrosswalks(Entity node)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<MarkingCrosswalk>(node)) return System.Array.Empty<CrosswalkVM>();
            var cws = EntityManager.GetBuffer<MarkingCrosswalk>(node, isReadOnly: true);
            var endpoints = MarkingEndpointExtractor.Extract(EntityManager, node);
            var result = new CrosswalkVM[cws.Length];
            for (int i = 0; i < cws.Length; i++)
            {
                float len = CrosswalkGeometry.TryGetEnds(endpoints, cws[i], out var a, out var b) ? math.distance(a.xz, b.xz) : 0f;
                result[i] = new CrosswalkVM
                {
                    index = i,
                    style = cws[i].style,
                    widthCm = (int)math.round(cws[i].width * 100f),
                    lengthCm = (int)math.round(len * 100f),
                };
            }
            return result;
        }

        private int CountCrosswalks(Entity node, MarkingFreePoint p)
        {
            if (!EntityManager.HasBuffer<MarkingCrosswalk>(node)) return 0;
            var cws = EntityManager.GetBuffer<MarkingCrosswalk>(node, isReadOnly: true);
            int gap = GapOf(p), n = 0;
            for (int i = 0; i < cws.Length; i++) if (cws[i].Uses(p.edge, gap)) n++;
            return n;
        }

        private void OnSetCrosswalkStyle(int index, int style)
        {
            if (!TryGetSelectedBuffer<MarkingCrosswalk>("SetCrosswalkStyle", out var node, out var cws) || !IsInRange(cws, index)) return;
            var cw = cws[index];
            cw.style = math.clamp(style, 0, 3);
            cws[index] = cw;
            EntityManager.MarkUpdated(node);
        }

        private void OnSetCrosswalkWidth(int index, int widthCm)
        {
            if (!TryGetSelectedBuffer<MarkingCrosswalk>("SetCrosswalkWidth", out var node, out var cws) || !IsInRange(cws, index)) return;
            var cw = cws[index];
            cw.width = math.clamp(widthCm / 100f, MarkingCrosswalk.kMinWidth, MarkingCrosswalk.kMaxWidth);
            cws[index] = cw;
            EntityManager.MarkUpdated(node);
        }

        private void OnDeleteCrosswalk(int index)
        {
            if (!TryGetSelectedBuffer<MarkingCrosswalk>("DeleteCrosswalk", out var node, out var cws) || !IsInRange(cws, index)) return;
            MarkingEdits.DeleteCrosswalk(EntityManager, node, index);
            _tool.OnNodeEditedExternally();
            _uiHoveredCrosswalk = -1;
            log.Debug($"UI: deleted crosswalk#{index} on node#{node.Index}");
        }
    }
}
