using System;
using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;

namespace ILM.Systems.Tool
{
    // Snapping while placing or dragging, like the game's own snap options. Each option can be
    // turned on and off from the panel's snap bar ("All" toggles every option):
    //
    //   Existing geometry: dots, kerb corners and line crossings, and the marking lines.
    //   90 degrees:        from the previous point, along or across the road and the
    //                      previous segment.
    //   Road side:         the kerb lines of the carriageway.
    //   Guide lines:       the lane lines continued straight through every road dot, and lines
    //                      along and across the road through your own points.
    //   Length:            whole half metres from the previous point.
    //
    // The snapped position (_placePos) is what a click places and what a drag commits; the
    // raw cursor stays in _cursorWorldPos for hovering. Two constraints that cross (a guide
    // and the kerb, say) snap to their crossing.
    public partial class MarkingNodeToolSystem
    {
        [Flags]
        public enum SnapFlags
        {
            None = 0,
            Existing = 1,
            Angle = 2,
            RoadSide = 4,
            Guides = 8,
            Length = 16,
            All = Existing | Angle | RoadSide | Guides | Length,
        }

        public enum SnapKind { None, Point, Curve, Guide, Angle, Crossing, Length }

        private const float kSnapPointRadius = 1.0f;
        private const float kSnapCurveRadius = 0.6f;
        private const float kSnapGuideRadius = 0.5f;
        // How close to the projected point a second constraint must cross to snap to the crossing.
        private const float kSnapCrossRadius = 0.9f;
        private const float kSnapAngleTolDeg = 5f;
        private const float kSnapLengthStep = 0.5f;
        // Half-length of the straight polyline used for a guide or angle line.
        private const float kGuideHalfLength = 60f;

        private SnapFlags _snap = SnapFlags.All;
        private float3 _placePos;
        private SnapKind _snapKind;
        // What the snap followed, for the overlay to draw (guide lines, the snapped curve).
        private readonly List<float3[]> _snapGuides = new List<float3[]>(2);

        // Constraint polylines near the cursor, rebuilt per snap.
        private readonly List<float3[]> _snapCons = new List<float3[]>(16);
        private readonly List<bool> _snapConsIsGuide = new List<bool>(16);

        public SnapFlags Snap => _snap;
        /// <summary>Where a click places a point or a drag drops: the cursor, snapped.</summary>
        public float3 PlacePos => _placePos;
        public SnapKind CurrentSnap => _snapKind;
        public IReadOnlyList<float3[]> SnapGuides => _snapGuides;

        private struct SnapContext
        {
            public bool active;
            public bool hasFrom;
            public float3 from;
            public float2 fromDir;     // road direction at the previous point, zero if unknown
            public float2 prevDir;     // direction of the previous segment, zero if none
            public bool allowPoints;   // handles don't snap onto dots or lines
            public bool allowCurves;
            public bool hasExclude;    // the point being dragged: never snap to itself
            public float3 exclude;
            public Entity excludeEdge;
            public int excludeGap;
        }

        /// <summary>Sets the snap options (from the panel) and keeps them in the settings.</summary>
        public void SetSnap(int mask)
        {
            _snap = (SnapFlags)mask & SnapFlags.All;
            if (Mod.Settings != null)
            {
                Mod.Settings.SnapMask = (int)_snap;
                Mod.Settings.Apply();
            }
            log.Debug($"tool: snap → {_snap}");
        }

        private void LoadSnapSetting()
        {
            if (Mod.Settings != null) _snap = (SnapFlags)Mod.Settings.SnapMask & SnapFlags.All;
        }

        /// <summary>Computes _placePos for this frame. Runs after the draw-mode hover and the
        /// area candidate are known, before the open area is refreshed (its drag preview uses
        /// the snapped position).</summary>
        private void UpdateSnap(bool hitSomething)
        {
            _placePos = _cursorWorldPos;
            _snapKind = SnapKind.None;
            _snapGuides.Clear();
            if (!hitSomething || _snap == SnapFlags.None || _selectedNode == Entity.Null) return;
            var ctx = BuildSnapContext();
            if (!ctx.active) return;
            if (_state == MarkingToolState.AreaSelecting || (ctx.allowPoints && _snap.HasFlag(SnapFlags.Existing)))
                RefreshIntersectionAnchorsIfStale();
            _placePos = ComputeSnap(_cursorWorldPos, ctx);
        }

