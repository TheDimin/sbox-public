using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.Tests;

using NM = System.Numerics.Matrix4x4;
using NV3 = System.Numerics.Vector3;
using NQ = System.Numerics.Quaternion;

/// <summary>
/// Model-scoped independent source oracle. No GltfDocument, producer accessor,
/// animation, palette or basis helpers are used to calculate expected values.
/// Supports Fox's actual core source/compiled container forms, not all glTF.
/// </summary>
public sealed class FoxReference : IDisposable
{
	private readonly JsonDocument owner;
	private readonly byte[][] buffers;
	private readonly int[] parents;
	private readonly int[] joints;
	private readonly NM[] inverseBinds;
	private readonly NV3[] positions;
	private readonly NV3[] normals;
	private readonly float[] jointIndices, weights;
	public int MeshNode { get; }
	public int Mesh { get; }
	public int VertexCount => positions.Length;
	public int NodeCount => parents.Length;
	public int JointCount => joints.Length;
	public int ClipCount => owner.RootElement.GetProperty( "animations" ).GetArrayLength();
	public string ClipName( int clip ) => owner.RootElement.GetProperty( "animations" )[clip].GetProperty( "name" ).GetString();
	public float[] TexCoords { get; }
	public float[] KeyTimes( int clip ) => owner.RootElement.GetProperty( "animations" )[clip].GetProperty( "samplers" ).EnumerateArray()
		.SelectMany( x => Accessor( x.GetProperty( "input" ).GetInt32() ) ).Distinct().Order().ToArray();
	public float Duration( int clip ) => KeyTimes( clip ).Max();

