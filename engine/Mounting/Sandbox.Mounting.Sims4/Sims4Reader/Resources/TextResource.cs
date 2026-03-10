using System.Text;

namespace Sims4Reader;

/// <summary>
/// Plain text / XML resource. Stores the raw text content.
/// </summary>
public class TextResource : IResource
{
    public string Text { get; private set; } = string.Empty;
    public ReadOnlyMemory<byte> RawData { get; private set; }

    public void Parse(ReadOnlyMemory<byte> data)
    {
        RawData = data;
        Text = Encoding.UTF8.GetString(data.Span);
    }
}
