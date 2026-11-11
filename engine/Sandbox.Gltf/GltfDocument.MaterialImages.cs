using System.Text.Json;

namespace Sandbox.Gltf;

/// <summary>
/// Owned encoded image generation. Each read returns a defensive copy; no
/// later access consults a mutable external resolver or exposes retained bytes.
/// The byte count is encoded payload, not decoder/native allocation cost.
/// </summary>
public sealed class GltfMaterialImageSnapshot
{
	private readonly Dictionary<int, (byte[] Bytes, string Mime)> images;
	private readonly Dictionary<int, GltfException> failures;
	public long EncodedBytes { get; }
	internal GltfMaterialImageSnapshot( Dictionary<int, (byte[] Bytes, string Mime)> images, Dictionary<int, GltfException> failures, long encodedBytes )
	{
		this.images = images; this.failures = failures; EncodedBytes = encodedBytes;
	}

	public GltfImage ReadImage( int index )
	{
		if ( failures.TryGetValue( index, out var failure ) )
			throw new GltfException( $"Image {index} could not be retained when this material generation was prepared: {failure.Message}", failure );
		if ( !images.TryGetValue( index, out var image ) )
			throw new GltfException( $"Image {index} is not retained in this material generation." );
		return new GltfImage { Bytes = (byte[])image.Bytes.Clone(), MimeType = image.Mime };
	}
}

public sealed partial class GltfDocument
{
	/// <summary>
	/// Capture core texture image dependencies of the supplied material indices,
	/// deduplicated by image identity, without decoding or realizing materials.
	/// An implicit default (null) has no images. Unknown material extensions
	/// remain ignored until their own supported dependency discovery exists.
	/// Unavailable or invalid images retain their load error and fail when a
	/// selected material actually reads them; they are never reread externally.
	/// The budget bounds retained encoded bytes after resolver return; it cannot
	/// bound allocations already performed by a caller-provided resolver.
	/// </summary>
	public GltfMaterialImageSnapshot SnapshotMaterialImages( IEnumerable<int?> materialIndices, long maxEncodedBytes )
	{
		ArgumentNullException.ThrowIfNull( materialIndices );
		ArgumentOutOfRangeException.ThrowIfNegative( maxEncodedBytes );
		_ = Root;
		var selectedTextures = new Dictionary<int, int>();
		foreach ( var materialIndex in materialIndices.Distinct() )
		{
			if ( materialIndex is not int index ) continue;
			var material = ElementAt( "materials", index );
			var pbr = MaterialProperty( material, "pbrMetallicRoughness" );
			Add( pbr, "baseColorTexture" );
			var unlit = SupportsExtension( "KHR_materials_unlit" ) ? MaterialExtension( material, "KHR_materials_unlit" ) : default;
			if ( unlit.ValueKind != JsonValueKind.Undefined )
			{
				MaterialRequireObject( unlit, "KHR_materials_unlit" );
				continue; // Those roles are fallback-only and are not consumed by supported unlit.
			}
			if ( SupportsExtension( "KHR_materials_pbrSpecularGlossiness" ) )
			{
				var gloss = MaterialExtension( material, "KHR_materials_pbrSpecularGlossiness" );
				Add( gloss, "diffuseTexture" ); Add( gloss, "specularGlossinessTexture" );
			}
			Add( pbr, "metallicRoughnessTexture" ); Add( material, "normalTexture" );
			Add( material, "occlusionTexture" ); Add( material, "emissiveTexture" );
			if ( SupportsExtension( "KHR_materials_diffuse_transmission" ) )
			{
				var transmission = MaterialExtension( material, "KHR_materials_diffuse_transmission" );
				Add( transmission, "diffuseTransmissionTexture" );
				Add( transmission, "diffuseTransmissionColorTexture" );
			}
			if ( SupportsExtension( "KHR_materials_clearcoat" ) )
			{
				var coat = MaterialExtension( material, "KHR_materials_clearcoat" );
				Add( coat, "clearcoatTexture" ); Add( coat, "clearcoatRoughnessTexture" ); Add( coat, "clearcoatNormalTexture" );
			}
			if ( SupportsExtension( "KHR_materials_specular" ) )
			{
				var specular = MaterialExtension( material, "KHR_materials_specular" );
				Add( specular, "specularTexture" ); Add( specular, "specularColorTexture" );
			}
			if ( SupportsExtension( "KHR_materials_sheen" ) )
			{
				var sheen = MaterialExtension( material, "KHR_materials_sheen" );
				Add( sheen, "sheenColorTexture" ); Add( sheen, "sheenRoughnessTexture" );
			}
			if ( SupportsExtension( "KHR_materials_iridescence" ) )
			{
				var iridescence = MaterialExtension( material, "KHR_materials_iridescence" );
				Add( iridescence, "iridescenceTexture" ); Add( iridescence, "iridescenceThicknessTexture" );
			}
			if ( SupportsExtension( "KHR_materials_transmission" ) )
				Add( MaterialExtension( material, "KHR_materials_transmission" ), "transmissionTexture" );
			if ( SupportsExtension( "KHR_materials_volume" ) )
				Add( MaterialExtension( material, "KHR_materials_volume" ), "thicknessTexture" );
			if ( SupportsExtension( "KHR_materials_anisotropy" ) )
				Add( MaterialExtension( material, "KHR_materials_anisotropy" ), "anisotropyTexture" );
		}
		var retained = new Dictionary<int, (byte[] Bytes, string Mime)>();
		var failures = new Dictionary<int, GltfException>();
		long bytes = 0;
		foreach ( var index in selectedTextures.Values.Distinct().Order() )
		{
			GltfImage image;
			try { image = ReadImage( index ); }
			catch ( GltfException error ) { failures.Add( index, error ); continue; }
			if ( image.Bytes.LongLength > maxEncodedBytes - bytes ) throw new GltfException( "Material image encoded-byte budget exceeded." );
			bytes += image.Bytes.LongLength;
			retained.Add( index, ((byte[])image.Bytes.Clone(), image.MimeType) );
		}
		// The material adapter validates each selected texture's source format
		// against these bytes, including when multiple roles share one image.
		return new GltfMaterialImageSnapshot( retained, failures, bytes );

		void Add( JsonElement parent, string property )
		{
			var info = MaterialProperty( parent, property );
			if ( info.ValueKind == JsonValueKind.Undefined ) return;
			MaterialRequireObject( info, property );
			var texture = MaterialNonnegativeInteger( MaterialProperty( info, "index" ), property + ".index" );
			try { if ( ReadTexture( texture ).Image is int image ) selectedTextures[texture] = image; }
			catch ( GltfException ) { /* Selected material parsing repeats and reports this invalid texture. */ }
		}
	}
}
