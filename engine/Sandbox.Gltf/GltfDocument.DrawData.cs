using System.Numerics;
using System.Collections.ObjectModel;

namespace Sandbox.Gltf;

/// <summary>Owned, decoded vertex streams for upload. Positions and directions use s&amp;box coordinates.</summary>
public sealed class GltfDrawData
{
	public int Mode { get; init; }
	public int? Material { get; init; }
	public bool HasSkinning { get; init; }
	public bool HasMorphTargets { get; init; }
	public ReadOnlyMemory<uint> Indices { get; init; }
	public ReadOnlyMemory<Vector3> Positions { get; init; }
	public ReadOnlyMemory<Vector3> Normals { get; init; }
	public ReadOnlyMemory<Vector4> Tangents { get; init; }
	public ReadOnlyMemory<Vector4> Colors { get; init; }
	public IReadOnlyDictionary<int, ReadOnlyMemory<Vector2>> TexCoords { get; init; }
}

public sealed partial class GltfDocument
{
	/// <summary>Decodes a primitive once for buffer upload; does not apply node, skin or morph transforms.</summary>
	public GltfDrawData CreateDrawData( int mesh, int primitive )
	{
		var source = ReadPrimitive( mesh, primitive );
		var primitiveJson = SceneArrayElement( ElementAt( "meshes", mesh ), "primitives", primitive );
		var hasMorphTargets = primitiveJson.TryGetProperty( "targets", out var targets ) && RequireArray( targets, "morph targets" ).GetArrayLength() > 0;
		if ( !source.Attributes.ContainsKey( "POSITION" ) )
			throw new GltfException( "Drawing a primitive requires a POSITION attribute." );
		var positions = DrawVectors3( source, "POSITION", true );
		var normals = DrawVectors3( source, "NORMAL", false );
		var tangents = Array.Empty<Vector4>();
		if ( source.Attributes.TryGetValue( "TANGENT", out var tangentIndex ) )
		{
			var values = ReadFloatAccessor( tangentIndex );
			tangents = new Vector4[source.VertexCount];
			for ( var i = 0; i < tangents.Length; i++ )
			{
				var offset = i * 4;
				var direction = GltfGeometry.ConvertDirection( new Vector3( values[offset], values[offset + 1], values[offset + 2] ) );
				tangents[i] = new Vector4( direction, values[offset + 3] );
			}
		}
		var colors = new Vector4[source.VertexCount];
		Array.Fill( colors, Vector4.One );
		if ( source.Attributes.TryGetValue( "COLOR_0", out var colorIndex ) )
		{
			var values = ReadFloatAccessor( colorIndex );
			var components = values.Length / source.VertexCount;
			for ( var i = 0; i < colors.Length; i++ )
			{
				var offset = i * components;
				colors[i] = new Vector4( values[offset], values[offset + 1], values[offset + 2], components == 4 ? values[offset + 3] : 1 );
			}
		}
		var coordinates = new Dictionary<int, ReadOnlyMemory<Vector2>>();
		for ( var set = 0; source.Attributes.TryGetValue( "TEXCOORD_" + set, out var index ); set++ )
		{
			var values = ReadFloatAccessor( index );
			var result = new Vector2[source.VertexCount];
			for ( var i = 0; i < result.Length; i++ ) result[i] = new Vector2( values[i * 2], values[i * 2 + 1] );
			coordinates.Add( set, result );
		}
		return new GltfDrawData
		{
			Mode = source.Mode == 0 ? 0 : source.Mode <= 3 ? 1 : 4,
			Material = source.Material,
			HasSkinning = source.Attributes.ContainsKey( "JOINTS_0" ), HasMorphTargets = hasMorphTargets,
			Indices = GltfGeometry.ExpandIndices( source.Indices, source.Mode ),
			Positions = positions, Normals = normals, Tangents = tangents, Colors = colors,
			TexCoords = new ReadOnlyDictionary<int, ReadOnlyMemory<Vector2>>( coordinates )
		};
	}

	private Vector3[] DrawVectors3( GltfPrimitive source, string semantic, bool position )
	{
		if ( !source.Attributes.TryGetValue( semantic, out var index ) ) return [];
		var values = ReadFloatAccessor( index );
		var result = new Vector3[source.VertexCount];
		for ( var i = 0; i < result.Length; i++ )
		{
			var value = new Vector3( values[i * 3], values[i * 3 + 1], values[i * 3 + 2] );
			result[i] = position ? GltfGeometry.ConvertPosition( value ) : GltfGeometry.ConvertDirection( value );
		}
		return result;
	}
}
