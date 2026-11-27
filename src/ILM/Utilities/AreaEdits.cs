using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Systems.Topology;

namespace ILM.Utilities
{
    /// <summary>
    /// Edits of a single area's outline, used by the tool's edit mode: move, add and remove
    /// vertices, and reshape an edge. Areas own contiguous slices of the node's
    /// <see cref="MarkingAreaVertex"/> buffer, so adding or removing a vertex shifts the
    /// firstVertex of the areas stored after it. Every edit forces a piece rebuild and marks the
    /// node Updated.
    /// </summary>
    public static class AreaEdits
    {
        /// <summary>A vertex anchored on a dot (lane endpoint, extra row or free point), stored
        /// with its stable edge/gap identity like the tool stores drawn vertices.</summary>
        public static MarkingAreaVertex EndpointVertex(MarkingEndpoint ep)
            => new MarkingAreaVertex
            {
                kind = AreaAnchorKind.LaneEndpoint,
                refIndex = 0,
                edgeToNext = AreaEdgeKind.Straight,
                refEdgeA = ep.edge,
                refGap = ep.gapIndex,
                refPos = ep.position,
            };

        public static bool TryGetArea(EntityManager em, Entity node, int areaIndex, out MarkingArea area)
        {
            area = default;
            if (!em.HasBuffer<MarkingArea>(node) || !em.HasBuffer<MarkingAreaVertex>(node)) return false;
            var areas = em.GetBuffer<MarkingArea>(node, isReadOnly: true);
            if (areaIndex < 0 || areaIndex >= areas.Length) return false;
            area = areas[areaIndex];
            var verts = em.GetBuffer<MarkingAreaVertex>(node, isReadOnly: true);
            return area.firstVertex >= 0 && area.firstVertex + area.vertexCount <= verts.Length;
        }

        /// <summary>Copies the area's vertices, in order.</summary>
        public static bool TryGetVertices(EntityManager em, Entity node, int areaIndex, List<MarkingAreaVertex> into)
        {
            into.Clear();
            if (!TryGetArea(em, node, areaIndex, out var area)) return false;
            var verts = em.GetBuffer<MarkingAreaVertex>(node, isReadOnly: true);
            for (int i = 0; i < area.vertexCount; i++) into.Add(verts[area.firstVertex + i]);
            return true;
        }

        /// <summary>Replaces one vertex (its anchor and edge data).</summary>
        public static bool SetVertex(EntityManager em, Entity node, int areaIndex, int localIndex, MarkingAreaVertex v)
        {
            if (!TryGetArea(em, node, areaIndex, out var area) || localIndex < 0 || localIndex >= area.vertexCount) return false;
            var verts = em.GetBuffer<MarkingAreaVertex>(node);
            verts[area.firstVertex + localIndex] = v;
            Invalidate(em, node);
            return true;
        }

        /// <summary>Moves a vertex onto another anchor, keeping the shape of its two edges where
        /// they were user curves (their handles are offsets, so they travel with the vertex).
        /// Edges that followed a marking line become straight: the new anchor is not on it.</summary>
        public static bool ReanchorVertex(EntityManager em, Entity node, int areaIndex, int localIndex, MarkingAreaVertex anchor)
        {
            if (!TryGetArea(em, node, areaIndex, out var area) || localIndex < 0 || localIndex >= area.vertexCount) return false;
            var verts = em.GetBuffer<MarkingAreaVertex>(node);
            int i = area.firstVertex + localIndex;
            int prev = area.firstVertex + (localIndex - 1 + area.vertexCount) % area.vertexCount;
            var old = verts[i];
            anchor.edgeToNext = old.edgeToNext == AreaEdgeKind.LineBezier ? AreaEdgeKind.Straight : old.edgeToNext;
            anchor.handleA = old.handleA;
            anchor.handleB = old.handleB;
            verts[i] = anchor;
            var p = verts[prev];
            if (p.edgeToNext == AreaEdgeKind.LineBezier)
            {
                p.edgeToNext = AreaEdgeKind.Straight;
                verts[prev] = p;
            }
            Invalidate(em, node);
            return true;
        }

        /// <summary>Makes the edge from vertex <paramref name="localIndex"/> to the next one a
        /// user curve with the given control points (world positions).</summary>
        public static bool SetEdgeCurve(EntityManager em, Entity node, int areaIndex, int localIndex,
                                        float3 from, float3 control1, float3 control2, float3 to)
        {
            if (!TryGetArea(em, node, areaIndex, out var area) || localIndex < 0 || localIndex >= area.vertexCount) return false;
            var verts = em.GetBuffer<MarkingAreaVertex>(node);
            var v = verts[area.firstVertex + localIndex];
            v.edgeToNext = AreaEdgeKind.Curve;
            v.handleA = control1 - from;
            v.handleB = control2 - to;
            verts[area.firstVertex + localIndex] = v;
            Invalidate(em, node);
            return true;
        }

