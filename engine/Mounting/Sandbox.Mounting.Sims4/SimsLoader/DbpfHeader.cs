// ============================================================================
// DbpfHeader — 96-byte fixed header at offset 0x00 of every .package file.
// Blittable struct — can be read directly via MemoryMarshal.Read<T>.
// ============================================================================

using System.Runtime.InteropServices;

namespace Sims4.Dbpf.Structures;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 96)]
public readonly struct DbpfHeader
{
    /// <summary>"DBPF" magic bytes (0x44425046).</summary>
    public readonly uint Magic;

    /// <summary>Major version — 2 for Sims 4.</summary>
    public readonly uint MajorVersion;

    /// <summary>Minor version — 1 for Sims 4.</summary>
    public readonly uint MinorVersion;

    private readonly uint _unknown1;
    private readonly uint _unknown2;
    private readonly uint _unknown3;

    /// <summary>Unix timestamp of package creation.</summary>
    public readonly uint DateCreated;

    /// <summary>Unix timestamp of last modification.</summary>
    public readonly uint DateModified;

    /// <summary>Index major version — 0 for Sims 4.</summary>
    public readonly uint IndexMajorVersion;

    /// <summary>Number of resource entries in the index.</summary>
    public readonly uint IndexEntryCount;

    /// <summary>Legacy DBPF 1.x field, ignored in 2.x.</summary>
    public readonly uint IndexFirstEntryOffset;

    /// <summary>Byte size of the entire index block on disk.</summary>
    public readonly uint IndexSize;

    /// <summary>Number of hole/gap records.</summary>
    public readonly uint HoleEntryCount;

    /// <summary>Absolute file offset to the hole table.</summary>
    public readonly uint HoleOffset;

    /// <summary>Byte size of the hole table.</summary>
    public readonly uint HoleSize;

    /// <summary>Index minor version — 3 for Sims 4.</summary>
    public readonly uint IndexMinorVersion;

    /// <summary>Absolute file offset to the start of the index block.</summary>
    public readonly uint IndexOffset;

    private readonly uint _unknown4;

    // 24 bytes of padding to reach 96 bytes total
    private readonly ulong _reserved0;
    private readonly ulong _reserved1;
    private readonly ulong _reserved2;

    public bool IsValid =>
        Magic == 0x46504244; // "DBPF" in LE

    public override string ToString() =>
        $"DBPF v{MajorVersion}.{MinorVersion} — {IndexEntryCount} entries @ 0x{IndexOffset:X8}";
}
