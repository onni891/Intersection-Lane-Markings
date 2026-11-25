using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Geometry;

namespace ILM.Components
{
    /// <summary>
    /// A user-placed anchor point at a road node (free-draw mode), one buffer entry per point.
    /// <see cref="MarkingEndpointExtractor"/> turns each into a regular endpoint with gapIndex =
    /// kFreePointGapBase + id, so lines and areas use free points exactly like built-in dots.
    ///
    /// Two placements, so a point follows the road when it is edited:
    ///   - on a connected road edge (edge != node): (t, lateral) on that edge's node-side
    ///     geometry half. t is the parameter on the cap curves, lateral the 0..1 fraction from
    ///     the left to the right kerb curve. The tangent runs along the road.
    ///   - inside the junction (edge == node): offset from the node position, no tangent.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct MarkingFreePoint : IBufferElementData, ISerializable
    {
        // Unique per node; never reused while the point exists, so gapIndex stays stable.
        public int id;
        // Connected edge the point sits on, or the node itself for a junction-interior point.
        public Entity edge;
        public float t;
        public float lateral;
        public float3 offset;

        public bool IsInterior(Entity node) => edge == node;

        private const int kVersion = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(kVersion);
            writer.Write(id);
            writer.Write(edge);
            writer.Write(t);
            writer.Write(lateral);
            writer.Write(offset);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int _); // version
            reader.Read(out id);
            reader.Read(out edge);
            reader.Read(out t);
            reader.Read(out lateral);
            reader.Read(out offset);
        }
    }
}
