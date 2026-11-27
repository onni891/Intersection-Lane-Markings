using System.Collections.Generic;
using Game.Input;
using Unity.Entities;
using ILM.Components;
using ILM.Systems.Topology;
using ILM.Utilities;

namespace ILM.Systems.Tool
{
    // Undo / redo for the selected junction or road (Ctrl+Z / Ctrl+Y and the panel buttons).
    //
    // Instead of every edit recording itself, the marking data of the selected owner is watched:
    // once a change has stayed the same for a few frames (topology rewrites the segments a frame
    // after a line edit, and both belong to one step) the state before it goes on the undo
    // stack. So tool clicks, drags and panel edits are all undoable without each having to
    // know about history. Every owner keeps its own stacks for the session.
    public partial class MarkingNodeToolSystem
    {
        private const int kHistoryLimit = 100;
        // Frames a change must stay unchanged before it becomes one undo step.
        private const int kSettleFrames = 6;
        // Frames after an undo / redo during which follow-up rewrites (topology) are absorbed.
        private const int kRestoreSettleFrames = 30;

        private sealed class Snapshot
        {
            public MarkingLine[] lines;
            public MarkingSegment[] segments;
            public MarkingArea[] areas;
            public MarkingAreaVertex[] vertices;
            public MarkingFreePoint[] points;
            public bool hasOverride;
            public MarkingOverride overrideData;
            public int hash;
        }

        private sealed class History
        {
            public readonly List<Snapshot> undo = new List<Snapshot>();
            public readonly List<Snapshot> redo = new List<Snapshot>();
        }

        private readonly Dictionary<Entity, History> _histories = new Dictionary<Entity, History>();
        private Entity _histNode;
        private Snapshot _histCurrent;
        private int _histPendingHash;
        private int _histStableFrames;
        private int _histRestoreFrames;
        private ProxyAction _undoAction;
        private ProxyAction _redoAction;

        public bool CanUndo => _histNode != Entity.Null && _histories.TryGetValue(_histNode, out var h) && h.undo.Count > 0;
        public bool CanRedo => _histNode != Entity.Null && _histories.TryGetValue(_histNode, out var h) && h.redo.Count > 0;

        private void InitHistoryActions()
        {
            if (Mod.Settings == null) return;
            _undoAction = Mod.Settings.GetAction(ILMSetting.UndoMarking);
            _redoAction = Mod.Settings.GetAction(ILMSetting.RedoMarking);
        }

        private void SetHistoryActionsEnabled(bool enabled)
        {
            if (_undoAction != null) _undoAction.shouldBeEnabled = enabled;
            if (_redoAction != null) _redoAction.shouldBeEnabled = enabled;
        }

        /// <summary>True when the redo hotkey fired this frame (its key also carries the style
        /// cycle key, which must then stay quiet).</summary>
        private bool RedoPressed => _redoAction != null && _redoAction.WasPerformedThisFrame();

        /// <summary>Hotkeys, then change tracking. Runs every frame while the tool is active.</summary>
        private void UpdateHistory()
        {
            if (_undoAction != null && _undoAction.WasPerformedThisFrame()) Undo();
            else if (RedoPressed) Redo();

            if (_selectedNode == Entity.Null || !EntityManager.Exists(_selectedNode))
            {
                _histNode = Entity.Null;
                _histCurrent = null;
                return;
            }
            if (_selectedNode != _histNode)
            {
                _histNode = _selectedNode;
                _histCurrent = Take(_selectedNode);
                _histPendingHash = _histCurrent.hash;
                _histStableFrames = 0;
                _histRestoreFrames = 0;
                return;
            }
            TrackChange(force: false);
        }

