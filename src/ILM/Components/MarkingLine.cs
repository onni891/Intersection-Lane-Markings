using Colossal.Serialization.Entities;
using Unity.Entities;
using Unity.Mathematics;
using ILM.Geometry;

namespace ILM.Components
{
    /// <summary>
    /// User-drawn marking line at a road node, one buffer entry per line (endpoints, style and
    /// curvature). The drawn pieces live in the node's <see cref="MarkingSegment"/> buffer and
    /// point back by <c>lineIndex</c>; a line is split into several segments where it crosses
    /// other lines. A node with a non-empty MarkingLine buffer gets no vanilla markings.
    ///
    /// Endpoints use the gap-based identity of <see cref="MarkingEndpointExtractor"/>, the same
    /// as the legacy <see cref="MarkingPair"/>, so migration copies them field for field.
    /// </summary>
    [FormerlySerializedAs("ILM.MarkingLine, ILM")]
    [InternalBufferCapacity(0)]
    public struct MarkingLine : IBufferElementData, ISerializable
    {
        public Entity sourceEdge;
        public int sourceGapIndex;
        public Entity targetEdge;
        public int targetGapIndex;

        // Default style for new segments of this line (a MarkingStyle value).
        public int style;

        // Bezier control-point offset as a fraction of the chord (see MarkingCurveBuilder):
        // 0 = straight, 0.4 = default arc, 0.55 ≈ quarter circle.
        public float curvature;

        // Hand-shaped curve: when set, the Bezier control points are the endpoints plus these
        // offsets (dragged in the tool), and curvature is ignored. Offsets rather than world
        // positions, so the shape travels with the road.
        public bool hasHandles;
        public float3 handleA; // control point 1 = source position + handleA
        public float3 handleB; // control point 2 = target position + handleB
        // Made by auto markings (MarkingAutoSystem) from the junction's lane connections. Auto lines are
        // replaced when the lanes change and do not hide the game's own markings.
        public bool isAuto;

        private const int kVersion = 6;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(kVersion);
            writer.Write(sourceEdge);
            writer.Write(sourceGapIndex);
            writer.Write(targetEdge);
            writer.Write(targetGapIndex);
            writer.Write(style);
            writer.Write(curvature);
            writer.Write(hasHandles);
            writer.Write(handleA);
            writer.Write(handleB);
            writer.Write(isAuto);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int version);
            reader.Read(out sourceEdge);
            reader.Read(out sourceGapIndex);
            reader.Read(out targetEdge);
            reader.Read(out targetGapIndex);
            reader.Read(out style);
            // Version 3 lines have no curvature field and used this constant.
            if (version >= 4) reader.Read(out curvature);
            else curvature = MarkingCurveBuilder.kPullFactor;
            if (version >= 5)
            {
                reader.Read(out hasHandles);
                reader.Read(out handleA);
                reader.Read(out handleB);
            }
            else
            {
                hasHandles = false;
                handleA = default;
                handleB = default;
            }
            if (version >= 6) reader.Read(out isAuto);
            else isAuto = false;
        }
    }
}