        /// <summary>When snapping applies and what it is relative to.</summary>
        private SnapContext BuildSnapContext()
        {
            var ctx = new SnapContext { allowPoints = true, allowCurves = true };
            var drag = Dragging;
            if (EditMode)
            {
                if (_state != MarkingToolState.NodeSelected || drag == DragKind.None) return ctx;
                ctx.active = true;
                switch (drag)
                {
                    case DragKind.FreePoint:
                        // Along or across the road from where the point was.
                        ctx.hasFrom = true;
                        ctx.from = _dragPoint.position;
                        ctx.fromDir = _dragPoint.tangent;
                        ctx.hasExclude = true;
                        ctx.exclude = _dragPoint.position;
                        ctx.excludeEdge = _dragPoint.edge;
                        ctx.excludeGap = _dragPoint.gapIndex;
                        break;
                    case DragKind.HandleA:
                    case DragKind.HandleB:
                        if (!TryGetEditLineEnds(out var a, out var d)) { ctx.active = false; return ctx; }
                        var end = drag == DragKind.HandleA ? a : d;
                        ctx.hasFrom = true;
                        ctx.from = end.position;
                        ctx.fromDir = end.tangent;
                        ctx.allowPoints = false;
                        ctx.allowCurves = false;
                        break;
                    case DragKind.AreaVertex:
                        int n = _editAreaPos.Count;
                        if (n < 3 || _dragIndex < 0 || _dragIndex >= n) { ctx.active = false; return ctx; }
                        var prev = _editAreaPos[(_dragIndex - 1 + n) % n];
                        var prev2 = _editAreaPos[(_dragIndex - 2 + n) % n];
                        ctx.hasFrom = true;
                        ctx.from = prev;
                        ctx.prevDir = math.normalizesafe(prev.xz - prev2.xz);
                        ctx.hasExclude = true;
                        ctx.exclude = _dragOrigin;
                        break;
                    case DragKind.CrosswalkWidth:
                        // The width rounds to 0.1 m (0.5 m with Length snapping) on its own.
                        ctx.active = false;
                        return ctx;
                    case DragKind.CrosswalkMove:
                        ctx.allowPoints = false;
                        break;
                    case DragKind.AreaHandleA:
                    case DragKind.AreaHandleB:
                        if (_dragIndex < 0 || _dragIndex >= _editAreaEdges.Count) { ctx.active = false; return ctx; }
                        var e = _editAreaEdges[_dragIndex];
                        ctx.hasFrom = true;
                        ctx.from = drag == DragKind.AreaHandleA ? e.a : e.d;
                        ctx.allowPoints = false;
                        ctx.allowCurves = false;
                        break;
                }
                return ctx;
            }

            if (_state == MarkingToolState.AreaSelecting)
            {
                // A hovered candidate is already an exact snap.
                if (_areaHover.IsValid) return ctx;
                ctx.active = true;
                int c = _areaPolygon.Count;
                if (c > 0)
                {
                    ctx.hasFrom = true;
                    ctx.from = _areaPolygon[c - 1].position;
                    if (c > 1) ctx.prevDir = math.normalizesafe(_areaPolygon[c - 1].position.xz - _areaPolygon[c - 2].position.xz);
                }
                return ctx;
            }

            if (CrosswalkDrawing) { ctx.active = true; ctx.allowPoints = false; return ctx; }
            if (!FreeDraw || _hoverIdx >= 0) return ctx;
            if (_state == MarkingToolState.NodeSelected) { ctx.active = true; return ctx; }
            if (_state == MarkingToolState.SourceSelected && _sourceIdx >= 0 && _sourceIdx < _endpoints.Count)
            {
                var src = _endpoints[_sourceIdx];
                ctx.active = true;
                ctx.hasFrom = true;
                ctx.from = src.position;
                ctx.fromDir = src.tangent;
                ctx.prevDir = LastSegmentDirInto(src);
            }
            return ctx;
        }

