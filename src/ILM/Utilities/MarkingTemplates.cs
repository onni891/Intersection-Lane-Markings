using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Systems.Topology;

namespace ILM.Utilities
{
    /// <summary>
    /// Copy / paste and saved templates. A template is a junction's (or road's) markings
    /// without any entity: every point is stored in a local frame (origin at the node, x axis
    /// along road 0, roads numbered counter-clockwise), and the road's own dots are found again
    /// on the target by position. That lets a template land on any junction with the same
    /// number of roads, turned so that road i of the template sits on road i + turn.
    /// </summary>
    public sealed class MarkingTemplate
    {
        public string name = "";
        public bool isRoad;
        public int roadCount;
        public int hide;
        public List<TPoint> points = new List<TPoint>();
        public List<TLine> lines = new List<TLine>();
        public List<MarkingSegment> segments = new List<MarkingSegment>();
        public List<MarkingArea> areas = new List<MarkingArea>();
        public List<TVertex> vertices = new List<TVertex>();

        public bool IsEmpty => lines.Count == 0 && areas.Count == 0 && points.Count == 0 && hide == 0;

        // A line end or area corner on the road: one of the road's dots (by road and position)
        // or one of the template's free points (by id).
        public struct TRef
        {
            public bool free;
            public int road;
            public int freeId;
            public float3 pos;
        }

        public struct TPoint
        {
            public int id;
            public float3 pos;
        }

        public struct TLine
        {
            public TRef a, b;
            public int style;
            public float curvature;
            public bool hasHandles;
            public float3 handleA, handleB; // local vectors
        }

        public struct TVertex
        {
            public AreaAnchorKind kind;
            public AreaEdgeKind edgeToNext;
            public TRef at;          // LaneEndpoint
            public float3 cornerPos; // NodeCorner (local)
            public int packed;       // LineIntersection: (lineA, lineB, hit)
            public float3 handleA, handleB; // local vectors
        }
    }

    public enum PasteResult { Ok, NothingToPaste, RoadCountDiffers, KindDiffers, NoRoads }

    public static class MarkingTemplates
    {
        // A template dot is found again on the target within this distance (XZ, metres).
        private const float kDotMatchRadius = 1.6f;
        private const float kCornerMatchRadius = 4f;

        // ------------------------------------------------------------------ frame

        private struct Frame
        {
            public float3 origin;
            public float angle; // of the local x axis, radians in the XZ plane
            public List<Entity> roads;

            public float3 ToLocal(float3 world) => Rotate(world - origin, -angle);
            public float3 ToWorld(float3 local) => origin + Rotate(local, angle);
            public float3 VecToLocal(float3 v) => Rotate(v, -angle);
            public float3 VecToWorld(float3 v) => Rotate(v, angle);
        }

        private static float3 Rotate(float3 v, float a)
        {
            float c = math.cos(a), s = math.sin(a);
            return new float3(v.x * c - v.z * s, v.y, v.x * s + v.z * c);
        }

        private static float AngleOf(float2 d) => math.atan2(d.y, d.x);

        /// <summary>The owner's roads, counter-clockwise from the +x axis, each with the
        /// direction it leaves the node in. A road owner has itself as its only road.</summary>
        private static bool TryGetRoads(EntityManager em, Entity owner, IReadOnlyList<MarkingEndpoint> endpoints,
                                        out float3 origin, out List<(Entity edge, float angle)> roads)
        {
            origin = default;
            roads = new List<(Entity, float)>();
            if (MarkingEdgeAnchors.IsEdgeOwner(em, owner))
            {
                if (!em.HasComponent<Curve>(owner)) return false;
                var bez = em.GetComponentData<Curve>(owner).m_Bezier;
                origin = MathUtils.Position(bez, 0.5f);
                roads.Add((owner, AngleOf(math.normalizesafe(MathUtils.Tangent(bez, 0.5f).xz))));
                return true;
            }
            if (!em.HasComponent<Node>(owner) || !em.HasBuffer<ConnectedEdge>(owner)) return false;
            origin = em.GetComponentData<Node>(owner).m_Position;
            var connected = em.GetBuffer<ConnectedEdge>(owner, isReadOnly: true);
            for (int i = 0; i < connected.Length; i++)
            {
                var edge = connected[i].m_Edge;
                // The road leaves the node in the direction its dots point.
                float2 sum = default;
                for (int k = 0; k < endpoints.Count; k++)
                    if (endpoints[k].edge == edge && endpoints[k].gapIndex < MarkingEndpointExtractor.kFreePointGapBase)
                        sum += endpoints[k].tangent;
                if (math.lengthsq(sum) < 1e-6f && em.HasComponent<Curve>(edge) && em.HasComponent<Edge>(edge))
                {
                    var bez = em.GetComponentData<Curve>(edge).m_Bezier;
                    bool start = em.GetComponentData<Edge>(edge).m_Start == owner;
                    sum = start ? MathUtils.Tangent(bez, 0f).xz : -MathUtils.Tangent(bez, 1f).xz;
                }
                if (math.lengthsq(sum) < 1e-6f) continue;
                roads.Add((edge, AngleOf(math.normalize(sum))));
            }
            roads.Sort((x, y) => x.angle.CompareTo(y.angle));
            return roads.Count > 0;
        }

