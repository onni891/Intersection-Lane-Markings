using Colossal.Mathematics;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Utilities;

namespace ILM.Systems.Tool
{
    // Dragging in edit mode: the two curve handles of the open line (clicked in the world or
    // expanded in the panel, like Move It's curve handles), the vertices and edge handles of
    // the open area (MarkingNodeToolSystem.AreaEdit.cs) and user-placed free points. A press
    // on a handle or a free point starts a drag; the change is written on release. A press
    // released without moving changes nothing. The draw modes never drag, so a click there
    // always draws.
    public partial class MarkingNodeToolSystem
    {
        // Area* drags act on the open area: a vertex, or a curve handle of edge _dragIndex.
        public enum DragKind { None, HandleA, HandleB, FreePoint, AreaVertex, AreaHandleA, AreaHandleB }

        // How far the cursor must move (XZ, metres) before a press counts as a drag. Pick radii
        // are in MarkingNodeToolSystem.EditHover.cs.
        private const float kDragThresholdSq = 0.3f * 0.3f;

        private int _editLine = -1;
        private DragKind _drag;
        private bool _dragMoved;
        private float3 _dragStart;
        private float3 _dragOrigin;
        private MarkingEndpoint _dragPoint;
        // Live drag: the change is written to the road every few frames while the mouse is held,
        // so the real marking follows the cursor, not only the overlay preview.
        private const int kLiveDragFrames = 4;
        private const float kLiveDragMinMoveSq = 0.02f * 0.02f;
        private int _liveDragFrames;
        private float3 _liveDragPos;

        /// <summary>Line open for editing (its handles are shown in edit mode), -1 for none.</summary>
        public int EditLine => _editLine;
        /// <summary>What is being dragged right now (None until the cursor actually moved).</summary>
        public DragKind Dragging => _dragMoved ? _drag : DragKind.None;

        public void SetEditLine(int lineIndex) => _editLine = lineIndex;

        /// <summary>True for the free point being dragged right now.</summary>
        public bool IsDraggedPoint(MarkingEndpoint ep)
            => Dragging == DragKind.FreePoint && ep.edge == _dragPoint.edge && ep.gapIndex == _dragPoint.gapIndex;

        /// <summary>Endpoints and control points of the edit line, with a handle drag applied
        /// live so the overlay can preview it.</summary>
        public bool TryGetEditCurve(out Bezier4x3 curve)
        {
            curve = default;
            if (!EditMode || _selectedNode == Entity.Null || _editLine < 0) return false;
            if (!EntityManager.HasBuffer<MarkingLine>(_selectedNode)) return false;
            var lines = EntityManager.GetBuffer<MarkingLine>(_selectedNode, isReadOnly: true);
            if (_editLine >= lines.Length) return false;
            var line = lines[_editLine];
            if (!MarkingCurveBuilder.TryFindEndpoints(_endpoints, line, out var src, out var dst)) return false;
            curve = MarkingCurveBuilder.BuildLine(src, dst, line);
            if (_dragMoved && _drag == DragKind.HandleA) curve.b = _placePos;
            if (_dragMoved && _drag == DragKind.HandleB) curve.c = _placePos;
            return true;
        }

