#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sandbox.Mounting.Sims4;

namespace Sandbox.Mounting.Sims4;

/// <summary>
/// Thin compatibility wrapper around the high-performance DbpfInstance parser.
/// </summary>
public sealed class DbpfPackage : IDisposable
{
    private const uint StblTypeId = 0x220557DA;
    private const uint TextureTypeId = 0x00B2D882;
    private const uint ModelTypeId = 0x01661233;

    private readonly DbpfInstance _instance;
    private Dictionary<ulong, string>? _nameMapCache;
    private List<StblNameEntry>? _objectNamesCache;

    private DbpfPackage(DbpfInstance instance)
    {
        _instance = instance;
    }

    public static DbpfPackage Open(string path) => new DbpfPackage(DbpfInstance.Open(path));

    /// <summary>
    /// Gets all records as a list-like interface for Count and indexing.
    /// </summary>
    public DbpfRecordList Records => new DbpfRecordList(_instance.Records.ToArray());

    public IEnumerable<DbpfRecord> GetRecords(ResourceType type) => _instance.GetRecords(type);

    public IEnumerable<DbpfRecord> GetModelRecords() => _instance.GetRecords(ResourceType.MODL);

    public IEnumerable<DbpfRecord> GetTextureRecords() => _instance.GetRecords(ResourceType.DST);

    public IEnumerable<DbpfRecord> GetStblRecords() => _instance.GetRecords((ResourceType)StblTypeId);

    public IEnumerable<Sims4Asset> GetValidRecords()
    {
        var records = _instance.Records.ToArray();
        foreach (var record in records)
        {
            var asset = RecordToAsset(record);
            if (asset.HasValue)
                yield return asset.Value;
        }
    }

    public IEnumerable<GEOMResource> GetGeomResources() => _instance.GetEnumerator(ResourceType.GEOM);

    public byte[] ReadData(DbpfRecord record) => _instance.ReadRaw(record);

    public IEnumerable<StblNameEntry> ReadObjectNames(uint unused)
    {
        _objectNamesCache ??= LoadObjectNames();
        return _objectNamesCache;
    }

    public Dictionary<ulong, string> ReadResolvedNames()
    {
        _nameMapCache ??= LoadResolvedNames();
        return _nameMapCache;
    }

    public bool TryResolveName(ulong instanceId, out string name)
    {
        var nameMap = ReadResolvedNames();
        return nameMap.TryGetValue(instanceId, out name!);
    }

    public static bool TryParseStblData(byte[] data, out List<StblNameEntry> strings)
    {
        strings = new List<StblNameEntry>();

        try
        {
            if (data.Length < 16)
                return false;

            // Check STBL magic
            if (data[0] != 'S' || data[1] != 'T' || data[2] != 'B' || data[3] != 'L')
                return false;

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            // Skip magic
            reader.ReadUInt32();

            // Read number of entries
            ms.Seek(12, SeekOrigin.Begin);
            uint numEntries = reader.ReadUInt32();

            for (uint i = 0; i < numEntries; i++)
            {
                if (ms.Position + 8 > data.Length)
                    break;

                uint key = reader.ReadUInt32();
                uint flags = reader.ReadUInt32();

                // Read string length
                if (ms.Position + 2 > data.Length)
                    break;

                ushort stringLength = reader.ReadUInt16();

                if (ms.Position + stringLength > data.Length)
                    break;

                var stringBytes = reader.ReadBytes(stringLength);
                string text = Encoding.UTF8.GetString(stringBytes).TrimEnd('\0');

                strings.Add(new StblNameEntry(key, text));
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private List<StblNameEntry> LoadObjectNames()
    {
        var objectNames = new List<StblNameEntry>();
        var stblRecords = GetStblRecords().ToList();

        foreach (var record in stblRecords)
        {
            try
            {
                var data = ReadData(record);
                if (TryParseStblData(data, out var names))
                {
                    objectNames.AddRange(names);
                }
            }
            catch
            {
                // Continue on parse errors
            }
        }

        return objectNames;
    }

    private Dictionary<ulong, string> LoadResolvedNames()
    {
        var nameMap = new Dictionary<ulong, string>();

        // Try to find object names
        var objectNames = ReadObjectNames(0);
        var groupedByInstance = objectNames.GroupBy(x => (ulong)x.Key).ToList();

        foreach (var group in groupedByInstance)
        {
            if (group.Count() > 0)
            {
                var name = group.First().Text;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    nameMap[group.Key] = name;
                }
            }
        }

        return nameMap;
    }

    private Sims4Asset? RecordToAsset(DbpfRecord record)
    {
        return record.TypeId switch
        {
            ModelTypeId => new Sims4Asset(Sims4AssetKind.Model, record),
            TextureTypeId => new Sims4Asset(Sims4AssetKind.Texture, record),
            _ => null
        };
    }

    public void Dispose()
    {
        _instance.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Lightweight list-like wrapper around array of DbpfRecord.
/// </summary>
public readonly struct DbpfRecordList : IEnumerable<DbpfRecord>
{
    private readonly DbpfRecord[] _records;

    internal DbpfRecordList(DbpfRecord[] records)
    {
        _records = records;
    }

    public int Count => _records.Length;

    public DbpfRecord this[int index] => _records[index];

    public IEnumerator<DbpfRecord> GetEnumerator()
    {
        foreach (var record in _records)
            yield return record;
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
