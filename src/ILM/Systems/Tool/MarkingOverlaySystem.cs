using System.Collections.Generic;
using Colossal.Mathematics;
using Game;
using Game.Net;
using Game.Rendering;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using ILM.Components;
using ILM.Geometry;
using ILM.Systems.UI;
using static ILM.Systems.Tool.OverlayPalette;
using MathUtils = Colossal.Mathematics.MathUtils;

namespace ILM.Systems.Tool
{
    /// <summary>
    /// Overlay of the marking tool: node rings, endpoint and corner dots, crossing markers,
    /// hover and hidden-segment traces, the drag preview, and the area-mode contour. Idle unless
    /// <see cref="MarkingNodeToolSystem"/> is the active tool. Everything is drawn through the
    /// vanilla <see cref="OverlayRenderSystem"/>; colours and sizes live in
    /// <see cref="OverlayPalette"/>.
    ///
    /// Overlay design: the user is here to judge the painted markings, so anything drawn on top
    /// competes with them. Visible committed lines get no overlay curve; hidden segments get a
    /// thin red ghost (otherwise there is no way to see them); hover gets a thin cyan trace.
    /// </summary>
    public partial class MarkingOverlaySystem : GameSystemBase
    {
        // Height above the terrain from which a marker counts as being on an elevated deck.
        private const float kElevatedMinHeight = 0.75f;
        // Minimum squared distance for a preview line to the cursor to be worth drawing.
        private const float kMinPreviewLengthSq = 0.01f;

        private ToolSystem _toolSystem;
        private MarkingNodeToolSystem _tool;
        private OverlayRenderSystem _overlayRenderSystem;
        private ILMUISystem _uiSystem;
        private TerrainSystem _terrainSystem;
        private EntityQuery _nodesWithPairsQuery;
        // Refreshed every update; DotStyle uses it to choose projected or absolute drawing.
        private TerrainHeightData _heightData;

        // Per-frame scratch buffers, reused to avoid allocations.
        private readonly List<float3> _areaContourScratch = new List<float3>();
        // (edge, gapIndex) of every dot that anchors a MarkingLine on the selected node.
        private readonly HashSet<(Entity, int)> _connectedScratch = new HashSet<(Entity, int)>();