        /// <summary>Turns a settled change into an undo step. With <paramref name="force"/> a
        /// change still settling is committed now (before an undo, so the undo reverts it).</summary>
        private void TrackChange(bool force)
        {
            if (_histCurrent == null) return;
            int h = HashOf(_selectedNode);
            if (h == _histCurrent.hash) { _histStableFrames = 0; _histPendingHash = h; return; }
            if (h != _histPendingHash) { _histPendingHash = h; _histStableFrames = 0; }
            else _histStableFrames++;
            // Mid-gesture states are not steps: a drag or an outline being drawn.
            bool busy = _drag != DragKind.None || (_state == MarkingToolState.AreaSelecting && _areaPolygon.Count > 0);
            if (!force && (busy || _histStableFrames < kSettleFrames)) return;

            if (_histRestoreFrames > 0)
            {
                // Rewrites right after an undo or redo belong to it: re-baseline only.
                _histCurrent = Take(_selectedNode);
                return;
            }
            var hist = GetHistory(_selectedNode);
            hist.undo.Add(_histCurrent);
            if (hist.undo.Count > kHistoryLimit) hist.undo.RemoveAt(0);
            hist.redo.Clear();
            _histCurrent = Take(_selectedNode);
            log.Debug($"history: step recorded on #{_selectedNode.Index} ({hist.undo.Count} to undo)");
        }

        /// <summary>Called every frame before TrackChange: counts down the restore window.</summary>
        private void TickRestoreWindow()
        {
            if (_histRestoreFrames > 0) _histRestoreFrames--;
        }

        public void Undo() => Step(undo: true);
        public void Redo() => Step(undo: false);

        private void Step(bool undo)
        {
            if (_selectedNode == Entity.Null || _histCurrent == null) return;
            if (_histRestoreFrames == 0) TrackChange(force: true);
            var hist = GetHistory(_selectedNode);
            var from = undo ? hist.undo : hist.redo;
            var to = undo ? hist.redo : hist.undo;
            if (from.Count == 0) return;
            var snap = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(_histCurrent);
            Restore(_selectedNode, snap);
            _histCurrent = snap;
            _histPendingHash = snap.hash;
            _histStableFrames = 0;
            _histRestoreFrames = kRestoreSettleFrames;
            log.Debug($"history: {(undo ? "undo" : "redo")} on #{_selectedNode.Index} ({hist.undo.Count} undo / {hist.redo.Count} redo left)");
        }

        private History GetHistory(Entity node)
        {
            if (!_histories.TryGetValue(node, out var h)) _histories[node] = h = new History();
            return h;
        }

