using System.Collections.Generic;
using Unity.Entities;
using ILM.Components;
using ILM.Geometry;
using ILM.Systems.Tool;
using ILM.Utilities;

namespace ILM.Systems.UI
{
    // The Points tab: every user-placed free point of the selected node, with how many lines
    // and areas use it, plus hover, delete and "remove unused".
    public partial class ILMUISystem
    {
        private readonly List<int> _pointIdScratch = new List<int>();

        private static int GapOf(MarkingFreePoint p) => MarkingEndpointExtractor.kFreePointGapBase + p.id;

        private int CountLines(Entity node, MarkingFreePoint p)
        {
            if (!EntityManager.HasBuffer<MarkingLine>(node)) return 0;
            var lines = EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true);
            int gap = GapOf(p), n = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if ((l.sourceEdge == p.edge && l.sourceGapIndex == gap) || (l.targetEdge == p.edge && l.targetGapIndex == gap)) n++;
            }
            return n;
        }

        private int CountAreas(Entity node, MarkingFreePoint p)
        {
            if (!EntityManager.HasBuffer<MarkingArea>(node) || !EntityManager.HasBuffer<MarkingAreaVertex>(node)) return 0;
            var areas = EntityManager.GetBuffer<MarkingArea>(node, isReadOnly: true);
            var verts = EntityManager.GetBuffer<MarkingAreaVertex>(node, isReadOnly: true);
            int gap = GapOf(p), n = 0;
            for (int a = 0; a < areas.Length; a++)
            {
                for (int v = 0; v < areas[a].vertexCount; v++)
                {
                    int idx = areas[a].firstVertex + v;
                    if (idx < 0 || idx >= verts.Length) break;
                    var av = verts[idx];
                    if (av.kind == AreaAnchorKind.LaneEndpoint && av.refEdgeA == p.edge && av.refGap == gap) { n++; break; }
                }
            }
            return n;
        }

        private void HashFreePoints(ref Fnv1a h, Entity node)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<MarkingFreePoint>(node)) { h.Add(0); return; }
            var points = EntityManager.GetBuffer<MarkingFreePoint>(node, isReadOnly: true);
            h.Add(points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                h.Add(points[i].id);
                h.Add(CountLines(node, points[i]));
                h.Add(CountAreas(node, points[i]));
            }
        }

        private FreePointVM[] BuildFreePoints(Entity node)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<MarkingFreePoint>(node)) return System.Array.Empty<FreePointVM>();
            var points = EntityManager.GetBuffer<MarkingFreePoint>(node, isReadOnly: true);
            var result = new FreePointVM[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                result[i] = new FreePointVM
                {
                    id = p.id,
                    onJunction = p.IsInterior(node),
                    lineCount = CountLines(node, p),
                    areaCount = CountAreas(node, p),
                };
            }
            return result;
        }

        /// <summary>The panel tab: what edit mode works on (0 lines, 1 areas, 2 points).</summary>
        private void OnSetEditTarget(int target)
        {
            if (target < 0 || target > (int)MarkingNodeToolSystem.EditTarget.Points) return;
            _tool.SetEditTarget((MarkingNodeToolSystem.EditTarget)target);
        }

        /// <summary>Removes the free point with its lines; areas only lose that corner.</summary>
        private void OnDeleteFreePoint(int id)
        {
            if (!TryGetSelectedBuffer<MarkingFreePoint>("DeleteFreePoint", out var node, out var points)) return;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i].id != id) continue;
                var p = points[i];
                bool ok = MarkingEdits.RemoveFreePoint(EntityManager, node, p.edge, GapOf(p));
                _tool.OnNodeEditedExternally();
                ClearUIHover();
                log.Debug($"UI: free point #{id} on node#{node.Index} {(ok ? "removed" : "not removed")}");
                return;
            }
        }

        /// <summary>Removes every free point no line or area uses.</summary>
        private void OnRemoveUnusedPoints()
        {
            if (!TryGetSelectedBuffer<MarkingFreePoint>("RemoveUnusedPoints", out var node, out var points)) return;
            _pointIdScratch.Clear();
            for (int i = 0; i < points.Length; i++)
                if (CountLines(node, points[i]) == 0 && CountAreas(node, points[i]) == 0) _pointIdScratch.Add(i);
            // Remove back to front so the indices stay valid.
            for (int k = _pointIdScratch.Count - 1; k >= 0; k--)
            {
                var p = EntityManager.GetBuffer<MarkingFreePoint>(node, isReadOnly: true)[_pointIdScratch[k]];
                MarkingEdits.RemoveFreePoint(EntityManager, node, p.edge, GapOf(p));
            }
            _tool.OnNodeEditedExternally();
            ClearUIHover();
            log.Debug($"UI: removed {_pointIdScratch.Count} unused free point(s) on node#{node.Index}");
        }

        private void OnSetHoveredPoint(int id) => _uiHoveredPointId = id;

        /// <summary>Clears only while that point is still the hovered one (see OnClearHoveredArea).</summary>
        private void OnClearHoveredPoint(int id)
        {
            if (_uiHoveredPointId == id) _uiHoveredPointId = -1;
        }
    }
}
