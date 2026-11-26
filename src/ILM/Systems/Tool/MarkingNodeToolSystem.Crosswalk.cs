using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Utilities;

namespace ILM.Systems.Tool
{
    // Crosswalks (the Crosswalks tab).
    //   Draw: one click on a road puts a crosswalk across it at that spot, kerb to kerb, with
    //         the panel's style and width. A preview shows where it will go. Right-click a
    //         crosswalk to remove it.
    //   Edit: click a crosswalk to open it. Drag its end points to move them, the blue side
    //         handles to change the width, and the middle handle to slide it along the road.
    //         Right-click removes it.
    public partial class MarkingNodeToolSystem
    {
        private CrosswalkStyle _currentCrosswalkStyle = CrosswalkStyle.Zebra;
        private float _currentCrosswalkWidth = MarkingCrosswalk.kDefaultWidth;

        private bool _cwPreviewValid;
        private MarkingFreePoint _cwPreviewA, _cwPreviewB;
        private float3 _cwPreviewPosA, _cwPreviewPosB;

        private int _editCrosswalk = -1;
        private int _hoveredCrosswalkInGame = -1;
        // Lateral kerb positions per road (see CrosswalkGeometry.TryPlace); cleared with the dots.
        private readonly Dictionary<Entity, float2> _kerbLatCache = new Dictionary<Entity, float2>();

        // The open crosswalk this frame, drags applied: ends and width.
        private bool _editCwValid;
        private float3 _editCwA, _editCwB;
        private float _editCwWidth;
        private MarkingCrosswalk _editCwData;

        public CrosswalkStyle CurrentCrosswalkStyle => _currentCrosswalkStyle;
        public float CurrentCrosswalkWidth => _currentCrosswalkWidth;
        public int EditCrosswalk => _editCrosswalk;
        public int HoveredCrosswalkInGame => _hoveredCrosswalkInGame;
        public bool CrosswalkPreviewValid => _cwPreviewValid;
        public float3 CrosswalkPreviewA => _cwPreviewPosA;
        public float3 CrosswalkPreviewB => _cwPreviewPosB;
        public bool EditCrosswalkValid => _editCwValid;
        public float3 EditCrosswalkA => _editCwA;
        public float3 EditCrosswalkB => _editCwB;
        public float EditCrosswalkWidth => _editCwWidth;

        /// <summary>Placing crosswalks: the Crosswalks tab outside edit mode.</summary>
        public bool CrosswalkDrawing => _editTarget == EditTarget.Crosswalks && !EditMode && _state == MarkingToolState.NodeSelected;

        public void SetCurrentCrosswalkStyle(int style) => _currentCrosswalkStyle = (CrosswalkStyle)math.clamp(style, 0, 3);
        public void SetCurrentCrosswalkWidth(float w) => _currentCrosswalkWidth = math.clamp(w, MarkingCrosswalk.kMinWidth, MarkingCrosswalk.kMaxWidth);
        public void SetEditCrosswalk(int index) => _editCrosswalk = index;

        /// <summary>Preview for placing, the open crosswalk's shape, and the crosswalk under the
        /// cursor. Runs after the snap, so both follow it.</summary>
        private void UpdateCrosswalks(bool hitSomething)
        {
            _cwPreviewValid = false;
            if (CrosswalkDrawing && hitSomething && _selectedNode != Entity.Null
                && CrosswalkGeometry.TryPlace(EntityManager, _selectedNode, _endpoints, _placePos, _kerbLatCache, out _cwPreviewA, out _cwPreviewB)
                && MarkingFreePointGeometry.TryResolve(EntityManager, _selectedNode, _cwPreviewA, out _cwPreviewPosA, out _)
                && MarkingFreePointGeometry.TryResolve(EntityManager, _selectedNode, _cwPreviewB, out _cwPreviewPosB, out _))
                _cwPreviewValid = math.distancesq(_cwPreviewPosA.xz, _cwPreviewPosB.xz) > 1f;

            _editCwValid = false;
            if (_editTarget == EditTarget.Crosswalks && EditMode && _editCrosswalk >= 0 && TryGetCrosswalk(_editCrosswalk, out var cw)
                && CrosswalkGeometry.TryGetEnds(_endpoints, cw, out var a, out var b))
            {
                _editCwData = cw;
                _editCwA = a;
                _editCwB = b;
                _editCwWidth = cw.width;
                var drag = Dragging;
                if (drag == DragKind.CrosswalkWidth) _editCwWidth = WidthAt(a, b, _placePos);
                if (drag == DragKind.CrosswalkMove && TrySlide(cw, _placePos, out var na, out var nb))
                {
                    _editCwA = na;
                    _editCwB = nb;
                }
                _editCwValid = true;
            }
            else if (_editCrosswalk >= 0 && !TryGetCrosswalk(_editCrosswalk, out _)) _editCrosswalk = -1;
        }

