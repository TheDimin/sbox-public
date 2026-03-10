namespace Sims4Reader;

/// <summary>
/// Identifies a resource by Type, Group, and Instance (TGI key).
/// </summary>
public readonly record struct ResourceKey(ResourceType Type, uint Group, ulong Instance)
{
    public override string ToString() => $"{Type} (0x{(uint)Type:X8}): G=0x{Group:X8}, I=0x{Instance:X16}";
}
