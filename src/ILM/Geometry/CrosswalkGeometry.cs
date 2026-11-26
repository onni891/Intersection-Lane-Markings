using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;

namespace ILM.Geometry
{
    /// <summary>
    /// Crosswalk shapes. A crosswalk runs along its path from one end point to the other
    /// (kerb to kerb) and is <see cref="MarkingCrosswalk.width"/> wide across the path, i.e.
    /// along the road. It is drawn from short wide line sublanes: bars across the path and, for
    /// some styles, border lines along it.
    /// </summary>
    public static class CrosswalkGeometry
    {
        public struct Piece
        {
            public Bezier4x3 curve;
            public MarkingStyle style;
        }

        // Bar and border sizes: the G87 wide lines are 30 and 60 cm.
        private const float kWide60 = 0.6f;
        private const float kWide30 = 0.3f;
        // Clear space at each kerb before the first bar.
        private const float kEndMargin = 0.25f;

        public static bool TryGetEnds(IReadOnlyList<MarkingEndpoint> endpoints, MarkingCrosswalk cw, out float3 a, out float3 b)
        {
            a = b = default;
            bool fa = false, fb = false;
            for (int i = 0; i < endpoints.Count && !(fa && fb); i++)
            {
                var ep = endpoints[i];
                if (!fa && ep.edge == cw.sourceEdge && ep.gapIndex == cw.sourceGapIndex) { a = ep.position; fa = true; }
                if (!fb && ep.edge == cw.targetEdge && ep.gapIndex == cw.targetGapIndex) { b = ep.position; fb = true; }
            }
            return fa && fb && math.distancesq(a.xz, b.xz) > 0.04f;
        }

        /// <summary>The four outline corners (a-side left, b-side left, b-side right, a-side
        /// right), for picking and the overlay.</summary>
        public static bool TryGetOutline(IReadOnlyList<MarkingEndpoint> endpoints, MarkingCrosswalk cw, out float3[] corners)
        {
            corners = null;
            if (!TryGetEnds(endpoints, cw, out var a, out var b)) return false;
            corners = Outline(a, b, cw.width);
            return true;
        }

        public static float3[] Outline(float3 a, float3 b, float width)
        {
            var n = Across(a, b) * (width * 0.5f);
            return new[] { a + n, b + n, b - n, a - n };
        }

        /// <summary>Unit vector across the path (along the road) in XZ.</summary>
        public static float3 Across(float3 a, float3 b)
        {
            float2 d = math.normalizesafe(b.xz - a.xz);
            return new float3(-d.y, 0f, d.x);
        }

        public static Bezier4x3 Straight(float3 a, float3 b)
            => new Bezier4x3(a, math.lerp(a, b, 1f / 3f), math.lerp(a, b, 2f / 3f), b);

        /// <summary>The sublanes that draw a crosswalk between <paramref name="a"/> and
        /// <paramref name="b"/>.</summary>
        public static void BuildPieces(float3 a, float3 b, float width, CrosswalkStyle style, List<Piece> outPieces)
        {
            outPieces.Clear();
            width = math.clamp(width, MarkingCrosswalk.kMinWidth, MarkingCrosswalk.kMaxWidth);
            float len = math.distance(a.xz, b.xz);
            if (len < 0.5f) return;
            float3 dir = (b - a) / math.max(math.length(b - a), 1e-4f);
            float3 across = Across(a, b);
            float half = width * 0.5f;

            bool borders = style == CrosswalkStyle.Ladder || style == CrosswalkStyle.TwoLines;
            if (borders)
            {
                // Border lines along the path, their outer edge on the crosswalk's edge.
                float o = half - kWide30 * 0.5f;
                outPieces.Add(new Piece { curve = Straight(a + across * o, b + across * o), style = MarkingStyle.Wide30 });
                outPieces.Add(new Piece { curve = Straight(a - across * o, b - across * o), style = MarkingStyle.Wide30 });
            }
            if (style == CrosswalkStyle.TwoLines) return;

            float bar = style == CrosswalkStyle.ZebraThin ? kWide30 : kWide60;
            float gap = style == CrosswalkStyle.ZebraThin ? 0.45f : 0.6f;
            var barStyle = style == CrosswalkStyle.ZebraThin ? MarkingStyle.Wide30 : MarkingStyle.Wide60;
            // Bars run across the path; inside the borders for the ladder.
            float barHalf = borders ? half - kWide30 - 0.15f : half;
            if (barHalf < 0.3f) return;
            float usable = len - 2f * kEndMargin;
            int count = (int)math.floor((usable + gap) / (bar + gap));
            if (count < 1) return;
            // Centre the pattern between the kerbs.
            float first = (len - (count * bar + (count - 1) * gap)) * 0.5f + bar * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float3 c = a + dir * (first + i * (bar + gap));
                outPieces.Add(new Piece { curve = Straight(c - across * barHalf, c + across * barHalf), style = barStyle });
            }
        }

        /// <summary>Whether a point is inside the crosswalk outline (XZ).</summary>
        public static bool Contains(float3[] corners, float3 p)
        {
            var ring = new List<float3>(corners);
            return PolygonUtils.ContainsXZ(ring, p);
        }

        /// <summary>
        /// The two free points for a crosswalk across the road at <paramref name="cursor"/>:
        /// same spot along the road, one on each kerb. The kerbs are taken from the outermost
        /// road dots on that road (lane edges and parking edges), so the crosswalk spans the
        /// carriageway and not the sidewalks. False off the road or inside the junction.
        /// </summary>
        public static bool TryPlace(EntityManager em, Entity owner, IReadOnlyList<MarkingEndpoint> endpoints, float3 cursor,
            Dictionary<Entity, float2> kerbCache, out MarkingFreePoint a, out MarkingFreePoint b)
        {
            a = b = default;
            if (!MarkingFreePointGeometry.TryCreate(em, owner, cursor, 0, out var p)) return false;
            if (p.IsInterior(owner)) return false;
            if (!kerbCache.TryGetValue(p.edge, out var lat))
            {
                lat = new float2(float.MaxValue, float.MinValue);
                for (int i = 0; i < endpoints.Count; i++)
                {
                    var ep = endpoints[i];
                    if (ep.edge != p.edge) continue;
                    var kind = MarkingEndpointExtractor.KindOf(ep.gapIndex);
                    if (kind != MarkingEndpointExtractor.AnchorKind.Main && kind != MarkingEndpointExtractor.AnchorKind.Parking) continue;
                    if (!MarkingFreePointGeometry.TryCreate(em, owner, ep.position, 0, out var q) || q.edge != p.edge) continue;
                    lat.x = math.min(lat.x, q.lateral);
                    lat.y = math.max(lat.y, q.lateral);
                }
                if (lat.x >= lat.y) lat = new float2(0f, 1f);
                kerbCache[p.edge] = lat;
            }
            a = new MarkingFreePoint { edge = p.edge, t = p.t, lateral = lat.x };
            b = new MarkingFreePoint { edge = p.edge, t = p.t, lateral = lat.y };
            return true;
        }

        public static int ShapeHash(float3 a, float3 b, MarkingCrosswalk cw)
        {
            var h = ILM.Utilities.Fnv1a.Create();
            h.Add((int)math.round(a.x * 100f)); h.Add((int)math.round(a.y * 100f)); h.Add((int)math.round(a.z * 100f));
            h.Add((int)math.round(b.x * 100f)); h.Add((int)math.round(b.y * 100f)); h.Add((int)math.round(b.z * 100f));
            h.Add((int)math.round(cw.width * 100f));
            h.Add(cw.style);
            return h.Value32;
        }
    }
}