        /// <summary>The crosswalk under the cursor, after the edit hover is known.</summary>
        private void UpdateCrosswalkHover(bool hitSomething)
        {
            _hoveredCrosswalkInGame = -1;
            if (_editTarget == EditTarget.Crosswalks && _state == MarkingToolState.NodeSelected && hitSomething)
            {
                if (EditMode) _hoveredCrosswalkInGame = _editHoverKind == EditHoverKind.Crosswalk ? _editHoverIndex : -1;
                else _hoveredCrosswalkInGame = HitTestCrosswalks(_cursorWorldPos);
            }
        }

        private float WidthAt(float3 a, float3 b, float3 p)
        {
            float w = 2f * math.abs(math.dot((p - a).xz, CrosswalkGeometry.Across(a, b).xz));
            float step = _snap.HasFlag(SnapFlags.Length) ? 0.5f : 0.1f;
            return math.clamp(math.round(w / step) * step, MarkingCrosswalk.kMinWidth, MarkingCrosswalk.kMaxWidth);
        }

        public bool TryGetCrosswalk(int index, out MarkingCrosswalk cw)
        {
            cw = default;
            if (_selectedNode == Entity.Null || !EntityManager.HasBuffer<MarkingCrosswalk>(_selectedNode)) return false;
            var buf = EntityManager.GetBuffer<MarkingCrosswalk>(_selectedNode, isReadOnly: true);
            if (index < 0 || index >= buf.Length) return false;
            cw = buf[index];
            return true;
        }

        /// <summary>Index of the crosswalk under the cursor, -1 for none.</summary>
        private int HitTestCrosswalks(float3 cursor)
        {
            if (_selectedNode == Entity.Null || !EntityManager.HasBuffer<MarkingCrosswalk>(_selectedNode)) return -1;
            var buf = EntityManager.GetBuffer<MarkingCrosswalk>(_selectedNode, isReadOnly: true);
            for (int i = 0; i < buf.Length; i++)
                if (CrosswalkGeometry.TryGetOutline(_endpoints, buf[i], out var corners) && CrosswalkGeometry.Contains(corners, cursor))
                    return i;
            return -1;
        }

        /// <summary>A click while placing: a crosswalk at the preview.</summary>
        private void PlaceCrosswalk()
        {
            if (!_cwPreviewValid) return;
            var a = _cwPreviewA;
            var b = _cwPreviewB;
            a.id = MarkingEdits.NextFreePointId(EntityManager, _selectedNode);
            var (ea, ga) = MarkingEdits.AddFreePoint(EntityManager, _selectedNode, a);
            b.id = MarkingEdits.NextFreePointId(EntityManager, _selectedNode);
            var (eb, gb) = MarkingEdits.AddFreePoint(EntityManager, _selectedNode, b);
            if (!EntityManager.HasBuffer<MarkingCrosswalk>(_selectedNode)) EntityManager.AddBuffer<MarkingCrosswalk>(_selectedNode);
            var buf = EntityManager.GetBuffer<MarkingCrosswalk>(_selectedNode);
            buf.Add(new MarkingCrosswalk
            {
                sourceEdge = ea,
                sourceGapIndex = ga,
                targetEdge = eb,
                targetGapIndex = gb,
                width = _currentCrosswalkWidth,
                style = (int)_currentCrosswalkStyle,
            });
            EntityManager.MarkUpdated(_selectedNode);
            RefreshEndpoints();
            _lastCreatedCrosswalk = buf.Length - 1;
            _lastCreatedCrosswalkTick++;
            log.Debug($"tool: crosswalk #{buf.Length - 1} placed ({_currentCrosswalkStyle}, {_currentCrosswalkWidth:0.0} m)");
        }

        // The panel opens a crosswalk that was just placed.
        private int _lastCreatedCrosswalk = -1;
        private int _lastCreatedCrosswalkTick;
        public int LastCreatedCrosswalk => _lastCreatedCrosswalk;
        public int LastCreatedCrosswalkTick => _lastCreatedCrosswalkTick;

        /// <summary>Right-click on a crosswalk (placing or editing): remove it.</summary>
        private bool TryRemoveHoveredCrosswalk()
        {
            int i = _hoveredCrosswalkInGame >= 0 ? _hoveredCrosswalkInGame
                : EditMode && _editCrosswalk >= 0 && _editHoverKind != EditHoverKind.None ? _editCrosswalk : -1;
            if (i < 0) return false;
            MarkingEdits.DeleteCrosswalk(EntityManager, _selectedNode, i);
            if (_editCrosswalk == i) _editCrosswalk = -1;
            else if (_editCrosswalk > i) _editCrosswalk--;
            RefreshEndpoints();
            _editHoverKind = EditHoverKind.None;
            log.Debug($"tool: crosswalk #{i} removed");
            return true;
        }

