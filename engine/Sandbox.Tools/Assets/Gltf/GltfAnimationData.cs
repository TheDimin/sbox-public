using System;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>
/// The keys of one animated node property as the file stores them, in glTF units. For CUBICSPLINE only the
/// values are kept; the tangents are left out.
/// </summary>
public sealed record GltfAnimationCurve( GltfElement Node, string Path, float[] Times, float[][] Values, string Interpolation );

/// <summary>The values of one animated node property at a fixed sample rate, in glTF units (meters, xyzw rotation).</summary>
public sealed record GltfAnimationChannelSamples( GltfElement Node, string Path, float[][] Values );

partial class GltfAuthoringDocument
{
	/// <summary>
	/// Sample the translation, rotation and scale channels of an animation at a fixed rate, from time 0 to the
	/// last key. STEP, LINEAR and CUBICSPLINE interpolation are evaluated like the runtime does.
	/// </summary>
	public (float Duration, IReadOnlyList<GltfAnimationChannelSamples> Channels) SampleAnimation( GltfElement animation, int sampleRate )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( sampleRate, 1 );
		var nodes = GetElements( GltfElementKind.Node );
		var samplers = animation.Json["samplers"] as JsonArray;
		var curves = new List<(GltfElement Node, string Path, float[] Times, float[] Values, int Components, string Interpolation)>();
		var duration = 0f;
		foreach ( var channel in (animation.Json["channels"] as JsonArray ?? []).OfType<JsonObject>() )
		{
			if ( channel["target"]?["node"] is not JsonValue nodeValue || !nodeValue.TryGetValue<int>( out var nodeIndex ) || nodeIndex < 0 || nodeIndex >= nodes.Count ) continue;
			var path = channel["target"]?["path"]?.GetValue<string>();
			if ( path is not ("translation" or "rotation" or "scale") ) continue;
			if ( channel["sampler"] is not JsonValue samplerValue || samplers?[samplerValue.GetValue<int>()] is not JsonObject sampler ) continue;
			var times = GltfAccessors.Read( root, ReadBufferView, sampler["input"].GetValue<int>(), out _ );
			var values = GltfAccessors.Read( root, ReadBufferView, sampler["output"].GetValue<int>(), out var components );
			if ( times is not { Length: > 0 } || values is null || components != (path == "rotation" ? 4 : 3) ) continue;
			duration = MathF.Max( duration, times[^1] );
			curves.Add( (nodes[nodeIndex], path, times, values, components, sampler["interpolation"]?.GetValue<string>() ?? "LINEAR") );
		}

