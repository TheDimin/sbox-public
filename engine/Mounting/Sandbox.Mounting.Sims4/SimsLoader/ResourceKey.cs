using System;
using Sims4.Dbpf.Enums;
using ResourceType = Sims4.Dbpf.Enums.ResourceType;

namespace Sims4.Dbpf.Structures;

/// <summary>
/// Type-Group-Instance key used for resource lookups.
/// </summary>
public readonly struct ResourceKey : IEquatable<ResourceKey>
{
    public readonly ResourceType Type;
    public readonly uint Group;
    public readonly ulong Instance;

    public ResourceKey(ResourceType type, uint group, ulong instance)
    {
        Type = type;
        Group = group;
        Instance = instance;
    }

    /// <summary>Reads a TGI in (Instance64, Type32, Group32) order — the RCOL/COBJ layout.</summary>
    public static ResourceKey ReadITG(ref SpanReader r)
    {
        ulong instance = r.ReadU64();
        uint type = r.ReadU32();
        uint group = r.ReadU32();
        return new ResourceKey((ResourceType)type, group, instance);
    }

    /// <summary>Reads a TGI in (Type32, Group32, Instance64) order — the standard TGI layout.</summary>
    public static ResourceKey ReadTGI(ref SpanReader r)
    {
        uint type = r.ReadU32();
        uint group = r.ReadU32();
        ulong instance = r.ReadU64();
        return new ResourceKey((ResourceType)type, group, instance);
    }

    public bool Equals(ResourceKey other) =>
        Type == other.Type && Group == other.Group && Instance == other.Instance;

    public override bool Equals(object obj) => obj is ResourceKey k && Equals(k);
    public override int GetHashCode() => HashCode.Combine(Type, Group, Instance);
    public static bool operator ==(ResourceKey a, ResourceKey b) => a.Equals(b);
    public static bool operator !=(ResourceKey a, ResourceKey b) => !a.Equals(b);

    public override string ToString() =>
        $"{Type}:{Group:X8}:{Instance:X16}";
}