        private bool TryGetEditLineEnds(out MarkingEndpoint a, out MarkingEndpoint d)
        {
            a = default; d = default;
            if (_editLine < 0 || !EntityManager.HasBuffer<MarkingLine>(_selectedNode)) return false;
            var lines = EntityManager.GetBuffer<MarkingLine>(_selectedNode, isReadOnly: true);
            if (_editLine >= lines.Length) return false;
            return MarkingCurveBuilder.TryFindEndpoints(_endpoints, lines[_editLine], out a, out d);
        }

        /// <summary>Direction of the newest line ending at the endpoint (the free-draw chain's
        /// last segment), zero if none.</summary>
        private float2 LastSegmentDirInto(MarkingEndpoint ep)
        {
            if (!EntityManager.HasBuffer<MarkingLine>(_selectedNode)) return float2.zero;
            var lines = EntityManager.GetBuffer<MarkingLine>(_selectedNode, isReadOnly: true);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var l = lines[i];
                bool endsHere = l.targetEdge == ep.edge && l.targetGapIndex == ep.gapIndex;
                bool startsHere = l.sourceEdge == ep.edge && l.sourceGapIndex == ep.gapIndex;
                if (!endsHere && !startsHere) continue;
                int other = endsHere ? IndexOfEndpoint(l.sourceEdge, l.sourceGapIndex) : IndexOfEndpoint(l.targetEdge, l.targetGapIndex);
                if (other < 0) return float2.zero;
                return math.normalizesafe(ep.position.xz - _endpoints[other].position.xz);
            }
            return float2.zero;
        }

        private bool IsExcluded(in SnapContext ctx, float3 p)
            => ctx.hasExclude && math.distancesq(p.xz, ctx.exclude.xz) < 0.01f;

        private float3 ComputeSnap(float3 cursor, in SnapContext ctx)
        {
            // 1. Existing points win outright.
            if (_snap.HasFlag(SnapFlags.Existing) && ctx.allowPoints && TrySnapToPoint(cursor, ctx, out var pt))
            {
                _snapKind = SnapKind.Point;
                return pt;
            }

            // 2. Constraints near the cursor.
            _snapCons.Clear();
            _snapConsIsGuide.Clear();
            if (_snap.HasFlag(SnapFlags.Guides)) CollectGuides(cursor, ctx);
            if (_snap.HasFlag(SnapFlags.RoadSide) && ctx.allowCurves) CollectKerbs(cursor);
            if (_snap.HasFlag(SnapFlags.Existing) && ctx.allowCurves) CollectLines(cursor, ctx);

            // 3. An angle line from the previous point takes precedence and is intersected with
            //    the other constraints.
            if (_snap.HasFlag(SnapFlags.Angle) && ctx.hasFrom && TryAngleLine(cursor, ctx, out var angleLine))
            {
                var onAngle = NearestOnPolyline(angleLine, cursor, out _);
                if (TryCrossing(angleLine, onAngle, out var cross, out int other))
                {
                    _snapKind = SnapKind.Crossing;
                    _snapGuides.Add(new[] { ctx.from, cross });
                    _snapGuides.Add(_snapCons[other]);
                    return cross;
                }
                if (_snap.HasFlag(SnapFlags.Length)) onAngle = SnapLength(ctx.from, onAngle);
                _snapKind = SnapKind.Angle;
                _snapGuides.Add(new[] { ctx.from, onAngle });
                return onAngle;
            }

            // 4. The nearest constraint, or the crossing of two.
            int best = -1;
            float bestSq = float.MaxValue;
            float3 bestPos = cursor;
            for (int i = 0; i < _snapCons.Count; i++)
            {
                var q = NearestOnPolyline(_snapCons[i], cursor, out float sq);
                if (sq < bestSq) { bestSq = sq; best = i; bestPos = q; }
            }
            if (best >= 0)
            {
                var first = _snapCons[best];
                bool firstIsGuide = _snapConsIsGuide[best];
                if (TryCrossing(first, bestPos, out var cross, out int other, skip: best))
                {
                    _snapKind = SnapKind.Crossing;
                    _snapGuides.Add(first);
                    _snapGuides.Add(_snapCons[other]);
                    return cross;
                }
                _snapKind = firstIsGuide ? SnapKind.Guide : SnapKind.Curve;
                _snapGuides.Add(first);
                return bestPos;
            }

            // 5. Only a length step from the previous point.
            if (_snap.HasFlag(SnapFlags.Length) && ctx.hasFrom)
            {
                var p = SnapLength(ctx.from, cursor);
                if (math.distancesq(p.xz, ctx.from.xz) > 0.01f)
                {
                    _snapKind = SnapKind.Length;
                    _snapGuides.Add(new[] { ctx.from, p });
                    return p;
                }
            }
            return cursor;
        }

