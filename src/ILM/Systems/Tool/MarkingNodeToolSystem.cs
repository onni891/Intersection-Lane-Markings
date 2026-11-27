using System.Collections.Generic;
using Colossal.Logging;
using Game;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Systems.Emission;
using ILM.Systems.Topology;
using ILM.Utilities;

namespace ILM.Systems.Tool
{
    /// <summary>
    /// The marking tool: per-node line and area editing, toggled by
    /// <see cref="MarkingToolHotkeySystem"/>. The state machine follows Traffic's
    /// LaneConnectorToolSystem:
    ///
    ///   Default: no node selected; click a node to select it.
    ///   NodeSelected: dots shown; click a dot to start a line.
    ///   SourceSelected: click a second dot to create the line, or remove it if it exists.
    ///   AreaSelecting: placing the vertices of an area polygon.
    ///
    /// Cancel (Esc) steps back one state. Every state transition is logged.
    /// Area mode lives in MarkingNodeToolSystem.Area.cs, cursor hit-testing in
    /// MarkingNodeToolSystem.HitTest.cs.
    /// </summary>
    public partial class MarkingNodeToolSystem : ToolBaseSystem
    {
        private static readonly ILog log = Mod.log;

        public override string toolID => "MarkingNodeTool";

        private MarkingToolState _state;
        private Entity _selectedNode;
        private List<MarkingEndpoint> _endpoints = new List<MarkingEndpoint>();
        // Corner anchors where the kerbs of neighbouring edges meet. Used only by area mode.
        private List<MarkingCornerAnchor> _cornerAnchors = new List<MarkingCornerAnchor>();

        // Line-crossing anchors for area mode. Re-extracted whenever the node's line topology
        // hash changes, since adding or deleting a line or editing curvature moves the crossings
        // (see RefreshIntersectionAnchorsIfStale).
        private List<MarkingIntersectionAnchor> _lineIntersections = new List<MarkingIntersectionAnchor>();
        private int _lineIntersectionsHash;

        // Vertices placed so far in AreaSelecting; committed as a MarkingArea on the closing
        // click and cleared on exit, cancel or commit.
        private readonly List<AreaPolygonVertex> _areaPolygon = new List<AreaPolygonVertex>();
        // Candidate under the cursor in AreaSelecting.
        private AreaCandidate _areaHover = AreaCandidate.None;
        private int _sourceIdx = -1;
        private int _hoverIdx = -1;
        private int _lastLoggedHoverIdx = -2; // -2 = "never logged"; -1 = "no hover"
        private float3 _cursorWorldPos;
        private Entity _hoveredNode; // raycast result while tool is active; Entity.Null when no node under cursor

        // Style for the next line drawn. Kept when the tool is closed and reopened within a
        // session, so a style picked once stays picked.
        private MarkingStyle _currentStyle = MarkingStyle.Solid;
        private ProxyAction _cycleStyleAction;

        private ProxyAction _enterAreaAction;
        // Fill style for the next closed area, the area counterpart of _currentStyle.
        private ProxyAction _cycleAreaStyleAction;
        private int _currentAreaStyle;

        // Line mode, kept across node selections in a session:
        //   Points: clicks on dots draw lines between them.
        //   Free:   a click on bare road places a free point (MarkingFreePoint) and continues
        //           the line from the last point, so lines can go anywhere. Existing dots still
        //           snap; right-click on a free point removes it.
        //   Edit:   nothing is drawn. Click a line to show its curve handles, drag the handles
        //           or a free point; right-click a free point removes it. Only editable things
        //           are shown, so the anchor dots are hidden.
        // Handles and point dragging live only in Edit, so a click in the draw modes always
        // draws.
        public enum LineMode { Points = 0, Free = 1, Edit = 2 }
        private LineMode _mode = LineMode.Points;
        private ProxyAction _freeDrawAction;

        // A click in NodeSelected that misses every dot is hit-tested against committed lines,
        // and the panel expands the row of the line that was hit. The panel watches the tick,
        // so clicking the same line twice still registers. Line -1 means empty space: collapse
        // all rows.
        private int _lastClickedLine = -1;
        private int _lastClickedTick;

