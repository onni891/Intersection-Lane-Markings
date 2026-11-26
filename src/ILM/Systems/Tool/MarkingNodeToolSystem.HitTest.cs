using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;

namespace ILM.Systems.Tool
{
    // Cursor hit-testing against the selected node's dots, lines, areas and area anchors. All
    // distances are measured in the XZ plane: the raycast hit and the target can be at
    // different heights.
    public partial class MarkingNodeToolSystem
    {
        // Squared pick radius for dots, in metres, measured in the XZ plane from the cursor's
        // raycast hit. Deliberately larger than the drawn dot.
        private const float kDotPickRadiusSq = 1.5f * 1.5f;
        // Squared XZ distance within which the cursor counts as being on a line.
        private const float kLinePickRadiusSq = 1.5f * 1.5f;
        // Samples per Bezier: about 1 m spacing on a typical 10-12 m line.
        private const int kLineSampleCount = 12;

        // Reused point-in-polygon ring, to avoid per-frame allocations.
        private readonly List<float3> _areaHitScratch = new List<float3>();


        /// <summary>Closest dot within the pick radius, or -1. With <paramref name="freeOnly"/>
        /// only free points count (edit mode, where the other dots are not shown).</summary>
        private int FindHoveredEndpoint(float3 cursor, bool freeOnly = false)
        {
            int best = -1;
            float bestSq = kDotPickRadiusSq;
            for (int i = 0; i < _endpoints.Count; i++)
            {
                if (freeOnly && !IsFree(_endpoints[i])) continue;
                float sq = PolygonUtils.DistSqXZ(_endpoints[i].position, cursor);
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        /// <summary>Index of the MarkingLine passing closest to the cursor, or -1 if none is
        /// within <paramref name="radius"/> (default <see cref="kLinePickRadiusSq"/>). The
        /// distance is to a polyline through samples spaced about half a metre apart, so long
        /// mid-road lines are measured as precisely as short junction ones.</summary>
        private int HitTestLines(float3 cursor, float radius = -1f)
        {
            if (_selectedNode == Entity.Null) return -1;
            if (!EntityManager.HasBuffer<MarkingLine>(_selectedNode)) return -1;
            var lines = EntityManager.GetBuffer<MarkingLine>(_selectedNode, isReadOnly: true);
            if (lines.Length == 0) return -1;

            int best = -1;
            float bestSq = radius > 0f ? radius * radius : kLinePickRadiusSq;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!MarkingCurveBuilder.TryBuild(_endpoints, lines[i], out var bez)) continue;
                float sq = DistSqToCurveXZ(bez, cursor);
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        /// <summary>Squared XZ distance from the point to the curve.</summary>
        private static float DistSqToCurveXZ(Bezier4x3 bez, float3 p)
        {
            float approxLen = math.distance(bez.a.xz, bez.b.xz) + math.distance(bez.b.xz, bez.c.xz) + math.distance(bez.c.xz, bez.d.xz);
            int n = math.clamp((int)(approxLen * 2f), kLineSampleCount, 256);
            float best = float.MaxValue;
            float2 prev = bez.a.xz;
            for (int s = 1; s <= n; s++)
            {
                float2 cur = MathUtils.Position(bez, (float)s / n).xz;
                float2 seg = cur - prev;
                float len = math.lengthsq(seg);
                float t = len > 1e-6f ? math.saturate(math.dot(p.xz - prev, seg) / len) : 0f;
                best = math.min(best, math.lengthsq(prev + seg * t - p.xz));
                prev = cur;
            }
            return best;
        }

        /// <summary>areaIndex of the piece the cursor is inside, or -1. Where areas overlap the
        /// smallest piece wins, so an island inside a bigger fill can still be picked (the big
        /// one is picked outside the island). Uses the MarkingAreaPiece rings, whose positions
        /// the topology system has already resolved.</summary>
        private int HitTestAreas(float3 cursor)
        {
            if (_selectedNode == Entity.Null) return -1;
            if (!EntityManager.HasBuffer<MarkingAreaPiece>(_selectedNode)
                || !EntityManager.HasBuffer<MarkingAreaPieceVertex>(_selectedNode)) return -1;
            var pieces = EntityManager.GetBuffer<MarkingAreaPiece>(_selectedNode, isReadOnly: true);
            var verts = EntityManager.GetBuffer<MarkingAreaPieceVertex>(_selectedNode, isReadOnly: true);
            int best = -1;
            float bestArea = float.MaxValue;
            for (int p = 0; p < pieces.Length; p++)
            {
                var pd = pieces[p];
                if (pd.vertexCount < 3) continue;
                _areaHitScratch.Clear();
                bool ok = true;
                for (int v = 0; v < pd.vertexCount; v++)
                {
                    int idx = pd.firstVertex + v;
                    if (idx < 0 || idx >= verts.Length) { ok = false; break; }
                    _areaHitScratch.Add(verts[idx].position);
                }
                if (!ok || !PolygonUtils.ContainsXZ(_areaHitScratch, cursor)) continue;
                float size = AbsAreaXZ(_areaHitScratch);
                if (size < bestArea) { bestArea = size; best = pd.areaIndex; }
            }
            return best;
        }

        private static float AbsAreaXZ(List<float3> ring)
        {
            float sum = 0f;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
                sum += ring[j].x * ring[i].z - ring[i].x * ring[j].z;
            return math.abs(sum) * 0.5f;
        }

        /// <summary>Closest area candidate (lane endpoint, corner anchor or line crossing) to the
        /// cursor, measured in XZ like <see cref="FindHoveredEndpoint"/>.</summary>
        private AreaCandidate FindHoveredAreaCandidate(float3 cursor)
        {
            AreaCandidate best = AreaCandidate.None;
            float bestSq = kDotPickRadiusSq;
            for (int i = 0; i < _endpoints.Count; i++)
            {
                float sq = PolygonUtils.DistSqXZ(_endpoints[i].position, cursor);
                if (sq < bestSq) { bestSq = sq; best = new AreaCandidate { kind = AreaAnchorKind.LaneEndpoint, refIndex = i }; }
            }
            for (int i = 0; i < _cornerAnchors.Count; i++)
            {
                float sq = PolygonUtils.DistSqXZ(_cornerAnchors[i].position, cursor);
                if (sq < bestSq) { bestSq = sq; best = new AreaCandidate { kind = AreaAnchorKind.NodeCorner, refIndex = i }; }
            }
            for (int i = 0; i < _lineIntersections.Count; i++)
            {
                float sq = PolygonUtils.DistSqXZ(_lineIntersections[i].position, cursor);
                if (sq < bestSq) { bestSq = sq; best = new AreaCandidate { kind = AreaAnchorKind.LineIntersection, refIndex = _lineIntersections[i].PackedRef }; }
            }
            return best;
        }
    }
}