        private bool TrySnapToPoint(float3 cursor, in SnapContext ctx, out float3 pos)
        {
            pos = cursor;
            float best = kSnapPointRadius * kSnapPointRadius;
            bool found = false;
            void Try(float3 p, ref float b, ref float3 bp, ref bool f, in SnapContext c)
            {
                if (IsExcluded(c, p)) return;
                float sq = PolygonUtils.DistSqXZ(p, cursor);
                if (sq < b) { b = sq; bp = p; f = true; }
            }
            for (int i = 0; i < _endpoints.Count; i++)
            {
                var ep = _endpoints[i];
                if (ctx.hasExclude && ep.edge == ctx.excludeEdge && ep.gapIndex == ctx.excludeGap) continue;
                Try(ep.position, ref best, ref pos, ref found, ctx);
            }
            for (int i = 0; i < _cornerAnchors.Count; i++) Try(_cornerAnchors[i].position, ref best, ref pos, ref found, ctx);
            for (int i = 0; i < _lineIntersections.Count; i++) Try(_lineIntersections[i].position, ref best, ref pos, ref found, ctx);
            return found;
        }

        /// <summary>Guides: the lane line through every road dot continued straight, and lines
        /// along and across the road through free points.</summary>
        private void CollectGuides(float3 cursor, in SnapContext ctx)
        {
            float r2 = kSnapGuideRadius * kSnapGuideRadius;
            for (int i = 0; i < _endpoints.Count; i++)
            {
                var ep = _endpoints[i];
                if (math.lengthsq(ep.tangent) < 1e-4f) continue;
                if (ctx.hasExclude && ep.edge == ctx.excludeEdge && ep.gapIndex == ctx.excludeGap) continue;
                var dir = math.normalize(ep.tangent);
                AddGuideIfNear(ep.position, dir, cursor, r2);
                if (IsFree(ep)) AddGuideIfNear(ep.position, new float2(-dir.y, dir.x), cursor, r2);
            }
        }

        private void AddGuideIfNear(float3 origin, float2 dir, float3 cursor, float r2)
        {
            float2 v = cursor.xz - origin.xz;
            float along = math.dot(v, dir);
            float2 perp = v - dir * along;
            if (math.lengthsq(perp) > r2) return;
            // Skip duplicates: setback rows share their lane line.
            for (int i = 0; i < _snapCons.Count; i++)
            {
                if (!_snapConsIsGuide[i]) continue;
                var g = _snapCons[i];
                float2 gd = math.normalizesafe(g[1].xz - g[0].xz);
                if (math.abs(math.dot(gd, dir)) > 0.9995f && DistToInfiniteLineSq(g[0].xz, gd, origin.xz) < 0.0025f) return;
            }
            var o = new float3(origin.x, cursor.y, origin.z);
            var d3 = new float3(dir.x, 0f, dir.y);
            _snapCons.Add(new[] { o - d3 * kGuideHalfLength, o + d3 * kGuideHalfLength });
            _snapConsIsGuide.Add(true);
        }

        private static float DistToInfiniteLineSq(float2 origin, float2 dir, float2 p)
        {
            float2 v = p - origin;
            return math.lengthsq(v - dir * math.dot(v, dir));
        }