        // Line under the cursor in NodeSelected, for the overlay highlight. Kept apart from the
        // UI hover so hovering the panel doesn't override it; the overlay prefers the UI hover.
        private int _hoveredLineInGame = -1;

        // Area the cursor is inside (NodeSelected only, and only when no dot or line is hovered,
        // since those are more specific). -1 means none. Highlights the panel row and the overlay.
        private int _hoveredAreaInGame = -1;

        public MarkingToolState ToolState => _state;
        public Entity SelectedNode => _selectedNode;
        public Entity HoveredNode => _hoveredNode;
        public IReadOnlyList<MarkingEndpoint> Endpoints => _endpoints;
        public IReadOnlyList<MarkingCornerAnchor> CornerAnchors => _cornerAnchors;
        public IReadOnlyList<MarkingIntersectionAnchor> LineIntersections => _lineIntersections;
        public IReadOnlyList<AreaPolygonVertex> AreaPolygon => _areaPolygon;
        public AreaCandidate AreaHover => _areaHover;
        public int SourceEndpointIndex => _sourceIdx;
        public int HoveredEndpointIndex => _hoverIdx;
        public float3 CursorWorldPos => _cursorWorldPos;
        public MarkingStyle CurrentStyle => _currentStyle;
        public int CurrentAreaStyle => _currentAreaStyle;
        public int LastClickedLine => _lastClickedLine;
        public int LastClickedTick => _lastClickedTick;
        public int HoveredLineInGame => _hoveredLineInGame;
        public int HoveredAreaInGame => _hoveredAreaInGame;
        public LineMode Mode => _mode;
        // The Points tab always edits: points are placed in free draw on the Lines tab.
        public bool FreeDraw => _mode == LineMode.Free && _editTarget != EditTarget.Points;
        public bool EditMode => _mode == LineMode.Edit || _editTarget == EditTarget.Points;

        /// <summary>Turns free draw on or off (off goes back to snapping to points).</summary>
        public void SetFreeDraw(bool on) => SetMode(on ? LineMode.Free : LineMode.Points);

        /// <summary>Switches the line mode. Ends an open chain or line and any drag.</summary>
        public void SetMode(LineMode mode)
        {
            if (mode == _mode) return;
            _mode = mode;
            if (_state == MarkingToolState.SourceSelected) DropSource();
            _drag = DragKind.None;
            _dragMoved = false;
            log.Debug($"tool: line mode → {_mode}");
        }

        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;

        // Entry points for the panel controls that mirror the hotkeys (ILMUISystem).
        // They are called from TriggerBinding handlers on the main thread, in the same phase as
        // OnUpdate, so no synchronisation is needed. The area-mode ones are in the Area part.

        /// <summary>Sets the style for the next line drawn (the state the style hotkey cycles).</summary>
        public void SetCurrentStyle(MarkingStyle style)
        {
            _currentStyle = style;
            log.Debug($"tool: UI set next-line style → {_currentStyle}");
        }

