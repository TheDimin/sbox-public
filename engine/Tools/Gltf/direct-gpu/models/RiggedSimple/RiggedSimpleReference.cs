using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.Tests;

using Vector3 = global::System.Numerics.Vector3;
using Quaternion = global::System.Numerics.Quaternion;
using Matrix4x4 = global::System.Numerics.Matrix4x4;

/// <summary>
/// Independent, deliberately sample-scoped reference. No Sandbox.Gltf decoder,
/// pose evaluator, geometry converter or backend helper is used. An unexpected
/// source feature fails closed instead of silently inheriting a production path.
/// </summary>
public sealed partial class RiggedSimpleReference : IDisposable
{
	private readonly JsonDocument json;
	private readonly byte[][] buffers;
	private readonly int[] parents;
	private readonly Vector3[] sourcePositions;
	private readonly Vector3[] sourceNormals;
	private readonly float[][] jointSets;
	private readonly float[][] weightSets;
	private readonly Matrix4x4[] inverseBinds;
	public int NodeCount => parents.Length;
	public int VertexCount => sourcePositions.Length;
	public int MeshNode { get; }
	public int MeshIndex { get; }
	public int[] Joints { get; }
	public uint[] Indices { get; }
	public float[] KeyTimes { get; }
	public int InfluenceSetCount => jointSets.Length;

	private RiggedSimpleReference( JsonDocument json, byte[][] buffers )
	{
		this.json = json; this.buffers = buffers;
		var root = json.RootElement;
		var nodes = root.GetProperty( "nodes" );
		parents = Enumerable.Repeat( -1, nodes.GetArrayLength() ).ToArray();
		for ( var node = 0; node < nodes.GetArrayLength(); node++ )
		{
			if ( nodes[node].TryGetProperty( "mesh", out var mesh ) )
			{
				if ( !nodes[node].TryGetProperty( "skin", out _ ) ) throw new InvalidDataException( "Expected a skinned source mesh." );
				MeshNode = node; MeshIndex = mesh.GetInt32();
			}
			if ( nodes[node].TryGetProperty( "children", out var children ) )
				foreach ( var child in children.EnumerateArray() )
				{
					if ( parents[child.GetInt32()] != -1 ) throw new InvalidDataException( "Reference does not accept multiple parents." );
					parents[child.GetInt32()] = node;
				}
		}
		var primitive = root.GetProperty( "meshes" )[MeshIndex].GetProperty( "primitives" )[0];
		if ( primitive.GetProperty( "mode" ).GetInt32() != 4 || primitive.TryGetProperty( "targets", out _ ) )
			throw new InvalidDataException( "This sample reference expects unmorphed triangles." );
		var attributes = primitive.GetProperty( "attributes" );
		sourcePositions = Vectors( Accessor( attributes.GetProperty( "POSITION" ).GetInt32() ), 3 );
		sourceNormals = Vectors( Accessor( attributes.GetProperty( "NORMAL" ).GetInt32() ), 3 );
		var joints = new List<float[]>(); var weights = new List<float[]>();
		for ( var set = 0; attributes.TryGetProperty( "JOINTS_" + set, out var joint ); set++ )
		{
			joints.Add( Accessor( joint.GetInt32() ) );
			weights.Add( Accessor( attributes.GetProperty( "WEIGHTS_" + set ).GetInt32() ) );
		}
		jointSets = joints.ToArray(); weightSets = weights.ToArray();
		Indices = Accessor( primitive.GetProperty( "indices" ).GetInt32() ).Select( value => checked((uint)value) ).ToArray();
		var skin = root.GetProperty( "skins" )[nodes[MeshNode].GetProperty( "skin" ).GetInt32()];
		Joints = skin.GetProperty( "joints" ).EnumerateArray().Select( value => value.GetInt32() ).ToArray();
		var binds = Accessor( skin.GetProperty( "inverseBindMatrices" ).GetInt32() );
		inverseBinds = Enumerable.Range( 0, Joints.Length ).Select( joint => Matrix( binds, joint * 16 ) ).ToArray();
		var animation = root.GetProperty( "animations" )[0];
		KeyTimes = Accessor( animation.GetProperty( "samplers" )[0].GetProperty( "input" ).GetInt32() );
	}

