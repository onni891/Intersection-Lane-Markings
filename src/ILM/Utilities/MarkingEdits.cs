using Unity.Entities;
using ILM.Components;
using ILM.Geometry;
using ILM.Systems.Topology;

namespace ILM.Utilities
{
    /// <summary>
    /// Buffer edits shared by the tool and the panel commands. Each keeps the node's buffers
    /// consistent (segment and area references reindexed) and marks the node Updated so the
    /// topology and emission systems pick the change up.
    /// </summary>
    public static class MarkingEdits
    {
        /// <summary>Removes the line, reindexing segment overrides and area anchors.</summary>
        public static void DeleteLine(EntityManager em, Entity node, int lineIndex)
        {
            if (!em.HasBuffer<MarkingLine>(node)) return;
            var lines = em.GetBuffer<MarkingLine>(node);
            if (lineIndex < 0 || lineIndex >= lines.Length) return;
            lines.RemoveAt(lineIndex);
            MarkingTopologySystem.OnLineRemoved(em, node, lineIndex);
            em.MarkUpdated(node);
        }

        /// <summary>Removes the area and its vertex slice, shifts the firstVertex offsets of the
        /// areas after it, and forces a piece recompute.</summary>
        public static void DeleteArea(EntityManager em, Entity node, int areaIndex)
        {
            if (!em.HasBuffer<MarkingArea>(node)) return;
            var areas = em.GetBuffer<MarkingArea>(node);
            if (areaIndex < 0 || areaIndex >= areas.Length) return;

            var removed = areas[areaIndex];
            if (em.HasBuffer<MarkingAreaVertex>(node) && removed.vertexCount > 0)
            {
                var verts = em.GetBuffer<MarkingAreaVertex>(node);
                if (removed.firstVertex >= 0 && removed.firstVertex + removed.vertexCount <= verts.Length)
                    verts.RemoveRange(removed.firstVertex, removed.vertexCount);
            }
            areas = em.GetBuffer<MarkingArea>(node);
            areas.RemoveAt(areaIndex);
            for (int a = 0; a < areas.Length; a++)
            {
                var other = areas[a];
                if (other.firstVertex > removed.firstVertex)
                {
                    other.firstVertex -= removed.vertexCount;
                    areas[a] = other;
                }
            }

            // Pieces refer to areas by index, so MarkingAreaTopologySystem has to rebuild them
            // against the shifted list.
            if (em.HasComponent<MarkingAreaTopologyState>(node))
                em.SetComponentData(node, new MarkingAreaTopologyState { combinedHash = 0 });
            em.MarkUpdated(node);
        }

        /// <summary>Adds a free point and returns its endpoint identity (edge, gapIndex).</summary>
        public static (Entity edge, int gap) AddFreePoint(EntityManager em, Entity node, MarkingFreePoint point)
        {
            if (!em.HasBuffer<MarkingFreePoint>(node)) em.AddBuffer<MarkingFreePoint>(node);
            em.GetBuffer<MarkingFreePoint>(node).Add(point);
            em.MarkUpdated(node);
            return (point.edge, MarkingEndpointExtractor.kFreePointGapBase + point.id);
        }

        /// <summary>Next unused free-point id on the node.</summary>
        public static int NextFreePointId(EntityManager em, Entity node)
        {
            if (!em.HasBuffer<MarkingFreePoint>(node)) return 0;
            var points = em.GetBuffer<MarkingFreePoint>(node, isReadOnly: true);
            int next = 0;
            for (int i = 0; i < points.Length; i++)
                if (points[i].id >= next) next = points[i].id + 1;
            return next;
        }

        /// <summary>Removes a free point together with every line ending on it and every area
        /// with a vertex on it, so nothing is left pointing at a missing anchor.</summary>
        public static bool RemoveFreePoint(EntityManager em, Entity node, Entity edge, int gapIndex)
        {
            if (!em.HasBuffer<MarkingFreePoint>(node)) return false;
            int id = gapIndex - MarkingEndpointExtractor.kFreePointGapBase;
            var points = em.GetBuffer<MarkingFreePoint>(node);
            int found = -1;
            for (int i = 0; i < points.Length; i++)
                if (points[i].id == id && points[i].edge == edge) { found = i; break; }
            if (found < 0) return false;
            points.RemoveAt(found);

            if (em.HasBuffer<MarkingArea>(node) && em.HasBuffer<MarkingAreaVertex>(node))
            {
                var areas = em.GetBuffer<MarkingArea>(node, isReadOnly: true);
                for (int a = areas.Length - 1; a >= 0; a--)
                {
                    areas = em.GetBuffer<MarkingArea>(node, isReadOnly: true);
                    var verts = em.GetBuffer<MarkingAreaVertex>(node, isReadOnly: true);
                    var area = areas[a];
                    bool uses = false;
                    for (int v = 0; v < area.vertexCount && !uses; v++)
                    {
                        int idx = area.firstVertex + v;
                        if (idx < 0 || idx >= verts.Length) continue;
                        var av = verts[idx];
                        uses = av.kind == AreaAnchorKind.LaneEndpoint && av.refEdgeA == edge && av.refGap == gapIndex;
                    }
                    if (uses) DeleteArea(em, node, a);
                }
            }

            if (em.HasBuffer<MarkingLine>(node))
            {
                for (int i = em.GetBuffer<MarkingLine>(node).Length - 1; i >= 0; i--)
                {
                    var ln = em.GetBuffer<MarkingLine>(node)[i];
                    bool uses = (ln.sourceEdge == edge && ln.sourceGapIndex == gapIndex)
                             || (ln.targetEdge == edge && ln.targetGapIndex == gapIndex);
                    if (uses) DeleteLine(em, node, i);
                }
            }
            em.MarkUpdated(node);
            return true;
        }
    }
}
