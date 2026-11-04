using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>Reads encoded image bytes; image decoding and GPU upload are separate stages.</summary>
	public GltfImage ReadImage( int image )
	{
		var value = ElementAt( "images", image );
		var hasUri = value.TryGetProperty( "uri", out _ );
		var hasView = value.TryGetProperty( "bufferView", out _ );
		if ( hasUri == hasView ) throw new GltfException( $"Image {image} must specify exactly one URI or buffer view." );
		string mime = value.TryGetProperty( "mimeType", out _ ) ? GltfJson.String( value, "mimeType" ) : null;
		byte[] bytes;
		if ( hasUri )
		{
			var uri = GltfJson.String( value, "uri" );
			bytes = ResolveResource( uri );
			if ( uri.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) )
			{
				var comma = uri.IndexOf( ',' );
				var uriMime = uri[5..comma].Split( ';' )[0];
				if ( mime is not null && !string.Equals( mime, uriMime, StringComparison.OrdinalIgnoreCase ) )
					throw new GltfException( $"Image {image} data URI and mimeType disagree." );
				mime ??= uriMime;
			}
		}
		else
		{
			if ( mime is null ) throw new GltfException( $"Buffer view image {image} must declare mimeType." );
			var index = GltfJson.Int( value, "bufferView" );
			var view = ElementAt( "bufferViews", index );
			if ( view.TryGetProperty( "target", out _ ) || view.TryGetProperty( "byteStride", out _ ) )
				throw new GltfException( "Image buffer views cannot specify target or byteStride." );
			bytes = BufferView( index ).ToArray();
		}
		var detected = ImageMimeType( bytes );
		if ( mime is null ) mime = detected;
		if ( string.IsNullOrEmpty( mime ) ) throw new GltfException( $"Cannot determine image {image} MIME type." );
		if ( detected is not null && !string.Equals( mime, detected, StringComparison.OrdinalIgnoreCase ) )
			throw new GltfException( $"Image {image} MIME type does not match its encoded data." );
		return new GltfImage { Bytes = bytes, MimeType = mime };
	}

	public GltfTexture ReadTexture( int texture )
	{
		var value = ElementAt( "textures", texture );
		int? source = value.TryGetProperty( "source", out _ ) ? GltfJson.Int( value, "source" ) : null;
		if ( SupportsExtension( "KHR_texture_basisu" ) && value.TryGetProperty( "extensions", out var extensions ) )
		{
			if ( extensions.ValueKind != JsonValueKind.Object ) throw new GltfException( "Texture extensions must be an object." );
			if ( extensions.TryGetProperty( "KHR_texture_basisu", out var basis ) ) source = GltfJson.Int( basis, "source" );
		}
		int? sampler = value.TryGetProperty( "sampler", out _ ) ? GltfJson.Int( value, "sampler" ) : null;
		if ( source.HasValue ) _ = ElementAt( "images", source.Value );
		if ( sampler.HasValue ) _ = ReadSampler( sampler.Value );
		return new GltfTexture { Image = source, Sampler = sampler };
	}

	private static string ImageMimeType( ReadOnlySpan<byte> data )
	{
		if ( data.StartsWith( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } ) ) return "image/png";
		if ( data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff ) return "image/jpeg";
		if ( data.StartsWith( new byte[] { 0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30, 0xbb, 13, 10, 26, 10 } ) ) return "image/ktx2";
		return null;
	}
}
