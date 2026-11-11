using NativeEngine;
using Sandbox.Gltf;
using System.Buffers.Binary;
using System.Collections;

namespace Sandbox;

/// <summary>
/// Stages decoded static geometry with a runtime-described native vertex layout.
/// Transformed/generated positions and directions use float fields; untouched
/// attributes retain supported accessor formats, offsets and strides when copied.
/// The native Mesh API accepts one stream, so separate source views are interleaved
/// explicitly rather than interpreted as a fixed C# vertex structure.
/// </summary>
internal static class GltfModelPrimitiveBuilder
{
	// A diagnostic value, never used as a GPU vertex or given a VertexLayout.
	internal readonly record struct VertexSnapshot( Vector3 Position, Vector3 Normal, Vector4 Tangent, Vector4 Color, Vector2 TexCoord0, Vector2 TexCoord1 );

	internal sealed class VertexView : IEnumerable<VertexSnapshot>
	{
		private readonly PreparedPrimitive primitive;
		internal VertexView( PreparedPrimitive primitive ) => this.primitive = primitive;
		internal int Length => primitive.VertexCount;
		internal VertexSnapshot this[int index] => primitive.ReadVertex( index );
		public IEnumerator<VertexSnapshot> GetEnumerator()
		{
			for ( var i = 0; i < Length; i++ ) yield return this[i];
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	internal sealed class PreparedPrimitive
	{
		internal byte[] VertexBytes { get; init; }
		internal RawVertexField[] VertexFields { get; init; }
		internal int VertexStride { get; init; }
		internal int VertexCount { get; init; }
		internal int[] Indices { get; init; }
		internal MeshPrimitiveType PrimitiveType { get; init; }
		internal BBox Bounds { get; init; }
		internal VertexView Vertices => new( this );
		internal void CreateVertexBuffer( Mesh mesh ) => mesh.CreateRawVertexBuffer( VertexStride, VertexCount, VertexBytes, VertexFields );

		private Vector4 ReadField( string semantic, int semanticIndex, int vertex, Vector4 fallback )
		{
			foreach ( var field in VertexFields )
			{
				if ( field.Semantic != semantic || field.SemanticIndex != semanticIndex ) continue;
				var span = VertexBytes.AsSpan( vertex * VertexStride + field.Offset );
				return ReadNativeVector( span, field.Format, fallback );
			}
			return fallback;
		}

		internal VertexSnapshot ReadVertex( int vertex )
		{
			if ( (uint)vertex >= (uint)VertexCount ) throw new ArgumentOutOfRangeException( nameof( vertex ) );
			var p = ReadField( "position", 0, vertex, Vector4.Zero );
			var n = ReadField( "normal", 0, vertex, Vector4.Zero );
			var t = ReadField( "tangent", 0, vertex, Vector4.Zero );
			var c = ReadField( "color", 0, vertex, Vector4.One );
			var uv0 = ReadField( "texcoord", 0, vertex, Vector4.Zero );
			var uv1 = ReadField( "texcoord", 1, vertex, Vector4.Zero );
			return new VertexSnapshot( new( p.x, p.y, p.z ), new( n.x, n.y, n.z ), t, c, new( uv0.x, uv0.y ), new( uv1.x, uv1.y ) );
		}
	}

	/// <summary>The supplied material remains caller-owned.</summary>
	internal static Model Build( GltfDrawData data, Material suppliedMaterial, string name = null )
	{
		if ( suppliedMaterial is null || !suppliedMaterial.IsValid ) throw new ArgumentException( "A valid caller-supplied material is required.", nameof( suppliedMaterial ) );
		var prepared = Prepare( data );
		var mesh = new Mesh( name, suppliedMaterial, prepared.PrimitiveType );
		prepared.CreateVertexBuffer( mesh );
		mesh.CreateIndexBuffer( prepared.Indices.Length, prepared.Indices.AsSpan() );
		mesh.Bounds = prepared.Bounds;
		var builder = Model.Builder.AddMesh( mesh ).WithViewBounds( prepared.Bounds );
		if ( !string.IsNullOrWhiteSpace( name ) ) builder.WithName( name );
		return builder.Create();
	}

	private sealed class FieldSource
	{
		internal RawVertexField Field;
		internal int Size;
		internal Func<int, System.Numerics.Vector4> Values;
		internal GltfUploadStream Raw;
		internal int RawSize;
		internal ushort[] JointIndices;
		internal int? RigidBone;
	}

	/// <summary>
	/// Validates and stages all bytes before native allocation. Source upload
	/// descriptors are optional for already-decoded callers; Model.Load supplies
	/// them to retain normalized/integer attribute storage where native formats allow.
	/// </summary>
	internal static PreparedPrimitive Prepare( GltfDrawData data, GltfMaterial material = null, GltfPrimitiveUploadData upload = null, GltfVertexSkinning skinning = null, GltfModelMorphs morphs = null )
	{
		ArgumentNullException.ThrowIfNull( data );
		if ( data.HasSkinning && skinning is null || data.HasMorphTargets && morphs is null ) throw new GltfException( "Skinned and morphed primitives require a deformation-capable model path." );
		var count = data.Positions.Length;
		if ( skinning is not null && skinning.VertexCount != count ) throw new GltfException( "Skinning binding count differs from its primitive." );
		if ( count == 0 || !data.Normals.IsEmpty && data.Normals.Length != count || !data.Tangents.IsEmpty && data.Tangents.Length != count || !data.Colors.IsEmpty && data.Colors.Length != count )
			throw new GltfException( "Primitive vertex streams have inconsistent lengths." );
		if ( upload is not null && upload.Layout.VertexCount != count ) throw new GltfException( "Source upload descriptor has a different vertex count." );
		var mode = data.Mode switch { 0 => MeshPrimitiveType.Points, 1 => MeshPrimitiveType.Lines, 4 => MeshPrimitiveType.Triangles, _ => throw new GltfException( "Primitive must have expanded point, line or triangle topology." ) };
		var indexCount = data.Indices.Length;
		if ( indexCount == 0 || data.Mode == 1 && indexCount % 2 != 0 || data.Mode == 4 && indexCount % 3 != 0 ) throw new GltfException( "Primitive index count is invalid for its topology." );
		var indices = new int[indexCount];
		for ( var i = 0; i < indexCount; i++ )
		{
			var index = data.Indices.Span[i];
			if ( index >= (uint)count || index > int.MaxValue ) throw new GltfException( "Primitive index exceeds the vertex stream." );
			indices[i] = (int)index;
		}
		foreach ( var position in data.Positions.Span ) if ( !Finite( position ) ) throw new GltfException( "Position contains non-finite values." );
		foreach ( var normal in data.Normals.Span ) if ( !Finite( normal ) ) throw new GltfException( "Normal contains non-finite values." );
		foreach ( var tangent in data.Tangents.Span ) if ( !Finite( tangent ) ) throw new GltfException( "Tangent contains non-finite values." );
		foreach ( var color in data.Colors.Span ) if ( !Finite( color ) ) throw new GltfException( "Color contains non-finite values." );
		if ( material?.NormalTexture is not null && (data.Normals.IsEmpty || data.Tangents.IsEmpty) ) throw new GltfException( "Normal-mapped geometry without authored normals and tangents requires MikkTSpace tangent generation." );

		var texture = SelectStockUv( material );
		var texCoords = data.TexCoords ?? new Dictionary<int, ReadOnlyMemory<System.Numerics.Vector2>>();
		foreach ( var (set, values) in texCoords )
		{
			if ( set < 0 || values.Length != count ) throw new GltfException( $"TEXCOORD_{set} has an invalid set or vertex count." );
			foreach ( var uv in values.Span ) if ( !Finite( uv ) ) throw new GltfException( $"TEXCOORD_{set} contains non-finite values." );
		}
		if ( texture is not null && !texCoords.ContainsKey( texture.TexCoord ) ) throw new GltfException( $"Material requires missing TEXCOORD_{texture.TexCoord}." );
		var flat = data.Mode == 4 && data.Normals.IsEmpty;
		var outputCount = flat ? indexCount : count;
		if ( morphs is not null && morphs.Targets.Any( target => target.Positive.Length != outputCount || target.Negative.Length != outputCount ) ) throw new GltfException( "Morph targets do not match the staged native vertex mapping." );
		var normals = flat ? FlatNormals( data, indices ) : data.Normals;
		var fields = new List<FieldSource>();
		var stride = 0;
		void Add( string semantic, int set, int components, Func<int, System.Numerics.Vector4> values, string sourceSemantic = null, bool rawAllowed = false )
		{
			GltfUploadStream raw = null;
			if ( rawAllowed && sourceSemantic is not null && upload?.Attributes.TryGetValue( sourceSemantic, out raw ) == true && raw.Count != count ) throw new GltfException( "Source upload attribute count differs from the draw data." );
			var format = raw is null ? FloatFormat( components ) : SourceFormat( raw.ComponentType, raw.Components, raw.Normalized );
			// Native APIs have no three-component 8/16-bit format. Conversion here is
			// explicit and uses decoded values, including glTF's normalized semantics.
			if ( format == ColorFormat.COLOR_FORMAT_UNKNOWN ) { raw = null; format = FloatFormat( components ); }
			var size = raw is null ? components * sizeof( float ) : ComponentSize( raw.ComponentType ) * raw.Components;
			stride = checked((stride + 3) & ~3);
			fields.Add( new FieldSource { Field = new( semantic, set, stride, format ), Size = size, Values = values, Raw = raw, RawSize = size } );
			stride = checked(stride + size);
		}
		Add( "position", 0, 3, i => new( data.Positions.Span[i], 0 ) );
		Add( "normal", 0, 3, i => new( flat ? normals.Span[i] : data.Normals.IsEmpty ? System.Numerics.Vector3.Zero : data.Normals.Span[i], 0 ) );
		Add( "tangent", 0, 4, i => !flat && !data.Normals.IsEmpty && !data.Tangents.IsEmpty ? data.Tangents.Span[i] : StockFrameTangent( flat ? normals.Span[i] : data.Normals.IsEmpty ? System.Numerics.Vector3.Zero : data.Normals.Span[i] ) );
		if ( !data.Colors.IsEmpty || upload?.Attributes.ContainsKey( "COLOR_0" ) == true )
			Add( "color", 0, 4, i => data.Colors.IsEmpty ? System.Numerics.Vector4.One : data.Colors.Span[i], "COLOR_0", true );
		var uvSets = texCoords.Keys.ToHashSet();
		if ( texture is not null ) uvSets.Add( 0 );
		foreach ( var set in uvSets.Order() )
		{
			var uvSet = set == 0 && texture is not null ? texture.TexCoord : set;
			var transformed = set == 0 && texture is not null;
			var coordinates = texCoords[uvSet];
			Add( "texcoord", set, 2, i => new( transformed ? texture.TransformUv( coordinates.Span[i] ) : coordinates.Span[i], 0, 0 ), "TEXCOORD_" + uvSet, !transformed );
		}
		if ( skinning is not null )
		{
			var setCount = skinning.RigidBone.HasValue ? 1 : skinning.Sets.Count;
			for ( var set = 0; set < setCount; set++ )
			{
				stride = checked((stride + 3) & ~3);
				fields.Add( new FieldSource { Field = new( "blendindices", set, stride, ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT ), Size = 8, JointIndices = skinning.RigidBone.HasValue ? null : skinning.Sets[set].Joints, RigidBone = skinning.RigidBone } );
				stride = checked(stride + 8);
				if ( skinning.RigidBone.HasValue )
				{
					fields.Add( new FieldSource { Field = new( "blendweight", set, stride, ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM ), Size = 4, RigidBone = skinning.RigidBone } );
					stride = checked(stride + 4);
				}
				else
				{
					var weights = skinning.Sets[set].Weights;
					var size = ComponentSize( weights.ComponentType ) * 4;
					fields.Add( new FieldSource { Field = new( "blendweight", set, stride, SourceFormat( weights.ComponentType, 4, weights.Normalized ) ), Size = size, Raw = weights, RawSize = size } );
					stride = checked(stride + size);
				}
			}
		}
		// Preserve additional source attributes as declared fields. Stock shaders
		// may not consume them, but the importer does not discard their bytes.
		if ( upload is not null )
			foreach ( var (semantic, stream) in upload.Attributes )
			{
				if ( skinning is not null && (semantic.StartsWith( "JOINTS_", StringComparison.Ordinal ) || semantic.StartsWith( "WEIGHTS_", StringComparison.Ordinal )) ) continue;
				if ( semantic is "POSITION" or "NORMAL" or "TANGENT" or "COLOR_0" ) continue;
				if ( semantic.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) && int.TryParse( semantic.AsSpan( 9 ), out var texCoordSet ) && fields.Any( value => value.Field.Semantic == "texcoord" && value.Field.SemanticIndex == texCoordSet ) ) continue;
				var format = SourceFormat( stream.ComponentType, stream.Components, stream.Normalized );
				if ( format == ColorFormat.COLOR_FORMAT_UNKNOWN ) throw new GltfException( $"Native vertex layout cannot preserve attribute '{semantic}' in its accessor format." );
				stride = checked((stride + 3) & ~3);
				var size = ComponentSize( stream.ComponentType ) * stream.Components;
				var name = semantic; var set = 0;
				if ( semantic.StartsWith( "COLOR_", StringComparison.Ordinal ) && int.TryParse( semantic.AsSpan( 6 ), out var colorSet ) ) { name = "color"; set = colorSet; }
				if ( semantic.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) && int.TryParse( semantic.AsSpan( 9 ), out var uvSet ) ) { name = "texcoord"; set = uvSet; }
				fields.Add( new FieldSource { Field = new( name, set, stride, format ), Raw = stream, Size = size, RawSize = size } );
				stride = checked(stride + size);
			}
		stride = checked((stride + 3) & ~3);
		if ( (long)stride * outputCount > Array.MaxLength ) throw new GltfException( "Primitive exceeds supported native buffer size." );
		var bytes = new byte[stride * outputCount];
		for ( var vertex = 0; vertex < outputCount; vertex++ )
		{
			var source = flat ? indices[vertex] : vertex;
			foreach ( var field in fields )
			{
				var target = bytes.AsSpan( vertex * stride + field.Field.Offset, field.Size );
				if ( field.JointIndices is not null || field.RigidBone.HasValue && field.Field.Semantic == "blendindices" )
				{
					for ( var component = 0; component < 4; component++ ) BinaryPrimitives.WriteUInt16LittleEndian( target.Slice( component * 2, 2 ), field.JointIndices is not null ? field.JointIndices[source * 4 + component] : component == 0 ? (ushort)field.RigidBone.Value : (ushort)0 );
				}
				else if ( field.RigidBone.HasValue ) target[0] = 255;
				else if ( field.Raw is not null )
				{
					var stream = field.Raw;
					var offset = (long)stream.ByteOffset + (long)source * stream.ByteStride;
					if ( offset < 0 || offset + field.RawSize > stream.Storage.Bytes.Length ) throw new GltfException( "Source attribute range exceeds its owned storage." );
					stream.Storage.Bytes.Span.Slice( (int)offset, field.RawSize ).CopyTo( target );
				}
				else
				{
					// Generated flat normals/tangents use the corner index; all authored
					// attributes use the mapped original vertex after corner splitting.
					var generated = flat && (field.Field.Semantic is "normal" or "tangent");
					var value = field.Values( generated ? vertex : source );
					WriteFloat( target, value );
				}
			}
		}
		if ( flat ) for ( var i = 0; i < indices.Length; i++ ) indices[i] = i;
		var mins = data.Positions.Span[0]; var maxs = mins;
		foreach ( var position in data.Positions.Span ) { mins = System.Numerics.Vector3.Min( mins, position ); maxs = System.Numerics.Vector3.Max( maxs, position ); }
		var layout = fields.Select( field => field.Field ).ToArray();
		RawVertexLayout.Validate( stride, outputCount, bytes.Length, layout );
		return new PreparedPrimitive { VertexBytes = bytes, VertexFields = layout, VertexCount = outputCount, VertexStride = stride, Indices = indices, PrimitiveType = mode, Bounds = new( mins, maxs ) };
	}

