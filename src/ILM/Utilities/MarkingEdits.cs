using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
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
        /// <summary>Removes a crosswalk, and its end points when nothing else uses them.</summary>
        public static void DeleteCrosswalk(EntityManager em, Entity node, int index)
        {
            if (!em.HasBuffer<MarkingCrosswalk>(node)) return;
            var cws = em.GetBuffer<MarkingCrosswalk>(node);
            if (index < 0 || index >= cws.Length) return;
            var cw = cws[index];
            cws.RemoveAt(index);
            if (MarkingEndpointExtractor.KindOf(cw.sourceGapIndex) == MarkingEndpointExtractor.AnchorKind.Free)
                AreaEdits.RemoveFreePointIfUnused(em, node, cw.sourceEdge, cw.sourceGapIndex);
            if (MarkingEndpointExtractor.KindOf(cw.targetGapIndex) == MarkingEndpointExtractor.AnchorKind.Free)
                AreaEdits.RemoveFreePointIfUnused(em, node, cw.targetEdge, cw.targetGapIndex);
            em.MarkUpdated(node);
        }

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

        /// <summary>Splits a line in two at its middle, through a new free point there, keeping
        /// the exact shape: both halves get handles cut from the original curve. The new point
        /// can then be dragged to reshape the line. False when the middle is off the road.</summary>
        public static bool SplitLine(EntityManager em, Entity node, int lineIndex)
        {
            if (!em.HasBuffer<MarkingLine>(node)) return false;
            var line = em.GetBuffer<MarkingLine>(node, isReadOnly: true)[lineIndex];
            var endpoints = MarkingEndpointExtractor.Extract(em, node);
            if (!MarkingCurveBuilder.TryFindEndpoints(endpoints, line, out var src, out var dst)) return false;
            var bez = MarkingCurveBuilder.BuildLine(src, dst, line);

            float3 mid = MathUtils.Position(bez, 0.5f);
            int id = NextFreePointId(em, node);
            if (!MarkingFreePointGeometry.TryCreate(em, node, mid, id, out var point)) return false;
            var (pEdge, pGap) = AddFreePoint(em, node, point);

            endpoints = MarkingEndpointExtractor.Extract(em, node);
            MarkingEndpoint p = default;
            bool found = false;
            foreach (var ep in endpoints)
                if (ep.edge == pEdge && ep.gapIndex == pGap) { p = ep; found = true; break; }
            if (!found) return false;

            var first = MathUtils.Cut(bez, new float2(0f, 0.5f));
            var second = MathUtils.Cut(bez, new float2(0.5f, 1f));
            var a = line;
            a.targetEdge = pEdge; a.targetGapIndex = pGap;
            a.hasHandles = true;
            a.handleA = first.b - src.position;
            a.handleB = first.c - p.position;
            var b = line;
            b.sourceEdge = pEdge; b.sourceGapIndex = pGap;
            b.hasHandles = true;
            b.handleA = second.b - p.position;
            b.handleB = second.c - dst.position;

            // AddFreePoint may have added a buffer, which invalidates earlier handles.
            var lines = em.GetBuffer<MarkingLine>(node);
            lines[lineIndex] = a;
            lines.Add(b);
            em.MarkUpdated(node);
            return true;
        }

        /// <summary>Moves a free point to a new click position, keeping its id so lines and
        /// areas stay attached. When it lands on a different road, their references follow.</summary>
        public static bool MoveFreePoint(EntityManager em, Entity node, Entity oldEdge, int gapIndex, float3 cursor)
        {
            if (!em.HasBuffer<MarkingFreePoint>(node)) return false;
            int id = gapIndex - MarkingEndpointExtractor.kFreePointGapBase;
            if (!MarkingFreePointGeometry.TryCreate(em, node, cursor, id, out var moved)) return false;
            var points = em.GetBuffer<MarkingFreePoint>(node);
            int found = -1;
            for (int i = 0; i < points.Length; i++)
                if (points[i].id == id && points[i].edge == oldEdge) { found = i; break; }
            if (found < 0) return false;
            points[found] = moved;

            if (moved.edge != oldEdge)
            {
                if (em.HasBuffer<MarkingLine>(node))
                {
                    var lines = em.GetBuffer<MarkingLine>(node);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var ln = lines[i];
                        if (ln.sourceEdge == oldEdge && ln.sourceGapIndex == gapIndex) ln.sourceEdge = moved.edge;
                        if (ln.targetEdge == oldEdge && ln.targetGapIndex == gapIndex) ln.targetEdge = moved.edge;
                        lines[i] = ln;
                    }
                }
                if (em.HasBuffer<MarkingAreaVertex>(node))
                {
                    var verts = em.GetBuffer<MarkingAreaVertex>(node);
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var v = verts[i];
                        if (v.kind != AreaAnchorKind.LaneEndpoint || v.refEdgeA != oldEdge || v.refGap != gapIndex) continue;
                        v.refEdgeA = moved.edge;
                        verts[i] = v;
                    }
                }
            }
            // Point positions are not part of the line hash, so force a rebuild.
            if (em.HasComponent<MarkingTopologyState>(node))
                em.SetComponentData(node, new MarkingTopologyState { linesHash = 0 });
            if (em.HasComponent<MarkingAreaTopologyState>(node))
                em.SetComponentData(node, new MarkingAreaTopologyState { combinedHash = 0 });
            em.MarkUpdated(node);
            return true;
        }

        /// <summary>Removes a free point together with every line ending on it. Areas only lose
        /// the vertex on it (an area left with fewer than three vertices is deleted), so
        /// nothing is left pointing at a missing anchor.</summary>
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
                for (int a = em.GetBuffer<MarkingArea>(node).Length - 1; a >= 0; a--)
                {
                    // Walk backwards so removals don't shift the vertices still to check.
                    for (int v = em.GetBuffer<MarkingArea>(node)[a].vertexCount - 1; v >= 0; v--)
                    {
                        var area = em.GetBuffer<MarkingArea>(node)[a];
                        if (v >= area.vertexCount) continue;
                        var verts = em.GetBuffer<MarkingAreaVertex>(node, isReadOnly: true);
                        int idx = area.firstVertex + v;
                        if (idx < 0 || idx >= verts.Length) continue;
                        var av = verts[idx];
                        if (av.kind != AreaAnchorKind.LaneEndpoint || av.refEdgeA != edge || av.refGap != gapIndex) continue;
                        bool deletesArea = area.vertexCount <= 3;
                        AreaEdits.RemoveVertex(em, node, a, v);
                        if (deletesArea) break; // slot a now holds the next area, already checked
                    }
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
            // A crosswalk can't exist without both ends.
            if (em.HasBuffer<MarkingCrosswalk>(node))
            {
                var cws = em.GetBuffer<MarkingCrosswalk>(node);
                for (int i = cws.Length - 1; i >= 0; i--)
                    if (cws[i].Uses(edge, gapIndex)) cws.RemoveAt(i);
            }
            em.MarkUpdated(node);
            return true;
        }
    }
}
