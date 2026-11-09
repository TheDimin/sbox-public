using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.InterpolationTestValidation;

/// <summary>
/// Independent reference for this pinned sample. Does not call the reader,
/// sampler, pose evaluator, skinning helpers or renderer under test.
/// Cubic channels use Bezier control points and de Casteljau subdivision.
/// </summary>
public sealed class InterpolationReference : IDisposable
{
	private readonly JsonDocument json;
	private readonly byte[][] buffers;
	public JsonElement Root => json.RootElement;
	public int NodeCount => Root.GetProperty( "nodes" ).GetArrayLength();
	public int ClipCount => Root.GetProperty( "animations" ).GetArrayLength();
	public const string CorpusCommit = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public const string Specification = "https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#appendix-c-animation-sampler-interpolation-modes";

	public InterpolationReference( byte[] source, Func<string, byte[]> resolve )
	{
		// This oracle deliberately consumes the independently hashed text source;
		// binary/compiled producer results are compared to that same source.
		json = JsonDocument.Parse( source );
		buffers = Root.GetProperty( "buffers" ).EnumerateArray().Select( value =>
		{
			var bytes = resolve( value.GetProperty( "uri" ).GetString()! );
			if ( bytes.Length < value.GetProperty( "byteLength" ).GetInt32() ) throw new InvalidDataException( "Short oracle buffer." );
			return bytes;
		} ).ToArray();
		if ( ClipCount != 9 || NodeCount != 10 || Root.TryGetProperty( "skins", out _ ) ) throw new InvalidDataException( "Pinned sample shape changed." );
	}

