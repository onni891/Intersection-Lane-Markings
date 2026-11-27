using System.Collections.Generic;
using Colossal.Logging;
using Colossal.Mathematics;
using Game;
using Game.Common;
using Game.Net;
using NetLaneData = Game.Prefabs.NetLaneData;
using PrefabRef = Game.Prefabs.PrefabRef;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Utilities;

namespace ILM.Systems.Topology
{
    /// <summary>
    /// Auto markings (experimental): guide lines through junctions, made from the lanes the
    /// game actually built there. Those lanes already follow the Traffic mod's lane
    /// connections when it is installed, so the guides follow its connections without any
    /// direct dependency on it.
    ///
    /// For every car lane crossing the junction (turning lanes only by default), both of its
    /// edges are traced from the road dot where the lane enters to the dot where it leaves, and
    /// a line is added between them in the chosen style. Lanes side by side share an edge, so
    /// each pair of dots gets one line. The lines are ordinary <see cref="MarkingLine"/>s with
    /// <see cref="MarkingLine.isAuto"/> set: they can be restyled and shaped like any line, do
    /// not hide the game's own markings, and are replaced when the junction's lanes change.
    ///
    /// Whether a junction gets them: its own <see cref="MarkingAutoState.mode"/> when set to On
    /// or Off, otherwise the global setting. Runs after the game rebuilt lanes (Modification5)
    /// on the junctions updated this frame, plus a sweep over every junction when a setting
    /// changes or a save loads.
    /// </summary>
    public partial class MarkingAutoSystem : GameSystemBase
    {
        private static readonly ILog log = Mod.log;

        // A lane end and a road dot closer than this (XZ, metres) are the same place.
        private const float kDotMatchRadius = 0.8f;
        // "Turning" lanes turn by at least this much; U-turns (over kMaxTurnDeg) are skipped.
        private const float kMinTurnDeg = 25f;
        private const float kMaxTurnDeg = 150f;
        // Junctions handled per frame during a sweep.
        private const int kSweepBatch = 128;

        private EntityQuery _updated;
        private EntityQuery _junctions;
        private readonly HashSet<Entity> _pending = new HashSet<Entity>();
        private readonly Queue<Entity> _sweep = new Queue<Entity>();
        private readonly List<Entity> _scratch = new List<Entity>();
        private int _settingsKey;
        private bool _needSweep = true;

        protected override void OnCreate()
        {
            base.OnCreate();
            _updated = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Updated>() },
                Any = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<Edge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            _junctions = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
        }

        protected override void OnGameLoaded(Colossal.Serialization.Entities.Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            _pending.Clear();
            _sweep.Clear();
            _needSweep = true;
        }

        /// <summary>Re-checks one junction on the next update (the panel changed its mode).</summary>
        public void Request(Entity node)
        {
            if (node != Entity.Null) _pending.Add(node);
        }

        private static bool GlobalOn => Mod.Settings != null && Mod.Settings.AutoMarkings;
        private static int Style => Mod.Settings != null ? Mod.Settings.AutoMarkingStyle : (int)MarkingStyle.Dashed;
        private static bool TurnsOnly => Mod.Settings == null || Mod.Settings.AutoMarkingTurnsOnly;

        protected override void OnUpdate()
        {
            int key = (GlobalOn ? 1 : 0) | (TurnsOnly ? 2 : 0) | (Style << 2);
            if (key != _settingsKey) { _settingsKey = key; _needSweep = true; }
            if (_needSweep)
            {
                _needSweep = false;
                _sweep.Clear();
                using (var all = _junctions.ToEntityArray(Allocator.Temp))
                    for (int i = 0; i < all.Length; i++) _sweep.Enqueue(all[i]);
            }

            if (!_updated.IsEmptyIgnoreFilter)
            {
                using (var ents = _updated.ToEntityArray(Allocator.Temp))
                {
                    for (int i = 0; i < ents.Length; i++)
                    {
                        var e = ents[i];
                        if (EntityManager.HasComponent<Edge>(e))
                        {
                            var edge = EntityManager.GetComponentData<Edge>(e);
                            _pending.Add(edge.m_Start);
                            _pending.Add(edge.m_End);
                        }
                        else _pending.Add(e);
                    }
                }
            }

            if (_pending.Count > 0)
            {
                _scratch.Clear();
                _scratch.AddRange(_pending);
                _pending.Clear();
                foreach (var node in _scratch)
                    if (!Process(node)) _pending.Add(node); // roads not ready yet: next frame
            }

            for (int n = 0; n < kSweepBatch && _sweep.Count > 0; n++)
            {
                var node = _sweep.Dequeue();
                if (!Process(node)) _pending.Add(node);
            }
        }

