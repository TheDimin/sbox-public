using System.Collections.ObjectModel;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	public GltfPrimitive ReadPrimitive( int mesh, int primitive )
	{
		var value = SceneArrayElement( ElementAt( "meshes", mesh ), "primitives", primitive );
		if ( value.ValueKind != JsonValueKind.Object ) throw new GltfException( "A mesh primitive must be an object." );
		var attributes = GltfJson.Required( value, "attributes" );
		if ( attributes.ValueKind != JsonValueKind.Object || !attributes.EnumerateObject().Any() )
			throw new GltfException( "A primitive must define at least one attribute." );
		var result = new Dictionary<string, int>( StringComparer.Ordinal );
		int? vertexCount = null;
		foreach ( var attribute in attributes.EnumerateObject() )
		{
			var index = GltfJson.Integer( attribute.Value, attribute.Name );
			var layout = Accessor( index );
			if ( vertexCount.HasValue && vertexCount != layout.Count ) throw new GltfException( "Primitive attribute counts must agree." );
			vertexCount = layout.Count;
			ValidateAttribute( attribute.Name, layout, index );
			if ( !result.TryAdd( attribute.Name, index ) ) throw new GltfException( $"Duplicate primitive attribute '{attribute.Name}'." );
		}
		ValidateAttributeSets( result.Keys );
		var mode = GltfJson.Int( value, "mode", 4 );
		uint[] indices;
		if ( TryReadDracoPrimitive( mesh, primitive, out var dracoIndices ) )
		{
			indices = dracoIndices;
			mode = 4;
		}
		else if ( value.TryGetProperty( "indices", out _ ) )
		{
			var index = GltfJson.Int( value, "indices" );
			indices = ReadIndexAccessor( index );
			var type = Accessor( index ).ComponentType;
			var restart = type == 5121 ? byte.MaxValue : type == 5123 ? ushort.MaxValue : uint.MaxValue;
			if ( indices.Any( x => x >= vertexCount.Value || x == restart ) ) throw new GltfException( "Primitive index is outside its attributes or uses the reserved restart value." );
		}
		else
		{
			indices = new uint[vertexCount.Value];
			for ( var i = 0; i < indices.Length; i++ ) indices[i] = (uint)i;
		}
		var validCount = mode switch
		{
			0 => indices.Length > 0,
			1 => indices.Length > 0 && indices.Length % 2 == 0,
			2 or 3 => indices.Length >= 2,
			4 => indices.Length > 0 && indices.Length % 3 == 0,
			5 or 6 => indices.Length >= 3,
			_ => false
		};
		if ( !validCount ) throw new GltfException( "Invalid primitive mode or index count." );
		int? material = value.TryGetProperty( "material", out _ ) ? GltfJson.Int( value, "material" ) : null;
		if ( material.HasValue ) _ = ElementAt( "materials", material.Value );
		return new GltfPrimitive { VertexCount = vertexCount.Value, Mode = mode, Indices = indices, Material = material, Attributes = new ReadOnlyDictionary<string, int>( result ) };
	}

	private static void ValidateAttributeSets( IEnumerable<string> names )
	{
		var keys = names.ToHashSet( StringComparer.Ordinal );
		foreach ( var name in keys )
		{
			foreach ( var group in new[] { "TEXCOORD_", "COLOR_", "JOINTS_", "WEIGHTS_" } )
			{
				if ( !name.StartsWith( group, StringComparison.Ordinal ) ) continue;
				var number = AttributeSetIndex( name, group );
				if ( number > 0 && !keys.Contains( group + (number - 1) ) ) throw new GltfException( $"Attribute set '{name}' has a gap." );
				if ( group is "JOINTS_" or "WEIGHTS_" )
				{
					var partner = (group == "JOINTS_" ? "WEIGHTS_" : "JOINTS_") + number;
					if ( !keys.Contains( partner ) ) throw new GltfException( $"Skinning attribute '{name}' requires '{partner}'." );
				}
			}
		}
	}

	private void ValidateAttribute( string semantic, AccessorLayout layout, int accessorIndex, bool? selectedDraco = null )
	{
		// Published glTF 2.0.1 forbids UNSIGNED_INT for mesh vertex attributes.
		// Keep this role-specific: general accessors, indices, and extension
		// instance attributes have their own validation and decoding paths.
		if ( layout.ComponentType == 5125 ) throw new GltfException( "Mesh vertex attributes cannot use UNSIGNED_INT component type." );
		var type = GltfJson.String( layout.Json, "type" );
		// Capability alone does not make extension-only attribute layouts legal:
		// the asset must declare this non-optional extension as required too.
		var quantized = SupportsExtension( "KHR_mesh_quantization" ) && SceneRequiresQuantization();
		var floating = layout.ComponentType == 5126;
		var unsignedNormalized = layout.Normalized && layout.ComponentType is 5121 or 5123;
		var signedNormalized = layout.Normalized && layout.ComponentType is 5120 or 5122;
		var smallInteger = layout.ComponentType is 5120 or 5121 or 5122 or 5123;
		bool valid;
		if ( semantic == "POSITION" )
		{
			valid = type == "VEC3" && (floating || quantized && smallInteger);
			foreach ( var bound in new[] { "min", "max" } )
				_ = SceneFloatArray( GltfJson.Required( layout.Json, bound ), 3, "POSITION bounds" );
		}
		else if ( semantic == "NORMAL" ) valid = type == "VEC3" && (floating || quantized && signedNormalized);
		else if ( semantic == "TANGENT" ) valid = type == "VEC4" && (floating || quantized && signedNormalized);
		else if ( semantic.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) ) valid = type == "VEC2" && (floating || unsignedNormalized || quantized && smallInteger);
		else if ( semantic.StartsWith( "COLOR_", StringComparison.Ordinal ) ) valid = type is "VEC3" or "VEC4" && (floating || unsignedNormalized);
		else if ( semantic.StartsWith( "JOINTS_", StringComparison.Ordinal ) ) valid = type == "VEC4" && !layout.Normalized && layout.ComponentType is 5121 or 5123;
		else if ( semantic.StartsWith( "WEIGHTS_", StringComparison.Ordinal ) ) valid = type == "VEC4" && (floating || unsignedNormalized);
		else valid = true;
		if ( !valid ) throw new GltfException( $"Invalid accessor layout for '{semantic}'." );
		if ( !(selectedDraco ?? IsDracoAttributeAccessor( accessorIndex )) && layout.Json.TryGetProperty( "bufferView", out _ ) )
		{
			var view = ElementAt( "bufferViews", GltfJson.Int( layout.Json, "bufferView" ) );
			var offset = GltfJson.Int( layout.Json, "byteOffset", 0 );
			var stride = GltfJson.Int( view, "byteStride", layout.Stride );
			if ( offset % 4 != 0 || stride % 4 != 0 )
				throw new GltfException( "Vertex attribute offsets and strides must be aligned to four bytes." );
		}
	}

	private static int AttributeSetIndex( string name, string group )
	{
		var number = name.AsSpan( group.Length );
		if ( number.Length is < 1 or > 9 || number.Length > 1 && number[0] == '0' ) throw new GltfException( $"Invalid attribute set '{name}'." );
		foreach ( var c in number ) if ( c is < '0' or > '9' ) throw new GltfException( $"Invalid attribute set '{name}'." );
		return int.Parse( number, System.Globalization.CultureInfo.InvariantCulture );
	}
}
