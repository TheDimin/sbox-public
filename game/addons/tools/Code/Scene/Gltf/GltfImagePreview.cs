using System.Runtime.CompilerServices;

namespace Editor;

/// <summary>Small preview pictures for glTF images and textures, decoded once per document revision-independent element.</summary>
static class GltfImagePreview
{
	static readonly ConditionalWeakTable<GltfElement, Pixmap> cache = new();

	/// <summary>A square preview of at most <paramref name="size"/> pixels, or null if the image cannot be decoded.</summary>
	public static Pixmap Get( GltfElement element, int size )
	{
		var image = element.Document.GetImage( element );
		if ( image is null ) return null;
		if ( cache.TryGetValue( image, out var existing ) && existing.Width >= size ) return existing;
		var bytes = image.Document.ReadImageBytes( image );
		if ( bytes is null ) return null;
		try
		{
			using var bitmap = Bitmap.CreateFromBytes( bytes );
			if ( bitmap is null ) return null;
			var scale = Math.Min( 1f, (float)size / Math.Max( bitmap.Width, bitmap.Height ) );
			using var small = scale < 1f ? bitmap.Resize( Math.Max( 1, (int)(bitmap.Width * scale) ), Math.Max( 1, (int)(bitmap.Height * scale) ) ) : bitmap;
			var pixmap = Pixmap.FromBitmap( small );
			cache.AddOrUpdate( image, pixmap );
			return pixmap;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not decode a preview of {image}: {e.Message}" );
			return null;
		}
	}
}