        /// <summary>Brings one junction's auto lines up to date. False when its roads are not
        /// ready yet (right after loading) and it should be tried again.</summary>
        private bool Process(Entity node)
        {
            if (!EntityManager.Exists(node) || EntityManager.HasComponent<Deleted>(node) || EntityManager.HasComponent<Temp>(node)) return true;
            if (!EntityManager.HasComponent<Node>(node) || !EntityManager.HasBuffer<ConnectedEdge>(node)) return true;

            var mode = EntityManager.HasComponent<MarkingAutoState>(node)
                ? EntityManager.GetComponentData<MarkingAutoState>(node).mode
                : AutoMarkingMode.Default;
            bool on = mode == AutoMarkingMode.On || (mode == AutoMarkingMode.Default && GlobalOn);
            var connected = EntityManager.GetBuffer<ConnectedEdge>(node, isReadOnly: true);
            if (connected.Length < 3) on = false; // plain road joins and dead ends

            if (!on)
            {
                if (EntityManager.HasComponent<MarkingAutoState>(node))
                {
                    RemoveAutoLines(node);
                    SetState(node, mode, 0);
                }
                return true;
            }

            for (int i = 0; i < connected.Length; i++)
                if (MarkingEndpointExtractor.IsEdgeAliveButUnready(EntityManager, connected[i].m_Edge)) return false;

            var lanes = CollectLanes(node, out int hash);
            int old = EntityManager.HasComponent<MarkingAutoState>(node) ? EntityManager.GetComponentData<MarkingAutoState>(node).laneHash : 0;
            if (hash == old) return true;

            RemoveAutoLines(node);
            int added = AddAutoLines(node, lanes);
            SetState(node, mode, hash);
            log.Debug($"auto-markings: node#{node.Index} — {lanes.Count} lane(s), {added} guide line(s)");
            return true;
        }

        private struct LaneEdges
        {
            public float3 startL, startR, endL, endR;
        }