        // ------------------------------------------------------------------ capture

        /// <summary>The markings of <paramref name="owner"/> as a template; null when the owner
        /// has no roads to measure from.</summary>
        public static MarkingTemplate Capture(EntityManager em, Entity owner)
        {
            var endpoints = MarkingEndpointExtractor.Extract(em, owner);
            if (!TryGetRoads(em, owner, endpoints, out var origin, out var roads)) return null;
            var frame = new Frame { origin = origin, angle = roads[0].angle, roads = new List<Entity>() };
            foreach (var r in roads) frame.roads.Add(r.edge);

            var t = new MarkingTemplate
            {
                isRoad = MarkingEdgeAnchors.IsEdgeOwner(em, owner),
                roadCount = roads.Count,
                hide = em.HasComponent<MarkingOverride>(owner) ? (int)em.GetComponentData<MarkingOverride>(owner).hide : 0,
            };

            if (em.HasBuffer<MarkingFreePoint>(owner))
            {
                var fps = em.GetBuffer<MarkingFreePoint>(owner, isReadOnly: true);
                for (int i = 0; i < fps.Length; i++)
                {
                    int ep = Find(endpoints, fps[i].edge, MarkingEndpointExtractor.kFreePointGapBase + fps[i].id);
                    if (ep < 0) continue;
                    t.points.Add(new MarkingTemplate.TPoint { id = fps[i].id, pos = frame.ToLocal(endpoints[ep].position) });
                }
            }

            // Old line index → template line index (lines that can't be captured drop out).
            var lineMap = new Dictionary<int, int>();
            if (em.HasBuffer<MarkingLine>(owner))
            {
                var lines = em.GetBuffer<MarkingLine>(owner, isReadOnly: true);
                for (int i = 0; i < lines.Length; i++)
                {
                    var ln = lines[i];
                    // Auto lines are made by each junction itself from its own lanes.
                    if (ln.isAuto) continue;
                    if (!TryRef(endpoints, frame, ln.sourceEdge, ln.sourceGapIndex, out var a)) continue;
                    if (!TryRef(endpoints, frame, ln.targetEdge, ln.targetGapIndex, out var b)) continue;
                    lineMap[i] = t.lines.Count;
                    t.lines.Add(new MarkingTemplate.TLine
                    {
                        a = a,
                        b = b,
                        style = ln.style,
                        curvature = ln.curvature,
                        hasHandles = ln.hasHandles,
                        handleA = frame.VecToLocal(ln.handleA),
                        handleB = frame.VecToLocal(ln.handleB),
                    });
                }
            }
            if (em.HasBuffer<MarkingSegment>(owner))
            {
                var segs = em.GetBuffer<MarkingSegment>(owner, isReadOnly: true);
                for (int i = 0; i < segs.Length; i++)
                {
                    if (!lineMap.TryGetValue(segs[i].lineIndex, out int li)) continue;
                    var s = segs[i];
                    s.lineIndex = li;
                    t.segments.Add(s);
                }
            }

            if (em.HasBuffer<MarkingArea>(owner) && em.HasBuffer<MarkingAreaVertex>(owner))
            {
                var corners = MarkingEndpointExtractor.ExtractCornerAnchors(em, owner);
                var areas = em.GetBuffer<MarkingArea>(owner, isReadOnly: true);
                var verts = em.GetBuffer<MarkingAreaVertex>(owner, isReadOnly: true);
                var tmp = new List<MarkingTemplate.TVertex>();
                for (int a = 0; a < areas.Length; a++)
                {
                    var area = areas[a];
                    tmp.Clear();
                    bool ok = area.vertexCount >= 3 && area.firstVertex + area.vertexCount <= verts.Length;
                    for (int k = 0; ok && k < area.vertexCount; k++)
                    {
                        var v = verts[area.firstVertex + k];
                        var tv = new MarkingTemplate.TVertex
                        {
                            kind = v.kind,
                            edgeToNext = v.edgeToNext,
                            handleA = frame.VecToLocal(v.handleA),
                            handleB = frame.VecToLocal(v.handleB),
                        };
                        switch (v.kind)
                        {
                            case AreaAnchorKind.LaneEndpoint:
                                int ep = MarkingEndpointExtractor.ResolveEndpointIndex(endpoints, v);
                                ok = ep >= 0 && TryRef(endpoints, frame, endpoints[ep].edge, endpoints[ep].gapIndex, out tv.at);
                                break;
                            case AreaAnchorKind.NodeCorner:
                                int c = MarkingEndpointExtractor.ResolveCornerIndex(corners, v);
                                ok = c >= 0;
                                if (ok) tv.cornerPos = frame.ToLocal(corners[c].position);
                                break;
                            case AreaAnchorKind.LineIntersection:
                                MarkingIntersectionExtractor.Unpack(v.refIndex, out int la, out int lb, out int hit);
                                ok = lineMap.TryGetValue(la, out int na) && lineMap.TryGetValue(lb, out int nb);
                                if (ok) tv.packed = MarkingIntersectionExtractor.Pack(lineMap[la], lineMap[lb], hit);
                                break;
                            default:
                                ok = false;
                                break;
                        }
                        tmp.Add(tv);
                    }
                    if (!ok) continue;
                    t.areas.Add(new MarkingArea { styleId = area.styleId, visible = area.visible, firstVertex = t.vertices.Count, vertexCount = tmp.Count });
                    t.vertices.AddRange(tmp);
                }
            }
            return t;
        }