        /// <summary>Sets the fill style for the next closed area (the state the area-style hotkey
        /// cycles). A disabled style falls back to solid concrete.</summary>
        public void SetCurrentAreaStyle(int styleId)
        {
            _currentAreaStyle = math.clamp(styleId, 0, MarkingAreaEmissionSystem.kStyleCount - 1);
            if (!MarkingAreaEmissionSystem.IsStyleEnabled(_currentAreaStyle))
                _currentAreaStyle = MarkingAreaEmissionSystem.kStyleSolidConcrete;
            log.Debug($"tool: UI set next-area style → {_currentAreaStyle}");
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            // The actions stay disabled until OnStartRunning, so their keys aren't intercepted
            // while the tool is inactive.
            if (Mod.Settings != null)
            {
                _cycleStyleAction = Mod.Settings.GetAction(ILMSetting.CycleMarkingStyle);
                _enterAreaAction = Mod.Settings.GetAction(ILMSetting.EnterAreaMode);
                _cycleAreaStyleAction = Mod.Settings.GetAction(ILMSetting.CycleAreaStyle);
                _freeDrawAction = Mod.Settings.GetAction(ILMSetting.ToggleFreeDraw);
                InitHistoryActions();
            }
            log.Info($"MarkingNodeToolSystem: OnCreate — toolID='{toolID}' registered with ToolSystem.tools (count={m_ToolSystem.tools.Count}), cycleStyleAction={(_cycleStyleAction != null ? "OK" : "NULL")}, enterAreaAction={(_enterAreaAction != null ? "OK" : "NULL")}, cycleAreaStyleAction={(_cycleAreaStyleAction != null ? "OK" : "NULL")}");
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();
            ResetSelection();
            LoadSnapSetting();
            // The base UpdateActions is empty, so applyAction and friends stay disabled from the
            // last ResetActions and clicks do nothing. Enable them the way DefaultToolSystem does,
            // minus its internal DeferStateUpdating batching, which isn't accessible from a mod.
            applyAction.shouldBeEnabled = true;
            secondaryApplyAction.shouldBeEnabled = true;
            cancelAction.shouldBeEnabled = true;
            SetModActionsEnabled(true);
            log.Debug($"MarkingNodeToolSystem: activated, actions enabled (apply={applyAction != null}, cancel={cancelAction != null}, cycleStyle={_cycleStyleAction != null})");
        }

        protected override void OnStopRunning()
        {
            log.Debug($"MarkingNodeToolSystem: deactivated (state was {_state}, selectedNode #{_selectedNode.Index})");
            SetModActionsEnabled(false);
            ResetSelection();
            base.OnStopRunning();
        }

        /// <summary>Enables or disables the mod's own tool hotkeys. Each may be null when the
        /// settings or the binding failed to resolve.</summary>
        private void SetModActionsEnabled(bool enabled)
        {
            if (_cycleStyleAction != null) _cycleStyleAction.shouldBeEnabled = enabled;
            if (_enterAreaAction != null) _enterAreaAction.shouldBeEnabled = enabled;
            if (_cycleAreaStyleAction != null) _cycleAreaStyleAction.shouldBeEnabled = enabled;
            if (_freeDrawAction != null) _freeDrawAction.shouldBeEnabled = enabled;
            SetHistoryActionsEnabled(enabled);
        }

        private void ResetSelection()
        {
            // An outline in progress leaves no stray points behind.
            if (_selectedNode != Entity.Null && EntityManager.Exists(_selectedNode)) DiscardDraftPoints();
            else _draftFreePoints.Clear();
            _editArea = -1;
            _state = MarkingToolState.Default;
            _selectedNode = Entity.Null;
            _endpoints.Clear();
            _cornerAnchors.Clear();
            _areaPolygon.Clear();
            _areaHover = AreaCandidate.None;
            _sourceIdx = -1;
            _hoverIdx = -1;
            _lastLoggedHoverIdx = -2;
            _hoveredNode = Entity.Null;
            _lastClickedLine = -1; // so a new selection doesn't expand a stale panel row
            _lastClickedTick++;
            _hoveredLineInGame = -1;
            _editLine = -1;
            _drag = DragKind.None;
            _dragMoved = false;
        }

