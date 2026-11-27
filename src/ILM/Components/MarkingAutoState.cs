using Colossal.Serialization.Entities;
using Unity.Entities;

namespace ILM.Components
{
    /// <summary>Auto markings on this junction: follow the global setting, or always on or
    /// off. Set from the panel's Advanced tab; a junction set by hand ignores the global
    /// switch.</summary>
    public enum AutoMarkingMode : byte
    {
        Default = 0,
        On = 1,
        Off = 2,
    }

    /// <summary>
    /// Per-junction auto-marking state (see MarkingAutoSystem): the mode chosen for the junction
    /// and a hash of the lane connections the current auto lines were made from, so they are
    /// only rebuilt when the lanes actually change.
    /// </summary>
    public struct MarkingAutoState : IComponentData, ISerializable
    {
        public AutoMarkingMode mode;
        public int laneHash;

        private const int kVersion = 1;

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            writer.Write(kVersion);
            writer.Write((byte)mode);
            writer.Write(laneHash);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int _); // version
            reader.Read(out byte m);
            mode = (AutoMarkingMode)m;
            reader.Read(out laneHash);
        }
    }
}