        private static int Find(IReadOnlyList<MarkingEndpoint> endpoints, Entity edge, int gap)
        {
            for (int i = 0; i < endpoints.Count; i++)
                if (endpoints[i].edge == edge && endpoints[i].gapIndex == gap) return i;
            return -1;
        }

        private static bool TryRef(IReadOnlyList<MarkingEndpoint> endpoints, Frame frame, Entity edge, int gap, out MarkingTemplate.TRef r)
        {
            r = default;
            int ep = Find(endpoints, edge, gap);
            if (ep < 0) return false;
            r.pos = frame.ToLocal(endpoints[ep].position);
            if (gap >= MarkingEndpointExtractor.kFreePointGapBase && gap < MarkingEdgeAnchors.kEdgeRowGapBase)
            {
                r.free = true;
                r.freeId = gap - MarkingEndpointExtractor.kFreePointGapBase;
                r.road = -1;
                return true;
            }
            r.road = frame.roads.IndexOf(edge);
            return r.road >= 0;
        }

        // ------------------------------------------------------------------ apply

        /// <summary>Number of ways the template can be turned on this owner: one per road on a
        /// junction, two (as drawn, reversed) on a road.</summary>
        public static int TurnCount(MarkingTemplate t) => t.isRoad ? 2 : math.max(1, t.roadCount);

        /// <summary>Checks that the template fits the owner: same kind (junction or road) and
        /// the same number of roads.</summary>
        public static PasteResult CanApply(EntityManager em, Entity owner, MarkingTemplate t)
        {
            if (t == null || t.IsEmpty) return PasteResult.NothingToPaste;
            bool isRoad = MarkingEdgeAnchors.IsEdgeOwner(em, owner);
            if (isRoad != t.isRoad) return PasteResult.KindDiffers;
            var endpoints = MarkingEndpointExtractor.Extract(em, owner);
            if (!TryGetRoads(em, owner, endpoints, out _, out var roads)) return PasteResult.NoRoads;
            if (roads.Count != t.roadCount) return PasteResult.RoadCountDiffers;
            return PasteResult.Ok;
        }