        public override void InitializeRaycast()
        {
            base.InitializeRaycast();
            m_ToolRaycastSystem.typeMask = TypeMask.Net | TypeMask.Terrain;
            m_ToolRaycastSystem.netLayerMask = Layer.Road | Layer.PublicTransportRoad | Layer.SubwayTrack | Layer.TrainTrack | Layer.TramTrack;
            m_ToolRaycastSystem.raycastFlags |= RaycastFlags.SubElements;
            m_ToolRaycastSystem.collisionMask = CollisionMask.OnGround | CollisionMask.Overground;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            bool hitSomething = GetRaycastResult(out Entity hitEntity, out RaycastHit hit);
            bool hitNode = hitSomething && EntityManager.HasComponent<Node>(hitEntity);
            // A road stretch: selectable for mid-road markings.
            bool hitEdge = hitSomething && !hitNode && MarkingEdgeAnchors.IsEdgeOwner(EntityManager, hitEntity);
            _cursorWorldPos = hitSomething ? hit.m_HitPosition : float3.zero;
            _hoveredNode = hitNode || hitEdge ? hitEntity : Entity.Null;
            // Draw-mode hover and the area candidate first: snapping only applies when the
            // cursor isn't already on a dot.
            _hoverIdx = (!EditMode && _state != MarkingToolState.Default && _state != MarkingToolState.AreaSelecting && hitSomething)
                ? FindHoveredEndpoint(_cursorWorldPos)
                : -1;
            // Lines can change from the panel while an area is being drawn.
            if (_state == MarkingToolState.AreaSelecting) RefreshIntersectionAnchorsIfStale();
            _areaHover = (_state == MarkingToolState.AreaSelecting && hitSomething) ? FindHoveredAreaCandidate(_cursorWorldPos) : AreaCandidate.None;
            // The snapped place position, then the open area's outline and handles (its drag
            // preview follows the snap), then the one edit target under the cursor.
            UpdateSnap(hitSomething);
            RefreshEditArea();
            UpdateEditHover(hitSomething);
            if (EditMode)
                _hoverIdx = _editHoverKind == EditHoverKind.FreePoint ? _editHoverIndex : -1;

            // Skipped in SourceSelected, where the cursor is aiming at a target dot and line
            // highlights would be noise.
            // In edit mode only the edit hover counts, so a line under a handle isn't lit.
            if (EditMode)
                _hoveredLineInGame = _editHoverKind == EditHoverKind.Line ? _editHoverIndex : -1;
            else
                _hoveredLineInGame = (_state == MarkingToolState.NodeSelected && _hoverIdx < 0 && hitSomething)
                    ? HitTestLines(_cursorWorldPos)
                    : -1;

            // Dots and lines are more specific targets, so they take priority over areas.
            if (EditMode)
                _hoveredAreaInGame = _editHoverKind == EditHoverKind.Area ? _editHoverIndex : -1;
            else
                _hoveredAreaInGame = (_state == MarkingToolState.NodeSelected && _hoverIdx < 0 && _hoveredLineInGame < 0 && hitSomething)
                    ? HitTestAreas(_cursorWorldPos)
                    : -1;

            LogHoverChange();

            // Undo / redo hotkeys and change tracking for the selected owner.
            TickRestoreWindow();
            UpdateHistory();

            // Works in any state; affects only lines created afterwards.
            // Ctrl+Y (redo) shares the Y key; skip the cycle then.
            if (_cycleStyleAction != null && _cycleStyleAction.WasPerformedThisFrame() && !RedoPressed)
            {
                _currentStyle = NextStyle(_currentStyle);
                log.Debug($"tool: cycled style → {_currentStyle}");
            }

            if (_cycleAreaStyleAction != null && _cycleAreaStyleAction.WasPerformedThisFrame())
            {
                _currentAreaStyle = MarkingAreaEmissionSystem.NextEnabledStyle(_currentAreaStyle);
                log.Debug($"tool: cycled area style → {_currentAreaStyle}");
            }

            // The area hotkey enters area mode, and pressing it again leaves without committing.
            if (_enterAreaAction != null && _enterAreaAction.WasPerformedThisFrame())
            {
                if (_state == MarkingToolState.NodeSelected)
                {
                    EnterAreaSelecting();
                    log.Debug($"area: entered AreaSelecting on node #{_selectedNode.Index}");
                }
                else if (_state == MarkingToolState.AreaSelecting)
                {
                    log.Debug($"area: cancelled AreaSelecting via hotkey (had {_areaPolygon.Count} vertices)");
                    LeaveAreaSelecting();
                }
            }

            // Cancel steps back one state; from Default it closes the tool.
            if (cancelAction.WasPressedThisFrame())
            {
                HandleCancel();
                return inputDeps;
            }

            // Right-click in area mode undoes the last vertex, as in IMT.
            if (_state == MarkingToolState.AreaSelecting && secondaryApplyAction.WasPressedThisFrame())
            {
                AreaUndoVertex();
                return inputDeps;
            }

            if (_freeDrawAction != null && _freeDrawAction.WasPerformedThisFrame())
                SetFreeDraw(!FreeDraw);

            // Right-click removes a point: in edit mode a vertex of the open area (the area keeps
            // its other vertices), otherwise the hovered free point with its lines. Lines are
            // removed by repeating the create gesture (see TogglePair) or from the panel.
            if (_state != MarkingToolState.Default && secondaryApplyAction.WasPressedThisFrame())
            {
                if (EditMode && _editTarget == EditTarget.Areas) TryRemoveHoveredAreaVertex();
                else TryRemoveHoveredFreePoint();
                return inputDeps;
            }

            // Handle and free-point drags take the mouse before a normal click.
            if (UpdateDrag(hitSomething))
                return inputDeps;

            if (applyAction.WasPressedThisFrame())
                HandleApply(hitSomething, hitNode, hitEdge, hitEntity);

            return inputDeps;
        }

