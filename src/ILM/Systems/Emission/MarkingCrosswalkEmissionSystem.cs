using System.Collections.Generic;
using Colossal.Logging;
using Colossal.Mathematics;
using Game;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Pathfind;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Geometry;
using ILM.Systems.Prefabs;

namespace ILM.Systems.Emission
{
    /// <summary>
    /// Keeps the sublanes that draw every <see cref="MarkingCrosswalk"/>: one per bar or border
    /// line and draw pass, tagged <see cref="ILMCrosswalkLink"/>. Works like
    /// <see cref="MarkingSegmentEmissionSystem"/>: wanted keys are diffed against the tagged
    /// sublanes, missing ones are spawned and the rest deleted. The key carries a hash of the
    /// crosswalk's shape and end positions, so moving an end, changing the width or style, or a
    /// road moving under it (Move It) respawns it in the new place.
    ///
    /// When the owner is updated, CustomSecondaryLaneSystem deletes all of its old lanes, ours
    /// included; the count check below then respawns them.
    /// </summary>
    [UpdateAfter(typeof(MarkingSegmentEmissionSystem))]
    public partial class MarkingCrosswalkEmissionSystem : GameSystemBase
    {
        private static readonly ILog log = Mod.log;

        // PathNode slots: 16384 + crosswalk * 512 + lane * 3, below the lines' range (32768+)
        // and well clear of the vanilla lanes' 0..N-1.
        private const int kSlotBase = 16384;
        private const int kSlotsPerCrosswalk = 512;
        private const int kMaxCrosswalks = 32;
        // Sublanes per draw pass: 3 passes x 56 lanes x 3 slots fit in 512 (a 60+ m crossing).
        private const int kLanesPerPass = 56;

        private EntityQuery _owners;
        private EntityQuery _ourLanes;
        private EntityQuery _changed;
        private int _laneCountAfterPass = -1;
        private bool _passPending = true;
        // After a load, owners are marked Updated once so the untagged lanes the save restored
        // are removed (see ILMCrosswalkLink).
        private bool _cleanupPending;
        private readonly HashSet<Entity> _cleanedOwners = new HashSet<Entity>();
        private readonly List<CrosswalkGeometry.Piece> _pieces = new List<CrosswalkGeometry.Piece>(32);
        private PrefabSystem _prefabSystem;
        private EdgeLineCloneSystem _edgeLineSys;
        private CityConfigurationSystem _cityConfig;