        /// <summary>Drives a drag. Returns true when it consumed this frame's mouse input.</summary>
        private bool UpdateDrag(bool hitSomething)
        {
            if (_drag != DragKind.None)
            {
                if (hitSomething && !_dragMoved && math.distancesq(_cursorWorldPos.xz, _dragStart.xz) > kDragThresholdSq)
                    _dragMoved = true;
                if (applyAction.IsPressed())
                {
                    LiveDrag();
                    return true;
                }

                var kind = _drag;
                int index = _dragIndex;
                bool moved = _dragMoved;
                _drag = DragKind.None;
                _dragMoved = false;
                _dragIndex = -1;
                if (moved)
                {
                    if (kind == DragKind.AreaVertex || kind == DragKind.AreaHandleA || kind == DragKind.AreaHandleB)
                        CommitAreaDrag(kind, index);
                    else
                        CommitDrag(kind);
                }
                return true;
            }

            if (!EditMode || _state != MarkingToolState.NodeSelected || !hitSomething || !applyAction.WasPressedThisFrame())
                return false;

            // Only what is highlighted under the cursor (UpdateEditHover) can be grabbed.
            int hi = _editHoverIndex;
            switch (_editHoverKind)
            {
                case EditHoverKind.LineHandleA: _drag = DragKind.HandleA; break;
                case EditHoverKind.LineHandleB: _drag = DragKind.HandleB; break;
                case EditHoverKind.AreaHandleA: _drag = DragKind.AreaHandleA; _dragIndex = hi; break;
                case EditHoverKind.AreaHandleB: _drag = DragKind.AreaHandleB; _dragIndex = hi; break;
                case EditHoverKind.AreaVertex: _drag = DragKind.AreaVertex; _dragIndex = hi; break;
                case EditHoverKind.FreePoint when hi >= 0 && hi < _endpoints.Count:
                    _drag = DragKind.FreePoint;
                    _dragPoint = _endpoints[hi];
                    break;
            }
            if (_drag == DragKind.None) return false;
            // Where the dragged vertex was, so snapping never pulls it back onto itself.
            _dragOrigin = _drag == DragKind.AreaVertex && hi >= 0 && hi < _editAreaPos.Count ? _editAreaPos[hi] : _cursorWorldPos;
            _dragStart = _cursorWorldPos;
            _dragMoved = false;
            _liveDragFrames = 0;
            _liveDragPos = _cursorWorldPos;
            return true;
        }

        /// <summary>Writes the drag to the road while the mouse is still held, every
        /// <see cref="kLiveDragFrames"/> frames when the cursor moved. Undo still sees one step:
        /// history does not record while a drag is in progress. An area corner that is not a free
        /// point is only re-anchored on release (each write would place a new point).</summary>
        private void LiveDrag()
        {
            if (!_dragMoved || ++_liveDragFrames < kLiveDragFrames) return;
            if (math.distancesq(_placePos, _liveDragPos) < kLiveDragMinMoveSq) return;
            switch (_drag)
            {
                case DragKind.HandleA:
                case DragKind.HandleB:
                case DragKind.FreePoint:
                    CommitDrag(_drag);
                    break;
                case DragKind.AreaHandleA:
                case DragKind.AreaHandleB:
                    CommitAreaDrag(_drag, _dragIndex);
                    break;
                case DragKind.AreaVertex:
                    if (_dragIndex < 0 || _dragIndex >= _editAreaVerts.Count || !IsFreeVertex(_editAreaVerts[_dragIndex])) return;
                    CommitAreaDrag(_drag, _dragIndex);
                    break;
                default:
                    return;
            }
            _liveDragFrames = 0;
            _liveDragPos = _placePos;
        }

        private void CommitDrag(DragKind kind)
        {
            if (kind == DragKind.FreePoint)
            {
                bool ok = MarkingEdits.MoveFreePoint(EntityManager, _selectedNode, _dragPoint.edge, _dragPoint.gapIndex, _placePos);
                RefreshEndpoints();
                // The point may now sit on another road; keep following it by id. The position
                // and tangent stay those of the drag start (snapping measures from there).
                for (int i = 0; i < _endpoints.Count; i++)
                    if (_endpoints[i].gapIndex == _dragPoint.gapIndex) { _dragPoint.edge = _endpoints[i].edge; break; }
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
            if (kind == DragKind.HandleA) line.handleA = _placePos - src.position;
            else line.handleB = _placePos - dst.position;
            lines[_editLine] = line;
            EntityManager.MarkUpdated(_selectedNode);
            log.Debug($"tool: line#{_editLine} handle {(kind == DragKind.HandleA ? "A" : "B")} moved");
        }
    }
}