        /// <summary>Logs hover changes only, not every frame.</summary>
        private void LogHoverChange()
        {
            if (_hoverIdx == _lastLoggedHoverIdx) return;
            if (_hoverIdx >= 0)
            {
                var ep = _endpoints[_hoverIdx];
                log.Debug($"tool: hover endpoint idx={_hoverIdx} edge=#{ep.edge.Index} gap={ep.gapIndex}");
            }
            else if (_lastLoggedHoverIdx >= 0)
            {
                log.Debug($"tool: hover cleared (was idx={_lastLoggedHoverIdx})");
            }
            _lastLoggedHoverIdx = _hoverIdx;
        }

        private void HandleCancel()
        {
            if (_state == MarkingToolState.AreaSelecting)
            {
                // First Esc drops a half-drawn outline; Esc with nothing placed leaves area mode.
                if (_areaPolygon.Count > 0)
                {
                    log.Debug($"area: Esc cleared the unfinished outline ({_areaPolygon.Count} vertices)");
                    _areaPolygon.Clear();
                    DiscardDraftPoints();
                }
                else
                {
                    log.Debug("area: left AreaSelecting via Esc");
                    LeaveAreaSelecting();
                }
            }
            else if (_state == MarkingToolState.SourceSelected)
            {
                log.Debug($"tool: cancel — clearing source #{_sourceIdx}");
                DropSource();
            }
            else if (_state == MarkingToolState.NodeSelected && EditMode && (_editLine >= 0 || _editArea >= 0))
            {
                // Edit mode: the first Esc puts the open line or area down, the next deselects.
                log.Debug($"tool: cancel — closing edit line #{_editLine} / area #{_editArea}");
                SetClickedLine(-1);
            }
            else if (_state == MarkingToolState.NodeSelected)
            {
                log.Debug($"tool: cancel — deselecting node #{_selectedNode.Index}");
                ResetSelection();
            }
            else
            {
                log.Debug("tool: cancel from Default — deactivating tool");
                m_ToolSystem.activeTool = m_DefaultToolSystem;
            }
        }