        /// <summary>Replaces the owner's markings with the template. <paramref name="turn"/> is
        /// the road the template's road 0 lands on (0..TurnCount-1), or -1 to pick the turn where
        /// the most dots line up. <paramref name="usedTurn"/> is the turn applied.</summary>
        public static PasteResult Apply(EntityManager em, Entity owner, MarkingTemplate t, int turn, out int usedTurn, out int placedLines, out int placedAreas)
        {
            usedTurn = 0; placedLines = 0; placedAreas = 0;
            var check = CanApply(em, owner, t);
            if (check != PasteResult.Ok) return check;

            var endpoints = MarkingEndpointExtractor.Extract(em, owner);
            TryGetRoads(em, owner, endpoints, out var origin, out var roads);
            var corners = MarkingEndpointExtractor.ExtractCornerAnchors(em, owner);

            int turns = TurnCount(t);
            if (turn < 0)
            {
                int bestScore = -1;
                float bestErr = float.MaxValue;
                for (int k = 0; k < turns; k++)
                {
                    Score(t, MakeFrame(t, origin, roads, k), endpoints, corners, out int score, out float err);
                    if (score > bestScore || (score == bestScore && err < bestErr)) { bestScore = score; bestErr = err; usedTurn = k; }
                }
            }
            else usedTurn = ((turn % turns) + turns) % turns;
            var frame = MakeFrame(t, origin, roads, usedTurn);

            // Start clean: the template replaces what is there (undo brings it back).
            Clear<MarkingLine>(em, owner);
            Clear<MarkingSegment>(em, owner);
            Clear<MarkingArea>(em, owner);
            Clear<MarkingAreaVertex>(em, owner);
            Clear<MarkingFreePoint>(em, owner);

            // Free points, by id. Their road follows from where they land.
            var freeRefs = new Dictionary<int, (Entity edge, int gap)>();
            foreach (var p in t.points)
            {
                if (!MarkingFreePointGeometry.TryCreate(em, owner, frame.ToWorld(p.pos), p.id, out var fp)) continue;
                freeRefs[p.id] = MarkingEdits.AddFreePoint(em, owner, fp);
            }
            // Endpoints again, now with the new free points in place.
            endpoints = MarkingEndpointExtractor.Extract(em, owner);

            var lineMap = new Dictionary<int, int>();
            if (t.lines.Count > 0)
            {
                var lines = em.HasBuffer<MarkingLine>(owner) ? em.GetBuffer<MarkingLine>(owner) : em.AddBuffer<MarkingLine>(owner);
                for (int i = 0; i < t.lines.Count; i++)
                {
                    var tl = t.lines[i];
                    if (!Resolve(tl.a, frame, endpoints, freeRefs, out var ea, out var ga)) continue;
                    if (!Resolve(tl.b, frame, endpoints, freeRefs, out var eb, out var gb)) continue;
                    lineMap[i] = lines.Length;
                    lines.Add(new MarkingLine
                    {
                        sourceEdge = ea,
                        sourceGapIndex = ga,
                        targetEdge = eb,
                        targetGapIndex = gb,
                        style = tl.style,
                        curvature = tl.curvature,
                        hasHandles = tl.hasHandles,
                        handleA = frame.VecToWorld(tl.handleA),
                        handleB = frame.VecToWorld(tl.handleB),
                    });
                }
                placedLines = lines.Length;
            }
            if (t.segments.Count > 0 && lineMap.Count > 0)
            {
                var segs = em.HasBuffer<MarkingSegment>(owner) ? em.GetBuffer<MarkingSegment>(owner) : em.AddBuffer<MarkingSegment>(owner);
                foreach (var s0 in t.segments)
                {
                    if (!lineMap.TryGetValue(s0.lineIndex, out int li)) continue;
                    var s = s0;
                    s.lineIndex = li;
                    segs.Add(s);
                }
            }

            if (t.areas.Count > 0)
            {
                var tmp = new List<MarkingAreaVertex>();
                foreach (var area in t.areas)
                {
                    tmp.Clear();
                    bool ok = true;
                    for (int k = 0; ok && k < area.vertexCount; k++)
                    {
                        var tv = t.vertices[area.firstVertex + k];
                        var v = new MarkingAreaVertex
                        {
                            kind = tv.kind,
                            edgeToNext = tv.edgeToNext,
                            handleA = frame.VecToWorld(tv.handleA),
                            handleB = frame.VecToWorld(tv.handleB),
                        };
                        switch (tv.kind)
                        {
                            case AreaAnchorKind.LaneEndpoint:
                                ok = Resolve(tv.at, frame, endpoints, freeRefs, out v.refEdgeA, out v.refGap);
                                if (ok) v.refPos = endpoints[Find(endpoints, v.refEdgeA, v.refGap)].position;
                                break;
                            case AreaAnchorKind.NodeCorner:
                                int c = Nearest(corners, frame.ToWorld(tv.cornerPos), kCornerMatchRadius);
                                ok = c >= 0;
                                if (ok) { v.refEdgeA = corners[c].edgeA; v.refEdgeB = corners[c].edgeB; v.refPos = corners[c].position; }
                                break;
                            case AreaAnchorKind.LineIntersection:
                                MarkingIntersectionExtractor.Unpack(tv.packed, out int la, out int lb, out int hit);
                                ok = lineMap.TryGetValue(la, out int na) && lineMap.TryGetValue(lb, out int nb);
                                if (ok) v.refIndex = MarkingIntersectionExtractor.Pack(lineMap[la], lineMap[lb], hit);
                                break;
                        }
                        tmp.Add(v);
                    }
                    if (!ok) continue;
                    var areas = em.HasBuffer<MarkingArea>(owner) ? em.GetBuffer<MarkingArea>(owner) : em.AddBuffer<MarkingArea>(owner);
                    var verts = em.HasBuffer<MarkingAreaVertex>(owner) ? em.GetBuffer<MarkingAreaVertex>(owner) : em.AddBuffer<MarkingAreaVertex>(owner);
                    areas.Add(new MarkingArea { styleId = area.styleId, visible = area.visible, firstVertex = verts.Length, vertexCount = tmp.Count });
                    foreach (var v in tmp) verts.Add(v);
                    placedAreas++;
                }
            }

            if (t.hide != 0)
            {
                var o = new MarkingOverride { hide = (MarkingCategory)t.hide };
                if (em.HasComponent<MarkingOverride>(owner)) em.SetComponentData(owner, o);
                else em.AddComponentData(owner, o);
            }
            else if (em.HasComponent<MarkingOverride>(owner)) em.RemoveComponent<MarkingOverride>(owner);

            if (em.HasComponent<MarkingTopologyState>(owner))
                em.SetComponentData(owner, new MarkingTopologyState { linesHash = 0 });
            if (em.HasComponent<MarkingAreaTopologyState>(owner))
                em.SetComponentData(owner, new MarkingAreaTopologyState { combinedHash = 0 });
            em.MarkUpdated(owner);
            return PasteResult.Ok;
        }

