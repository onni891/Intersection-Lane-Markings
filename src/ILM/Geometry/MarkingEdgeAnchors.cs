using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace ILM.Geometry
{
    /// <summary>
    /// Anchor points for markings on a road stretch (an Edge that owns MarkingLine buffers
    /// itself, edited by clicking the middle of a road). The same lateral positions as at a
    /// junction (lane boundaries, plus lane centres when enabled) are repeated in rows across
    /// the road every <see cref="kRowSpacingM"/> metres, with one more row at the far end.
    ///
    /// gapIndex = kEdgeRowGapBase + row * 100 + lateral, where lateral is the main-row gap
    /// (0..49) or kCentreOffset + lane index for lane centres; the end row is row kEndRow, so
    /// lengthening the road does not renumber it. Distance rows keep their distance from the
    /// start node when the road changes.
    ///
    /// Along an edge, EdgeGeometry is two halves (m_Start then m_End), both oriented from the
    /// start node to the end node. Positions along the whole edge use u in [0, 2]: the integer
    /// part picks the half, the fraction is the parameter on its kerb curves.
    /// </summary>
    public static class MarkingEdgeAnchors
    {
        public const int kEdgeRowGapBase = 20000;
        public const float kRowSpacingM = 10f;
        private const int kMaxRows = 60;
        public const int kEndRow = 99;
        public const int kCentreOffset = 50;
        private const int kSamplesPerHalf = 32;
        private const float kCarriagewayGapM = 0.75f;

        /// <summary>True for a road edge used as a marking owner (as opposed to a junction).</summary>
        public static bool IsEdgeOwner(EntityManager em, Entity owner)
            => owner != Entity.Null && em.HasComponent<Edge>(owner) && !em.HasComponent<Node>(owner);

        public static bool IsLaneCentreGap(int gapIndex)
            => gapIndex >= kEdgeRowGapBase && (gapIndex - kEdgeRowGapBase) % 100 >= kCentreOffset;

        public static void Extract(EntityManager em, Entity edgeEntity, List<MarkingEndpoint> outList)
        {
            if (!MarkingEndpointExtractor.IsEdgeExtractionReady(em, edgeEntity)) return;
            if (!TryReadLaneLayout(em, edgeEntity, out var mainX, out var centreX, out float halfWidth)) return;
            var geom = em.GetComponentData<EdgeGeometry>(edgeEntity);

            BuildArcTable(geom, out var us, out var dists);
            float length = dists[dists.Count - 1];
            if (length < 1f) return;

            var settings = Mod.Settings;
            bool centres = settings == null || settings.LaneCentrePoints;

            int row = 0;
            for (float s = 0f; s < length - kRowSpacingM * 0.5f && row < kMaxRows; s += kRowSpacingM, row++)
                AddRow(edgeEntity, geom, UAtDistance(us, dists, s), row, mainX, centres ? centreX : null, halfWidth, outList);
            AddRow(edgeEntity, geom, 2f, kEndRow, mainX, centres ? centreX : null, halfWidth, outList);
        }

        private static void AddRow(Entity edgeEntity, in EdgeGeometry geom, float u, int row, List<float> mainX, List<float> centreX,
            float halfWidth, List<MarkingEndpoint> outList)
        {
            Locate(geom, u, out float3 left, out float3 right, out float3 tanL, out float3 tanR);
            int baseGap = kEdgeRowGapBase + row * 100;
            for (int i = 0; i < mainX.Count; i++)
                outList.Add(Make(edgeEntity, baseGap + i, mainX[i], halfWidth, left, right, tanL, tanR));
            if (centreX == null) return;
            for (int i = 0; i < centreX.Count; i++)
                outList.Add(Make(edgeEntity, baseGap + kCentreOffset + i, centreX[i], halfWidth, left, right, tanL, tanR));
        }

        private static MarkingEndpoint Make(Entity edge, int gap, float x, float halfWidth, float3 left, float3 right, float3 tanL, float3 tanR)
        {
            float f = math.saturate((x + halfWidth) / math.max(0.001f, halfWidth * 2f));
            return new MarkingEndpoint
            {
                edge = edge,
                gapIndex = gap,
                position = math.lerp(left, right, f),
                tangent = math.normalizesafe(math.lerp(tanL, tanR, f).xz),
            };
        }

        /// <summary>Kerb points and tangents (start→end direction) at u in [0, 2].</summary>
        public static void Locate(in EdgeGeometry geom, float u, out float3 left, out float3 right, out float3 tanL, out float3 tanR)
        {
            u = math.clamp(u, 0f, 2f);
            bool second = u > 1f;
            float t = second ? u - 1f : u;
            var l = second ? geom.m_End.m_Left : geom.m_Start.m_Left;
            var r = second ? geom.m_End.m_Right : geom.m_Start.m_Right;
            left = MathUtils.Position(l, t);
            right = MathUtils.Position(r, t);
            tanL = MathUtils.Tangent(l, t);
            tanR = MathUtils.Tangent(r, t);
        }

        /// <summary>Cumulative XZ arc length of the centreline against u.</summary>
        private static void BuildArcTable(in EdgeGeometry geom, out List<float> us, out List<float> dists)
        {
            int n = kSamplesPerHalf * 2;
            us = new List<float>(n + 1);
            dists = new List<float>(n + 1);
            float acc = 0f;
            float3 prev = default;
            for (int i = 0; i <= n; i++)
            {
                float u = i * 2f / n;
                Locate(geom, u, out var l, out var r, out _, out _);
                float3 c = (l + r) * 0.5f;
                if (i > 0) acc += math.distance(prev.xz, c.xz);
                us.Add(u);
                dists.Add(acc);
                prev = c;
            }
        }

        private static float UAtDistance(List<float> us, List<float> dists, float s)
        {
            for (int i = 1; i < dists.Count; i++)
            {
                if (dists[i] < s) continue;
                float span = dists[i] - dists[i - 1];
                float f = span > 1e-6f ? (s - dists[i - 1]) / span : 0f;
                return math.lerp(us[i - 1], us[i], f);
            }
            return 2f;
        }

        /// <summary>Lateral positions across the carriageway, the same rules as the junction
        /// extractor: kerbs, shared lane boundaries, both sides of a median; plus lane centres.
        /// Lateral x is measured from the road centre, negative to the left.</summary>
        public static bool TryReadLaneLayout(EntityManager em, Entity edgeEntity, out List<float> mainX, out List<float> centreX, out float halfWidth)
        {
            mainX = null; centreX = null; halfWidth = 0f;
            var composition = em.GetComponentData<Composition>(edgeEntity);
            Entity comp = composition.m_Edge;
            if (comp == Entity.Null || !em.HasBuffer<NetCompositionLane>(comp) || !em.HasComponent<NetCompositionData>(comp)) return false;
            var lanes = em.GetBuffer<NetCompositionLane>(comp, isReadOnly: true);
            halfWidth = em.GetComponentData<NetCompositionData>(comp).m_Width * 0.5f;

            var byX = new SortedDictionary<float, float>();
            for (int i = 0; i < lanes.Length; i++)
            {
                var cl = lanes[i];
                if ((cl.m_Flags & LaneFlags.Road) == 0) continue;
                if ((cl.m_Flags & (LaneFlags.Secondary | LaneFlags.Utility | LaneFlags.Master)) != 0) continue;
                float hw = em.HasComponent<NetLaneData>(cl.m_Lane) ? em.GetComponentData<NetLaneData>(cl.m_Lane).m_Width * 0.5f : 0f;
                float x = cl.m_Position.x;
                byX[x] = byX.TryGetValue(x, out float existing) ? math.max(existing, hw) : hw;
            }
            if (byX.Count == 0) return false;

            var sorted = new List<(float x, float hw)>(byX.Count);
            foreach (var kv in byX) sorted.Add((kv.Key, kv.Value));
            mainX = new List<float>(sorted.Count + 3);
            centreX = new List<float>(sorted.Count);
            mainX.Add(sorted[0].x - sorted[0].hw);
            for (int i = 1; i < sorted.Count; i++)
            {
                float prevRight = sorted[i - 1].x + sorted[i - 1].hw;
                float nextLeft = sorted[i].x - sorted[i].hw;
                if (nextLeft - prevRight > kCarriagewayGapM) { mainX.Add(prevRight); mainX.Add(nextLeft); }
                else mainX.Add((prevRight + nextLeft) * 0.5f);
            }
            int last = sorted.Count - 1;
            mainX.Add(sorted[last].x + sorted[last].hw);
            foreach (var s in sorted) centreX.Add(s.x);
            return true;
        }
    }
}