        /// <summary>
        /// Inserts <paramref name="newVertex"/> after vertex <paramref name="afterLocal"/>,
        /// splitting that edge. <paramref name="edge"/> is the edge's current Bezier; both halves
        /// become user curves cut from it, so the outline keeps its shape exactly.
        /// </summary>
        public static bool InsertVertex(EntityManager em, Entity node, int areaIndex, int afterLocal,
                                        MarkingAreaVertex newVertex, Bezier4x3 edge, float t)
        {
            if (!TryGetArea(em, node, areaIndex, out var area) || afterLocal < 0 || afterLocal >= area.vertexCount) return false;
            var first = MathUtils.Cut(edge, new float2(0f, t));
            var second = MathUtils.Cut(edge, new float2(t, 1f));

            var verts = em.GetBuffer<MarkingAreaVertex>(node);
            int at = area.firstVertex + afterLocal;
            var a = verts[at];
            a.edgeToNext = AreaEdgeKind.Curve;
            a.handleA = first.b - first.a;
            a.handleB = first.c - first.d;
            verts[at] = a;

            newVertex.edgeToNext = AreaEdgeKind.Curve;
            newVertex.handleA = second.b - second.a;
            newVertex.handleB = second.c - second.d;
            verts.Insert(at + 1, newVertex);

            var areas = em.GetBuffer<MarkingArea>(node);
            for (int i = 0; i < areas.Length; i++)
            {
                var other = areas[i];
                if (i == areaIndex) other.vertexCount++;
                else if (other.firstVertex > at) other.firstVertex++;
                areas[i] = other;
            }
            Invalidate(em, node);
            return true;
        }

        /// <summary>Removes one vertex. An area left with fewer than three is deleted. The edge
        /// from the previous vertex now spans to the next one, so it becomes straight.</summary>
        public static void RemoveVertex(EntityManager em, Entity node, int areaIndex, int localIndex)
        {
            if (!TryGetArea(em, node, areaIndex, out var area) || localIndex < 0 || localIndex >= area.vertexCount) return;
            if (area.vertexCount <= 3)
            {
                MarkingEdits.DeleteArea(em, node, areaIndex);
                return;
            }
            var verts = em.GetBuffer<MarkingAreaVertex>(node);
            int prev = area.firstVertex + (localIndex - 1 + area.vertexCount) % area.vertexCount;
            var p = verts[prev];
            p.edgeToNext = AreaEdgeKind.Straight;
            verts[prev] = p;
            int at = area.firstVertex + localIndex;
            verts.RemoveAt(at);

            var areas = em.GetBuffer<MarkingArea>(node);
            for (int i = 0; i < areas.Length; i++)
            {
                var other = areas[i];
                if (i == areaIndex) other.vertexCount--;
                else if (other.firstVertex > at) other.firstVertex--;
                areas[i] = other;
            }
            Invalidate(em, node);
        }

        /// <summary>True when any line or area of the node uses the free point.</summary>
        public static bool IsFreePointUsed(EntityManager em, Entity node, Entity edge, int gapIndex)
        {
            if (em.HasBuffer<MarkingLine>(node))
            {
                var lines = em.GetBuffer<MarkingLine>(node, isReadOnly: true);
                for (int i = 0; i < lines.Length; i++)
                {
                    var ln = lines[i];
                    if ((ln.sourceEdge == edge && ln.sourceGapIndex == gapIndex)
                        || (ln.targetEdge == edge && ln.targetGapIndex == gapIndex)) return true;
                }
            }
            if (em.HasBuffer<MarkingAreaVertex>(node))
            {
                var verts = em.GetBuffer<MarkingAreaVertex>(node, isReadOnly: true);
                for (int i = 0; i < verts.Length; i++)
                {
                    var v = verts[i];
                    if (v.kind == AreaAnchorKind.LaneEndpoint && v.refEdgeA == edge && v.refGap == gapIndex) return true;
                }
            }
            return false;
        }

        /// <summary>Removes a free point that nothing uses any more (left behind by a removed
        /// vertex or a cancelled outline). Does nothing for a point still in use.</summary>
        public static void RemoveFreePointIfUnused(EntityManager em, Entity node, Entity edge, int gapIndex)
        {
            if (MarkingEndpointExtractor.KindOf(gapIndex) != MarkingEndpointExtractor.AnchorKind.Free) return;
            if (IsFreePointUsed(em, node, edge, gapIndex)) return;
            MarkingEdits.RemoveFreePoint(em, node, edge, gapIndex);
        }

        private static void Invalidate(EntityManager em, Entity node)
        {
            if (em.HasComponent<MarkingAreaTopologyState>(node))
                em.SetComponentData(node, new MarkingAreaTopologyState { combinedHash = 0 });
            em.MarkUpdated(node);
        }
    }
}