        private static Frame MakeFrame(MarkingTemplate t, float3 origin, List<(Entity edge, float angle)> roads, int turn)
        {
            var frame = new Frame { origin = origin, roads = new List<Entity>(roads.Count) };
            if (t.isRoad)
            {
                frame.angle = roads[0].angle + (turn == 1 ? math.PI : 0f);
                frame.roads.Add(roads[0].edge);
                return frame;
            }
            int n = roads.Count;
            frame.angle = roads[turn % n].angle;
            // Template road i lands on road i + turn.
            for (int i = 0; i < n; i++) frame.roads.Add(roads[(i + turn) % n].edge);
            return frame;
        }

        private static void Score(MarkingTemplate t, Frame frame, IReadOnlyList<MarkingEndpoint> endpoints,
                                  IReadOnlyList<MarkingCornerAnchor> corners, out int score, out float err)
        {
            score = 0; err = 0f;
            void Dot(MarkingTemplate.TRef r, ref int s, ref float e)
            {
                if (r.free) return;
                int i = NearestDot(endpoints, frame.roads[r.road], frame.ToWorld(r.pos), out float d);
                if (i >= 0) { s++; e += d; }
            }
            foreach (var l in t.lines) { Dot(l.a, ref score, ref err); Dot(l.b, ref score, ref err); }
            foreach (var v in t.vertices)
            {
                if (v.kind == AreaAnchorKind.LaneEndpoint) Dot(v.at, ref score, ref err);
                else if (v.kind == AreaAnchorKind.NodeCorner && Nearest(corners, frame.ToWorld(v.cornerPos), kCornerMatchRadius) >= 0) score++;
            }
        }