        private void HandleApply(bool hitSomething, bool hitNode, bool hitEdge, Entity hitEntity)
        {
            int hitIndex = hitSomething ? hitEntity.Index : -1;
            // Tells apart a missing apply event, a click with no raycast hit, and a hit on
            // something that isn't a node.
            log.Debug($"tool: LMB fired — state={_state}, hitSomething={hitSomething}, hitEntity=#{hitIndex}, hasNode={hitNode}");
            switch (_state)
            {
                case MarkingToolState.Default:
                    if (hitNode || hitEdge)
                        SelectNode(hitEntity);
                    else
                        log.Debug($"tool: click in Default ignored (hit #{hitIndex}, no Node)");
                    break;

                case MarkingToolState.NodeSelected when EditMode:
                    HandleEditClick(hitSomething, hitNode, hitEdge, hitEntity);
                    break;

                case MarkingToolState.NodeSelected:
                    if (_hoverIdx >= 0)
                    {
                        _sourceIdx = _hoverIdx;
                        _state = MarkingToolState.SourceSelected;
                        log.Debug($"tool: source endpoint chosen — idx={_sourceIdx} edge=#{_endpoints[_sourceIdx].edge.Index} gap={_endpoints[_sourceIdx].gapIndex}");
                    }
                    else if (hitNode && hitEntity != _selectedNode)
                    {
                        SelectNode(hitEntity);
                    }
                    // Another road stretch. Roads leading into the selected junction stay part of
                    // its editing area (free draw places points on them), so only unrelated roads
                    // switch the selection; Esc first to pick one of those.
                    else if (hitEdge && hitEntity != _selectedNode && !FreeDraw && !IsRoadOfSelectedJunction(hitEntity))
                    {
                        SelectNode(hitEntity);
                    }
                    else if (FreeDraw && hitSomething)
                    {
                        // Start a chain from a new free point.
                        int idx = PlaceFreePoint(_placePos);
                        if (idx >= 0)
                        {
                            _sourceIdx = idx;
                            _state = MarkingToolState.SourceSelected;
                        }
                    }
                    else if (hitSomething)
                    {
                        // Neither a dot nor another node: expand the panel row of the line
                        // under the cursor, if any.
                        int clickedLine = HitTestLines(_cursorWorldPos);
                        _lastClickedLine = clickedLine;
                        _lastClickedTick++;
                        log.Debug($"tool: click on line #{clickedLine} (or -1 = empty space)");
                    }
                    else
                    {
                        log.Debug("tool: click in NodeSelected — no dot/raycast, ignored");
                    }
                    break;

                case MarkingToolState.SourceSelected:
                    if (_hoverIdx >= 0 && _hoverIdx != _sourceIdx)
                    {
                        TogglePair(_endpoints[_sourceIdx], _endpoints[_hoverIdx]);
                        // Free draw continues the chain from the dot just reached.
                        if (FreeDraw) _sourceIdx = _hoverIdx;
                        else DropSource();
                    }
                    else if (FreeDraw && _hoverIdx < 0 && hitSomething)
                    {
                        var src = _endpoints[_sourceIdx];
                        int idx = PlaceFreePoint(_placePos);
                        if (idx >= 0)
                        {
                            // The endpoint list was re-extracted; find the source again by identity.
                            int srcIdx = IndexOfEndpoint(src.edge, src.gapIndex);
                            if (srcIdx >= 0) TogglePair(_endpoints[srcIdx], _endpoints[idx]);
                            _sourceIdx = idx;
                        }
                    }
                    else
                    {
                        log.Debug("tool: click in SourceSelected — no different target dot hovered, ignored");
                    }
                    break;

                case MarkingToolState.AreaSelecting:
                    if (!_areaHover.IsValid)
                    {
                        // Bare road: place a point there, as in free draw.
                        if (hitSomething) AreaPlaceFreeVertex();
                    }
                    else if (AreaCanCloseOn(_areaHover))
                        AreaClose();
                    else
                        AreaAddVertex(_areaHover);
                    break;
            }
        }

        /// <summary>A click in edit mode that no drag took. It acts only on the highlighted edit
        /// target (see UpdateEditHover): a line opens it (Lines tab), an area opens it (Areas
        /// tab), a "+" adds a vertex. A click on nothing editable selects another junction or
        /// an unrelated road, or else puts the open line or area down.</summary>
        private void HandleEditClick(bool hitSomething, bool hitNode, bool hitEdge, Entity hitEntity)
        {
            switch (_editHoverKind)
            {
                case EditHoverKind.AreaInsert:
                    InsertAreaVertexAt(_editHoverIndex);
                    return;
                case EditHoverKind.Area:
                    if (_editHoverIndex != _editArea) OpenEditArea(_editHoverIndex);
                    return;
                case EditHoverKind.Line:
                    SetClickedLine(_editHoverIndex);
                    log.Debug($"tool: edit — opened line #{_editHoverIndex}");
                    return;
                case EditHoverKind.None:
                    break;
                default:
                    // A handle, vertex or point pressed and released without moving: nothing.
                    return;
            }
            if (!hitSomething) return;
            if (hitNode && hitEntity != _selectedNode)
            {
                SelectNode(hitEntity);
                return;
            }
            if (hitEdge && hitEntity != _selectedNode && !IsRoadOfSelectedJunction(hitEntity))
            {
                SelectNode(hitEntity);
                return;
            }
            if (_editTarget == EditTarget.Areas)
            {
                if (_editArea >= 0) log.Debug($"tool: edit — closed area #{_editArea}");
                _editArea = -1;
            }
            else if (_editTarget == EditTarget.Lines)
            {
                SetClickedLine(-1);
                log.Debug("tool: edit — closed the open line");
            }
        }

