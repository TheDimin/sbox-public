using System.Text;

namespace Sims4Reader;

/// <summary>
/// Sims 4 String Table (STBL) resource.
/// Maps uint hashes to localized strings. Resource type: 0x220557DA.
/// </summary>
public class StringTable : IResource
{
    private const uint StblMagic = 0x4C425453; // "STBL" little-endian
    private Dictionary<uint, string> _entries = new();

    public IReadOnlyDictionary<uint, string> Entries => _entries;
    public ushort Version { get; private set; }

    /// <summary>
    /// Look up a string by its hash key. Returns null if not found.
    /// </summary>
    public string? GetString(uint hash)
    {
        return _entries.TryGetValue(hash, out var str) ? str : null;
    }

    public void Parse(ReadOnlyMemory<byte> data)
    {
        using var ms = new MemoryStream(data.ToArray());
        using var r = new BinaryReader(ms, Encoding.UTF8);

        uint magic = r.ReadUInt32();
        if (magic != StblMagic)
            throw new InvalidDataException($"Invalid STBL magic: 0x{magic:X8}");

        Version = r.ReadUInt16();
        byte isCompressed = r.ReadByte(); // compression flag (unused in practice)
        ulong numEntries = r.ReadUInt64();
        r.ReadBytes(2); // reserved
        uint stringLength = r.ReadUInt32(); // total string data size

        _entries = new Dictionary<uint, string>((int)numEntries);

        for (ulong i = 0; i < numEntries; i++)
        {
            uint keyHash = r.ReadUInt32();
            byte flags = r.ReadByte();
            ushort length = r.ReadUInt16();
            string value = Encoding.UTF8.GetString(r.ReadBytes(length));
            _entries[keyHash] = value;
        }
    }
}