        /// <summary>The ends moved together to the road spot under <paramref name="p"/>, kept on
        /// their kerbs. Only for crosswalks whose ends are free points on the same road.</summary>
        private bool TrySlide(MarkingCrosswalk cw, float3 p, out float3 na, out float3 nb)
        {
            na = nb = default;
            if (!TryGetFreePoint(cw.sourceEdge, cw.sourceGapIndex, out var fa) || !TryGetFreePoint(cw.targetEdge, cw.targetGapIndex, out var fb)) return false;
            if (fa.edge != fb.edge || fa.IsInterior(_selectedNode)) return false;
            if (!MarkingFreePointGeometry.TryCreate(EntityManager, _selectedNode, p, 0, out var at) || at.edge != fa.edge) return false;
            fa.t = at.t;
            fb.t = at.t;
            return MarkingFreePointGeometry.TryResolve(EntityManager, _selectedNode, fa, out na, out _)
                && MarkingFreePointGeometry.TryResolve(EntityManager, _selectedNode, fb, out nb, out _);
        }

        private bool TryGetFreePoint(Entity edge, int gap, out MarkingFreePoint point)
        {
            point = default;
            if (MarkingEndpointExtractor.KindOf(gap) != MarkingEndpointExtractor.AnchorKind.Free) return false;
            if (!EntityManager.HasBuffer<MarkingFreePoint>(_selectedNode)) return false;
            var buf = EntityManager.GetBuffer<MarkingFreePoint>(_selectedNode, isReadOnly: true);
            int id = gap - MarkingEndpointExtractor.kFreePointGapBase;
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].id == id && buf[i].edge == edge) { point = buf[i]; return true; }
            return false;
        }

        private void SetFreePointT(Entity edge, int gap, float t)
        {
            var buf = EntityManager.GetBuffer<MarkingFreePoint>(_selectedNode);
            int id = gap - MarkingEndpointExtractor.kFreePointGapBase;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].id != id || buf[i].edge != edge) continue;
                var p = buf[i];
                p.t = t;
                buf[i] = p;
                return;
            }
        }

        /// <summary>Writes a finished width or slide drag of the open crosswalk.</summary>
        private void CommitCrosswalkDrag(DragKind kind)
        {
            if (!TryGetCrosswalk(_editCrosswalk, out var cw) || !CrosswalkGeometry.TryGetEnds(_endpoints, cw, out var a, out var b)) return;
            if (kind == DragKind.CrosswalkWidth)
            {
                var buf = EntityManager.GetBuffer<MarkingCrosswalk>(_selectedNode);
                cw.width = WidthAt(a, b, _placePos);
                buf[_editCrosswalk] = cw;
                EntityManager.MarkUpdated(_selectedNode);
                log.Debug($"tool: crosswalk #{_editCrosswalk} width → {cw.width:0.0} m");
                return;
            }
            if (!MarkingFreePointGeometry.TryCreate(EntityManager, _selectedNode, _placePos, 0, out var at)) return;
            if (!TryGetFreePoint(cw.sourceEdge, cw.sourceGapIndex, out var fa) || at.edge != fa.edge) return;
            SetFreePointT(cw.sourceEdge, cw.sourceGapIndex, at.t);
            SetFreePointT(cw.targetEdge, cw.targetGapIndex, at.t);
            EntityManager.MarkUpdated(_selectedNode);
            RefreshEndpoints();
            log.Debug($"tool: crosswalk #{_editCrosswalk} moved along the road");
        }

        /// <summary>Handle positions of the open crosswalk: the middle of each long side (width)
        /// and the centre (slide).</summary>
        public float3 CrosswalkSideHandle(int side)
        {
            var mid = (_editCwA + _editCwB) * 0.5f;
            return mid + CrosswalkGeometry.Across(_editCwA, _editCwB) * (_editCwWidth * 0.5f * (side == 0 ? 1f : -1f));
        }

        public float3 CrosswalkMoveHandle => (_editCwA + _editCwB) * 0.5f;

        /// <summary>Hover candidates in crosswalk editing (called from UpdateEditHover).</summary>
        private void ConsiderCrosswalkHover(float3 cursor, ref float best)
        {
            if (!_editCwValid) return;
            for (int side = 0; side < 2; side++)
            {
                float sq = PolygonUtils.DistSqXZ(CrosswalkSideHandle(side), cursor);
                if (sq < best) { best = sq; _editHoverKind = EditHoverKind.CrosswalkWidth; _editHoverIndex = side; }
            }
            if (CanSlide())
            {
                float sq = PolygonUtils.DistSqXZ(CrosswalkMoveHandle, cursor);
                if (sq < best) { best = sq; _editHoverKind = EditHoverKind.CrosswalkMove; _editHoverIndex = _editCrosswalk; }
            }
        }

        public bool CanSlide()
            => TryGetFreePoint(_editCwData.sourceEdge, _editCwData.sourceGapIndex, out var fa)
               && TryGetFreePoint(_editCwData.targetEdge, _editCwData.targetGapIndex, out var fb)
               && fa.edge == fb.edge && !fa.IsInterior(_selectedNode);

        /// <summary>Points the Crosswalks tab can drag: the ends of the open crosswalk.</summary>
        private bool IsOpenCrosswalkEnd(MarkingEndpoint ep)
            => _editCwValid && ((ep.edge == _editCwData.sourceEdge && ep.gapIndex == _editCwData.sourceGapIndex)
                                || (ep.edge == _editCwData.targetEdge && ep.gapIndex == _editCwData.targetGapIndex));
    }
}
