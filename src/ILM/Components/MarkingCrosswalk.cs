using Colossal.Serialization.Entities;
using Unity.Entities;

namespace ILM.Components
{
    /// <summary>Crosswalk look, saved as an int. Append-only, like <see cref="MarkingStyle"/>.</summary>
    public enum CrosswalkStyle : int
    {
        // 60 cm bars with 60 cm gaps.
        Zebra = 0,
        // Zebra bars between two 30 cm border lines.
        Ladder = 1,
        // Two 30 cm border lines only (signal-controlled crossings).
        TwoLines = 2,
        // 30 cm bars with 45 cm gaps.
        ZebraThin = 3,
    }

    /// <summary>
    /// A pedestrian crossing on a junction or road stretch, one buffer entry per crossing. It
    /// runs from one point to the other (normally the two kerbs) and is <see cref="width"/>
    /// metres wide along the road. The ends are ordinary endpoints (usually two free points the
    /// crosswalk tool placed), so they move with the road and can be dragged like any point.
    /// <see cref="ILM.Systems.Emission.MarkingCrosswalkEmissionSystem"/> draws the bars and
    /// borders as short wide line sublanes.
    /// </summary>
    [InternalBufferCapacity(0)]
    public struct MarkingCrosswalk : IBufferElementData, ISerializable
    {
        public Entity sourceEdge;
        public int sourceGapIndex;
        public Entity targetEdge;
        public int targetGapIndex;
        // Width along the road (the length of each bar), metres.
        public float width;
        public int style;

        public const float kMinWidth = 1f;
        public const float kMaxWidth = 12f;
        public const float kDefaultWidth = 3f;

        private const int kVersion = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(kVersion);
            writer.Write(sourceEdge);
            writer.Write(sourceGapIndex);
            writer.Write(targetEdge);
            writer.Write(targetGapIndex);
            writer.Write(width);
            writer.Write(style);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int _); // version
            reader.Read(out sourceEdge);
            reader.Read(out sourceGapIndex);
            reader.Read(out targetEdge);
            reader.Read(out targetGapIndex);
            reader.Read(out width);
            reader.Read(out style);
        }

        public bool Uses(Entity edge, int gapIndex)
            => (sourceEdge == edge && sourceGapIndex == gapIndex) || (targetEdge == edge && targetGapIndex == gapIndex);
    }

    /// <summary>
    /// Tag on every sublane spawned by
    /// <see cref="ILM.Systems.Emission.MarkingCrosswalkEmissionSystem"/>, keyed by
    /// (owner, crosswalk index, lane, pass, shape hash). The hash covers the crosswalk's shape and
    /// end positions, so any change respawns its lanes. Not serialized, like ILMSegmentLink: the
    /// emission system marks crosswalk owners Updated once after a load so the untagged copies the
    /// save restored are removed with the node's old lanes.
    /// </summary>
    public struct ILMCrosswalkLink : IComponentData
    {
        public Entity node;
        public int crosswalkIndex;
        public int laneIndex;
        public int passIndex;
        public int shapeHash;
    }
}