        /// <summary>Opens a line for editing from the world (-1 closes it). The panel follows
        /// through lastClickedLine/Tick and expands or collapses the row.</summary>
        private void SetClickedLine(int lineIndex)
        {
            _editLine = lineIndex;
            _editArea = -1;
            _lastClickedLine = lineIndex;
            _lastClickedTick++;
        }

        private void SelectNode(Entity node)
        {
            _selectedNode = node;
            // Logs a per-edge, per-lane breakdown, to debug missing endpoints from a user's log.
            // Runs only on node click.
            _endpoints = MarkingEndpointExtractor.Extract(EntityManager, node, log: true);
            _cornerAnchors = MarkingEndpointExtractor.ExtractCornerAnchors(EntityManager, node);
            RefreshIntersectionAnchors();
            _sourceIdx = -1;
            _editLine = -1;
            _state = MarkingToolState.NodeSelected;
            int existingLines = EntityManager.HasBuffer<MarkingLine>(node)
                ? EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true).Length
                : 0;
            int existingSegs = EntityManager.HasBuffer<MarkingSegment>(node)
                ? EntityManager.GetBuffer<MarkingSegment>(node, isReadOnly: true).Length
                : 0;
            log.Debug($"tool: selected node #{node.Index} — {_endpoints.Count} endpoint(s), {_cornerAnchors.Count} corner(s), {existingLines} line(s), {existingSegs} segment(s)");
        }

        /// <summary>Forgets the half-picked line and goes back to NodeSelected.</summary>
        private void DropSource()
        {
            _sourceIdx = -1;
            _state = MarkingToolState.NodeSelected;
        }

        /// <summary>Removes the line between the two endpoints (in either direction) if it
        /// exists, otherwise adds it, as in Traffic. On removal,
        /// MarkingTopologySystem.OnLineRemoved reindexes the segment buffer so per-segment
        /// overrides on the other lines survive.</summary>
        private void TogglePair(MarkingEndpoint src, MarkingEndpoint dst)
        {
            if (!EntityManager.HasBuffer<MarkingLine>(_selectedNode))
                EntityManager.AddBuffer<MarkingLine>(_selectedNode);
            var buf = EntityManager.GetBuffer<MarkingLine>(_selectedNode);

            for (int i = 0; i < buf.Length; i++)
            {
                var p = buf[i];
                bool sameDirection = LineStartsAt(p, src) && LineEndsAt(p, dst);
                bool swappedSides = LineStartsAt(p, dst) && LineEndsAt(p, src);
                if (sameDirection || swappedSides)
                {
                    log.Debug($"tool: toggled OFF line #{i} on node #{_selectedNode.Index}");
                    buf.RemoveAt(i);
                    MarkingTopologySystem.OnLineRemoved(EntityManager, _selectedNode, i);
                    EntityManager.MarkUpdated(_selectedNode);
                    return;
                }
            }

            buf.Add(new MarkingLine
            {
                sourceEdge = src.edge,
                sourceGapIndex = src.gapIndex,
                targetEdge = dst.edge,
                targetGapIndex = dst.gapIndex,
                style = (int)_currentStyle,
                // Free-draw segments start straight; built-in dots get the arc that follows
                // their lanes.
                curvature = IsFree(src) || IsFree(dst) ? 0f : MarkingCurveBuilder.AdaptivePullFactor(src, dst),
            });
            EntityManager.MarkUpdated(_selectedNode);
            log.Debug($"tool: toggled ON line #{buf.Length - 1} on node #{_selectedNode.Index} style={_currentStyle} — "
                + $"src(edge=#{src.edge.Index} gap={src.gapIndex}) → "
                + $"dst(edge=#{dst.edge.Index} gap={dst.gapIndex})");
        }