	public static FoxReference Read( byte[] container, Func<string, byte[]> resolve ) => new( container, resolve );
	private FoxReference( byte[] container, Func<string, byte[]> resolve )
	{
		ReadOnlyMemory<byte> json = container; byte[] binary = null;
		if ( container.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian( container ) == 0x46546c67 )
		{
			Require( BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( 8 ) ) == container.Length, "Invalid reference GLB header." );
			for ( var offset = 12; offset < container.Length; )
			{
				var length = BinaryPrimitives.ReadInt32LittleEndian( container.AsSpan( offset ) ); var type = BinaryPrimitives.ReadUInt32LittleEndian( container.AsSpan( offset + 4 ) ); offset += 8;
				Require( length >= 0 && offset + length <= container.Length && length % 4 == 0, "Invalid reference GLB chunk." );
				if ( type == 0x4e4f534a ) json = container.AsMemory( offset, length );
				if ( type == 0x004e4942 ) binary = container.AsSpan( offset, length ).ToArray();
				offset += length;
			}
		}
		owner = JsonDocument.Parse( json ); var root = owner.RootElement;
		buffers = root.GetProperty( "buffers" ).EnumerateArray().Select( buffer =>
		{
			if ( !buffer.TryGetProperty( "uri", out var uri ) ) return binary ?? throw new InvalidDataException( "Reference GLB binary missing." );
			var path = uri.GetString(); return path.StartsWith( "data:", StringComparison.Ordinal ) ? Convert.FromBase64String( path[(path.IndexOf( ',' ) + 1)..] ) : resolve( path );
		} ).ToArray();
		var nodes = root.GetProperty( "nodes" ); parents = Enumerable.Repeat( -1, nodes.GetArrayLength() ).ToArray();
		for ( var index = 0; index < nodes.GetArrayLength(); index++ ) if ( nodes[index].TryGetProperty( "children", out var children ) )
			foreach ( var child in children.EnumerateArray() ) { Require( parents[child.GetInt32()] == -1, "Reference node has duplicate parents." ); parents[child.GetInt32()] = index; }
		var meshNodes = nodes.EnumerateArray().Select( (value, index) => (value, index) ).Where( x => x.value.TryGetProperty( "mesh", out _ ) ).ToArray();
		Require( meshNodes.Length == 1, "Fox oracle expects one mesh node." ); MeshNode = meshNodes[0].index; Mesh = meshNodes[0].value.GetProperty( "mesh" ).GetInt32();
		var primitive = root.GetProperty( "meshes" )[Mesh].GetProperty( "primitives" )[0]; var attributes = primitive.GetProperty( "attributes" );
		Require( !primitive.TryGetProperty( "indices", out _ ) && !attributes.TryGetProperty( "NORMAL", out _ ), "Fox oracle expects its actual non-indexed flat geometry." );
		var positionValues = Accessor( attributes.GetProperty( "POSITION" ).GetInt32() ); positions = Enumerable.Range( 0, positionValues.Length / 3 ).Select( x => new NV3( positionValues[x * 3], positionValues[x * 3 + 1], positionValues[x * 3 + 2] ) ).ToArray();
		Require( positions.Length % 3 == 0, "Fox triangle corner count is invalid." ); normals = new NV3[positions.Length];
		for ( var vertex = 0; vertex < positions.Length; vertex += 3 )
		{
			var n = Unit( NV3.Cross( positions[vertex + 1] - positions[vertex], positions[vertex + 2] - positions[vertex] ), new NV3( 0, 0, 1 ) );
			normals[vertex] = normals[vertex + 1] = normals[vertex + 2] = n;
		}
		jointIndices = Accessor( attributes.GetProperty( "JOINTS_0" ).GetInt32() ); weights = Accessor( attributes.GetProperty( "WEIGHTS_0" ).GetInt32() );
		TexCoords = Accessor( attributes.GetProperty( "TEXCOORD_0" ).GetInt32() );
		var skin = root.GetProperty( "skins" )[meshNodes[0].value.GetProperty( "skin" ).GetInt32()]; joints = skin.GetProperty( "joints" ).EnumerateArray().Select( x => x.GetInt32() ).ToArray();
		var inverseValues = skin.TryGetProperty( "inverseBindMatrices", out var inverse ) ? Accessor( inverse.GetInt32() ) : null;
		inverseBinds = Enumerable.Range( 0, joints.Length ).Select( x => inverseValues is null ? NM.Identity : Matrix( inverseValues.AsSpan( x * 16, 16 ) ) ).ToArray();
		Require( positions.Length == 1728 && parents.Length == 26 && joints.Length == 24 && ClipCount == 3, "Pinned Fox source inventory changed; audit before accepting." );
	}

	public FoxReferencePose Evaluate( int? clip, float time )
	{
		var nodes = owner.RootElement.GetProperty( "nodes" ); var transforms = new NM[nodes.GetArrayLength()];
		var updates = new Dictionary<(int, string), float[]>();
		if ( clip.HasValue )
		{
			var animation = owner.RootElement.GetProperty( "animations" )[clip.Value]; var samplers = animation.GetProperty( "samplers" );
			foreach ( var channel in animation.GetProperty( "channels" ).EnumerateArray() )
			{
				var target = channel.GetProperty( "target" ); var path = target.GetProperty( "path" ).GetString(); var sampler = samplers[channel.GetProperty( "sampler" ).GetInt32()];
				Require( !sampler.TryGetProperty( "interpolation", out var interpolation ) || interpolation.GetString() == "LINEAR", "Fox oracle only covers authored LINEAR curves." );
				updates.Add( (target.GetProperty( "node" ).GetInt32(), path), Sample( Accessor( sampler.GetProperty( "input" ).GetInt32() ), Accessor( sampler.GetProperty( "output" ).GetInt32() ), path == "rotation" ? 4 : 3, time, path == "rotation" ) );
			}
		}
		for ( var node = 0; node < transforms.Length; node++ )
		{
			var value = nodes[node];
			if ( value.TryGetProperty( "matrix", out var matrix ) ) transforms[node] = Matrix( matrix.EnumerateArray().Select( x => x.GetSingle() ).ToArray() );
			else
			{
				var t = Trs( value, updates, node, "translation", new[] { 0f, 0f, 0f } ); var r = Trs( value, updates, node, "rotation", new[] { 0f, 0f, 0f, 1f } ); var s = Trs( value, updates, node, "scale", new[] { 1f, 1f, 1f } );
				transforms[node] = NM.CreateScale( s[0], s[1], s[2] ) * NM.CreateFromQuaternion( NQ.Normalize( new NQ( r[0], r[1], r[2], r[3] ) ) ) * NM.CreateTranslation( t[0], t[1], t[2] );
			}
		}
		var worlds = new NM[transforms.Length];
		for ( var node = 0; node < transforms.Length; node++ )
		{
			var world = NM.Identity; var visited = new HashSet<int>();
			for ( var current = node; current >= 0; current = parents[current] ) { Require( visited.Add( current ), "Reference hierarchy cycle." ); world *= transforms[current]; }
			worlds[node] = world;
		}
		var palette = inverseBinds.Select( (inverse, joint) => inverse * worlds[joints[joint]] ).ToArray();
		var resultPositions = new NV3[positions.Length]; var resultNormals = new NV3[positions.Length];
		for ( var vertex = 0; vertex < positions.Length; vertex++ )
		{
			var point = NV3.Zero; var linear = new double[9];
			for ( var influence = 0; influence < 4; influence++ )
			{
				var offset = vertex * 4 + influence; var weight = weights[offset]; if ( weight == 0 ) continue;
				var matrix = palette[(int)jointIndices[offset]]; point += NV3.Transform( positions[vertex], matrix ) * weight;
				var m = new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M21, matrix.M22, matrix.M23, matrix.M31, matrix.M32, matrix.M33 };
				for ( var c = 0; c < 9; c++ ) linear[c] += (double)m[c] * weight;
			}
			resultPositions[vertex] = point; resultNormals[vertex] = Normal( normals[vertex], linear );
		}
		return new FoxReferencePose { Worlds = worlds, Positions = resultPositions, Normals = resultNormals };
	}

	public FoxReferencePose ToEngine( FoxReferencePose value, NM placement ) => new()
	{
		Worlds = value.Worlds,
		Positions = value.Positions.Select( x => NV3.Transform( new NV3( x.Z / .0254f, x.X / .0254f, x.Y / .0254f ), placement ) ).ToArray(),
		Normals = value.Normals.Select( x => Normal( new NV3( x.Z, x.X, x.Y ), new double[] { placement.M11, placement.M12, placement.M13, placement.M21, placement.M22, placement.M23, placement.M31, placement.M32, placement.M33 } ) ).ToArray()
	};

	private float[] Accessor( int index )
	{
		var root = owner.RootElement; var accessor = root.GetProperty( "accessors" )[index]; Require( !accessor.TryGetProperty( "sparse", out _ ), "Fox reference source unexpectedly contains sparse data." );
		var view = root.GetProperty( "bufferViews" )[accessor.GetProperty( "bufferView" ).GetInt32()];
		Require( !view.TryGetProperty( "extensions", out _ ), "Compressed Fox reference views require a separately audited oracle." );
		var type = accessor.GetProperty( "componentType" ).GetInt32(); var components = accessor.GetProperty( "type" ).GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => throw new InvalidDataException( "Unknown Fox reference shape." ) };
		var size = type is 5120 or 5121 ? 1 : type is 5122 or 5123 ? 2 : 4;
		var count = accessor.GetProperty( "count" ).GetInt32(); var stride = view.TryGetProperty( "byteStride", out var s ) ? s.GetInt32() : size * components;
		var origin = (view.TryGetProperty( "byteOffset", out var vo ) ? vo.GetInt32() : 0) + (accessor.TryGetProperty( "byteOffset", out var ao ) ? ao.GetInt32() : 0);
		var buffer = buffers[view.GetProperty( "buffer" ).GetInt32()]; var result = new float[count * components]; var normalized = accessor.TryGetProperty( "normalized", out var n ) && n.GetBoolean();
		for ( var vertex = 0; vertex < count; vertex++ ) for ( var component = 0; component < components; component++ )
		{
			var offset = origin + vertex * stride + component * size;
			var number = type switch { 5120 => (float)(sbyte)buffer[offset], 5121 => buffer[offset], 5122 => BinaryPrimitives.ReadInt16LittleEndian( buffer.AsSpan( offset ) ), 5123 => BinaryPrimitives.ReadUInt16LittleEndian( buffer.AsSpan( offset ) ), 5125 => BinaryPrimitives.ReadUInt32LittleEndian( buffer.AsSpan( offset ) ), 5126 => BinaryPrimitives.ReadSingleLittleEndian( buffer.AsSpan( offset ) ), _ => throw new InvalidDataException( "Unknown reference component." ) };
			result[vertex * components + component] = !normalized ? number : type switch { 5120 => Math.Max( number / 127f, -1 ), 5121 => number / 255f, 5122 => Math.Max( number / 32767f, -1 ), 5123 => number / 65535f, _ => throw new InvalidDataException( "Invalid reference normalization." ) };
		}
		return result;
	}
	private static float[] Trs( JsonElement node, Dictionary<(int, string), float[]> updates, int index, string path, float[] fallback ) => updates.TryGetValue( (index, path), out var value ) ? value : node.TryGetProperty( path, out var original ) ? original.EnumerateArray().Select( x => x.GetSingle() ).ToArray() : fallback;
	private static float[] Sample( float[] times, float[] values, int components, float time, bool rotation )
	{
		var a = 0; while ( a + 1 < times.Length && times[a + 1] <= time ) a++;
		if ( time <= times[0] ) a = 0;
		if ( time <= times[0] || a == times.Length - 1 || time == times[a] )
		{
			var held = new float[components]; Array.Copy( values, a * components, held, 0, components ); return held;
		}
		var b = a + 1;
		var t = a == b ? 0d : ((double)time - times[a]) / ((double)times[b] - times[a]); var output = new float[components];
		if ( !rotation ) for ( var c = 0; c < components; c++ ) output[c] = (float)(values[a * components + c] * (1 - t) + values[b * components + c] * t);
		else
		{
			var qa = Enumerable.Range( 0, 4 ).Select( c => (double)values[a * 4 + c] ).ToArray(); var qb = Enumerable.Range( 0, 4 ).Select( c => (double)values[b * 4 + c] ).ToArray();
			var la = Math.Sqrt( qa.Sum( x => x * x ) ); var lb = Math.Sqrt( qb.Sum( x => x * x ) ); for ( var c = 0; c < 4; c++ ) { qa[c] /= la; qb[c] /= lb; }
			var dot = qa.Zip( qb, (x, y) => x * y ).Sum(); if ( dot < 0 ) { dot = -dot; for ( var c = 0; c < 4; c++ ) qb[c] = -qb[c]; }
			var sa = 1 - t; var sb = t;
			if ( dot < .9995 ) { var theta = Math.Acos( Math.Clamp( dot, -1, 1 ) ); var divisor = Math.Sin( theta ); sa = Math.Sin( (1 - t) * theta ) / divisor; sb = Math.Sin( t * theta ) / divisor; }
			var q = Enumerable.Range( 0, 4 ).Select( c => qa[c] * sa + qb[c] * sb ).ToArray(); var length = Math.Sqrt( q.Sum( x => x * x ) ); for ( var c = 0; c < 4; c++ ) output[c] = (float)(q[c] / length);
		}
		return output;
	}
	private static NM Matrix( ReadOnlySpan<float> m ) => new( m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15] );
	private static NV3 Normal( NV3 n, double[] m )
	{
		var maximum = m.Max( Math.Abs ); if ( maximum == 0 ) return new NV3( 1, 0, 0 ); var a = m.Select( x => x / maximum ).ToArray();
		var x = a[4] * a[8] - a[5] * a[7]; var y = a[5] * a[6] - a[3] * a[8]; var z = a[3] * a[7] - a[4] * a[6];
		var determinant = a[0] * x + a[1] * y + a[2] * z; var sign = determinant < 0 ? -1d : 1d;
		return Unit( new NV3( (float)((n.X * x + n.Y * (a[2] * a[7] - a[1] * a[8]) + n.Z * (a[1] * a[5] - a[2] * a[4])) * sign),
			(float)((n.X * y + n.Y * (a[0] * a[8] - a[2] * a[6]) + n.Z * (a[2] * a[3] - a[0] * a[5])) * sign),
			(float)((n.X * z + n.Y * (a[1] * a[6] - a[0] * a[7]) + n.Z * (a[0] * a[4] - a[1] * a[3])) * sign) ), new NV3( 1, 0, 0 ) );
	}
	private static NV3 Unit( NV3 value, NV3 fallback ) { var length = Math.Sqrt( (double)value.X * value.X + (double)value.Y * value.Y + (double)value.Z * value.Z ); return length == 0 ? fallback : new NV3( (float)(value.X / length), (float)(value.Y / length), (float)(value.Z / length) ); }
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidDataException( message ); }
	public void Dispose() => owner.Dispose();
}

public sealed class FoxReferencePose
{
	public NM[] Worlds { get; init; }
	public NV3[] Positions { get; init; }
	public NV3[] Normals { get; init; }
}