	public static RiggedSimpleReference Read( string path )
	{
		var json = JsonDocument.Parse( File.ReadAllBytes( path ) );
		try
		{
			var bufferJson = json.RootElement.GetProperty( "buffers" );
			var buffers = new byte[bufferJson.GetArrayLength()][];
			for ( var i = 0; i < buffers.Length; i++ )
			{
				var uri = bufferJson[i].GetProperty( "uri" ).GetString();
				buffers[i] = uri.StartsWith( "data:", StringComparison.Ordinal )
					? Convert.FromBase64String( uri[(uri.IndexOf( ',' ) + 1)..] )
					: File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( path ), Uri.UnescapeDataString( uri ) ) );
				if ( buffers[i].Length < bufferJson[i].GetProperty( "byteLength" ).GetInt32() ) throw new InvalidDataException( "Truncated reference buffer." );
			}
			return new RiggedSimpleReference( json, buffers );
		}
		catch { json.Dispose(); throw; }
	}

	public RiggedSimpleReferencePose Evaluate( float? time, bool reversePaletteOrder = false )
	{
		var root = json.RootElement;
		var nodes = root.GetProperty( "nodes" );
		var translation = new Vector3[nodes.GetArrayLength()];
		var rotation = new Quaternion[nodes.GetArrayLength()];
		var scale = new Vector3[nodes.GetArrayLength()];
		var local = new Matrix4x4[nodes.GetArrayLength()];
		for ( var node = 0; node < nodes.GetArrayLength(); node++ )
		{
			translation[node] = ReadVector( nodes[node], "translation", Vector3.Zero );
			rotation[node] = nodes[node].TryGetProperty( "rotation", out var q )
				? Quaternion.Normalize( new Quaternion( q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle(), q[3].GetSingle() ) ) : Quaternion.Identity;
			scale[node] = ReadVector( nodes[node], "scale", Vector3.One );
		}
		if ( time.HasValue )
		{
			var animation = root.GetProperty( "animations" )[0];
			foreach ( var channel in animation.GetProperty( "channels" ).EnumerateArray() )
			{
				var sampler = animation.GetProperty( "samplers" )[channel.GetProperty( "sampler" ).GetInt32()];
				if ( sampler.TryGetProperty( "interpolation", out var interpolation ) && interpolation.GetString() != "LINEAR" )
					throw new InvalidDataException( "This reference expects LINEAR source channels." );
				var target = channel.GetProperty( "target" ); var node = target.GetProperty( "node" ).GetInt32();
				var times = Accessor( sampler.GetProperty( "input" ).GetInt32() );
				var values = Accessor( sampler.GetProperty( "output" ).GetInt32() );
				var next = 0;
				while ( next < times.Length - 1 && times[next] < time.Value ) next++;
				var previous = next > 0 && time.Value < times[next] ? next - 1 : next;
				var fraction = previous == next ? 0f : (float)(((double)time.Value - times[previous]) / ((double)times[next] - times[previous]));
				// Clamp before/after the authored channel range; do not extrapolate.
				fraction = Math.Clamp( fraction, 0, 1 );
				switch ( target.GetProperty( "path" ).GetString() )
				{
					case "translation": translation[node] = Vector3.Lerp( VectorAt( values, previous ), VectorAt( values, next ), fraction ); break;
					case "scale": scale[node] = Vector3.Lerp( VectorAt( values, previous ), VectorAt( values, next ), fraction ); break;
					case "rotation": rotation[node] = Quaternion.Normalize( Quaternion.Slerp( QuaternionAt( values, previous ), QuaternionAt( values, next ), fraction ) ); break;
					default: throw new InvalidDataException( "Unexpected animation path in sample-scoped reference." );
				}
			}
		}
		for ( var node = 0; node < nodes.GetArrayLength(); node++ )
			local[node] = nodes[node].TryGetProperty( "matrix", out var matrix )
				? Matrix( matrix.EnumerateArray().Select( value => value.GetSingle() ).ToArray(), 0 )
				: Matrix4x4.CreateScale( scale[node] ) * Matrix4x4.CreateFromQuaternion( rotation[node] ) * Matrix4x4.CreateTranslation( translation[node] );
		var worlds = new Matrix4x4[local.Length];
		for ( var node = 0; node < local.Length; node++ )
		{
			var result = local[node]; var iterations = 0;
			for ( var parent = parents[node]; parent >= 0; parent = parents[parent] )
			{
				if ( ++iterations > local.Length ) throw new InvalidDataException( "Cyclic reference hierarchy." );
				result *= local[parent];
			}
			worlds[node] = result;
		}
		var palette = new Matrix4x4[Joints.Length];
		for ( var joint = 0; joint < palette.Length; joint++ )
			palette[joint] = reversePaletteOrder ? worlds[Joints[joint]] * inverseBinds[joint] : inverseBinds[joint] * worlds[Joints[joint]];
		var positions = new Vector3[VertexCount]; var normals = new Vector3[VertexCount];
		for ( var vertex = 0; vertex < VertexCount; vertex++ )
		{
			var blended = new Matrix4x4();
			var position = Vector3.Zero;
			for ( var set = 0; set < jointSets.Length; set++ )
			for ( var component = 0; component < 4; component++ )
			{
				var weight = weightSets[set][vertex * 4 + component];
				if ( weight == 0 ) continue;
				var joint = checked((int)jointSets[set][vertex * 4 + component]);
				position += Vector3.Transform( sourcePositions[vertex], palette[joint] ) * weight;
				blended += palette[joint] * weight;
			}
			if ( !Matrix4x4.Invert( blended, out var inverse ) ) throw new InvalidDataException( "Singular blended normal transform in model reference." );
			positions[vertex] = position;
			normals[vertex] = Vector3.Normalize( Vector3.TransformNormal( sourceNormals[vertex], Matrix4x4.Transpose( inverse ) ) );
		}
		return new RiggedSimpleReferencePose( worlds, positions, normals );
	}

	/// <summary>Apply the literal coordinate permutation/units and complete object placement once.</summary>
	public RiggedSimpleReferencePose ToEngine( RiggedSimpleReferencePose pose, Matrix4x4 placement )
	{
		if ( !Matrix4x4.Invert( placement, out var inverse ) ) throw new InvalidDataException( "Singular object placement in reference." );
		var normalMatrix = Matrix4x4.Transpose( inverse );
		var positions = pose.Positions.Select( p => Vector3.Transform( new Vector3( p.Z, p.X, p.Y ) / 0.0254f, placement ) ).ToArray();
		var normals = pose.Normals.Select( n => Vector3.Normalize( Vector3.TransformNormal( new Vector3( n.Z, n.X, n.Y ), normalMatrix ) ) ).ToArray();
		return new RiggedSimpleReferencePose( pose.Worlds, positions, normals );
	}

	private float[] Accessor( int index )
	{
		var root = json.RootElement; var accessor = root.GetProperty( "accessors" )[index];
		if ( accessor.TryGetProperty( "sparse", out _ ) ) throw new InvalidDataException( "Unexpected sparse source in sample-scoped oracle." );
		var view = root.GetProperty( "bufferViews" )[accessor.GetProperty( "bufferView" ).GetInt32()];
		var bytes = buffers[view.GetProperty( "buffer" ).GetInt32()];
		var type = accessor.GetProperty( "componentType" ).GetInt32();
		var width = type switch { 5121 => 1, 5123 => 2, 5125 or 5126 => 4, _ => throw new InvalidDataException( "Unexpected reference component type." ) };
		var components = accessor.GetProperty( "type" ).GetString() switch { "SCALAR" => 1, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => throw new InvalidDataException( "Unexpected reference accessor shape." ) };
		var offset = (view.TryGetProperty( "byteOffset", out var vo ) ? vo.GetInt32() : 0) + (accessor.TryGetProperty( "byteOffset", out var ao ) ? ao.GetInt32() : 0);
		var stride = view.TryGetProperty( "byteStride", out var s ) ? s.GetInt32() : width * components;
		var count = accessor.GetProperty( "count" ).GetInt32();
		var normalized = accessor.TryGetProperty( "normalized", out var n ) && n.GetBoolean();
		var output = new float[count * components];
		for ( var element = 0; element < count; element++ )
		for ( var component = 0; component < components; component++ )
		{
			var at = offset + element * stride + component * width;
			var source = bytes.AsSpan( at, width );
			output[element * components + component] = type switch
			{
				5121 => normalized ? source[0] / 255f : source[0],
				5123 => normalized ? BinaryPrimitives.ReadUInt16LittleEndian( source ) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian( source ),
				5125 when !normalized => BinaryPrimitives.ReadUInt32LittleEndian( source ),
				5126 when !normalized => BinaryPrimitives.ReadSingleLittleEndian( source ),
				_ => throw new InvalidDataException( "Unexpected reference normalization." )
			};
		}
		return output;
	}

	private static Vector3[] Vectors( float[] values, int components ) => Enumerable.Range( 0, values.Length / components ).Select( i => new Vector3( values[i * components], values[i * components + 1], values[i * components + 2] ) ).ToArray();
	private static Vector3 VectorAt( float[] values, int index ) => new( values[index * 3], values[index * 3 + 1], values[index * 3 + 2] );
	private static Quaternion QuaternionAt( float[] values, int index ) => Quaternion.Normalize( new Quaternion( values[index * 4], values[index * 4 + 1], values[index * 4 + 2], values[index * 4 + 3] ) );
	private static Vector3 ReadVector( JsonElement node, string name, Vector3 fallback ) => node.TryGetProperty( name, out var value ) ? new( value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle() ) : fallback;
	private static Matrix4x4 Matrix( float[] a, int at ) => new( a[at], a[at + 1], a[at + 2], a[at + 3], a[at + 4], a[at + 5], a[at + 6], a[at + 7], a[at + 8], a[at + 9], a[at + 10], a[at + 11], a[at + 12], a[at + 13], a[at + 14], a[at + 15] );
	public void Dispose() => json.Dispose();
}

public sealed class RiggedSimpleReferencePose
{
	public Matrix4x4[] Worlds { get; }
	public Vector3[] Positions { get; }
	public Vector3[] Normals { get; }
	public RiggedSimpleReferencePose( Matrix4x4[] worlds, Vector3[] positions, Vector3[] normals ) => (Worlds, Positions, Normals) = (worlds, positions, normals);
}