	/// <summary>Track original accessor elements explicitly when generated flat normals split triangle corners.</summary>
	internal static int[] SourceVertexMap( GltfDrawData data ) => data.Mode == 4 && data.Normals.IsEmpty
		? data.Indices.ToArray().Select( index => checked((int)index) ).ToArray()
		: Enumerable.Range( 0, data.Positions.Length ).ToArray();

	private static GltfTextureInfo SelectStockUv( GltfMaterial material )
	{
		if ( material is null ) return null;
		var textures = new[] { material.BaseColorTexture, material.MetallicRoughnessTexture, material.NormalTexture, material.OcclusionTexture, material.EmissiveTexture }.Where( value => value is not null ).ToArray();
		if ( textures.Length == 0 ) return null;
		var first = textures[0];
		if ( textures.Any( value => value.TexCoord != first.TexCoord || value.Offset != first.Offset || value.Tiling != first.Tiling || value.Rotation != first.Rotation ) )
			throw new GltfException( "Stock model shaders require the same texture coordinate set and transform for all material texture roles." );
		return first;
	}

	private static ReadOnlyMemory<System.Numerics.Vector3> FlatNormals( GltfDrawData data, int[] indices )
	{
		var normals = new System.Numerics.Vector3[indices.Length];
		for ( var i = 0; i < indices.Length; i += 3 )
		{
			var a = data.Positions.Span[indices[i]]; var b = data.Positions.Span[indices[i + 1]]; var c = data.Positions.Span[indices[i + 2]];
			var ux = (double)b.X - a.X; var uy = (double)b.Y - a.Y; var uz = (double)b.Z - a.Z;
			var vx = (double)c.X - a.X; var vy = (double)c.Y - a.Y; var vz = (double)c.Z - a.Z;
			var nx = uy * vz - uz * vy; var ny = uz * vx - ux * vz; var nz = ux * vy - uy * vx;
			var length = Math.Sqrt( nx * nx + ny * ny + nz * nz );
			var normal = length > 0 ? new System.Numerics.Vector3( (float)(nx / length), (float)(ny / length), (float)(nz / length) ) : System.Numerics.Vector3.Zero;
			for ( var corner = 0; corner < 3; corner++ ) normals[i + corner] = normal;
		}
		return normals;
	}