        /// <summary>The junction's car lanes to trace, as the four corners of each lane, and a
        /// hash of them together with the settings that shape the result.</summary>
        private List<LaneEdges> CollectLanes(Entity node, out int hash)
        {
            var result = new List<LaneEdges>();
            var h = Fnv1a.Create();
            h.Add(Style);
            h.Add(TurnsOnly);
            if (!EntityManager.HasBuffer<SubLane>(node)) { hash = h.Value32 | 1; return result; }
            var subLanes = EntityManager.GetBuffer<SubLane>(node, isReadOnly: true);
            float minTurn = math.radians(kMinTurnDeg), maxTurn = math.radians(kMaxTurnDeg);
            for (int i = 0; i < subLanes.Length; i++)
            {
                var lane = subLanes[i].m_SubLane;
                if (!EntityManager.HasComponent<CarLane>(lane) || EntityManager.HasComponent<MasterLane>(lane)) continue;
                if (EntityManager.HasComponent<SecondaryLane>(lane) || !EntityManager.HasComponent<Curve>(lane)) continue;
                var bez = EntityManager.GetComponentData<Curve>(lane).m_Bezier;
                float2 d0 = math.normalizesafe(MathUtils.Tangent(bez, 0f).xz);
                float2 d1 = math.normalizesafe(MathUtils.Tangent(bez, 1f).xz);
                float turn = math.acos(math.clamp(math.dot(d0, d1), -1f, 1f));
                if (turn > maxTurn) continue;
                if (TurnsOnly && turn < minTurn) continue;

                float width = 3f;
                if (EntityManager.HasComponent<PrefabRef>(lane))
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
                    if (EntityManager.HasComponent<NetLaneData>(prefab)) width = math.max(1f, EntityManager.GetComponentData<NetLaneData>(prefab).m_Width);
                }
                float half = width * 0.5f;
                float3 r0 = new float3(d0.y, 0f, -d0.x) * half;
                float3 r1 = new float3(d1.y, 0f, -d1.x) * half;
                result.Add(new LaneEdges { startL = bez.a - r0, startR = bez.a + r0, endL = bez.d - r1, endR = bez.d + r1 });

                h.Add((int)math.round(bez.a.x * 10f)); h.Add((int)math.round(bez.a.z * 10f));
                h.Add((int)math.round(bez.d.x * 10f)); h.Add((int)math.round(bez.d.z * 10f));
                h.Add((int)math.round(width * 10f));
            }
            // Never 0: 0 means "no auto lines made".
            hash = h.Value32 == 0 ? 1 : h.Value32;
            return result;
        }

        private int AddAutoLines(Entity node, List<LaneEdges> lanes)
        {
            if (lanes.Count == 0) return 0;
            var endpoints = MarkingEndpointExtractor.Extract(EntityManager, node);
            var lines = EntityManager.HasBuffer<MarkingLine>(node) ? EntityManager.GetBuffer<MarkingLine>(node) : EntityManager.AddBuffer<MarkingLine>(node);

            // Pairs already joined, by the user or by another lane's edge.
            var pairs = new HashSet<(Entity, int, Entity, int)>();
            for (int i = 0; i < lines.Length; i++)
                pairs.Add(Key(lines[i].sourceEdge, lines[i].sourceGapIndex, lines[i].targetEdge, lines[i].targetGapIndex));

            int added = 0;
            foreach (var lane in lanes)
            {
                added += TryAdd(lines, pairs, endpoints, lane.startL, lane.endL);
                added += TryAdd(lines, pairs, endpoints, lane.startR, lane.endR);
            }
            return added;
        }

        private static int TryAdd(DynamicBuffer<MarkingLine> lines, HashSet<(Entity, int, Entity, int)> pairs,
                                  List<MarkingEndpoint> endpoints, float3 from, float3 to)
        {
            int a = NearestDot(endpoints, from), b = NearestDot(endpoints, to);
            if (a < 0 || b < 0 || endpoints[a].edge == endpoints[b].edge) return 0;
            var key = Key(endpoints[a].edge, endpoints[a].gapIndex, endpoints[b].edge, endpoints[b].gapIndex);
            if (!pairs.Add(key)) return 0;
            lines.Add(new MarkingLine
            {
                sourceEdge = endpoints[a].edge,
                sourceGapIndex = endpoints[a].gapIndex,
                targetEdge = endpoints[b].edge,
                targetGapIndex = endpoints[b].gapIndex,
                style = Style,
                curvature = MarkingCurveBuilder.kPullFactor,
                isAuto = true,
            });
            return 1;
        }

        private static (Entity, int, Entity, int) Key(Entity ea, int ga, Entity eb, int gb)
            => (ea.Index < eb.Index || (ea.Index == eb.Index && ga <= gb)) ? (ea, ga, eb, gb) : (eb, gb, ea, ga);

        // Only the main row of lane-boundary dots: lane edges start and end there.
        private static int NearestDot(List<MarkingEndpoint> endpoints, float3 p)
        {
            int best = -1;
            float bestSq = kDotMatchRadius * kDotMatchRadius;
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (MarkingEndpointExtractor.KindOf(endpoints[i].gapIndex) != MarkingEndpointExtractor.AnchorKind.Main) continue;
                float sq = math.distancesq(endpoints[i].position.xz, p.xz);
                if (sq < bestSq) { bestSq = sq; best = i; }
            }
            return best;
        }

        private void RemoveAutoLines(Entity node)
        {
            if (!EntityManager.HasBuffer<MarkingLine>(node)) return;
            var lines = EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (!EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true)[i].isAuto) continue;
                MarkingEdits.DeleteLine(EntityManager, node, i);
            }
        }

        private void SetState(Entity node, AutoMarkingMode mode, int hash)
        {
            var s = new MarkingAutoState { mode = mode, laneHash = hash };
            if (EntityManager.HasComponent<MarkingAutoState>(node)) EntityManager.SetComponentData(node, s);
            else if (mode != AutoMarkingMode.Default || hash != 0) EntityManager.AddComponentData(node, s);
        }

        /// <summary>Sets a junction's own mode (the panel). Default follows the global setting
        /// again.</summary>
        public void SetMode(Entity node, AutoMarkingMode mode)
        {
            if (node == Entity.Null || !EntityManager.Exists(node)) return;
            int hash = EntityManager.HasComponent<MarkingAutoState>(node) ? EntityManager.GetComponentData<MarkingAutoState>(node).laneHash : 0;
            SetState(node, mode, hash);
            Request(node);
        }

        public AutoMarkingMode GetMode(Entity node)
            => node != Entity.Null && EntityManager.Exists(node) && EntityManager.HasComponent<MarkingAutoState>(node)
                ? EntityManager.GetComponentData<MarkingAutoState>(node).mode
                : AutoMarkingMode.Default;

        /// <summary>Number of auto lines on the junction.</summary>
        public int CountAutoLines(Entity node)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<MarkingLine>(node)) return 0;
            var lines = EntityManager.GetBuffer<MarkingLine>(node, isReadOnly: true);
            int n = 0;
            for (int i = 0; i < lines.Length; i++) if (lines[i].isAuto) n++;
            return n;
        }

        /// <summary>Makes the junction's auto lines its own: they stop being auto lines, so
        /// they are no longer replaced and turning auto markings off keeps them.</summary>
        public void KeepAutoLines(Entity node)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<MarkingLine>(node)) return;
            var lines = EntityManager.GetBuffer<MarkingLine>(node);
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i];
                if (!l.isAuto) continue;
                l.isAuto = false;
                lines[i] = l;
            }
            // They now hide the game's markings like any drawn line.
            EntityManager.MarkUpdated(node);
            // Stop following the global switch, or the next lane change makes new ones.
            SetMode(node, AutoMarkingMode.Off);
        }
    }
}