        private static bool Resolve(MarkingTemplate.TRef r, Frame frame, IReadOnlyList<MarkingEndpoint> endpoints,
                                    Dictionary<int, (Entity edge, int gap)> freeRefs, out Entity edge, out int gap)
        {
            edge = Entity.Null; gap = 0;
            if (r.free)
            {
                if (!freeRefs.TryGetValue(r.freeId, out var f)) return false;
                edge = f.edge; gap = f.gap;
                return true;
            }
            if (r.road < 0 || r.road >= frame.roads.Count) return false;
            int i = NearestDot(endpoints, frame.roads[r.road], frame.ToWorld(r.pos), out _);
            if (i < 0) return false;
            edge = endpoints[i].edge; gap = endpoints[i].gapIndex;
            return true;
        }

        private static int NearestDot(IReadOnlyList<MarkingEndpoint> endpoints, Entity road, float3 pos, out float dist)
        {
            int best = -1;
            float bestSq = kDotMatchRadius * kDotMatchRadius;
            for (int i = 0; i < endpoints.Count; i++)
            {
                var ep = endpoints[i];
                if (ep.edge != road) continue;
                if (ep.gapIndex >= MarkingEndpointExtractor.kFreePointGapBase && ep.gapIndex < MarkingEdgeAnchors.kEdgeRowGapBase) continue;
                float sq = math.distancesq(ep.position.xz, pos.xz);
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            dist = math.sqrt(bestSq);
            return best;
        }

        private static int Nearest(IReadOnlyList<MarkingCornerAnchor> corners, float3 pos, float radius)
        {
            int best = -1;
            float bestSq = radius * radius;
            for (int i = 0; i < corners.Count; i++)
            {
                float sq = math.distancesq(corners[i].position.xz, pos.xz);
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        private static void Clear<T>(EntityManager em, Entity e) where T : unmanaged, IBufferElementData
        {
            if (em.HasBuffer<T>(e)) em.GetBuffer<T>(e).Clear();
        }

        // ------------------------------------------------------------------ text format

        // One template per block, whitespace-separated tokens, invariant culture. Kept
        // dependency-free on purpose (no JSON library to resolve at runtime).
        private const string kMagic = "ILMT";
        private const int kFormat = 1;

        public static string Write(IEnumerable<MarkingTemplate> templates)
        {
            var sb = new StringBuilder();
            sb.Append(kMagic).Append(' ').Append(kFormat).Append('\n');
            foreach (var t in templates)
            {
                W(sb, "template", Uri.EscapeDataString(t.name), t.isRoad ? 1 : 0, t.roadCount, t.hide);
                W(sb, "points", t.points.Count);
                foreach (var p in t.points) W(sb, p.id, p.pos.x, p.pos.y, p.pos.z);
                W(sb, "lines", t.lines.Count);
                foreach (var l in t.lines)
                    W(sb, Ref(l.a), Ref(l.b), l.style, l.curvature, l.hasHandles ? 1 : 0,
                      l.handleA.x, l.handleA.y, l.handleA.z, l.handleB.x, l.handleB.y, l.handleB.z);
                W(sb, "segments", t.segments.Count);
                foreach (var s in t.segments) W(sb, s.lineIndex, s.tStart, s.tEnd, s.visible ? 1 : 0, s.style);
                W(sb, "areas", t.areas.Count);
                foreach (var a in t.areas) W(sb, a.styleId, a.visible ? 1 : 0, a.firstVertex, a.vertexCount);
                W(sb, "vertices", t.vertices.Count);
                foreach (var v in t.vertices)
                    W(sb, (int)v.kind, (int)v.edgeToNext, Ref(v.at), v.cornerPos.x, v.cornerPos.y, v.cornerPos.z, v.packed,
                      v.handleA.x, v.handleA.y, v.handleA.z, v.handleB.x, v.handleB.y, v.handleB.z);
                W(sb, "end");
            }
            return sb.ToString();
        }

        private static string Ref(MarkingTemplate.TRef r)
            => string.Join(" ", r.free ? "1" : "0", r.road.ToString(CultureInfo.InvariantCulture), r.freeId.ToString(CultureInfo.InvariantCulture),
                           F(r.pos.x), F(r.pos.y), F(r.pos.z));

        private static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);

        private static void W(StringBuilder sb, params object[] tokens)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(tokens[i] is float f ? F(f) : Convert.ToString(tokens[i], CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
        }

        /// <summary>Reads templates written by <see cref="Write"/>. A damaged block is skipped;
        /// the rest still loads.</summary>
        public static List<MarkingTemplate> Read(string text)
        {
            var result = new List<MarkingTemplate>();
            if (string.IsNullOrEmpty(text)) return result;
            var tok = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            if (tok.Length < 2 || tok[0] != kMagic) return result;
            i = 2;
            while (i < tok.Length)
            {
                if (tok[i] != "template") { i++; continue; }
                int start = i;
                try
                {
                    i++;
                    var t = new MarkingTemplate
                    {
                        name = Uri.UnescapeDataString(tok[i++]),
                        isRoad = I(tok[i++]) != 0,
                        roadCount = I(tok[i++]),
                        hide = I(tok[i++]),
                    };
                    Expect(tok, ref i, "points");
                    for (int n = I(tok[i++]); n > 0; n--)
                        t.points.Add(new MarkingTemplate.TPoint { id = I(tok[i++]), pos = V(tok, ref i) });
                    Expect(tok, ref i, "lines");
                    for (int n = I(tok[i++]); n > 0; n--)
                        t.lines.Add(new MarkingTemplate.TLine
                        {
                            a = R(tok, ref i),
                            b = R(tok, ref i),
                            style = I(tok[i++]),
                            curvature = Fl(tok[i++]),
                            hasHandles = I(tok[i++]) != 0,
                            handleA = V(tok, ref i),
                            handleB = V(tok, ref i),
                        });
                    Expect(tok, ref i, "segments");
                    for (int n = I(tok[i++]); n > 0; n--)
                        t.segments.Add(new MarkingSegment { lineIndex = I(tok[i++]), tStart = Fl(tok[i++]), tEnd = Fl(tok[i++]), visible = I(tok[i++]) != 0, style = I(tok[i++]) });
                    Expect(tok, ref i, "areas");
                    for (int n = I(tok[i++]); n > 0; n--)
                        t.areas.Add(new MarkingArea { styleId = I(tok[i++]), visible = I(tok[i++]) != 0, firstVertex = I(tok[i++]), vertexCount = I(tok[i++]) });
                    Expect(tok, ref i, "vertices");
                    for (int n = I(tok[i++]); n > 0; n--)
                        t.vertices.Add(new MarkingTemplate.TVertex
                        {
                            kind = (AreaAnchorKind)I(tok[i++]),
                            edgeToNext = (AreaEdgeKind)I(tok[i++]),
                            at = R(tok, ref i),
                            cornerPos = V(tok, ref i),
                            packed = I(tok[i++]),
                            handleA = V(tok, ref i),
                            handleB = V(tok, ref i),
                        });
                    Expect(tok, ref i, "end");
                    result.Add(t);
                }
                catch (Exception)
                {
                    i = start + 1;
                }
            }
            return result;
        }

        private static void Expect(string[] tok, ref int i, string word)
        {
            if (tok[i] != word) throw new FormatException(word);
            i++;
        }

        private static int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        private static float Fl(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static float3 V(string[] tok, ref int i) => new float3(Fl(tok[i++]), Fl(tok[i++]), Fl(tok[i++]));

        private static MarkingTemplate.TRef R(string[] tok, ref int i)
            => new MarkingTemplate.TRef { free = I(tok[i++]) != 0, road = I(tok[i++]), freeId = I(tok[i++]), pos = V(tok, ref i) };
    }
}
