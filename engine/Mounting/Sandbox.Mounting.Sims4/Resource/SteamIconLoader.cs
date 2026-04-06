using Sandbox;
using SkiaSharp;

namespace Mounting.Sims4;

/// <summary>
/// Loads a Steam library cache icon (JPG) as a <see cref="Texture"/>.
/// The source JPG has a black background which is keyed out to transparent.
/// </summary>
internal class SteamIconLoader( string filePath ) : ResourceLoader<SimsMount>
{
	protected override object? Load()
	{
		if ( !System.IO.File.Exists( filePath ) )
			return null;

		var bytes = System.IO.File.ReadAllBytes( filePath );

		using var data = SKData.CreateCopy( bytes );
		using var image = SKImage.FromEncodedData( data );
		if ( image is null )
			return null;

		using var bitmap = SKBitmap.FromImage( image );
		if ( bitmap is null )
			return null;

		// Ensure RGBA8888 for the engine
		using var rgba = bitmap.ColorType == SKColorType.Rgba8888
			? null
			: bitmap.Copy( SKColorType.Rgba8888 );

		var source = rgba ?? bitmap;
		var pixels = source.Bytes;

		// Key out black background — the source JPG has no alpha channel,
		// so near-black pixels are treated as fully transparent.
		for ( int i = 0; i < pixels.Length; i += 4 )
		{
			byte r = pixels[i];
			byte g = pixels[i + 1];
			byte b = pixels[i + 2];

			// Luminance-based threshold to catch dark edges from JPG compression
			if ( r + g + b < 30 )
			{
				pixels[i + 3] = 0;
			}
		}

		return Texture.Create( source.Width, source.Height, ImageFormat.RGBA8888 )
			.WithData( pixels )
			.Finish();
	}
}
