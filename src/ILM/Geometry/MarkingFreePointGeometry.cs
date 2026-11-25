using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;

namespace ILM.Geometry
{
    /// <summary>
    /// Placement and resolution of user-placed free points (<see cref="MarkingFreePoint"/>).
    /// </summary>
    public static class MarkingFreePointGeometry
    {
        // Cap-curve samples when locating a click on a road edge; an edge half is rarely longer
        // than ~50 m, so 48 samples is about 1 m spacing before refinement.
        private const int kSamples = 48;
        // A click counts as on the carriageway between the kerb curves with this much slack.
        private const float kLateralSlack = 0.02f;

        /// <summary>Appends the node's free points to the endpoint list. Points on an edge
        /// that is no longer connected, or not ready yet, are skipped (their lines then wait or
        /// stay undrawn, like lines on any missing dot).</summary>
        public static void AppendFreePoints(EntityManager em, Entity node, List<MarkingEndpoint> outList)
        {
            if (!em.HasBuffer<MarkingFreePoint>(node)) return;
            var points = em.GetBuffer<MarkingFreePoint>(node, isReadOnly: true);
            if (points.Length == 0) return;

            // Road-stretch owner: t runs 0..2 over the whole edge (see MarkingEdgeAnchors).
            if (MarkingEdgeAnchors.IsEdgeOwner(em, node))
            {
                if (!MarkingEndpointExtractor.IsEdgeExtractionReady(em, node)) return;
                var eg = em.GetComponentData<EdgeGeometry>(node);
                for (int i = 0; i < points.Length; i++)
                {
                    var p = points[i];
                    MarkingEdgeAnchors.Locate(eg, p.t, out var l, out var r, out var tl, out var tr);
                    outList.Add(new MarkingEndpoint
                    {
                        edge = node,
                        gapIndex = MarkingEndpointExtractor.kFreePointGapBase + p.id,
                        position = math.lerp(l, r, p.lateral),
                        tangent = math.normalizesafe(math.lerp(tl, tr, p.lateral).xz),
                    });
                }
                return;
            }

            if (!em.HasComponent<Node>(node)) return;
            float3 nodePos = em.GetComponentData<Node>(node).m_Position;

            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                int gap = MarkingEndpointExtractor.kFreePointGapBase + p.id;
                if (p.IsInterior(node))
                {
                    outList.Add(new MarkingEndpoint { edge = node, gapIndex = gap, position = nodePos + p.offset, tangent = float2.zero });
                    continue;
                }
                if (!TryGetCapCurves(em, node, p.edge, out var left, out var right, out bool nodeIsStart)) continue;
                float3 pos = math.lerp(MathUtils.Position(left, p.t), MathUtils.Position(right, p.t), p.lateral);
                float3 tan = math.lerp(MathUtils.Tangent(left, p.t), MathUtils.Tangent(right, p.t), p.lateral);
                if (!nodeIsStart) tan = -tan; // away from the node, like the built-in dots
                outList.Add(new MarkingEndpoint { edge = p.edge, gapIndex = gap, position = pos, tangent = math.normalizesafe(tan.xz) });
            }
        }

        /// <summary>Builds a free point for a click at <paramref name="cursor"/>: on the
        /// node-side half of a connected road, or inside the junction outline. False when the
        /// click is off the road (sidewalk, another junction, empty terrain).</summary>
        public static bool TryCreate(EntityManager em, Entity node, float3 cursor, int id, out MarkingFreePoint point)
        {
            point = default;
            if (MarkingEdgeAnchors.IsEdgeOwner(em, node)) return TryCreateOnRoad(em, node, cursor, id, out point);
            if (!em.HasComponent<Node>(node) || !em.HasBuffer<ConnectedEdge>(node)) return false;
            float3 nodePos = em.GetComponentData<Node>(node).m_Position;
            var connected = em.GetBuffer<ConnectedEdge>(node, isReadOnly: true);

            // 1. On a road edge: closest cross-section whose kerb-to-kerb chord contains the click.
            float bestDist = float.MaxValue;
            for (int e = 0; e < connected.Length; e++)
            {
                var edge = connected[e].m_Edge;
                if (!TryGetCapCurves(em, node, edge, out var left, out var right, out _)) continue;
                for (int s = 0; s <= kSamples; s++)
                {
                    float t = s / (float)kSamples;
                    if (!ProjectOnChord(left, right, t, cursor, out float lateral, out float dist)) continue;
                    if (dist >= bestDist) continue;
                    bestDist = dist;
                    point = new MarkingFreePoint { id = id, edge = edge, t = t, lateral = lateral };
                }
            }
            if (point.edge != Entity.Null)
            {
                // Refine t around the best sample: the chord through the click is where the
                // perpendicular distance is smallest.
                TryGetCapCurves(em, node, point.edge, out var l, out var r, out _);
                float step = 1f / kSamples;
                for (int iter = 0; iter < 12; iter++)
                {
                    step *= 0.5f;
                    foreach (float cand in new[] { point.t - step, point.t + step })
                    {
                        float tc = math.saturate(cand);
                        if (ProjectOnChord(l, r, tc, cursor, out float lat, out float d) && d < bestDist)
                        {
                            bestDist = d;
                            point.t = tc;
                            point.lateral = lat;
                        }
                    }
                }
                // Within a metre of a cross-section: the sample spacing on long edges.
                if (bestDist < 1.0f) return true;
                point = default;
            }

            // 2. Inside the junction: the polygon of the roads' node-side kerb points.
            var outline = new List<(float angle, float3 pos)>(connected.Length * 2);
            for (int e = 0; e < connected.Length; e++)
            {
                if (!TryGetCapCurves(em, node, connected[e].m_Edge, out var left, out var right, out bool nodeIsStart)) continue;
                float3 a = nodeIsStart ? left.a : left.d;
                float3 b = nodeIsStart ? right.a : right.d;
                outline.Add((math.atan2(a.z - nodePos.z, a.x - nodePos.x), a));
                outline.Add((math.atan2(b.z - nodePos.z, b.x - nodePos.x), b));
            }
            if (outline.Count >= 3)
            {
                outline.Sort((x, y) => x.angle.CompareTo(y.angle));
                var ring = new List<float3>(outline.Count);
                foreach (var o in outline) ring.Add(o.pos);
                if (PolygonUtils.ContainsXZ(ring, cursor))
                {
                    point = new MarkingFreePoint { id = id, edge = node, offset = cursor - nodePos };
                    return true;
                }
            }
            else if (outline.Count == 2)
            {
                // Dead end or a plain joint between two edges: accept a small disc around the node.
                float r = math.distance(outline[0].pos.xz, outline[1].pos.xz) * 0.5f;
                if (math.distance(cursor.xz, nodePos.xz) <= r)
                {
                    point = new MarkingFreePoint { id = id, edge = node, offset = cursor - nodePos };
                    return true;
                }
            }
            return false;
        }