        private static T[] Copy<T>(EntityManager em, Entity e) where T : unmanaged, IBufferElementData
        {
            if (!em.HasBuffer<T>(e)) return System.Array.Empty<T>();
            var buf = em.GetBuffer<T>(e, isReadOnly: true);
            var arr = new T[buf.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = buf[i];
            return arr;
        }

        private static void Write<T>(EntityManager em, Entity e, T[] data) where T : unmanaged, IBufferElementData
        {
            if (!em.HasBuffer<T>(e))
            {
                if (data.Length == 0) return;
                em.AddBuffer<T>(e);
            }
            var buf = em.GetBuffer<T>(e);
            buf.Clear();
            for (int i = 0; i < data.Length; i++) buf.Add(data[i]);
        }

        private Snapshot Take(Entity node)
        {
            var s = new Snapshot
            {
                lines = Copy<MarkingLine>(EntityManager, node),
                segments = Copy<MarkingSegment>(EntityManager, node),
                areas = Copy<MarkingArea>(EntityManager, node),
                vertices = Copy<MarkingAreaVertex>(EntityManager, node),
                points = Copy<MarkingFreePoint>(EntityManager, node),
                hasOverride = EntityManager.HasComponent<MarkingOverride>(node),
            };
            if (s.hasOverride) s.overrideData = EntityManager.GetComponentData<MarkingOverride>(node);
            s.hash = HashOf(node);
            return s;
        }

        private void Restore(Entity node, Snapshot s)
        {
            Write(EntityManager, node, s.lines);
            Write(EntityManager, node, s.segments);
            Write(EntityManager, node, s.areas);
            Write(EntityManager, node, s.vertices);
            Write(EntityManager, node, s.points);
            if (s.hasOverride)
            {
                if (EntityManager.HasComponent<MarkingOverride>(node)) EntityManager.SetComponentData(node, s.overrideData);
                else EntityManager.AddComponentData(node, s.overrideData);
            }
            else if (EntityManager.HasComponent<MarkingOverride>(node)) EntityManager.RemoveComponent<MarkingOverride>(node);
            if (EntityManager.HasComponent<MarkingTopologyState>(node))
                EntityManager.SetComponentData(node, new MarkingTopologyState { linesHash = 0 });
            if (EntityManager.HasComponent<MarkingAreaTopologyState>(node))
                EntityManager.SetComponentData(node, new MarkingAreaTopologyState { combinedHash = 0 });
            EntityManager.MarkUpdated(node);

            // The tool's own view of the node: drop what may no longer exist.
            _areaPolygon.Clear();
            _draftFreePoints.Clear();
            if (_state == MarkingToolState.SourceSelected) DropSource();
            _drag = DragKind.None;
            _dragMoved = false;
            _editLine = -1;
            _editArea = -1;
            _lastClickedLine = -1;
            _lastClickedTick++;
            RefreshEndpoints();
        }

        /// <summary>Hash of what the user edits. Leaves out what the game derives (segment
        /// ranges, resolved positions), so a road update alone is not a step.</summary>
        private int HashOf(Entity node)
        {
            var h = Fnv1a.Create();
            if (EntityManager.HasBuffer<MarkingLine>(node))
            {
                var b = EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true);
                h.Add(b.Length);
                for (int i = 0; i < b.Length; i++)
                {
                    var l = b[i];
                    h.Add(l.sourceEdge); h.Add(l.sourceGapIndex); h.Add(l.targetEdge); h.Add(l.targetGapIndex);
                    h.Add(l.style); h.Add(l.curvature); h.Add(l.hasHandles);
                    h.Add(l.handleA.x); h.Add(l.handleA.z); h.Add(l.handleB.x); h.Add(l.handleB.z);
                }
            }
            if (EntityManager.HasBuffer<MarkingSegment>(node))
            {
                var b = EntityManager.GetBuffer<MarkingSegment>(node, isReadOnly: true);
                for (int i = 0; i < b.Length; i++)
                {
                    // Only what the user sets; hidden or restyled pieces are the edits.
                    if (b[i].visible && b[i].style == LineStyleOf(node, b[i].lineIndex)) continue;
                    h.Add(i); h.Add(b[i].lineIndex); h.Add(b[i].visible); h.Add(b[i].style);
                }
            }
            if (EntityManager.HasBuffer<MarkingArea>(node))
            {
                var b = EntityManager.GetBuffer<MarkingArea>(node, isReadOnly: true);
                h.Add(b.Length);
                for (int i = 0; i < b.Length; i++) { h.Add(b[i].styleId); h.Add(b[i].visible); h.Add(b[i].firstVertex); h.Add(b[i].vertexCount); }
            }
            if (EntityManager.HasBuffer<MarkingAreaVertex>(node))
            {
                var b = EntityManager.GetBuffer<MarkingAreaVertex>(node, isReadOnly: true);
                h.Add(b.Length);
                for (int i = 0; i < b.Length; i++)
                {
                    var v = b[i];
                    h.Add((int)v.kind); h.Add(v.refIndex); h.Add((int)v.edgeToNext); h.Add(v.refEdgeA); h.Add(v.refEdgeB); h.Add(v.refGap);
                    h.Add(v.handleA.x); h.Add(v.handleA.z); h.Add(v.handleB.x); h.Add(v.handleB.z);
                }
            }
            if (EntityManager.HasBuffer<MarkingFreePoint>(node))
            {
                var b = EntityManager.GetBuffer<MarkingFreePoint>(node, isReadOnly: true);
                h.Add(b.Length);
                for (int i = 0; i < b.Length; i++)
                {
                    var p = b[i];
                    h.Add(p.id); h.Add(p.edge); h.Add(p.t); h.Add(p.lateral); h.Add(p.offset.x); h.Add(p.offset.z);
                }
            }
            h.Add(EntityManager.HasComponent<MarkingOverride>(node) && EntityManager.GetComponentData<MarkingOverride>(node).HideAll);
            return h.Value32;
        }

        private int LineStyleOf(Entity node, int lineIndex)
        {
            if (!EntityManager.HasBuffer<MarkingLine>(node)) return -1;
            var b = EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true);
            return lineIndex >= 0 && lineIndex < b.Length ? b[lineIndex].style : -1;
        }
    }
}
