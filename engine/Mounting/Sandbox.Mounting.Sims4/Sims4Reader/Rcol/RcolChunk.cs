namespace Sims4Reader.Rcol;

/// <summary>
/// Base class for all RCOL chunk types (MATD, MTST, etc.).
/// </summary>
public abstract class RcolChunk
{
    public uint Tag { get; protected set; }

    public abstract void Parse(BinaryReader reader, uint version, ResourceKey[] externalReferences);
}