        /// <summary>Free point on a road-stretch owner: (t in 0..2 over both geometry halves,
        /// lateral fraction). The edge field holds the road itself.</summary>
        private static bool TryCreateOnRoad(EntityManager em, Entity road, float3 cursor, int id, out MarkingFreePoint point)
        {
            point = default;
            if (!MarkingEndpointExtractor.IsEdgeExtractionReady(em, road)) return false;
            var geom = em.GetComponentData<EdgeGeometry>(road);
            float bestDist = float.MaxValue;
            float bestU = 0f, bestLat = 0f;
            for (int half = 0; half < 2; half++)
            {
                var left = half == 0 ? geom.m_Start.m_Left : geom.m_End.m_Left;
                var right = half == 0 ? geom.m_Start.m_Right : geom.m_End.m_Right;
                for (int s = 0; s <= kSamples; s++)
                {
                    float t = s / (float)kSamples;
                    if (!ProjectOnChord(left, right, t, cursor, out float lat, out float d) || d >= bestDist) continue;
                    bestDist = d; bestU = half + t; bestLat = lat;
                }
            }
            if (bestDist == float.MaxValue) return false;
            // Refine around the best sample within its half.
            int h = bestU > 1f ? 1 : 0;
            var lc = h == 0 ? geom.m_Start.m_Left : geom.m_End.m_Left;
            var rc = h == 0 ? geom.m_Start.m_Right : geom.m_End.m_Right;
            float tt = bestU - h;
            float step = 1f / kSamples;
            for (int iter = 0; iter < 12; iter++)
            {
                step *= 0.5f;
                foreach (float cand in new[] { tt - step, tt + step })
                {
                    float c = math.saturate(cand);
                    if (ProjectOnChord(lc, rc, c, cursor, out float lat, out float d) && d < bestDist)
                    { bestDist = d; tt = c; bestLat = lat; }
                }
            }
            if (bestDist >= 1.0f) return false;
            point = new MarkingFreePoint { id = id, edge = road, t = h + tt, lateral = bestLat };
            return true;
        }

        /// <summary>Lateral fraction and perpendicular XZ distance of the click from the
        /// kerb-to-kerb chord at parameter t. False when the click is outside the kerbs.</summary>
        private static bool ProjectOnChord(in Bezier4x3 left, in Bezier4x3 right, float t, float3 cursor, out float lateral, out float dist)
        {
            float2 l = MathUtils.Position(left, t).xz;
            float2 r = MathUtils.Position(right, t).xz;
            float2 lr = r - l;
            float lenSq = math.lengthsq(lr);
            lateral = 0f;
            dist = float.MaxValue;
            if (lenSq < 1e-4f) return false;
            float f = math.dot(cursor.xz - l, lr) / lenSq;
            if (f < -kLateralSlack || f > 1f + kLateralSlack) return false;
            lateral = math.saturate(f);
            dist = math.distance(cursor.xz, l + lr * f);
            return true;
        }

        /// <summary>The node-side left/right kerb curves of a connected edge (EdgeGeometry
        /// m_Start or m_End), as <see cref="MarkingEndpointExtractor"/> uses them.</summary>
        public static bool TryGetCapCurves(EntityManager em, Entity node, Entity edgeEntity, out Bezier4x3 left, out Bezier4x3 right, out bool nodeIsStart)
        {
            left = default; right = default; nodeIsStart = false;
            if (!MarkingEndpointExtractor.IsEdgeExtractionReady(em, edgeEntity)) return false;
            var edge = em.GetComponentData<Edge>(edgeEntity);
            nodeIsStart = edge.m_Start == node;
            if (!nodeIsStart && edge.m_End != node) return false;
            var geom = em.GetComponentData<EdgeGeometry>(edgeEntity);
            left = nodeIsStart ? geom.m_Start.m_Left : geom.m_End.m_Left;
            right = nodeIsStart ? geom.m_Start.m_Right : geom.m_End.m_Right;
            return true;
        }
    }
}
