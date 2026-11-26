using System.Collections.Generic;
using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;

namespace ILM.Systems.Tool
{
    // Edit-mode picking. One target under the cursor is resolved per frame and used by the
    // overlay highlight, the drag start, the click and the right-click alike, so what lights up
    // is exactly what a click acts on.
    //
    // The panel tab decides what can be edited (EditTarget):
    //   Lines:  the open line's handles, the free points lines run through, and lines.
    //   Areas:  the open area's handles, vertices and "+" markers, and areas.
    //   Points: free points only (move, right-click to remove).
    // Lines can't be picked while editing areas and the other way round.
    public partial class MarkingNodeToolSystem
    {
        public enum EditTarget { Lines = 0, Areas = 1, Points = 2 }

        public enum EditHoverKind { None, LineHandleA, LineHandleB, FreePoint, AreaVertex, AreaHandleA, AreaHandleB, AreaInsert, Line, Area }

        // Pick radii in edit mode (XZ, metres). Close to the drawn sizes so a click lands on
        // what is under the cursor and not on a neighbour; point targets beat lines and areas
        // and the nearest point target wins.
        private const float kEditPointPickRadius = 0.9f;
        private const float kEditInsertPickRadius = 0.6f;
        private const float kEditLinePickRadius = 1.0f;

        private EditTarget _editTarget = EditTarget.Lines;
        private EditHoverKind _editHoverKind;
        private int _editHoverIndex = -1;

        // Free points used by a line / by an area, rebuilt each frame in edit mode.
        private readonly HashSet<(Entity, int)> _pointsOnLines = new HashSet<(Entity, int)>();
        private readonly HashSet<(Entity, int)> _pointsOnAreas = new HashSet<(Entity, int)>();

        public EditTarget CurrentEditTarget => _editTarget;
        public EditHoverKind EditHover => _editHoverKind;
        public int EditHoverIndex => _editHoverIndex;

        /// <summary>Free point id under the cursor in the world (Points and Lines editing), -1
        /// for none. The Points tab highlights its row.</summary>
        public int HoveredFreePointId
            => _hoverIdx >= 0 && _hoverIdx < _endpoints.Count && IsFree(_endpoints[_hoverIdx])
                ? _endpoints[_hoverIdx].gapIndex - MarkingEndpointExtractor.kFreePointGapBase
                : -1;

        /// <summary>The panel tab: what edit mode works on. Switching closes whatever the other
        /// target had open and ends a drag.</summary>
        public void SetEditTarget(EditTarget target)
        {
            if (target == _editTarget) return;
            _editTarget = target;
            if (target != EditTarget.Lines) _editLine = -1;
            if (target != EditTarget.Areas) _editArea = -1;
            if (_state == MarkingToolState.SourceSelected && EditMode) DropSource();
            _drag = DragKind.None;
            _dragMoved = false;
            _editHoverKind = EditHoverKind.None;
            _editHoverIndex = -1;
            log.Debug($"tool: edit target → {_editTarget}");
        }

        /// <summary>A free point that can be picked for the current target: every one on the
        /// Points tab; on the Lines tab those a line uses or nothing uses (an area corner
        /// belongs to area editing).</summary>
        public bool IsEditablePoint(MarkingEndpoint ep)
        {
            if (!IsFree(ep)) return false;
            switch (_editTarget)
            {
                case EditTarget.Points: return true;
                case EditTarget.Lines:
                    var key = (ep.edge, ep.gapIndex);
                    return _pointsOnLines.Contains(key) || !_pointsOnAreas.Contains(key);
                default: return false;
            }
        }

        private void RefreshPointUsage()
        {
            _pointsOnLines.Clear();
            _pointsOnAreas.Clear();
            if (_selectedNode == Entity.Null) return;
            if (EntityManager.HasBuffer<MarkingLine>(_selectedNode))
            {
                var lines = EntityManager.GetBuffer<MarkingLine>(_selectedNode, isReadOnly: true);
                for (int i = 0; i < lines.Length; i++)
                {
                    _pointsOnLines.Add((lines[i].sourceEdge, lines[i].sourceGapIndex));
                    _pointsOnLines.Add((lines[i].targetEdge, lines[i].targetGapIndex));
                }
            }
            if (EntityManager.HasBuffer<MarkingAreaVertex>(_selectedNode))
            {
                var verts = EntityManager.GetBuffer<MarkingAreaVertex>(_selectedNode, isReadOnly: true);
                for (int i = 0; i < verts.Length; i++)
                    if (verts[i].kind == AreaAnchorKind.LaneEndpoint) _pointsOnAreas.Add((verts[i].refEdgeA, verts[i].refGap));
            }
        }

        /// <summary>Resolves the single edit target under the cursor. Runs after
        /// <see cref="RefreshEditArea"/>, which has the open area's geometry ready.</summary>
        private void UpdateEditHover(bool hitSomething)
        {
            _editHoverKind = EditHoverKind.None;
            _editHoverIndex = -1;
            if (!EditMode || _state != MarkingToolState.NodeSelected) return;
            RefreshPointUsage();
            if (!hitSomething || _drag != DragKind.None) return;

            var cursor = _cursorWorldPos;
            float best = kEditPointPickRadius * kEditPointPickRadius;

            void Consider(float3 p, EditHoverKind kind, int index, ref float bestSq)
            {
                float sq = PolygonUtils.DistSqXZ(p, cursor);
                if (sq < bestSq) { bestSq = sq; _editHoverKind = kind; _editHoverIndex = index; }
            }

            if (_editTarget == EditTarget.Lines && TryGetEditCurve(out var c))
            {
                Consider(c.b, EditHoverKind.LineHandleA, _editLine, ref best);
                Consider(c.c, EditHoverKind.LineHandleB, _editLine, ref best);
            }
            if (_editTarget == EditTarget.Lines || _editTarget == EditTarget.Points)
            {
                for (int i = 0; i < _endpoints.Count; i++)
                    if (IsEditablePoint(_endpoints[i])) Consider(_endpoints[i].position, EditHoverKind.FreePoint, i, ref best);
            }
            if (_editTarget == EditTarget.Areas)
            {
                for (int i = 0; i < _editAreaEdges.Count; i++)
                {
                    Consider(_editAreaEdges[i].b, EditHoverKind.AreaHandleA, i, ref best);
                    Consider(_editAreaEdges[i].c, EditHoverKind.AreaHandleB, i, ref best);
                }
                for (int i = 0; i < _editAreaPos.Count; i++)
                    Consider(_editAreaPos[i], EditHoverKind.AreaVertex, i, ref best);
                if (_editHoverKind == EditHoverKind.None)
                {
                    float bestInsert = kEditInsertPickRadius * kEditInsertPickRadius;
                    for (int i = 0; i < _editAreaEdges.Count; i++)
                        Consider(MathUtils.Position(_editAreaEdges[i], 0.5f), EditHoverKind.AreaInsert, i, ref bestInsert);
                }
            }
            if (_editHoverKind != EditHoverKind.None) return;

            if (_editTarget == EditTarget.Lines)
            {
                int line = HitTestLines(cursor, kEditLinePickRadius);
                if (line >= 0) { _editHoverKind = EditHoverKind.Line; _editHoverIndex = line; }
            }
            else if (_editTarget == EditTarget.Areas)
            {
                int area = HitTestAreas(cursor);
                if (area >= 0) { _editHoverKind = EditHoverKind.Area; _editHoverIndex = area; }
            }
        }
    }
}
