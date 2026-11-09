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
		// KHR_texture_basisu permits omitting the core image only when the
		// extension is required; otherwise an unsupported client needs a fallback.
		if ( !source.HasValue && value.TryGetProperty( "extensions", out var declaredExtensions ) &&
			declaredExtensions.ValueKind == JsonValueKind.Object && declaredExtensions.TryGetProperty( "KHR_texture_basisu", out _ ) &&
			!ExtensionNames( "extensionsRequired" ).Contains( "KHR_texture_basisu", StringComparer.Ordinal ) )
			throw new GltfException( "A KHR_texture_basisu texture without a core source must require KHR_texture_basisu." );
		if ( !source.HasValue && value.TryGetProperty( "extensions", out var webpDeclarations ) &&
			webpDeclarations.ValueKind == JsonValueKind.Object && webpDeclarations.TryGetProperty( "EXT_texture_webp", out _ ) &&
			!ExtensionNames( "extensionsRequired" ).Contains( "EXT_texture_webp", StringComparer.Ordinal ) )
			throw new GltfException( "An EXT_texture_webp texture without a core source must require EXT_texture_webp." );
		if ( value.TryGetProperty( "extensions", out var extensions ) )
		{
			if ( extensions.ValueKind != JsonValueKind.Object ) throw new GltfException( "Texture extensions must be an object." );
			var used = ExtensionNames( "extensionsUsed" );
			foreach ( var name in new[] { "KHR_texture_basisu", "EXT_texture_webp" } )
				if ( extensions.TryGetProperty( name, out _ ) && !used.Contains( name, StringComparer.Ordinal ) )
					throw new GltfException( $"{name} must be listed in extensionsUsed." );
			// Prefer Basis when both formats are offered; otherwise use WebP or the
			// core image according to this reader's declared capabilities.
			if ( SupportsExtension( "KHR_texture_basisu" ) && extensions.TryGetProperty( "KHR_texture_basisu", out var basis ) ) source = GltfJson.Int( basis, "source" );
			else if ( SupportsExtension( "EXT_texture_webp" ) && extensions.TryGetProperty( "EXT_texture_webp", out var webp ) ) source = GltfJson.Int( webp, "source" );
		}
		int? sampler = value.TryGetProperty( "sampler", out _ ) ? GltfJson.Int( value, "sampler" ) : null;
		if ( source.HasValue ) _ = ElementAt( "images", source.Value );
		if ( sampler.HasValue ) _ = ReadSampler( sampler.Value );
		return new GltfTexture { Image = source, Sampler = sampler };
	}

	/// <summary>
	/// Validate a texture's already loaded selected image without rereading it.
	/// The caller must supply the image obtained from the index returned by
	/// <see cref="ReadTexture"/>. This permits per-image caching while each
	/// texture independently checks its selected source's required format.
	/// Full image decoding and payload validation remain the compiler's job.
	/// </summary>
	public void ValidateTextureImage( int texture, GltfImage image )
	{
		var selected = ReadTexture( texture );
		if ( !selected.Image.HasValue || image?.Bytes is null ) throw new GltfException( "Texture has no selected image data." );
		var value = ElementAt( "textures", texture );
		string expected = null;
		if ( value.TryGetProperty( "extensions", out var extensions ) )
		{
			if ( SupportsExtension( "KHR_texture_basisu" ) && extensions.TryGetProperty( "KHR_texture_basisu", out _ ) ) expected = "image/ktx2";
			else if ( SupportsExtension( "EXT_texture_webp" ) && extensions.TryGetProperty( "EXT_texture_webp", out _ ) ) expected = "image/webp";
		}
		var detected = ImageMimeType( image.Bytes );
		if ( detected is null || !string.Equals( detected, image.MimeType, StringComparison.OrdinalIgnoreCase ) ||
			(expected is not null ? detected != expected : detected is not ("image/png" or "image/jpeg")) )
			throw new GltfException( "Selected texture image format does not match its source or encoded data." );
	}

	/// <summary>Read and validate the encoded image selected by one texture.</summary>
	public GltfImage ReadTextureImage( int texture )
	{
		var selected = ReadTexture( texture );
		if ( !selected.Image.HasValue ) throw new GltfException( "Texture has no selected image source." );
		var image = ReadImage( selected.Image.Value );
		ValidateTextureImage( texture, image );
		return image;
	}

	internal static string ImageMimeType( ReadOnlySpan<byte> data )
	{
		if ( data.StartsWith( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } ) ) return "image/png";
		if ( data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff ) return "image/jpeg";
		if ( data.Length >= 12 && data[..4].SequenceEqual( "RIFF"u8 ) && data.Slice( 8, 4 ).SequenceEqual( "WEBP"u8 ) ) return "image/webp";
		if ( data.StartsWith( new byte[] { 0xab, 0x4b, 0x54, 0x58, 0x20, 0x32, 0x30, 0xbb, 13, 10, 26, 10 } ) ) return "image/ktx2";
		return null;
	}
}
