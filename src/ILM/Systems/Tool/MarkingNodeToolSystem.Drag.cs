using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Utilities;

namespace ILM.Systems.Tool
{
    // Dragging in the line editor: the two curve handles of the line expanded in the panel
    // (like Move It's curve handles) and user-placed free points. A press on a handle or a free
    // point starts a drag; the change is written on release. A press on a free point that is
    // released without moving is an ordinary click (start a line from it).
    public partial class MarkingNodeToolSystem
    {
        public enum DragKind { None, HandleA, HandleB, FreePoint }

        // Pick radius for handles (XZ, metres), and how far the cursor must move before a
        // press counts as a drag.
        private const float kHandlePickRadiusSq = 1.2f * 1.2f;
        private const float kDragThresholdSq = 0.3f * 0.3f;

        private int _editLine = -1;
        private DragKind _drag;
        private bool _dragMoved;
        private float3 _dragStart;
        private MarkingEndpoint _dragPoint;

        /// <summary>Line whose handles are shown (the row expanded in the panel), -1 for none.</summary>
        public int EditLine => _editLine;
        /// <summary>What is being dragged right now (None until the cursor actually moved).</summary>
        public DragKind Dragging => _dragMoved ? _drag : DragKind.None;

        public void SetEditLine(int lineIndex) => _editLine = lineIndex;

        /// <summary>Endpoints and control points of the edit line, with a handle drag applied
        /// live so the overlay can preview it.</summary>
        public bool TryGetEditCurve(out Bezier4x3 curve)
        {
            curve = default;
            if (_selectedNode == Entity.Null || _editLine < 0) return false;
            if (!EntityManager.HasBuffer<MarkingLine>(_selectedNode)) return false;
            var lines = EntityManager.GetBuffer<MarkingLine>(_selectedNode, isReadOnly: true);
            if (_editLine >= lines.Length) return false;
            var line = lines[_editLine];
            if (!MarkingCurveBuilder.TryFindEndpoints(_endpoints, line, out var src, out var dst)) return false;
            curve = MarkingCurveBuilder.BuildLine(src, dst, line);
            if (_dragMoved && _drag == DragKind.HandleA) curve.b = _cursorWorldPos;
            if (_dragMoved && _drag == DragKind.HandleB) curve.c = _cursorWorldPos;
            return true;
        }

        /// <summary>Drives a drag. Returns true when it consumed this frame's mouse input.</summary>
        private bool UpdateDrag(bool hitSomething)
        {
            if (_drag != DragKind.None)
            {
                if (hitSomething && !_dragMoved && math.distancesq(_cursorWorldPos.xz, _dragStart.xz) > kDragThresholdSq)
                    _dragMoved = true;
                if (applyAction.IsPressed()) return true;

                var kind = _drag;
                bool moved = _dragMoved;
                _drag = DragKind.None;
                _dragMoved = false;
                if (moved) CommitDrag(kind);
                else if (kind == DragKind.FreePoint)
                {
                    // A click, not a drag: start a line from the point, as for any dot.
                    int idx = IndexOfEndpoint(_dragPoint.edge, _dragPoint.gapIndex);
                    if (idx >= 0)
                    {
                        _sourceIdx = idx;
                        _state = MarkingToolState.SourceSelected;
                    }
                }
                return true;
            }

            if (_state != MarkingToolState.NodeSelected || !hitSomething || !applyAction.WasPressedThisFrame())
                return false;

            if (TryGetEditCurve(out var c))
            {
                if (math.distancesq(c.b.xz, _cursorWorldPos.xz) < kHandlePickRadiusSq) _drag = DragKind.HandleA;
                else if (math.distancesq(c.c.xz, _cursorWorldPos.xz) < kHandlePickRadiusSq) _drag = DragKind.HandleB;
            }
            if (_drag == DragKind.None && _hoverIdx >= 0 && IsFree(_endpoints[_hoverIdx]))
            {
                _drag = DragKind.FreePoint;
                _dragPoint = _endpoints[_hoverIdx];
            }
            if (_drag == DragKind.None) return false;
            _dragStart = _cursorWorldPos;
            _dragMoved = false;
            return true;
        }

        private void CommitDrag(DragKind kind)
        {
            if (kind == DragKind.FreePoint)
            {
                bool ok = MarkingEdits.MoveFreePoint(EntityManager, _selectedNode, _dragPoint.edge, _dragPoint.gapIndex, _cursorWorldPos);
                RefreshEndpoints();
                log.Debug($"tool: free point gap={_dragPoint.gapIndex} {(ok ? "moved" : "not moved (dropped off the road)")}");
                return;
            }

            if (!EntityManager.HasBuffer<MarkingLine>(_selectedNode)) return;
            var lines = EntityManager.GetBuffer<MarkingLine>(_selectedNode);
            if (_editLine < 0 || _editLine >= lines.Length) return;
            var line = lines[_editLine];
            if (!MarkingCurveBuilder.TryFindEndpoints(_endpoints, line, out var src, out var dst)) return;
            if (!line.hasHandles)
            {
                // Start from the current automatic arc so only the dragged side changes.
                var auto = MarkingCurveBuilder.BuildLine(src, dst, line);
                line.handleA = auto.b - src.position;
                line.handleB = auto.c - dst.position;
                line.hasHandles = true;
            }
            if (kind == DragKind.HandleA) line.handleA = _cursorWorldPos - src.position;
            else line.handleB = _cursorWorldPos - dst.position;
            lines[_editLine] = line;
            EntityManager.MarkUpdated(_selectedNode);
            log.Debug($"tool: line#{_editLine} handle {(kind == DragKind.HandleA ? "A" : "B")} moved");
        }
    }
}
