using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.Tests;

/// <summary>
/// A model-derived metamorphic layout fixture. It preserves every geometry value
/// and animation byte, but places the four vertex attributes in one padded,
/// nonzero-origin interleaved view. This intentional fixture layout is never a
/// runtime vertex contract and is not called an upstream Khronos variant.
/// </summary>
public static class RiggedSimpleLayoutFixture
{
	public static byte[] Create( string originalGltfPath )
	{
		var root = JsonNode.Parse( File.ReadAllBytes( originalGltfPath ) ).AsObject();
		var buffers = root["buffers"].AsArray();
		if ( buffers.Count != 1 ) throw new InvalidDataException( "Expected this model's one original buffer." );
		var original = File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( originalGltfPath ), buffers[0]["uri"].GetValue<string>() ) );
		if ( original.Length != buffers[0]["byteLength"].GetValue<int>() ) throw new InvalidDataException( "Unexpected original buffer size." );
		var primitive = root["meshes"][0]["primitives"][0];
		var attributes = primitive["attributes"].AsObject();
		var offsets = new Dictionary<string, int>( StringComparer.Ordinal )
		{
			["POSITION"] = 0, ["NORMAL"] = 16, ["WEIGHTS_0"] = 28, ["JOINTS_0"] = 48
		};
		if ( attributes.Count != offsets.Count ) throw new InvalidDataException( "Unexpected source attributes in this fixture profile." );
		var accessors = root["accessors"].AsArray(); var views = root["bufferViews"].AsArray();
		var count = accessors[attributes["POSITION"].GetValue<int>()]["count"].GetValue<int>();
		const int stride = 64;
		var origin = checked(original.Length + 20);
		var bytes = new byte[checked(origin + count * stride)];
		Array.Fill( bytes, (byte)0xcd ); original.CopyTo( bytes, 0 );
		var viewIndex = views.Count;
		foreach ( var attribute in attributes )
		{
			if ( !offsets.TryGetValue( attribute.Key, out var destinationOffset ) ) throw new InvalidDataException( "Unexpected fixture semantic." );
			var accessor = accessors[attribute.Value.GetValue<int>()];
			if ( accessor["count"].GetValue<int>() != count || accessor["sparse"] is not null ) throw new InvalidDataException( "Unexpected source accessor count or sparse data." );
			var view = views[accessor["bufferView"].GetValue<int>()];
			if ( view["buffer"].GetValue<int>() != 0 ) throw new InvalidDataException( "Unexpected source buffer identity." );
			var componentSize = accessor["componentType"].GetValue<int>() switch
			{
				5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
				_ => throw new InvalidDataException( "Unexpected fixture component type." )
			};
			var components = accessor["type"].GetValue<string>() switch
			{
				"VEC2" => 2, "VEC3" => 3, "VEC4" => 4,
				_ => throw new InvalidDataException( "Unexpected fixture attribute shape." )
			};
			var width = componentSize * components;
			var sourceStride = view["byteStride"]?.GetValue<int>() ?? width;
			var sourceOffset = (view["byteOffset"]?.GetValue<int>() ?? 0) + (accessor["byteOffset"]?.GetValue<int>() ?? 0);
			for ( var vertex = 0; vertex < count; vertex++ )
				original.AsSpan( sourceOffset + vertex * sourceStride, width ).CopyTo( bytes.AsSpan( origin + vertex * stride + destinationOffset, width ) );
			accessor["bufferView"] = viewIndex; accessor["byteOffset"] = destinationOffset;
		}
		views.Add( new JsonObject { ["buffer"] = 0, ["byteOffset"] = origin, ["byteLength"] = count * stride, ["byteStride"] = stride, ["target"] = 34962 } );
		buffers[0]["byteLength"] = bytes.Length;
		buffers[0]["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes );
		return JsonSerializer.SerializeToUtf8Bytes( root );
	}
}