        /// <summary>The carriageway kerb curves of the selection: both sides of every road at
        /// a junction, or of the road stretch itself.</summary>
        private void CollectKerbs(float3 cursor)
        {
            if (MarkingEdgeAnchors.IsEdgeOwner(EntityManager, _selectedNode))
            {
                if (!EntityManager.HasComponent<EdgeGeometry>(_selectedNode)) return;
                var g = EntityManager.GetComponentData<EdgeGeometry>(_selectedNode);
                AddCurveIfNear(g.m_Start.m_Left, cursor);
                AddCurveIfNear(g.m_Start.m_Right, cursor);
                AddCurveIfNear(g.m_End.m_Left, cursor);
                AddCurveIfNear(g.m_End.m_Right, cursor);
                return;
            }
            if (!EntityManager.HasBuffer<ConnectedEdge>(_selectedNode)) return;
            var connected = EntityManager.GetBuffer<ConnectedEdge>(_selectedNode, isReadOnly: true);
            for (int i = 0; i < connected.Length; i++)
            {
                if (!MarkingFreePointGeometry.TryGetCapCurves(EntityManager, _selectedNode, connected[i].m_Edge, out var l, out var r, out _)) continue;
                AddCurveIfNear(l, cursor);
                AddCurveIfNear(r, cursor);
            }
        }