        protected override void OnCreate()
        {
            base.OnCreate();
            _prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            _edgeLineSys = World.GetOrCreateSystemManaged<EdgeLineCloneSystem>();
            _cityConfig = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            _owners = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<MarkingCrosswalk>() },
                Any = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<Game.Net.Edge>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            _ourLanes = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<ILMCrosswalkLink>() },
                None = new[] { ComponentType.ReadOnly<Deleted>() },
            });
            _changed = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<MarkingCrosswalk>() },
                None = new[] { ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Temp>() },
            });
            _changed.SetChangedVersionFilter(ComponentType.ReadOnly<MarkingCrosswalk>());
        }

        protected override void OnGameLoaded(Colossal.Serialization.Entities.Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            _passPending = true;
            _cleanupPending = true;
            _cleanedOwners.Clear();
        }

        protected override void OnUpdate()
        {
            bool needed = _passPending || _cleanupPending || !_changed.IsEmpty
                || _ourLanes.CalculateEntityCount() != _laneCountAfterPass;
            if (!needed) return;
            _passPending = false;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var wanted = new Dictionary<(Entity, int, int, int, int), (Bezier4x3 curve, MarkingStyle style)>();
            bool anyUnready = false;
            using (var owners = _owners.ToEntityArray(Allocator.Temp))
            {
                for (int o = 0; o < owners.Length; o++)
                {
                    var owner = owners[o];
                    var cws = EntityManager.GetBuffer<MarkingCrosswalk>(owner, isReadOnly: true);
                    if (cws.Length == 0) continue;
                    var endpoints = MarkingEndpointExtractor.Extract(EntityManager, owner);
                    bool ownerReady = true;
                    for (int c = 0; c < cws.Length && c < kMaxCrosswalks; c++)
                    {
                        var cw = cws[c];
                        if (!CrosswalkGeometry.TryGetEnds(endpoints, cw, out var a, out var b))
                        {
                            if (MarkingEndpointExtractor.IsEdgeAliveButUnready(EntityManager, cw.sourceEdge)
                                || MarkingEndpointExtractor.IsEdgeAliveButUnready(EntityManager, cw.targetEdge))
                            { anyUnready = true; ownerReady = false; }
                            continue;
                        }
                        int hash = CrosswalkGeometry.ShapeHash(a, b, cw);
                        CrosswalkGeometry.BuildPieces(a, b, cw.width, (CrosswalkStyle)cw.style, _pieces);
                        for (int p = 0; p < _pieces.Count && p < kLanesPerPass; p++)
                        {
                            int passes = _pieces[p].style.DrawPasses();
                            for (int pass = 0; pass < passes; pass++)
                                wanted[(owner, c, p, pass, hash)] = (_pieces[p].curve, _pieces[p].style);
                        }
                    }
                    // Once per load and owner: drop the untagged lanes the save restored.
                    if (_cleanupPending && ownerReady && _cleanedOwners.Add(owner))
                        EntityManager.AddComponent<Updated>(owner);
                }
            }
            if (!anyUnready) _cleanupPending = false;
            else _passPending = true;

            int deleted = 0;
            using (var existing = _ourLanes.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < existing.Length; i++)
                {
                    var l = EntityManager.GetComponentData<ILMCrosswalkLink>(existing[i]);
                    if (wanted.Remove((l.node, l.crosswalkIndex, l.laneIndex, l.passIndex, l.shapeHash))) continue;
                    ecb.AddComponent<Deleted>(existing[i]);
                    deleted++;
                }
            }

            int created = 0;
            if (wanted.Count > 0)
            {
                bool isNA = IsNATheme();
                var prefabs = new Dictionary<MarkingStyle, (Entity prefab, EntityArchetype arch)>();
                foreach (var entry in wanted)
                {
                    var (owner, c, laneIdx, pass, hash) = entry.Key;
                    var style = entry.Value.style;
                    if (!prefabs.TryGetValue(style, out var pair))
                    {
                        if (!TryResolve(style, isNA, out pair) && !TryResolve(MarkingStyle.Solid, isNA, out pair))
                        {
                            _passPending = true;
                            continue;
                        }
                        prefabs[style] = pair;
                    }
                    if (Spawn(ecb, owner, c, laneIdx, pass, hash, entry.Value.curve, pair.prefab, pair.arch)) created++;
                }
            }

            if (created > 0 || deleted > 0)
                log.Debug($"crosswalk-emission: +{created} created, -{deleted} deleted");
            ecb.Playback(EntityManager);
            ecb.Dispose();
            _laneCountAfterPass = _ourLanes.CalculateEntityCount();
        }

        private static bool Spawn(EntityCommandBuffer ecb, Entity owner, int cwIndex, int laneIdx, int pass, int hash,
            Bezier4x3 curve, Entity prefab, EntityArchetype arch)
        {
            // Three slots per sublane (start, middle, end), passes after the lanes of the pass before.
            int laneSlot = laneIdx + pass * kLanesPerPass;
            int slot = kSlotBase + cwIndex * kSlotsPerCrosswalk + laneSlot * 3;
            if (laneSlot * 3 + 2 >= kSlotsPerCrosswalk || slot + 2 > ushort.MaxValue) return false;
            var lane = new Lane
            {
                m_StartNode = new PathNode(new PathNode(owner, (ushort)slot), secondaryNode: true),
                m_MiddleNode = new PathNode(new PathNode(owner, (ushort)(slot + 1)), secondaryNode: true),
                m_EndNode = new PathNode(new PathNode(owner, (ushort)(slot + 2)), secondaryNode: true),
            };
            Entity e = ecb.CreateEntity(arch);
            ecb.SetComponent(e, new PrefabRef(prefab));
            ecb.SetComponent(e, lane);
            ecb.SetComponent(e, new Curve { m_Bezier = curve, m_Length = MathUtils.Length(curve) });
            ecb.AddComponent(e, new Owner { m_Owner = owner });
            ecb.AddComponent(e, default(Elevation));
            ecb.AddComponent(e, new ILMCrosswalkLink { node = owner, crosswalkIndex = cwIndex, laneIndex = laneIdx, passIndex = pass, shapeHash = hash });
            ecb.AddComponent(e, default(Created));
            ecb.AddComponent(e, default(Updated));
            return true;
        }

        private bool IsNATheme()
        {
            var theme = _cityConfig.defaultTheme;
            if (theme == Entity.Null) return false;
            if (_prefabSystem.TryGetPrefab<PrefabBase>(theme, out var themePrefab) && themePrefab != null)
            {
                var n = themePrefab.name;
                if (!string.IsNullOrEmpty(n) && (n.Contains("North American") || n.StartsWith("NA "))) return true;
            }
            return false;
        }

        private bool TryResolve(MarkingStyle style, bool isNA, out (Entity prefab, EntityArchetype arch) pair)
        {
            pair = default;
            Entity prefab = _edgeLineSys.GetCloneEntity(style, isNA);
            if (prefab == Entity.Null || !EntityManager.HasComponent<NetLaneArchetypeData>(prefab)) return false;
            pair = (prefab, EntityManager.GetComponentData<NetLaneArchetypeData>(prefab).m_LaneArchetype);
            return true;
        }
    }
}
