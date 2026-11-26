using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Systems.Topology;
using ILM.Utilities;

namespace ILM.Systems.Tool
{
    // Edit mode for areas. A click inside an area opens it: its vertices become draggable dots,
    // every edge gets two curve handles like a line, and a "+" in the middle of each edge adds a
    // vertex there. Right-click on a vertex removes it. Dragging a vertex that sits on a road
    // dot turns it into a free point where it is dropped, unless it is dropped on another dot.
    public partial class MarkingNodeToolSystem
    {
        private int _editArea = -1;

        // The open area, resolved every frame in edit mode (with a drag in progress applied, so
        // the overlay previews it): vertices, their world positions and the edge curves.
        // Edge i runs from vertex i to vertex i + 1.
        private readonly List<MarkingAreaVertex> _editAreaVerts = new List<MarkingAreaVertex>();
        private readonly List<float3> _editAreaPos = new List<float3>();
        private readonly List<Bezier4x3> _editAreaEdges = new List<Bezier4x3>();
        private int _dragIndex = -1;

        /// <summary>Area open for editing, -1 for none.</summary>
        public int EditArea => _editArea;
        public IReadOnlyList<MarkingAreaVertex> EditAreaVertices => _editAreaVerts;
        public IReadOnlyList<float3> EditAreaPositions => _editAreaPos;
        public IReadOnlyList<Bezier4x3> EditAreaEdges => _editAreaEdges;
        public int HoveredAreaVertex => _editHoverKind == EditHoverKind.AreaVertex ? _editHoverIndex : -1;
        public int HoveredAreaInsert => _editHoverKind == EditHoverKind.AreaInsert ? _editHoverIndex : -1;
        public int DragIndex => _dragIndex;

        /// <summary>The area row expanded in the panel (Areas tab in edit mode), -1 for none.</summary>
        public void SetEditArea(int areaIndex)
        {
            _editArea = areaIndex;
            if (areaIndex >= 0) _editLine = -1;
        }

        private void OpenEditArea(int areaIndex)
        {
            _editArea = areaIndex;
            _editLine = -1;
            _lastClickedLine = -1;
            _lastClickedTick++;
            log.Debug($"tool: edit — opened area #{areaIndex}");
        }

        private static bool IsFreeVertex(MarkingAreaVertex v)
            => v.kind == AreaAnchorKind.LaneEndpoint
               && MarkingEndpointExtractor.KindOf(v.refGap) == MarkingEndpointExtractor.AnchorKind.Free;

        private MarkingLine[] LinesSnapshot()
        {
            if (_selectedNode == Entity.Null || !EntityManager.HasBuffer<MarkingLine>(_selectedNode))
                return System.Array.Empty<MarkingLine>();
            var buf = EntityManager.GetBuffer<MarkingLine>(_selectedNode, isReadOnly: true);
            var arr = new MarkingLine[buf.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = buf[i];
            return arr;
        }

        /// <summary>Resolves the open area and its hover targets for this frame. Closes the area
        /// when it no longer exists (deleted from the panel, say).</summary>
        private void RefreshEditArea()
        {
            _editAreaVerts.Clear();
            _editAreaPos.Clear();
            _editAreaEdges.Clear();
            if (_editTarget != EditTarget.Areas || !EditMode || _state != MarkingToolState.NodeSelected || _editArea < 0 || _selectedNode == Entity.Null) return;
            if (!AreaEdits.TryGetVertices(EntityManager, _selectedNode, _editArea, _editAreaVerts))
            {
                _editArea = -1;
                return;
            }

            var lines = LinesSnapshot();
            for (int i = 0; i < _editAreaVerts.Count; i++)
            {
                if (!MarkingAreaTopologySystem.TryResolveVertexPos(_editAreaVerts[i], _endpoints, _cornerAnchors, lines, out var p))
                {
                    // An anchor can't be resolved (road changed): nothing to edit safely.
                    _editAreaVerts.Clear();
                    _editAreaPos.Clear();
                    return;
                }
                _editAreaPos.Add(p);
            }

            int n = _editAreaVerts.Count;
            var drag = Dragging;
            if (drag == DragKind.AreaVertex && _dragIndex >= 0 && _dragIndex < n) _editAreaPos[_dragIndex] = _cursorWorldPos;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var from = _editAreaVerts[i];
                // An edge that followed a line can't follow it to a dragged vertex: preview it
                // straight, as the commit will store it.
                if (drag == DragKind.AreaVertex && (i == _dragIndex || j == _dragIndex) && from.edgeToNext == AreaEdgeKind.LineBezier)
                    from.edgeToNext = AreaEdgeKind.Straight;
                var bez = MarkingAreaTopologySystem.EdgeCurve(from, _editAreaVerts[j], _editAreaPos[i], _editAreaPos[j], _endpoints, lines);
                if (i == _dragIndex && drag == DragKind.AreaHandleA) bez.b = _cursorWorldPos;
                if (i == _dragIndex && drag == DragKind.AreaHandleB) bez.c = _cursorWorldPos;
                _editAreaEdges.Add(bez);
            }
        }

