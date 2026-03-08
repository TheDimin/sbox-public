namespace Sims4.Dbpf.Enums;

public enum CompressionType : ushort
{
    Uncompressed = 0x0000,
    Zlib         = 0x5A42,  // "ZB"
    Streamable   = 0xFFFF,
    Deleted      = 0xFFE0,
}