	private static System.Numerics.Vector4 StockFrameTangent( System.Numerics.Vector3 normal )
	{
		var length = Math.Sqrt( (double)normal.X * normal.X + (double)normal.Y * normal.Y + (double)normal.Z * normal.Z );
		if ( length == 0 ) return new( 0, 1, 0, 1 );
		var n = new System.Numerics.Vector3( (float)(normal.X / length), (float)(normal.Y / length), (float)(normal.Z / length) );
		var axis = MathF.Abs( n.X ) <= MathF.Abs( n.Y ) && MathF.Abs( n.X ) <= MathF.Abs( n.Z ) ? System.Numerics.Vector3.UnitX : MathF.Abs( n.Y ) <= MathF.Abs( n.Z ) ? System.Numerics.Vector3.UnitY : System.Numerics.Vector3.UnitZ;
		return new( System.Numerics.Vector3.Normalize( System.Numerics.Vector3.Cross( axis, n ) ), 1 );
	}

	private static ColorFormat FloatFormat( int components ) => components switch { 1 => ColorFormat.COLOR_FORMAT_R32_FLOAT, 2 => ColorFormat.COLOR_FORMAT_R32G32_FLOAT, 3 => ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT, 4 => ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT, _ => ColorFormat.COLOR_FORMAT_UNKNOWN };
	private static int ComponentSize( int type ) => type switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw new GltfException( "Unsupported source component type." ) };
	private static ColorFormat SourceFormat( int type, int components, bool normalized ) => (type, components, normalized) switch
	{
		(5126, _, false ) => FloatFormat( components ),
		(5121, 1, true ) => ColorFormat.COLOR_FORMAT_R8_UNORM,
		(5121, 2, true ) => ColorFormat.COLOR_FORMAT_R8G8_UNORM,
		(5121, 4, true ) => ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM,
		(5123, 2, true ) => ColorFormat.COLOR_FORMAT_R16G16_UNORM,
		(5123, 1, true ) => ColorFormat.COLOR_FORMAT_R16_UNORM,
		(5123, 4, true ) => ColorFormat.COLOR_FORMAT_R16G16B16A16_UNORM,
		(5120, 2, true ) => ColorFormat.COLOR_FORMAT_R8G8_SNORM,
		(5120, 1, true ) => ColorFormat.COLOR_FORMAT_R8_SNORM,
		(5120, 4, true ) => ColorFormat.COLOR_FORMAT_R8G8B8A8_SNORM,
		(5122, 2, true ) => ColorFormat.COLOR_FORMAT_R16G16_SNORM,
		(5122, 1, true ) => ColorFormat.COLOR_FORMAT_R16_SNORM,
		(5122, 4, true ) => ColorFormat.COLOR_FORMAT_R16G16B16A16_SNORM,
		(5121, 1, false ) => ColorFormat.COLOR_FORMAT_R8_UINT,
		(5121, 2, false ) => ColorFormat.COLOR_FORMAT_R8G8_UINT,
		(5121, 4, false ) => ColorFormat.COLOR_FORMAT_R8G8B8A8_UINT,
		(5123, 1, false ) => ColorFormat.COLOR_FORMAT_R16_UINT,
		(5123, 2, false ) => ColorFormat.COLOR_FORMAT_R16G16_UINT,
		(5123, 4, false ) => ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT,
		(5120, 1, false ) => ColorFormat.COLOR_FORMAT_R8_SINT,
		(5120, 2, false ) => ColorFormat.COLOR_FORMAT_R8G8_SINT,
		(5120, 4, false ) => ColorFormat.COLOR_FORMAT_R8G8B8A8_SINT,
		(5122, 1, false ) => ColorFormat.COLOR_FORMAT_R16_SINT,
		(5122, 2, false ) => ColorFormat.COLOR_FORMAT_R16G16_SINT,
		(5122, 4, false ) => ColorFormat.COLOR_FORMAT_R16G16B16A16_SINT,
		(5125, 1, false ) => ColorFormat.COLOR_FORMAT_R32_UINT,
		(5125, 2, false ) => ColorFormat.COLOR_FORMAT_R32G32_UINT,
		(5125, 3, false ) => ColorFormat.COLOR_FORMAT_R32G32B32_UINT,
		(5125, 4, false ) => ColorFormat.COLOR_FORMAT_R32G32B32A32_UINT,
		_ => ColorFormat.COLOR_FORMAT_UNKNOWN
	};

	private static void WriteFloat( Span<byte> output, System.Numerics.Vector4 value )
	{
		for ( var i = 0; i < output.Length / sizeof( float ); i++ ) BinaryPrimitives.WriteSingleLittleEndian( output.Slice( i * sizeof( float ), sizeof( float ) ), i switch { 0 => value.X, 1 => value.Y, 2 => value.Z, _ => value.W } );
	}

	private static Vector4 ReadNativeVector( ReadOnlySpan<byte> bytes, ColorFormat format, Vector4 fallback )
	{
		var components = format switch
		{
			ColorFormat.COLOR_FORMAT_R32_FLOAT => 1,
			ColorFormat.COLOR_FORMAT_R32G32_FLOAT or ColorFormat.COLOR_FORMAT_R8G8_UNORM or ColorFormat.COLOR_FORMAT_R16G16_UNORM or ColorFormat.COLOR_FORMAT_R8G8_SNORM or ColorFormat.COLOR_FORMAT_R16G16_SNORM => 2,
			ColorFormat.COLOR_FORMAT_R32G32B32_FLOAT => 3,
			_ => 4
		};
		for ( var i = 0; i < components; i++ )
		{
			var value = format switch
			{
				ColorFormat.COLOR_FORMAT_R8G8_UNORM or ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM => bytes[i] / 255f,
				ColorFormat.COLOR_FORMAT_R16G16_UNORM or ColorFormat.COLOR_FORMAT_R16G16B16A16_UNORM => BinaryPrimitives.ReadUInt16LittleEndian( bytes.Slice( i * 2, 2 ) ) / 65535f,
				ColorFormat.COLOR_FORMAT_R8G8_SNORM or ColorFormat.COLOR_FORMAT_R8G8B8A8_SNORM => MathF.Max( (sbyte)bytes[i] / 127f, -1 ),
				ColorFormat.COLOR_FORMAT_R16G16_SNORM or ColorFormat.COLOR_FORMAT_R16G16B16A16_SNORM => MathF.Max( BinaryPrimitives.ReadInt16LittleEndian( bytes.Slice( i * 2, 2 ) ) / 32767f, -1 ),
				_ => BinaryPrimitives.ReadSingleLittleEndian( bytes.Slice( i * 4, 4 ) )
			};
			if ( i == 0 ) fallback.x = value; else if ( i == 1 ) fallback.y = value; else if ( i == 2 ) fallback.z = value; else fallback.w = value;
		}
		return fallback;
	}

	private static bool Finite( System.Numerics.Vector2 v ) => float.IsFinite( v.X ) && float.IsFinite( v.Y );
	private static bool Finite( System.Numerics.Vector3 v ) => float.IsFinite( v.X ) && float.IsFinite( v.Y ) && float.IsFinite( v.Z );
	private static bool Finite( System.Numerics.Vector4 v ) => float.IsFinite( v.X ) && float.IsFinite( v.Y ) && float.IsFinite( v.Z ) && float.IsFinite( v.W );
}
