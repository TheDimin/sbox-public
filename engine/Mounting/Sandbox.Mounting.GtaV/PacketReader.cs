using System.IO;
using System.Text;

namespace GtaV;

internal static class PacketReader
{
	internal static string Text( this BinaryReader reader ) => Encoding.UTF8.GetString( reader.ReadBytes( reader.ReadInt32() ) );
	internal static Vector3 Vector( this BinaryReader reader ) => new( reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() );
	internal const float UnitsPerMetre = 1f / 0.0254f;
}