		var count = SampleCount( duration, sampleRate );
		var channels = curves.Select( curve => new GltfAnimationChannelSamples( curve.Node, curve.Path,
			Enumerable.Range( 0, count ).Select( i => Evaluate( curve.Times, curve.Values, curve.Components, curve.Interpolation, MathF.Min( (float)i / sampleRate, duration ), curve.Path == "rotation" ) ).ToArray() ) ).ToList();
		return (duration, channels);
	}

	/// <summary>The keys of the translation, rotation and scale channels of an animation.</summary>
	public IReadOnlyList<GltfAnimationCurve> GetAnimationCurves( GltfElement animation )
	{
		var nodes = GetElements( GltfElementKind.Node );
		var samplers = animation.Json["samplers"] as JsonArray;
		var curves = new List<GltfAnimationCurve>();
		foreach ( var channel in (animation.Json["channels"] as JsonArray ?? []).OfType<JsonObject>() )
		{
			if ( channel["target"]?["node"] is not JsonValue nodeValue || !nodeValue.TryGetValue<int>( out var nodeIndex ) || nodeIndex < 0 || nodeIndex >= nodes.Count ) continue;
			var path = channel["target"]?["path"]?.GetValue<string>();
			if ( path is not ("translation" or "rotation" or "scale") ) continue;
			if ( channel["sampler"] is not JsonValue samplerValue || samplers?[samplerValue.GetValue<int>()] is not JsonObject sampler ) continue;
			var times = GltfAccessors.Read( root, ReadBufferView, sampler["input"].GetValue<int>(), out _ );
			var values = GltfAccessors.Read( root, ReadBufferView, sampler["output"].GetValue<int>(), out var components );
			if ( times is not { Length: > 0 } || values is null || components != (path == "rotation" ? 4 : 3) ) continue;
			var interpolation = sampler["interpolation"]?.GetValue<string>() ?? "LINEAR";
			var cubic = interpolation == "CUBICSPLINE";
			var stride = cubic ? components * 3 : components;
			var keys = Math.Min( times.Length, values.Length / stride );
			var keyValues = Enumerable.Range( 0, keys ).Select( k => values.AsSpan( k * stride + (cubic ? components : 0), components ).ToArray() ).ToArray();
			curves.Add( new GltfAnimationCurve( nodes[nodeIndex], path, times[..keys], keyValues, interpolation ) );
		}
		return curves;
	}

	/// <summary>The node properties (translation, rotation, scale) that an animation has channels for.</summary>
	public IEnumerable<(GltfElement Node, string Path)> GetAnimatedProperties( GltfElement animation )
	{
		var nodes = GetElements( GltfElementKind.Node );
		foreach ( var channel in (animation.Json["channels"] as JsonArray ?? []).OfType<JsonObject>() )
		{
			if ( channel["target"]?["node"] is not JsonValue nodeValue || !nodeValue.TryGetValue<int>( out var index ) || index < 0 || index >= nodes.Count ) continue;
			var path = channel["target"]?["path"]?.GetValue<string>();
			if ( path is "translation" or "rotation" or "scale" ) yield return (nodes[index], path);
		}
	}

	/// <summary>Samples from time 0 to <paramref name="duration"/>, both included.</summary>
	public static int SampleCount( float duration, int sampleRate ) => Math.Max( 1, (int)MathF.Ceiling( duration * sampleRate - 1e-3f ) + 1 );

	static float[] Evaluate( float[] times, float[] values, int components, string interpolation, float time, bool rotation )
	{
		var cubic = interpolation == "CUBICSPLINE";
		var stride = cubic ? components * 3 : components;
		float[] Read( int key, int part ) => values.AsSpan( key * stride + part * components, components ).ToArray();
		var valuePart = cubic ? 1 : 0;
		var last = times.Length - 1;
		if ( time <= times[0] || last == 0 ) return Read( 0, valuePart );
		if ( time >= times[last] ) return Read( last, valuePart );

		var k = Array.BinarySearch( times, time );
		if ( k >= 0 ) return Read( k, valuePart );
		k = ~k - 1;
		var dt = times[k + 1] - times[k];
		var u = dt > 0 ? (time - times[k]) / dt : 0;
		if ( interpolation == "STEP" ) return Read( k, valuePart );

		float[] result;
		if ( cubic )
		{
			var p0 = Read( k, 1 ); var m0 = Read( k, 2 ); var p1 = Read( k + 1, 1 ); var m1 = Read( k + 1, 0 );
			var u2 = u * u; var u3 = u2 * u;
			result = new float[components];
			for ( var c = 0; c < components; c++ )
				result[c] = (2 * u3 - 3 * u2 + 1) * p0[c] + (u3 - 2 * u2 + u) * dt * m0[c] + (-2 * u3 + 3 * u2) * p1[c] + (u3 - u2) * dt * m1[c];
		}
		else if ( rotation )
		{
			var a = Read( k, 0 ); var b = Read( k + 1, 0 );
			var q = Quaternion.Slerp( new Quaternion( a[0], a[1], a[2], a[3] ), new Quaternion( b[0], b[1], b[2], b[3] ), u );
			return [q.X, q.Y, q.Z, q.W];
		}
		else
		{
			var a = Read( k, 0 ); var b = Read( k + 1, 0 );
			result = new float[components];
			for ( var c = 0; c < components; c++ ) result[c] = a[c] + (b[c] - a[c]) * u;
		}

		if ( rotation )
		{
			var q = Quaternion.Normalize( new Quaternion( result[0], result[1], result[2], result[3] ) );
			return [q.X, q.Y, q.Z, q.W];
		}
		return result;
	}

	/// <summary>
	/// Write sampled values into an animation as LINEAR samplers. Each written node property gets its own
	/// sampler; channels that target other properties (morph weights) stay. The data goes into one embedded buffer
	/// per animation, which later writes replace, so repeated edits do not grow the file.
	/// The accessors that the old samplers used stay in the file.
	/// </summary>
	public void WriteAnimationSamples( GltfElement animation, int sampleRate, IReadOnlyList<GltfAnimationChannelSamples> channels )
	{
		if ( channels.Count == 0 ) return;
		var count = channels[0].Values.Length;
		if ( count == 0 || channels.Any( x => x.Values.Length != count ) ) throw new ArgumentException( "Every channel needs the same number of samples.", nameof( channels ) );

		var tag = $"gltf-editor animation {animation.Index}";
		var data = new List<float>();
		var times = Enumerable.Range( 0, count ).Select( i => (float)i / sampleRate ).ToArray();
		data.AddRange( times );
		var offsets = new List<int>();
		foreach ( var channel in channels )
		{
			offsets.Add( data.Count * 4 );
			foreach ( var value in channel.Values ) data.AddRange( value );
		}
		var bytes = new byte[data.Count * 4];
		Buffer.BlockCopy( data.ToArray(), 0, bytes, 0, bytes.Length );

		var buffers = GetOrAddCollection( "buffers" );
		var bufferIndex = FindNamed( buffers, tag );
		var buffer = new JsonObject { ["name"] = tag, ["byteLength"] = bytes.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes ) };
		if ( bufferIndex < 0 ) { buffers.Add( buffer ); bufferIndex = buffers.Count - 1; }
		else buffers[bufferIndex] = buffer;

		int Accessor( string name, int offset, int length, int elements, string type, JsonArray min = null, JsonArray max = null )
		{
			var views = GetOrAddCollection( "bufferViews" );
			var view = new JsonObject { ["name"] = name, ["buffer"] = bufferIndex, ["byteOffset"] = offset, ["byteLength"] = length };
			var viewIndex = FindNamed( views, name );
			if ( viewIndex < 0 ) { views.Add( view ); viewIndex = views.Count - 1; }
			else views[viewIndex] = view;

			var accessors = GetOrAddCollection( "accessors" );
			var accessor = new JsonObject { ["name"] = name, ["bufferView"] = viewIndex, ["componentType"] = 5126, ["count"] = elements, ["type"] = type };
			if ( min is not null ) { accessor["min"] = min; accessor["max"] = max; }
			var accessorIndex = FindNamed( accessors, name );
			if ( accessorIndex < 0 ) { accessors.Add( accessor ); accessorIndex = accessors.Count - 1; }
			else accessors[accessorIndex] = accessor;
			return accessorIndex;
		}

		var input = Accessor( $"{tag} time", 0, count * 4, count, "SCALAR", new JsonArray( 0f ), new JsonArray( times[^1] ) );
		var json = animation.Json;
		if ( json["channels"] is not JsonArray channelArray ) json["channels"] = channelArray = new JsonArray();
		if ( json["samplers"] is not JsonArray samplerArray ) json["samplers"] = samplerArray = new JsonArray();
		for ( var c = 0; c < channels.Count; c++ )
		{
			var channel = channels[c];
			var output = Accessor( $"{tag} {channel.Node.Index} {channel.Path}", offsets[c], count * channel.Values[0].Length * 4, count, channel.Path == "rotation" ? "VEC4" : "VEC3" );
			var sampler = new JsonObject { ["input"] = input, ["output"] = output, ["interpolation"] = "LINEAR" };
			var existing = channelArray.OfType<JsonObject>().FirstOrDefault( x =>
				x["target"]?["node"] is JsonValue n && n.TryGetValue<int>( out var node ) && node == channel.Node.Index && x["target"]?["path"]?.GetValue<string>() == channel.Path );
			var samplerIndex = existing?["sampler"] is JsonValue s && s.TryGetValue<int>( out var si ) ? si : -1;
			var shared = samplerIndex >= 0 && channelArray.OfType<JsonObject>().Count( x => x["sampler"] is JsonValue o && o.TryGetValue<int>( out var oi ) && oi == samplerIndex ) > 1;
			if ( samplerIndex >= 0 && samplerIndex < samplerArray.Count && !shared ) samplerArray[samplerIndex] = sampler;
			else
			{
				samplerArray.Add( sampler );
				samplerIndex = samplerArray.Count - 1;
				if ( existing is not null ) existing["sampler"] = samplerIndex;
				else channelArray.Add( new JsonObject { ["sampler"] = samplerIndex, ["target"] = new JsonObject { ["node"] = channel.Node.Index, ["path"] = channel.Path } } );
			}
		}
	}

	JsonArray GetOrAddCollection( string name )
	{
		if ( root[name] is not JsonArray collection ) root[name] = collection = new JsonArray();
		return collection;
	}

	static int FindNamed( JsonArray collection, string name )
	{
		for ( var i = 0; i < collection.Count; i++ )
			if ( collection[i]?["name"] is JsonValue value && value.TryGetValue<string>( out var existing ) && existing == name ) return i;
		return -1;
	}
}