        protected override void OnCreate()
        {
            base.OnCreate();
            _toolSystem = World.GetOrCreateSystemManaged<ToolSystem>();
            _tool = World.GetOrCreateSystemManaged<MarkingNodeToolSystem>();
            _overlayRenderSystem = World.GetOrCreateSystemManaged<OverlayRenderSystem>();
            _uiSystem = World.GetOrCreateSystemManaged<ILMUISystem>();
            _terrainSystem = World.GetOrCreateSystemManaged<TerrainSystem>();
            // Nodes with a MarkingLine buffer, for the "has custom markings" ring. Few nodes
            // carry the buffer, so the query stays cheap.
            _nodesWithPairsQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<MarkingLine>() },
                Any = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<Edge>() },
            });
        }

        protected override void OnUpdate()
        {
            if (_toolSystem.activeTool != _tool) return;

            _heightData = _terrainSystem.GetHeightData();
            var buf = _overlayRenderSystem.GetBuffer(out JobHandle deps);
            // Drawing happens on the main thread, so jobs still writing to the buffer finish first.
            deps.Complete();
            Draw(buf);
            _overlayRenderSystem.AddBufferWriter(Dependency);
        }

        /// <summary>Style for a dot or ring at the given position. At ground level it is
        /// <see cref="OverlayRenderSystem.StyleFlags.Projected"/>, so the marker follows terrain on
        /// slopes. On elevated decks (bridges, ramps) projection would drop the marker to the
        /// ground below, so those draw at the true 3D position.</summary>
        private OverlayRenderSystem.StyleFlags DotStyle(float3 pos)
        {
            float ground = TerrainUtils.SampleHeight(ref _heightData, pos);
            return pos.y - ground > kElevatedMinHeight
                ? (OverlayRenderSystem.StyleFlags)0
                : OverlayRenderSystem.StyleFlags.Projected;
        }

        private void Draw(OverlayRenderSystem.Buffer buf)
        {
            DrawHasPairsRings(buf, _tool.SelectedNode);
            DrawSnap(buf);

            // Only before a node is selected; after that the dots show where the user is.
            if (_tool.ToolState == MarkingToolState.Default && _tool.HoveredNode != Entity.Null)
            {
                DrawNodeRing(buf, _tool.HoveredNode, kColNodeHoverRing, kNodeHoverDiameter, kNodeHoverOutlineWidth);
            }

            // Area mode replaces the line overlay. Checked before the early return on empty
            // endpoints so an area built only from corner anchors still draws.
            if (_tool.ToolState == MarkingToolState.AreaSelecting)
            {
                DrawAreaModeOverlay(buf);
                return;
            }

            var endpoints = _tool.Endpoints;
            if (endpoints == null || endpoints.Count == 0)
                return;

            var node = _tool.SelectedNode;

            // Hovered area: UI hover wins over the cursor inside an area, as for lines.
            int hoveredArea = _uiSystem.UIHoveredAreaIndex;
            if (hoveredArea < 0) hoveredArea = _tool.HoveredAreaInGame;
            if (hoveredArea >= 0 && node != Entity.Null)
                DrawHoveredAreaOutline(buf, node, hoveredArea);

            // Edit mode shows only what can be edited: the lines, the open line's handles and
            // the free points. Anchor dots and corners can't be edited, so they are left out.

            // Each panel tab shows only its own kind, so nothing else can be mistaken for a
            // target: lines (Lines tab), areas (Areas tab) or free points (Points tab).
            if (_tool.EditMode)
            {
                switch (_tool.CurrentEditTarget)
                {
                    case MarkingNodeToolSystem.EditTarget.Lines:
                        DrawEditableLines(buf, node, endpoints);
                        DrawCommittedLines(buf, node, endpoints);
                        DrawEditHandles(buf);
                        DrawEditFreePoints(buf, endpoints);
                        break;
                    case MarkingNodeToolSystem.EditTarget.Areas:
                        DrawEditableAreas(buf, node);
                        DrawOpenArea(buf);
                        break;
                    default:
                        DrawEditableLines(buf, node, endpoints, faint: true);
                        DrawEditFreePoints(buf, endpoints);
                        break;
                }
                return;
            }

            DrawCommittedLines(buf, node, endpoints);
            DrawDragPreview(buf, endpoints);
            DrawEndpointDots(buf, node, endpoints);

            // Free draw: a ghost dot under the cursor shows where a click would place a point.
            if (_tool.FreeDraw && _tool.HoveredEndpointIndex < 0
                && (_tool.ToolState == MarkingToolState.NodeSelected || _tool.ToolState == MarkingToolState.SourceSelected)
                && math.lengthsq(_tool.CursorWorldPos) > 0f)
            {
                DrawDot(buf, _tool.PlacePos, kDotDiameter, WithAlpha(kColFreePoint, 0.25f), kColFreePoint, kDotOutlineWidth);
            }

            // Corner anchors. Not clickable in line mode (only area mode uses them), so they are
            // drawn smaller and with less contrast than lane endpoints.
            var corners = _tool.CornerAnchors;
            if (corners != null)
            {
                for (int i = 0; i < corners.Count; i++)
                    DrawDot(buf, corners[i].position, kCornerDotDiameter, kColCornerFill, kColCornerOutline, kCornerDotOutlineWidth);
            }
        }

        /// <summary>
        /// Committed lines and crossing markers (bottom layer). Per segment:
        ///   visible, not hovered: nothing (the road paint shows it)
        ///   visible, hovered:     thin cyan trace
        ///   hidden, not hovered:  thin red ghost
        ///   hidden, hovered:      brighter red ghost
        /// Crossing markers are drawn at inner segment boundaries regardless of hover.
        ///
        /// Hover sources: a segment hovered in the UI popover lights only that segment; a line
        /// hovered in the UI panel (or, failing that, under the cursor in the world) lights all of
        /// its segments.
        /// </summary>
        private void DrawCommittedLines(OverlayRenderSystem.Buffer buf, Entity node, IReadOnlyList<MarkingEndpoint> endpoints)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<MarkingLine>(node) || !EntityManager.HasBuffer<MarkingSegment>(node))
                return;

            int hoveredLine = _uiSystem.UIHoveredLineIndex;
            if (hoveredLine < 0) hoveredLine = _tool.HoveredLineInGame;
            int hoveredSegLine = _uiSystem.UIHoveredSegmentLineIndex;
            int hoveredSegIdx = _uiSystem.UIHoveredSegmentIndex;

            var lines = EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true);
            var segs = EntityManager.GetBuffer<MarkingSegment>(node, isReadOnly: true);
            for (int l = 0; l < lines.Length; l++)
            {
                if (!MarkingCurveBuilder.TryBuild(endpoints, lines[l], out var full)) continue;
                bool isLineHighlighted = l == hoveredLine;
                // The UI numbers segments 0..K-1 within each line, not by buffer index.
                int perLineCounter = -1;
                for (int s = 0; s < segs.Length; s++)
                {
                    var seg = segs[s];
                    if (seg.lineIndex != l) continue;
                    perLineCounter++;
                    bool isThisSegmentHovered = l == hoveredSegLine && perLineCounter == hoveredSegIdx;

                    if (isLineHighlighted || isThisSegmentHovered)
                    {
                        Color color = seg.visible ? kColHighlightedCurve : kColHighlightedHidden;
                        float width = isThisSegmentHovered ? kHoveredSegmentCurveWidth : kHighlightedPairCurveWidth;
                        DrawSegmentCurve(buf, full, seg, color, width);
                    }
                    else if (!seg.visible)
                    {
                        DrawSegmentCurve(buf, full, seg, kColHiddenSegment, kHiddenSegmentWidth);
                    }

                    if (IsInnerBoundary(seg.tStart))
                        DrawIntersectionMarker(buf, MathUtils.Position(full, seg.tStart));
                    if (IsInnerBoundary(seg.tEnd))
                        DrawIntersectionMarker(buf, MathUtils.Position(full, seg.tEnd));
                }
            }
        }

        private static bool IsInnerBoundary(float t) => t > 0.001f && t < 0.999f;

        private static void DrawSegmentCurve(OverlayRenderSystem.Buffer buf, Bezier4x3 line, MarkingSegment seg, Color color, float width)
            => buf.DrawCurve(color, MathUtils.Cut(line, new float2(seg.tStart, seg.tEnd)), width);

        /// <summary>Drag preview from the source dot to the hovered dot, or to the cursor.</summary>
        private void DrawDragPreview(OverlayRenderSystem.Buffer buf, IReadOnlyList<MarkingEndpoint> endpoints)
        {
            int sourceIdx = _tool.SourceEndpointIndex;
            int hoverIdx = _tool.HoveredEndpointIndex;
            if (sourceIdx < 0 || sourceIdx >= endpoints.Count) return;

            var src = endpoints[sourceIdx];
            if (hoverIdx >= 0 && hoverIdx < endpoints.Count && hoverIdx != sourceIdx)
            {
                var dst = endpoints[hoverIdx];
                // Lines touching a free point start straight (see TogglePair), so preview them so.
                bool straight = MarkingEndpointExtractor.KindOf(src.gapIndex) == MarkingEndpointExtractor.AnchorKind.Free
                    || MarkingEndpointExtractor.KindOf(dst.gapIndex) == MarkingEndpointExtractor.AnchorKind.Free;
                if (straight)
                    buf.DrawLine(kColPreviewCurve, new Line3.Segment(src.position, dst.position), kPreviewCurveWidth);
                else
                    buf.DrawCurve(kColPreviewCurve, BuildSmoothCurve(src.position, src.tangent, dst.position, dst.tangent), kPreviewCurveWidth);
            }
            else
            {
                // No dot hovered: straight line to the cursor's terrain hit.
                float3 to = _tool.PlacePos;
                if (math.lengthsq(to - src.position) > kMinPreviewLengthSq)
                    buf.DrawLine(kColPreviewCurve, new Line3.Segment(src.position, to), kPreviewCurveWidth);
            }
        }

        /// <summary>Drag-preview curve. Uses <see cref="MarkingCurveBuilder"/> with the same
        /// starting pull factor a new line gets, so the preview matches the committed line.</summary>
        private static Bezier4x3 BuildSmoothCurve(float3 a, float2 ta, float3 b, float2 tb)
            => MarkingCurveBuilder.Build(a, ta, b, tb, MarkingCurveBuilder.AdaptivePullFactor(a, ta, b, tb));

        /// <summary>Endpoint dots: free ones as hollow rings in the edge colour, connected ones
        /// filled with a white core, the source dot solid white inside a faint halo, the hovered
        /// dot solid and slightly larger.</summary>
        private void DrawEndpointDots(OverlayRenderSystem.Buffer buf, Entity node, IReadOnlyList<MarkingEndpoint> endpoints)
        {
            int sourceIdx = _tool.SourceEndpointIndex;
            int hoverIdx = _tool.HoveredEndpointIndex;

            _connectedScratch.Clear();
            if (node != Entity.Null && EntityManager.HasBuffer<MarkingLine>(node))
            {
                var lines = EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true);
                for (int l = 0; l < lines.Length; l++)
                {
                    _connectedScratch.Add((lines[l].sourceEdge, lines[l].sourceGapIndex));
                    _connectedScratch.Add((lines[l].targetEdge, lines[l].targetGapIndex));
                }
            }

            for (int i = 0; i < endpoints.Count; i++)
            {
                var ep = endpoints[i];
                var kind = MarkingEndpointExtractor.KindOf(ep.gapIndex);
                Color edgeColor = kind == MarkingEndpointExtractor.AnchorKind.Free ? kColFreePoint : EdgeDotColor(ep.edge);
                bool connected = _connectedScratch.Contains((ep.edge, ep.gapIndex));
                Color fill;
                Color outline;
                bool small = kind == MarkingEndpointExtractor.AnchorKind.LaneCentre
                    || kind == MarkingEndpointExtractor.AnchorKind.ExtraSetback;
                float diameter = small ? kExtraDotDiameter : kDotDiameter;
                float outlineWidth = small ? kExtraDotOutlineWidth : kDotOutlineWidth;

                if (i == sourceIdx)
                {
                    fill = kColDotFillSource;
                    outline = kColDotOutlineSrc;
                    // Halo, drawn first so the dot sits on top.
                    DrawDot(buf, ep.position, kSourceHaloDiameter, kColTransparent, kColSourceHalo, kSourceHaloOutlineWidth);
                }
                else if (i == hoverIdx)
                {
                    fill = WithAlpha(edgeColor, 1.00f);
                    outline = kColDotOutline;
                    diameter = kDotDiameterHover;
                    outlineWidth = kDotOutlineWidthHover;
                }
                else if (connected)
                {
                    fill = edgeColor;
                    outline = kColDotOutline;
                }
                else
                {
                    // Hollow ring; the faint fill keeps it readable on dark asphalt.
                    fill = WithAlpha(edgeColor, kDotFreeFillAlpha);
                    outline = WithAlpha(edgeColor, kDotFreeOutlineAlpha);
                }

                DrawDot(buf, ep.position, diameter, fill, outline, outlineWidth);

                if (connected && i != sourceIdx)
                    DrawDot(buf, ep.position, kDotConnectedCoreDiameter, kColDotConnectedCore, kColTransparent, 0f);
            }
        }

        private static Color WithAlpha(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

        /// <summary>Edit mode: a faint trace on every line so the user sees what can be clicked,
        /// and a solid one on the open line. Hover highlights come on top from
        /// <see cref="DrawCommittedLines"/>.</summary>
        private void DrawEditableLines(OverlayRenderSystem.Buffer buf, Entity node, IReadOnlyList<MarkingEndpoint> endpoints, bool faint = false)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<MarkingLine>(node)) return;
            var lines = EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true);
            int open = faint ? -1 : _tool.EditLine;
            var color = faint ? WithAlpha(kColEditableLine, kColEditableLine.a * 0.5f) : kColEditableLine;
            for (int l = 0; l < lines.Length; l++)
            {
                if (!MarkingCurveBuilder.TryBuild(endpoints, lines[l], out var curve)) continue;
                if (l == open) buf.DrawCurve(kColEditSelectedLine, curve, kEditSelectedLineWidth);
                else buf.DrawCurve(color, curve, kEditableLineWidth);
            }
        }

        /// <summary>Edit mode: a faint outline on every area so it reads as clickable. The open
        /// area is drawn on top by <see cref="DrawOpenArea"/>.</summary>
        private void DrawEditableAreas(OverlayRenderSystem.Buffer buf, Entity node)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<MarkingAreaPiece>(node)
                || !EntityManager.HasBuffer<MarkingAreaPieceVertex>(node)) return;
            var pieces = EntityManager.GetBuffer<MarkingAreaPiece>(node, isReadOnly: true);
            var verts = EntityManager.GetBuffer<MarkingAreaPieceVertex>(node, isReadOnly: true);
            for (int p = 0; p < pieces.Length; p++)
            {
                var pd = pieces[p];
                if (pd.areaIndex == _tool.EditArea || pd.vertexCount < 3) continue;
                for (int v = 0; v < pd.vertexCount; v++)
                {
                    int i0 = pd.firstVertex + v;
                    int i1 = pd.firstVertex + (v + 1) % pd.vertexCount;
                    if (i0 >= verts.Length || i1 >= verts.Length) break;
                    buf.DrawLine(kColEditableLine, new Line3.Segment(verts[i0].position, verts[i1].position), kEditableLineWidth);
                }
            }
        }

        /// <summary>The area open in edit mode: its outline, a stem and grab dot for both handles
        /// of every edge, a small "+" marker in the middle of each edge, and the vertices on
        /// top (white for free points, yellow for vertices sitting on a road dot).</summary>
        private void DrawOpenArea(OverlayRenderSystem.Buffer buf)
        {
            var pos = _tool.EditAreaPositions;
            var edges = _tool.EditAreaEdges;
            var verts = _tool.EditAreaVertices;
            if (pos.Count < 3 || edges.Count != pos.Count) return;

            var drag = _tool.Dragging;
            for (int i = 0; i < edges.Count; i++)
                buf.DrawCurve(kColEditSelectedLine, edges[i], kEditSelectedLineWidth);

            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                buf.DrawLine(kColHandleStem, new Line3.Segment(e.a, e.b), kHandleStemWidth);
                buf.DrawLine(kColHandleStem, new Line3.Segment(e.d, e.c), kHandleStemWidth);
                DrawHandleDot(buf, e.b, kAreaHandleDotDiameter, IsHover(MarkingNodeToolSystem.EditHoverKind.AreaHandleA, i));
                DrawHandleDot(buf, e.c, kAreaHandleDotDiameter, IsHover(MarkingNodeToolSystem.EditHoverKind.AreaHandleB, i));
                if (drag != MarkingNodeToolSystem.DragKind.None) continue;
                bool hovered = i == _tool.HoveredAreaInsert;
                DrawDot(buf, MathUtils.Position(e, 0.5f), hovered ? kInsertDotDiameterHover : kInsertDotDiameter,
                    hovered ? kColInsertFill : WithAlpha(kColInsertFill, 0.55f), kColDotOutline, kDotOutlineWidth);
            }

            for (int i = 0; i < pos.Count; i++)
            {
                bool free = verts[i].kind == AreaAnchorKind.LaneEndpoint
                    && MarkingEndpointExtractor.KindOf(verts[i].refGap) == MarkingEndpointExtractor.AnchorKind.Free;
                Color fill = free ? kColFreePoint : kColAreaCandRing;
                bool big = i == _tool.HoveredAreaVertex
                    || (drag == MarkingNodeToolSystem.DragKind.AreaVertex && i == _tool.DragIndex);
                DrawDot(buf, pos[i], big ? kDotDiameterHover * 1.1f : kEditFreePointDiameter, fill, kColDotOutline,
                    big ? kDotOutlineWidthHover : kDotOutlineWidth);
            }
        }

        /// <summary>Edit mode: free points are the only dots, since they are the only ones that
        /// can be moved. The hovered one is drawn larger; the one being dragged stays faint at
        /// its old spot with a solid copy under the cursor.</summary>
        private void DrawEditFreePoints(OverlayRenderSystem.Buffer buf, IReadOnlyList<MarkingEndpoint> endpoints)
        {
            int hoverIdx = _tool.HoveredEndpointIndex;
            int uiHoverGap = _uiSystem.UIHoveredPointId >= 0 ? MarkingEndpointExtractor.kFreePointGapBase + _uiSystem.UIHoveredPointId : -1;
            bool draggingPoint = _tool.Dragging == MarkingNodeToolSystem.DragKind.FreePoint;
            for (int i = 0; i < endpoints.Count; i++)
            {
                var ep = endpoints[i];
                // Only points the current tab can edit (area corners belong to area editing).
                if (!_tool.IsEditablePoint(ep)) continue;
                if (_tool.IsDraggedPoint(ep))
                    DrawDot(buf, ep.position, kEditFreePointDiameter, WithAlpha(kColFreePoint, 0.25f), WithAlpha(kColFreePoint, 0.5f), kDotOutlineWidth);
                else if (ep.gapIndex == uiHoverGap && !draggingPoint)
                {
                    // Row hovered on the Points tab: a halo makes it easy to find on the road.
                    DrawDot(buf, ep.position, kSourceHaloDiameter, kColTransparent, kColHighlightedCurve, kSourceHaloOutlineWidth);
                    DrawDot(buf, ep.position, kDotDiameterHover * 1.1f, kColFreePoint, kColDotOutline, kDotOutlineWidthHover);
                }
                else if (i == hoverIdx && !draggingPoint)
                    DrawDot(buf, ep.position, kDotDiameterHover * 1.1f, kColFreePoint, kColDotOutline, kDotOutlineWidthHover);
                else
                    DrawDot(buf, ep.position, kEditFreePointDiameter, kColFreePoint, kColDotOutline, kDotOutlineWidth);
            }
            if (draggingPoint)
                DrawDot(buf, _tool.PlacePos, kDotDiameterHover * 1.1f, kColFreePoint, kColDotOutline, kDotOutlineWidthHover);
        }

        /// <summary>Curve handles of the open line: a stem from each end to its control point
        /// and a grab dot there. While a handle is dragged the reshaped curve is previewed.</summary>
        private void DrawEditHandles(OverlayRenderSystem.Buffer buf)
        {
            if (_tool.ToolState != MarkingToolState.NodeSelected) return;
            if (!_tool.TryGetEditCurve(out var c)) return;
            var drag = _tool.Dragging;
            if (drag == MarkingNodeToolSystem.DragKind.HandleA || drag == MarkingNodeToolSystem.DragKind.HandleB)
                buf.DrawCurve(kColPreviewCurve, c, kPreviewCurveWidth * 1.5f);
            buf.DrawLine(kColHandleStem, new Line3.Segment(c.a, c.b), kHandleStemWidth);
            buf.DrawLine(kColHandleStem, new Line3.Segment(c.d, c.c), kHandleStemWidth);
            DrawHandleDot(buf, c.b, kHandleDotDiameter, IsHover(MarkingNodeToolSystem.EditHoverKind.LineHandleA, _tool.EditLine));
            DrawHandleDot(buf, c.c, kHandleDotDiameter, IsHover(MarkingNodeToolSystem.EditHoverKind.LineHandleB, _tool.EditLine));
        }

        /// <summary>What the snap followed: guide and angle lines (only the stretch around
        /// the snapped spot), or the kerb or marking line it slid along, plus a ring on the
        /// snapped spot.</summary>
        private void DrawSnap(OverlayRenderSystem.Buffer buf)
        {
            if (_tool.CurrentSnap == MarkingNodeToolSystem.SnapKind.None) return;
            var p = _tool.PlacePos;
            var guides = _tool.SnapGuides;
            for (int g = 0; g < guides.Count; g++)
            {
                var poly = guides[g];
                if (poly.Length == 2)
                {
                    float3 a = poly[0], b = poly[1];
                    float3 dir = b - a;
                    dir.y = 0f;
                    float len = math.length(dir);
                    if (len < 1e-3f) continue;
                    dir /= len;
                    // Long guide lines are cut to the stretch around the snapped spot.
                    if (len > 2f * kSnapGuideShowHalfLength)
                    {
                        float along = math.dot(p - a, dir);
                        float lo = math.max(0f, along - kSnapGuideShowHalfLength), hi = math.min(len, along + kSnapGuideShowHalfLength);
                        b = a + dir * hi;
                        a += dir * lo;
                    }
                    a.y = b.y = p.y;
                    buf.DrawLine(kColSnapGuide, new Line3.Segment(a, b), kSnapGuideWidth);
                    continue;
                }
                for (int i = 1; i < poly.Length; i++)
                    buf.DrawLine(kColSnapGuide, new Line3.Segment(poly[i - 1], poly[i]), kSnapGuideWidth);
            }
            DrawDot(buf, p, kSnapRingDiameter, kColTransparent, kColSnapGuide, kSnapRingWidth);
        }

        private bool IsHover(MarkingNodeToolSystem.EditHoverKind kind, int index)
            => _tool.EditHover == kind && _tool.EditHoverIndex == index;

        /// <summary>A curve-handle grab dot; the one under the cursor is drawn larger with a
        /// white outline, so it is clear which handle a press will take.</summary>
        private void DrawHandleDot(OverlayRenderSystem.Buffer buf, float3 p, float diameter, bool hovered)
        {
            if (hovered) DrawDot(buf, p, diameter * 1.35f, kColHandleFill, kColFreePoint, kDotOutlineWidthHover);
            else DrawDot(buf, p, diameter, kColHandleFill, kColDotOutline, kDotOutlineWidth);
        }

        /// <summary>Outlines every piece of the hovered area, the area counterpart of the line
        /// hover trace: cyan for visible pieces, red for hidden ones.</summary>
        private void DrawHoveredAreaOutline(OverlayRenderSystem.Buffer buf, Entity node, int areaIndex)
        {
            if (!EntityManager.HasBuffer<MarkingAreaPiece>(node)
                || !EntityManager.HasBuffer<MarkingAreaPieceVertex>(node)) return;
            var pieces = EntityManager.GetBuffer<MarkingAreaPiece>(node, isReadOnly: true);
            var verts = EntityManager.GetBuffer<MarkingAreaPieceVertex>(node, isReadOnly: true);
            for (int p = 0; p < pieces.Length; p++)
            {
                var pd = pieces[p];
                if (pd.areaIndex != areaIndex || pd.vertexCount < 3) continue;
                var color = pd.visible ? kColHighlightedCurve : kColHighlightedHidden;
                for (int v = 0; v < pd.vertexCount; v++)
                {
                    int i0 = pd.firstVertex + v;
                    int i1 = pd.firstVertex + (v + 1) % pd.vertexCount;
                    if (i0 < 0 || i1 < 0 || i0 >= verts.Length || i1 >= verts.Length) return;
                    buf.DrawLine(color, new Line3.Segment(verts[i0].position, verts[i1].position), kHighlightedPairCurveWidth);
                }
            }
        }

        /// <summary>Overlay while the user places the vertices of an area polygon. Layers are
        /// drawn back to front, as numbered below.</summary>
        private void DrawAreaModeOverlay(OverlayRenderSystem.Buffer buf)
        {
            var endpoints = _tool.Endpoints;
            var corners = _tool.CornerAnchors;
            var crossings = _tool.LineIntersections;
            var polygon = _tool.AreaPolygon;
            var hover = _tool.AreaHover;
            float3 cursor = _tool.CursorWorldPos;

            // 1. Snap candidates: lane endpoints, corner anchors and line crossings, as hollow
            //    rings, and only those near the cursor. A click anywhere else places a new
            //    point, so the whole junction doesn't need to be covered in rings.
            bool cursorValid = math.lengthsq(cursor) > 0f;
            for (int i = 0; i < endpoints.Count; i++)
                DrawAreaCandidateNear(buf, endpoints[i].position, cursor, cursorValid);
            for (int i = 0; i < corners.Count; i++)
                DrawAreaCandidateNear(buf, corners[i].position, cursor, cursorValid);
            for (int i = 0; i < crossings.Count; i++)
                DrawAreaCandidateNear(buf, crossings[i].position, cursor, cursorValid);
            // Nothing to snap to: a ghost dot shows where the click will place a point.
            if (!hover.IsValid && cursorValid)
                DrawDot(buf, _tool.PlacePos, kDotDiameter, WithAlpha(kColFreePoint, 0.25f), kColFreePoint, kDotOutlineWidth);

            // 2. Contour through the placed vertices. Edges that follow a line come back sampled
            //    along it, so the preview shows the arc the committed area will follow.
            _tool.BuildAreaContourPath(_areaContourScratch);
            for (int i = 1; i < _areaContourScratch.Count; i++)
            {
                buf.DrawLine(kColAreaContour,
                    new Line3.Segment(_areaContourScratch[i - 1], _areaContourScratch[i]),
                    kAreaContourWidth);
            }

            // 3. Preview edge from the last placed vertex to the hovered candidate or the cursor,
            //    green when it would close the polygon.
            if (polygon.Count > 0)
            {
                var lastPos = polygon[polygon.Count - 1].position;
                float3 targetPos;
                bool closingImminent = false;
                if (hover.IsValid && _tool.TryGetAreaAnchorPos(hover, out var hovPos))
                {
                    targetPos = hovPos;
                    closingImminent = polygon.Count >= 3
                        && hover.kind == polygon[0].kind
                        && hover.refIndex == polygon[0].refIndex;
                }
                else
                {
                    targetPos = _tool.PlacePos;
                }
                if (math.lengthsq(targetPos - lastPos) > kMinPreviewLengthSq)
                {
                    buf.DrawLine(
                        closingImminent ? kColAreaPreviewClose : kColAreaPreview,
                        new Line3.Segment(lastPos, targetPos),
                        kAreaPreviewWidth);
                }
            }

            // 4. Placed vertices, on top of the contour.
            for (int i = 0; i < polygon.Count; i++)
                DrawDot(buf, polygon[i].position, kAreaPlacedDotDiameter, kColAreaPlacedFill, kColAreaCandOutline, kAreaCandDotOutlineWidth);

            // 5. Ring around the start vertex once clicking it would close the polygon.
            if (polygon.Count >= 3)
                DrawDot(buf, polygon[0].position, kAreaStartRingDiameter, kColTransparent, kColAreaPreviewClose, kAreaStartRingWidth);

            // 6. Hovered candidate, on top of everything.
            if (hover.IsValid && _tool.TryGetAreaAnchorPos(hover, out var hp))
                DrawDot(buf, hp, kAreaHoverDotDiameter, kColAreaHoverFill, kColAreaCandOutline, kAreaCandDotOutlineWidth);
        }

        /// <summary>A snap candidate, drawn only within <see cref="kAreaCandShowRadius"/> of the
        /// cursor and fading out towards that radius.</summary>
        private void DrawAreaCandidateNear(OverlayRenderSystem.Buffer buf, float3 position, float3 cursor, bool cursorValid)
        {
            if (!cursorValid) return;
            float d = math.distance(position.xz, cursor.xz);
            if (d > kAreaCandShowRadius) return;
            float fade = math.saturate(1.25f - d / kAreaCandShowRadius);
            DrawDot(buf, position, kAreaCandDotDiameter, WithAlpha(kColAreaCandFill, kColAreaCandFill.a * fade),
                WithAlpha(kColAreaCandRing, kColAreaCandRing.a * fade), kAreaCandDotOutlineWidth);
        }

        private void DrawIntersectionMarker(OverlayRenderSystem.Buffer buf, float3 p)
            => DrawDot(buf, p, kIntersectionDotDiameter, kColIntersection, kColIntersectionOutline, kIntersectionDotOutlineWidth);

        /// <summary>Faint ring around every node with at least one MarkingLine, so customised
        /// junctions are easy to spot. Skips the selected node, whose dots already mark it.</summary>
        private void DrawHasPairsRings(OverlayRenderSystem.Buffer buf, Entity excludeNode)
        {
            using var nodes = _nodesWithPairsQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                if (n == excludeNode) continue;
                if (!EntityManager.HasBuffer<MarkingLine>(n)) continue;
                if (EntityManager.GetBuffer<MarkingLine>(n, isReadOnly: true).Length == 0) continue;
                DrawNodeRing(buf, n, kColNodeHasPairsRing, kNodeHasPairsDiameter, kNodeHasPairsOutlineWidth);
            }
        }

        /// <summary>Ring around a junction; for a road stretch, its two kerb lines instead.</summary>
        private void DrawNodeRing(OverlayRenderSystem.Buffer buf, Entity node, Color ringColor, float diameter, float outlineWidth)
        {
            if (EntityManager.HasComponent<Node>(node))
            {
                float3 pos = EntityManager.GetComponentData<Node>(node).m_Position;
                DrawDot(buf, pos, diameter, kColTransparent, ringColor, outlineWidth);
                return;
            }
            if (EntityManager.HasComponent<Edge>(node) && EntityManager.HasComponent<EdgeGeometry>(node))
            {
                var g = EntityManager.GetComponentData<EdgeGeometry>(node);
                float w = outlineWidth * 1.5f;
                buf.DrawCurve(ringColor, g.m_Start.m_Left, w);
                buf.DrawCurve(ringColor, g.m_Start.m_Right, w);
                buf.DrawCurve(ringColor, g.m_End.m_Left, w);
                buf.DrawCurve(ringColor, g.m_End.m_Right, w);
            }
        }

        /// <summary>Flat circle at <paramref name="position"/>, projected onto the terrain unless
        /// it sits on an elevated deck (see <see cref="DotStyle"/>).</summary>
        private void DrawDot(OverlayRenderSystem.Buffer buf, float3 position, float diameter, Color fill, Color outline, float outlineWidth)
        {
            buf.DrawCircle(
                outlineColor: outline,
                fillColor: fill,
                outlineWidth: outlineWidth,
                styleFlags: DotStyle(position),
                direction: new float2(0f, 1f),
                position: position,
                diameter: diameter);
        }
    }
}
