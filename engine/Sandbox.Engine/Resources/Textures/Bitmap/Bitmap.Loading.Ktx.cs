using SkiaSharp;
using System.Runtime.InteropServices;

namespace Sandbox;

public partial class Bitmap
{
	private static ReadOnlySpan<byte> Ktx2Identifier => [0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30, 0xbb, 13, 10, 26, 10];

	/// <summary>True when the bytes start with the KTX2 file identifier.</summary>
	public static bool IsKtx2( ReadOnlySpan<byte> data ) => data.Length >= 12 && data[..12].SequenceEqual( Ktx2Identifier );

	/// <summary>Decode the top level of a KTX2 image, including Basis Universal (ETC1S and UASTC), to RGBA. Returns null for other data.</summary>
	public static Bitmap CreateFromKtx2Bytes( byte[] data )
	{
		if ( data is null || !IsKtx2( data ) )
			return default;

		var image = Gltf.GltfKtx.DecodeRgba32( data );
		var level = image.Levels[0];
		var info = new SKImageInfo( level.Width, level.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul );
		var bitmap = new SKBitmap( info );
		Marshal.Copy( level.Pixels, 0, bitmap.GetPixels(), Math.Min( level.Pixels.Length, info.BytesSize ) );
		return new Bitmap( bitmap );
	}
}