        /// <summary>The marking lines (not those running through the dragged point).</summary>
        private void CollectLines(float3 cursor, in SnapContext ctx)
        {
            if (!EntityManager.HasBuffer<MarkingLine>(_selectedNode)) return;
            var lines = EntityManager.GetBuffer<MarkingLine>(_selectedNode, isReadOnly: true);
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (ctx.hasExclude
                    && ((l.sourceEdge == ctx.excludeEdge && l.sourceGapIndex == ctx.excludeGap)
                        || (l.targetEdge == ctx.excludeEdge && l.targetGapIndex == ctx.excludeGap))) continue;
                if (!MarkingCurveBuilder.TryBuild(_endpoints, l, out var bez)) continue;
                AddCurveIfNear(bez, cursor);
            }
        }

        private void AddCurveIfNear(Bezier4x3 bez, float3 cursor)
        {
            // Cheap reject on the control-point bounds first.
            float2 min = math.min(math.min(bez.a.xz, bez.b.xz), math.min(bez.c.xz, bez.d.xz)) - kSnapCurveRadius;
            float2 max = math.max(math.max(bez.a.xz, bez.b.xz), math.max(bez.c.xz, bez.d.xz)) + kSnapCurveRadius;
            if (math.any(cursor.xz < min) || math.any(cursor.xz > max)) return;
            var poly = SamplePolyline(bez);
            NearestOnPolyline(poly, cursor, out float sq);
            if (sq > kSnapCurveRadius * kSnapCurveRadius) return;
            _snapCons.Add(poly);
            _snapConsIsGuide.Add(false);
        }

        private static float3[] SamplePolyline(Bezier4x3 bez)
        {
            float approx = math.distance(bez.a.xz, bez.b.xz) + math.distance(bez.b.xz, bez.c.xz) + math.distance(bez.c.xz, bez.d.xz);
            int n = math.clamp((int)(approx * 2f), 8, 160);
            var pts = new float3[n + 1];
            for (int s = 0; s <= n; s++) pts[s] = MathUtils.Position(bez, (float)s / n);
            return pts;
        }

        /// <summary>The 90-degree direction from the previous point closest to the cursor:
        /// along or across the road there and the previous segment. False when none is within
        /// the tolerance.</summary>
        private bool TryAngleLine(float3 cursor, in SnapContext ctx, out float3[] line)
        {
            line = null;
            float2 v = cursor.xz - ctx.from.xz;
            if (math.lengthsq(v) < 0.25f) return false;
            float2 vn = math.normalize(v);
            float2 road = math.lengthsq(ctx.fromDir) > 1e-4f ? math.normalize(ctx.fromDir) : NearestRoadDir(ctx.from);
            float cosTol = math.cos(math.radians(kSnapAngleTolDeg));
            float bestCos = cosTol;
            float2 bestDir = float2.zero;
            void Test(float2 d, ref float bc, ref float2 bd)
            {
                if (math.lengthsq(d) < 1e-4f) return;
                d = math.normalize(d);
                float2 perp = new float2(-d.y, d.x);
                foreach (var cand in new[] { d, -d, perp, -perp })
                {
                    float cosA = math.dot(vn, cand);
                    if (cosA > bc) { bc = cosA; bd = cand; }
                }
            }
            Test(road, ref bestCos, ref bestDir);
            Test(ctx.prevDir, ref bestCos, ref bestDir);
            if (math.lengthsq(bestDir) < 1e-4f) return false;
            var o = ctx.from;
            var d3 = new float3(bestDir.x, 0f, bestDir.y);
            line = new[] { new float3(o.x, cursor.y, o.z), new float3(o.x, cursor.y, o.z) + d3 * kGuideHalfLength };
            return true;
        }

        /// <summary>Road direction at a spot: the tangent of the nearest road dot.</summary>
        private float2 NearestRoadDir(float3 p)
        {
            float best = float.MaxValue;
            float2 dir = float2.zero;
            for (int i = 0; i < _endpoints.Count; i++)
            {
                if (math.lengthsq(_endpoints[i].tangent) < 1e-4f) continue;
                float sq = PolygonUtils.DistSqXZ(_endpoints[i].position, p);
                if (sq < best) { best = sq; dir = _endpoints[i].tangent; }
            }
            return dir;
        }

        private static float3 SnapLength(float3 from, float3 p)
        {
            float2 v = p.xz - from.xz;
            float len = math.length(v);
            if (len < 1e-3f) return p;
            float snapped = math.max(kSnapLengthStep, math.round(len / kSnapLengthStep) * kSnapLengthStep);
            float2 xz = from.xz + v / len * snapped;
            return new float3(xz.x, p.y, xz.y);
        }

        /// <summary>The crossing of <paramref name="poly"/> with another constraint nearest to
        /// <paramref name="near"/>, within <see cref="kSnapCrossRadius"/>.</summary>
        private bool TryCrossing(float3[] poly, float3 near, out float3 cross, out int other, int skip = -1)
        {
            cross = near;
            other = -1;
            float best = kSnapCrossRadius * kSnapCrossRadius;
            for (int k = 0; k < _snapCons.Count; k++)
            {
                if (k == skip || ReferenceEquals(_snapCons[k], poly)) continue;
                var q = _snapCons[k];
                for (int i = 1; i < poly.Length; i++)
                {
                    for (int j = 1; j < q.Length; j++)
                    {
                        if (!SegmentCross(poly[i - 1], poly[i], q[j - 1], q[j], out var x)) continue;
                        float sq = math.distancesq(x.xz, near.xz);
                        if (sq < best) { best = sq; cross = x; other = k; }
                    }
                }
            }
            cross.y = near.y;
            return other >= 0;
        }

        private static bool SegmentCross(float3 a0, float3 a1, float3 b0, float3 b1, out float3 x)
        {
            x = default;
            float2 r = a1.xz - a0.xz, s = b1.xz - b0.xz;
            float den = r.x * s.y - r.y * s.x;
            if (math.abs(den) < 1e-6f) return false;
            float2 qp = b0.xz - a0.xz;
            float t = (qp.x * s.y - qp.y * s.x) / den;
            float u = (qp.x * r.y - qp.y * r.x) / den;
            if (t < 0f || t > 1f || u < 0f || u > 1f) return false;
            x = math.lerp(a0, a1, t);
            return true;
        }

        private static float3 NearestOnPolyline(float3[] poly, float3 p, out float distSq)
        {
            distSq = float.MaxValue;
            float3 best = poly[0];
            for (int i = 1; i < poly.Length; i++)
            {
                float2 a = poly[i - 1].xz, seg = poly[i].xz - a;
                float len = math.lengthsq(seg);
                float t = len > 1e-8f ? math.saturate(math.dot(p.xz - a, seg) / len) : 0f;
                float2 q = a + seg * t;
                float sq = math.lengthsq(q - p.xz);
                if (sq < distSq)
                {
                    distSq = sq;
                    best = new float3(q.x, math.lerp(poly[i - 1].y, poly[i].y, t), q.y);
                }
            }
            // Keep the cursor's height: the raycast hit is on the road surface.
            best.y = p.y;
            return best;
        }
    }
}