	public double[][] FloatAccessor( int index )
	{
		var a = Root.GetProperty( "accessors" )[index];
		if ( a.GetProperty( "componentType" ).GetInt32() != 5126 || a.TryGetProperty( "sparse", out _ ) || a.TryGetProperty( "normalized", out var normalized ) && normalized.GetBoolean() )
			throw new InvalidDataException( "This sample reference expects a nonsparse FLOAT accessor; no general-format claim." );
		var components = a.GetProperty( "type" ).GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, _ => throw new InvalidDataException( "Unexpected oracle accessor type." ) };
		var view = Root.GetProperty( "bufferViews" )[a.GetProperty( "bufferView" ).GetInt32()];
		var bytes = buffers[view.GetProperty( "buffer" ).GetInt32()];
		var viewOffset = Integer( view, "byteOffset" );
		var offset = Integer( a, "byteOffset" );
		var stride = view.TryGetProperty( "byteStride", out var authoredStride ) ? authoredStride.GetInt32() : components * 4;
		var count = a.GetProperty( "count" ).GetInt32();
		if ( count < 1 || count > 4096 || stride < components * 4 || offset < 0 || (long)offset + (count - 1L) * stride + components * 4 > view.GetProperty( "byteLength" ).GetInt32() )
			throw new InvalidDataException( "Oracle accessor exceeds its bufferView." );
		var result = new double[count][];
		for ( var i = 0; i < count; i++ )
		{
			result[i] = new double[components];
			for ( var c = 0; c < components; c++ ) result[i][c] = BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( checked(viewOffset + offset + i * stride + c * 4), 4 ) );
		}
		return result;
	}

	public (int Node, string Path, string Mode, double[] Times, double[][] Values) Channel( int clip )
	{
		var animation = Root.GetProperty( "animations" )[clip];
		if ( animation.GetProperty( "channels" ).GetArrayLength() != 1 ) throw new InvalidDataException( "Focused sample reference requires one channel per clip." );
		var channel = animation.GetProperty( "channels" )[0];
		var sampler = animation.GetProperty( "samplers" )[channel.GetProperty( "sampler" ).GetInt32()];
		var target = channel.GetProperty( "target" );
		return (target.GetProperty( "node" ).GetInt32(), target.GetProperty( "path" ).GetString()!, sampler.TryGetProperty( "interpolation", out var mode ) ? mode.GetString()! : "LINEAR",
			FloatAccessor( sampler.GetProperty( "input" ).GetInt32() ).Select( value => value[0] ).ToArray(), FloatAccessor( sampler.GetProperty( "output" ).GetInt32() ));
	}

	public static double[] Sample( double[] times, double[][] values, string mode, double time, bool rotation )
	{
		var multiplier = mode == "CUBICSPLINE" ? 3 : 1;
		var valueOffset = multiplier == 3 ? 1 : 0;
		if ( times.Length == 0 || values.Length != times.Length * multiplier ) throw new InvalidDataException( "Invalid independent key data." );
		int left = 0;
		while ( left + 1 < times.Length && time >= times[left + 1] ) left++;
		if ( time <= times[0] ) return (double[])values[valueOffset].Clone();
		if ( left == times.Length - 1 || time == times[left] || mode == "STEP" ) return (double[])values[left * multiplier + valueOffset].Clone();
		var seconds = times[left + 1] - times[left];
		var fraction = (time - times[left]) / seconds;
		var start = values[left * multiplier + valueOffset];
		var end = values[(left + 1) * multiplier + valueOffset];
		if ( mode == "LINEAR" && rotation )
		{
			var a = Unit( start ); var b = Unit( end );
			var cosine = Dot( a, b );
			if ( cosine < 0 ) { b = b.Select( value => -value ).ToArray(); cosine = -cosine; }
			cosine = Math.Clamp( cosine, -1, 1 );
			var angle = Math.Acos( cosine );
			if ( angle < 1e-7 ) return Unit( Mix( a, b, fraction ) );
			return Unit( a.Zip( b, (x, y) => (x * Math.Sin( (1 - fraction) * angle ) + y * Math.Sin( fraction * angle )) / Math.Sin( angle ) ).ToArray() );
		}
		if ( mode == "LINEAR" ) return Mix( start, end, fraction );
		if ( mode != "CUBICSPLINE" ) throw new InvalidDataException( "Unexpected interpolation." );
		// Hermite -> Bezier control points, then three linear subdivision levels.
		// Tangents are derivatives per second, therefore duration belongs here.
		var control1 = start.Zip( values[left * 3 + 2], (value, tangent) => value + tangent * seconds / 3 ).ToArray();
		var control2 = end.Zip( values[(left + 1) * 3], (value, tangent) => value - tangent * seconds / 3 ).ToArray();
		var a0 = Mix( start, control1, fraction ); var a1 = Mix( control1, control2, fraction ); var a2 = Mix( control2, end, fraction );
		var cubic = Mix( Mix( a0, a1, fraction ), Mix( a1, a2, fraction ), fraction );
		return rotation ? Unit( cubic ) : cubic;
	}

	public double[] World( int node, int? clip = null, double time = 0 )
	{
		var local = Local( node, clip, time );
		for ( var parent = Parent( node ); parent >= 0; parent = Parent( parent ) ) local = Multiply( local, Local( parent, clip, time ) );
		return local;
	}

	private double[] Local( int node, int? clip, double time )
	{
		var value = Root.GetProperty( "nodes" )[node];
		if ( value.TryGetProperty( "matrix", out var authored ) ) return authored.EnumerateArray().Select( item => item.GetDouble() ).ToArray();
		var translation = Property( value, "translation", [0, 0, 0] );
		var scale = Property( value, "scale", [1, 1, 1] );
		var rotation = Property( value, "rotation", [0, 0, 0, 1] );
		if ( clip.HasValue )
		{
			var channel = Channel( clip.Value );
			if ( channel.Node == node )
			{
				var sampled = Sample( channel.Times, channel.Values, channel.Mode, time, channel.Path == "rotation" );
				switch ( channel.Path ) { case "translation": translation = sampled; break; case "scale": scale = sampled; break; case "rotation": rotation = sampled; break; default: throw new InvalidDataException( "Unexpected channel path." ); }
			}
		}
		var q = Unit( rotation ); var x = q[0]; var y = q[1]; var z = q[2]; var w = q[3];
		// Row-vector storage: equivalent to glTF column-vector T*R*S without
		// invoking Matrix4x4.CreateFromQuaternion or production TRS helpers.
		return [
			scale[0] * (1 - 2 * (y*y + z*z)), scale[0] * 2 * (x*y + z*w), scale[0] * 2 * (x*z - y*w), 0,
			scale[1] * 2 * (x*y - z*w), scale[1] * (1 - 2 * (x*x + z*z)), scale[1] * 2 * (y*z + x*w), 0,
			scale[2] * 2 * (x*z + y*w), scale[2] * 2 * (y*z - x*w), scale[2] * (1 - 2 * (x*x + y*y)), 0,
			translation[0], translation[1], translation[2], 1 ];
	}

	public double[] Position( int node, int primitive, int vertex, int? clip = null, double time = 0 )
	{
		var mesh = Root.GetProperty( "nodes" )[node].GetProperty( "mesh" ).GetInt32();
		var accessor = Root.GetProperty( "meshes" )[mesh].GetProperty( "primitives" )[primitive].GetProperty( "attributes" ).GetProperty( "POSITION" ).GetInt32();
		return Transform( FloatAccessor( accessor )[vertex], World( node, clip, time ) );
	}

	public static double[] Transform( double[] point, double[] matrix ) => [
		point[0]*matrix[0] + point[1]*matrix[4] + point[2]*matrix[8] + matrix[12],
		point[0]*matrix[1] + point[1]*matrix[5] + point[2]*matrix[9] + matrix[13],
		point[0]*matrix[2] + point[1]*matrix[6] + point[2]*matrix[10] + matrix[14] ];
	public static double[] EnginePosition( double[] source ) => [source[2] / .0254, source[0] / .0254, source[1] / .0254];
	public static double[] Multiply( double[] a, double[] b )
	{
		var result = new double[16];
		for ( var row = 0; row < 4; row++ ) for ( var column = 0; column < 4; column++ ) for ( var inner = 0; inner < 4; inner++ ) result[row*4+column] += a[row*4+inner] * b[inner*4+column];
		return result;
	}
	public IEnumerable<float> SampleTimes( int clip )
	{
		var times = Channel( clip ).Times.Select( value => (float)value ).ToArray();
		var samples = new SortedSet<float> { -0.25f, times[^1] + .25f };
		foreach ( var key in times ) { samples.Add( key ); samples.Add( MathF.BitDecrement( key ) ); samples.Add( MathF.BitIncrement( key ) ); }
		for ( var key = 0; key + 1 < times.Length; key++ ) foreach ( var fraction in new[] { .125f, .25f, .5f, .75f, .875f } ) samples.Add( times[key] + (times[key + 1] - times[key]) * fraction );
		return samples;
	}
	private int Parent( int node )
	{
		for ( var i = 0; i < NodeCount; i++ ) if ( Root.GetProperty( "nodes" )[i].TryGetProperty( "children", out var children ) && children.EnumerateArray().Any( child => child.GetInt32() == node ) ) return i;
		return -1;
	}
	private static double[] Property( JsonElement value, string name, double[] fallback ) => value.TryGetProperty( name, out var array ) ? array.EnumerateArray().Select( item => item.GetDouble() ).ToArray() : fallback;
	private static int Integer( JsonElement value, string name ) => value.TryGetProperty( name, out var number ) ? number.GetInt32() : 0;
	private static double Dot( double[] a, double[] b ) => a.Zip( b, (x, y) => x * y ).Sum();
	private static double[] Unit( double[] value ) { var length = Math.Sqrt( Dot( value, value ) ); if ( !double.IsFinite( length ) || length == 0 ) throw new InvalidDataException( "Invalid reference quaternion." ); return value.Select( component => component / length ).ToArray(); }
	private static double[] Mix( double[] a, double[] b, double fraction ) => a.Zip( b, (x, y) => x + (y - x) * fraction ).ToArray();
	public void Dispose() => json.Dispose();
}