        /// <summary>Writes a finished area drag. Called after the drag state was cleared, so the
        /// refresh below resolves the outline as stored.</summary>
        private void CommitAreaDrag(DragKind kind, int index)
        {
            RefreshEditArea();
            int n = _editAreaVerts.Count;
            if (index < 0 || index >= n) return;

            if (kind == DragKind.AreaHandleA || kind == DragKind.AreaHandleB)
            {
                var e = _editAreaEdges[index];
                var c1 = kind == DragKind.AreaHandleA ? _cursorWorldPos : e.b;
                var c2 = kind == DragKind.AreaHandleB ? _cursorWorldPos : e.c;
                AreaEdits.SetEdgeCurve(EntityManager, _selectedNode, _editArea, index, e.a, c1, c2, e.d);
                log.Debug($"tool: area#{_editArea} edge {index} reshaped");
                return;
            }

            // Vertex: a free point simply moves; any other anchor is replaced by the dot it was
            // dropped on, or by a new free point at the drop spot.
            var v = _editAreaVerts[index];
            if (IsFreeVertex(v))
            {
                MarkingEdits.MoveFreePoint(EntityManager, _selectedNode, v.refEdgeA, v.refGap, _cursorWorldPos);
                RefreshEndpoints();
                log.Debug($"tool: area#{_editArea} vertex {index} (free point) moved");
                return;
            }
            var target = FindHoveredAreaCandidate(_cursorWorldPos);
            MarkingAreaVertex anchor;
            if (target.IsValid && TryGetAreaAnchorPos(target, out var tpos))
            {
                anchor = ToStoredVertex(new AreaPolygonVertex { kind = target.kind, refIndex = target.refIndex, position = tpos });
            }
            else
            {
                int idx = PlaceFreePoint(_cursorWorldPos);
                if (idx < 0) { log.Debug("tool: area vertex dropped off the road, not moved"); return; }
                anchor = AreaEdits.EndpointVertex(_endpoints[idx]);
            }
            AreaEdits.ReanchorVertex(EntityManager, _selectedNode, _editArea, index, anchor);
            log.Debug($"tool: area#{_editArea} vertex {index} re-anchored ({anchor.kind})");
        }

        /// <summary>"+" click: a new free-point vertex in the middle of the edge, splitting its
        /// curve so the outline keeps its shape.</summary>
        private void InsertAreaVertexAt(int edgeIndex)
        {
            if (edgeIndex < 0 || edgeIndex >= _editAreaEdges.Count) return;
            var edge = _editAreaEdges[edgeIndex];
            int idx = PlaceFreePoint(MathUtils.Position(edge, 0.5f));
            if (idx < 0) { log.Debug("tool: edge middle is off the road, no vertex added"); return; }
            AreaEdits.InsertVertex(EntityManager, _selectedNode, _editArea, edgeIndex, AreaEdits.EndpointVertex(_endpoints[idx]), edge, 0.5f);
            log.Debug($"tool: area#{_editArea} vertex added on edge {edgeIndex}");
        }

        /// <summary>Right-click on a vertex of the open area removes it from the area (a
        /// three-vertex area is deleted). A free point nothing else uses goes with it.</summary>
        private bool TryRemoveHoveredAreaVertex()
        {
            int vi = HoveredAreaVertex;
            if (_editArea < 0 || vi < 0 || vi >= _editAreaVerts.Count) return false;
            var v = _editAreaVerts[vi];
            bool deletesArea = _editAreaVerts.Count <= 3;
            AreaEdits.RemoveVertex(EntityManager, _selectedNode, _editArea, vi);
            if (IsFreeVertex(v)) AreaEdits.RemoveFreePointIfUnused(EntityManager, _selectedNode, v.refEdgeA, v.refGap);
            RefreshEndpoints();
            log.Debug($"tool: area#{_editArea} vertex {vi} removed{(deletesArea ? " (area deleted)" : "")}");
            if (deletesArea) _editArea = -1;
            _editHoverKind = EditHoverKind.None;
            return true;
        }
    }
}