        /// <summary>The road is connected to the selected junction.</summary>
        private bool IsRoadOfSelectedJunction(Entity road)
        {
            if (!EntityManager.HasComponent<Node>(_selectedNode) || !EntityManager.HasComponent<Edge>(road)) return false;
            var e = EntityManager.GetComponentData<Edge>(road);
            return e.m_Start == _selectedNode || e.m_End == _selectedNode;
        }

        /// <summary>The selection is a road stretch rather than a junction.</summary>
        public bool SelectedIsRoad => MarkingEdgeAnchors.IsEdgeOwner(EntityManager, _selectedNode);

        private static bool IsFree(MarkingEndpoint ep)
            => MarkingEndpointExtractor.KindOf(ep.gapIndex) == MarkingEndpointExtractor.AnchorKind.Free;

        private int IndexOfEndpoint(Entity edge, int gapIndex)
        {
            for (int i = 0; i < _endpoints.Count; i++)
                if (_endpoints[i].edge == edge && _endpoints[i].gapIndex == gapIndex) return i;
            return -1;
        }

        /// <summary>Called by the panel after it changed the node's anchors (reset): drops a
        /// half-drawn line and re-extracts the dots.</summary>
        public void OnNodeEditedExternally()
        {
            if (_selectedNode == Entity.Null) return;
            if (_state == MarkingToolState.SourceSelected) DropSource();
            RefreshEndpoints();
        }

        /// <summary>Re-extracts the endpoints after a free point was added or removed.</summary>
        private void RefreshEndpoints()
        {
            _endpoints = MarkingEndpointExtractor.Extract(EntityManager, _selectedNode);
        }

        /// <summary>Places a free point at the cursor and returns its index in the refreshed
        /// endpoint list, or -1 when the click is not on this junction's road surface.</summary>
        private int PlaceFreePoint(float3 cursor)
        {
            int id = MarkingEdits.NextFreePointId(EntityManager, _selectedNode);
            if (!MarkingFreePointGeometry.TryCreate(EntityManager, _selectedNode, cursor, id, out var point))
            {
                log.Debug("tool: free point — click is not on this junction's road, ignored");
                return -1;
            }
            var (edge, gap) = MarkingEdits.AddFreePoint(EntityManager, _selectedNode, point);
            RefreshEndpoints();
            log.Debug($"tool: free point #{id} placed on {(point.IsInterior(_selectedNode) ? "junction" : $"edge #{edge.Index}")}");
            return IndexOfEndpoint(edge, gap);
        }

        private void TryRemoveHoveredFreePoint()
        {
            if (_hoverIdx < 0 || _hoverIdx >= _endpoints.Count) return;
            var ep = _endpoints[_hoverIdx];
            if (!IsFree(ep)) return;
            if (!MarkingEdits.RemoveFreePoint(EntityManager, _selectedNode, ep.edge, ep.gapIndex)) return;
            if (_state == MarkingToolState.SourceSelected) DropSource();
            RefreshEndpoints();
            _hoverIdx = -1;
            log.Debug($"tool: free point gap={ep.gapIndex} removed with its lines and areas");
        }

        private static bool LineStartsAt(MarkingLine line, MarkingEndpoint ep)
            => line.sourceEdge == ep.edge && line.sourceGapIndex == ep.gapIndex;

        private static bool LineEndsAt(MarkingLine line, MarkingEndpoint ep)
            => line.targetEdge == ep.edge && line.targetGapIndex == ep.gapIndex;

        /// <summary>Next <see cref="MarkingStyle"/> value, wrapping around. New enum values join
        /// the cycle automatically.</summary>
        private static MarkingStyle NextStyle(MarkingStyle current)
        {
            var values = (MarkingStyle[])System.Enum.GetValues(typeof(MarkingStyle));
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == current) return values[(i + 1) % values.Length];
            }
            return MarkingStyle.Solid;
        }
    }
}
