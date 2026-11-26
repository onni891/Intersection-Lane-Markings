using System.Collections.Generic;
using Colossal.Logging;
using Game;
using Game.Common;
using Game.Net;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Components;
using ILM.Utilities;

namespace ILM.Systems.Topology
{
    /// <summary>
    /// Makes markings follow their road when it moves or is reshaped (Move It, the road tools).
    ///
    /// The topology systems only rebuild when the marking data changes, so after a road moved
    /// the line crossings and the area fills stayed where they were (lines and crosswalks
    /// themselves are respawned from the current geometry). This system runs after the game has
    /// rebuilt road geometry, looks at every updated node and road, and when the geometry of a
    /// marking owner really changed it clears the owner's topology hashes. The next tick then
    /// rebuilds its segments and fills in the new place.
    ///
    /// The geometry is compared with a hash from the last time it was seen, because our own
    /// systems also tag owners Updated: comparing stops that from turning into a rebuild loop.
    /// An owner seen for the first time is rebuilt once.
    /// </summary>
    public partial class MarkingGeometryWatchSystem : GameSystemBase
    {
        private static readonly ILog log = Mod.log;

        private EntityQuery _updated;
        private readonly Dictionary<Entity, int> _geometryHash = new Dictionary<Entity, int>();
        private readonly HashSet<Entity> _owners = new HashSet<Entity>();

        protected override void OnCreate()
        {
            base.OnCreate();
            _updated = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Updated>() },
                Any = new[] { ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<Edge>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            RequireForUpdate(_updated);
        }

        protected override void OnGameLoaded(Colossal.Serialization.Entities.Context serializationContext)
        {
            base.OnGameLoaded(serializationContext);
            _geometryHash.Clear();
        }

        protected override void OnUpdate()
        {
            _owners.Clear();
            using (var ents = _updated.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    var e = ents[i];
                    _owners.Add(e);
                    if (EntityManager.HasComponent<Edge>(e))
                    {
                        // A road's geometry is part of both of its junctions.
                        var edge = EntityManager.GetComponentData<Edge>(e);
                        _owners.Add(edge.m_Start);
                        _owners.Add(edge.m_End);
                    }
                }
            }

            int rebuilt = 0;
            foreach (var owner in _owners)
            {
                if (!HasMarkings(owner)) continue;
                int h = GeometryHash(owner);
                if (_geometryHash.TryGetValue(owner, out int old) && old == h) continue;
                _geometryHash[owner] = h;
                if (EntityManager.HasComponent<MarkingTopologyState>(owner))
                    EntityManager.SetComponentData(owner, new MarkingTopologyState { linesHash = 0 });
                if (EntityManager.HasComponent<MarkingAreaTopologyState>(owner))
                    EntityManager.SetComponentData(owner, new MarkingAreaTopologyState { combinedHash = 0 });
                rebuilt++;
            }
            if (rebuilt > 0) log.Debug($"geometry-watch: road geometry changed under {rebuilt} marking owner(s), rebuilding");
        }

        private bool HasMarkings(Entity e)
        {
            if (!EntityManager.Exists(e) || EntityManager.HasComponent<Deleted>(e)) return false;
            return (EntityManager.HasBuffer<MarkingLine>(e) && EntityManager.GetBuffer<MarkingLine>(e, isReadOnly: true).Length > 0)
                || (EntityManager.HasBuffer<MarkingArea>(e) && EntityManager.GetBuffer<MarkingArea>(e, isReadOnly: true).Length > 0);
        }

        /// <summary>Node position and the corner points of every connected road (or of the road
        /// itself for a road stretch), to the centimetre.</summary>
        private int GeometryHash(Entity owner)
        {
            var h = Fnv1a.Create();
            if (EntityManager.HasComponent<Node>(owner)) Add(ref h, EntityManager.GetComponentData<Node>(owner).m_Position);
            if (EntityManager.HasComponent<EdgeGeometry>(owner)) Add(ref h, EntityManager.GetComponentData<EdgeGeometry>(owner));
            if (EntityManager.HasBuffer<ConnectedEdge>(owner))
            {
                var connected = EntityManager.GetBuffer<ConnectedEdge>(owner, isReadOnly: true);
                for (int i = 0; i < connected.Length; i++)
                {
                    var e = connected[i].m_Edge;
                    h.Add(e);
                    if (EntityManager.HasComponent<EdgeGeometry>(e)) Add(ref h, EntityManager.GetComponentData<EdgeGeometry>(e));
                }
            }
            return h.Value32;
        }

        private static void Add(ref Fnv1a h, EdgeGeometry g)
        {
            Add(ref h, g.m_Start.m_Left.a); Add(ref h, g.m_Start.m_Left.d);
            Add(ref h, g.m_Start.m_Right.a); Add(ref h, g.m_Start.m_Right.d);
            Add(ref h, g.m_End.m_Left.a); Add(ref h, g.m_End.m_Left.d);
            Add(ref h, g.m_End.m_Right.a); Add(ref h, g.m_End.m_Right.d);
        }

        private static void Add(ref Fnv1a h, float3 p)
        {
            h.Add((int)math.round(p.x * 100f));
            h.Add((int)math.round(p.y * 100f));
            h.Add((int)math.round(p.z * 100f));
        }
    }
}
