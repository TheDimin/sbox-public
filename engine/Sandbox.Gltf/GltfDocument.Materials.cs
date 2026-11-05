using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	private static void MaterialRequireObject( JsonElement value, string description )
	{
		if ( value.ValueKind != JsonValueKind.Object )
			throw new GltfException( $"{description} must be an object." );
	}

	private static JsonElement MaterialProperty( JsonElement value, string property )
	{
		if ( value.ValueKind == JsonValueKind.Undefined )
			return default;
		MaterialRequireObject( value, "glTF property" );
		return value.TryGetProperty( property, out var result ) ? result : default;
	}

	private static JsonElement MaterialExtension( JsonElement value, string name )
	{
		var extensions = MaterialProperty( value, "extensions" );
		return extensions.ValueKind == JsonValueKind.Undefined ? default : MaterialProperty( extensions, name );
	}

	private static int MaterialInteger( JsonElement value, string description )
		=> GltfJson.Integer( value, description );

	private static int MaterialNonnegativeInteger( JsonElement value, string description )
	{
		var result = MaterialInteger( value, description );
		if ( result < 0 )
			throw new GltfException( $"{description} must not be negative." );
		return result;
	}

	private static int MaterialOptionalInteger( JsonElement value, string property, int fallback )
	{
		var element = MaterialProperty( value, property );
		return element.ValueKind == JsonValueKind.Undefined ? fallback : MaterialNonnegativeInteger( element, property );
	}

	private static float MaterialNumber( JsonElement value, string description )
	{
		if ( value.ValueKind != JsonValueKind.Number || !value.TryGetSingle( out var result ) || !float.IsFinite( result ) )
			throw new GltfException( $"{description} must be a finite number." );
		return result;
	}

	private static float MaterialOptionalNumber( JsonElement value, string property, float fallback, float min = float.NegativeInfinity, float max = float.PositiveInfinity )
	{
		var element = MaterialProperty( value, property );
		var result = element.ValueKind == JsonValueKind.Undefined ? fallback : MaterialNumber( element, property );
		if ( result < min || result > max )
			throw new GltfException( $"{property} is outside its valid range." );
		return result;
	}

	private static float[] MaterialVector( JsonElement value, string property, float[] fallback, float min = float.NegativeInfinity, float max = float.PositiveInfinity )
	{
		var element = MaterialProperty( value, property );
		if ( element.ValueKind == JsonValueKind.Undefined )
			return fallback;
		if ( element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != fallback.Length )
			throw new GltfException( $"{property} must contain {fallback.Length} numbers." );
		var result = new float[fallback.Length];
		for ( var i = 0; i < result.Length; i++ )
		{
			result[i] = MaterialNumber( element[i], property );
			if ( result[i] < min || result[i] > max )
				throw new GltfException( $"{property} is outside its valid range." );
		}
		return result;
	}

	private GltfTextureInfo MaterialTexture( JsonElement parent, string name, bool srgb )
	{
		var value = MaterialProperty( parent, name );
		if ( value.ValueKind == JsonValueKind.Undefined )
			return null;
		MaterialRequireObject( value, name );
		var index = MaterialNonnegativeInteger( MaterialProperty( value, "index" ), $"{name}.index" );
		_ = ElementAt( "textures", index );
		var texCoord = MaterialOptionalInteger( value, "texCoord", 0 );
		var offset = new float[] { 0, 0 };
		var tiling = new float[] { 1, 1 };
		var rotation = 0f;
		if ( SupportsExtension( "KHR_texture_transform" ) )
		{
			var transform = MaterialExtension( value, "KHR_texture_transform" );
			if ( transform.ValueKind != JsonValueKind.Undefined )
			{
				if ( !ExtensionNames( "extensionsUsed" ).Contains( "KHR_texture_transform", StringComparer.Ordinal ) )
					throw new GltfException( "KHR_texture_transform must be listed in extensionsUsed." );
				MaterialRequireObject( transform, "KHR_texture_transform" );
				offset = MaterialVector( transform, "offset", offset );
				tiling = MaterialVector( transform, "scale", tiling );
				rotation = MaterialOptionalNumber( transform, "rotation", 0 );
				texCoord = MaterialOptionalInteger( transform, "texCoord", texCoord );
			}
		}
		return new GltfTextureInfo
		{
			Index = index,
			TexCoord = texCoord,
			IsSrgb = srgb,
			Scale = name == "normalTexture" ? MaterialOptionalNumber( value, "scale", 1 ) : 1,
			Strength = name == "occlusionTexture" ? MaterialOptionalNumber( value, "strength", 1, 0, 1 ) : 1,
			Offset = new Vector2( offset[0], offset[1] ),
			Tiling = new Vector2( tiling[0], tiling[1] ),
			Rotation = rotation
		};
	}

	public GltfMaterial ReadMaterial( int index )
	{
		var value = ElementAt( "materials", index );
		MaterialRequireObject( value, "material" );
		var pbr = MaterialProperty( value, "pbrMetallicRoughness" );
		if ( pbr.ValueKind != JsonValueKind.Undefined )
			MaterialRequireObject( pbr, "pbrMetallicRoughness" );
		var color = MaterialVector( pbr, "baseColorFactor", [1, 1, 1, 1], 0, 1 );
		var emissive = MaterialVector( value, "emissiveFactor", [0, 0, 0], 0, 1 );
		var alphaElement = MaterialProperty( value, "alphaMode" );
		var alpha = alphaElement.ValueKind == JsonValueKind.Undefined ? "OPAQUE" :
			alphaElement.ValueKind == JsonValueKind.String ? alphaElement.GetString() : throw new GltfException( "alphaMode must be a string." );
		if ( alpha is not ("OPAQUE" or "MASK" or "BLEND") )
			throw new GltfException( "Unsupported alphaMode." );
		var sidedElement = MaterialProperty( value, "doubleSided" );
		if ( sidedElement.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.True or JsonValueKind.False) )
			throw new GltfException( "doubleSided must be a boolean." );
		var unlit = false;
		if ( SupportsExtension( "KHR_materials_unlit" ) )
		{
			var extension = MaterialExtension( value, "KHR_materials_unlit" );
			if ( extension.ValueKind != JsonValueKind.Undefined )
			{
				MaterialRequireObject( extension, "KHR_materials_unlit" );
				unlit = true;
			}
		}
		return new GltfMaterial
		{
			BaseColorFactor = new Vector4( color[0], color[1], color[2], color[3] ),
			MetallicFactor = MaterialOptionalNumber( pbr, "metallicFactor", 1, 0, 1 ),
			RoughnessFactor = MaterialOptionalNumber( pbr, "roughnessFactor", 1, 0, 1 ),
			EmissiveFactor = new Vector3( emissive[0], emissive[1], emissive[2] ),
			AlphaMode = alpha,
			AlphaCutoff = MaterialOptionalNumber( value, "alphaCutoff", 0.5f, 0 ),
			DoubleSided = sidedElement.ValueKind == JsonValueKind.True,
			Unlit = unlit,
			BaseColorTexture = MaterialTexture( pbr, "baseColorTexture", true ),
			MetallicRoughnessTexture = MaterialTexture( pbr, "metallicRoughnessTexture", false ),
			NormalTexture = MaterialTexture( value, "normalTexture", false ),
			OcclusionTexture = MaterialTexture( value, "occlusionTexture", false ),
			EmissiveTexture = MaterialTexture( value, "emissiveTexture", true )
		};
	}

	public GltfSampler ReadSampler( int index )
	{
		var value = ElementAt( "samplers", index );
		MaterialRequireObject( value, "sampler" );
		var magValue = MaterialProperty( value, "magFilter" );
		var minValue = MaterialProperty( value, "minFilter" );
		int? mag = magValue.ValueKind == JsonValueKind.Undefined ? null : MaterialInteger( magValue, "magFilter" );
		int? min = minValue.ValueKind == JsonValueKind.Undefined ? null : MaterialInteger( minValue, "minFilter" );
		var wrapS = MaterialOptionalInteger( value, "wrapS", 10497 );
		var wrapT = MaterialOptionalInteger( value, "wrapT", 10497 );
		if ( mag is not (null or 9728 or 9729) || min is not (null or 9728 or 9729 or 9984 or 9985 or 9986 or 9987) ||
			wrapS is not (10497 or 33071 or 33648) || wrapT is not (10497 or 33071 or 33648) )
			throw new GltfException( "Invalid sampler filter or wrapping mode." );
		return new GltfSampler { MagFilter = mag, MinFilter = min, WrapS = wrapS, WrapT = wrapT };
	}

	public int? GetMaterialForVariant( int mesh, int primitive, int? variant )
	{
		var meshValue = ElementAt( "meshes", mesh );
		var primitives = MaterialProperty( meshValue, "primitives" );
		if ( primitives.ValueKind != JsonValueKind.Array || primitive < 0 || primitive >= primitives.GetArrayLength() )
			throw new GltfException( "Primitive index is out of range." );
		var value = primitives[primitive];
		MaterialRequireObject( value, "primitive" );
		var defaultValue = MaterialProperty( value, "material" );
		int? fallback = defaultValue.ValueKind == JsonValueKind.Undefined ? null : MaterialNonnegativeInteger( defaultValue, "material" );
		if ( fallback is int fallbackIndex )
			_ = ElementAt( "materials", fallbackIndex );
		if ( variant is null || !SupportsExtension( "KHR_materials_variants" ) )
			return fallback;
		var rootExtension = MaterialExtension( Root, "KHR_materials_variants" );
		var variants = MaterialProperty( rootExtension, "variants" );
		if ( variants.ValueKind != JsonValueKind.Array || variants.GetArrayLength() == 0 || variant < 0 || variant >= variants.GetArrayLength() )
			throw new GltfException( "Material variant index is out of range." );
		foreach ( var definition in variants.EnumerateArray() )
		{
			MaterialRequireObject( definition, "material variant" );
			if ( MaterialProperty( definition, "name" ).ValueKind != JsonValueKind.String )
				throw new GltfException( "Material variant name must be a string." );
		}
		var extension = MaterialExtension( value, "KHR_materials_variants" );
		if ( extension.ValueKind == JsonValueKind.Undefined )
			return fallback;
		var mappings = MaterialProperty( extension, "mappings" );
		if ( mappings.ValueKind != JsonValueKind.Array || mappings.GetArrayLength() == 0 )
			throw new GltfException( "Material variant mappings must be a nonempty array." );
		var seen = new HashSet<int>();
		int? result = fallback;
		foreach ( var mapping in mappings.EnumerateArray() )
		{
			var material = MaterialNonnegativeInteger( MaterialProperty( mapping, "material" ), "mapping.material" );
			_ = ElementAt( "materials", material );
			var mappedVariants = MaterialProperty( mapping, "variants" );
			if ( mappedVariants.ValueKind != JsonValueKind.Array || mappedVariants.GetArrayLength() == 0 )
				throw new GltfException( "Mapping variants must be a nonempty array." );
			foreach ( var mapped in mappedVariants.EnumerateArray() )
			{
				var mappedIndex = MaterialNonnegativeInteger( mapped, "mapping variant" );
				if ( mappedIndex >= variants.GetArrayLength() || !seen.Add( mappedIndex ) )
					throw new GltfException( "Invalid or duplicate material variant mapping." );
				if ( mappedIndex == variant )
					result = material;
			}
		}
		return result;
	}
}

public sealed partial class GltfTextureInfo
{
	public Vector2 TransformUv( Vector2 uv )
	{
		var scaled = uv * Tiling;
		var cosine = MathF.Cos( Rotation );
		var sine = MathF.Sin( Rotation );
		return new Vector2( cosine * scaled.X - sine * scaled.Y, sine * scaled.X + cosine * scaled.Y ) + Offset;
	}
}
