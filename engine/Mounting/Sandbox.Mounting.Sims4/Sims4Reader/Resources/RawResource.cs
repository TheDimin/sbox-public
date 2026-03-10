namespace Sims4Reader;

/// <summary>
/// A resource that simply holds raw bytes without any parsing.
/// </summary>
public class RawResource : IResource
{
    public ReadOnlyMemory<byte> Data { get; private set; }

    public void Parse(ReadOnlyMemory<byte> data)
    {
        Data = data;
    }
}
