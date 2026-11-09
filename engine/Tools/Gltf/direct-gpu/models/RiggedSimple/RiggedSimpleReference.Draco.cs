using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.Tests;

public sealed partial class RiggedSimpleReference
{
	/// <summary>
	/// Feed separately integrated official WASM geometry, in its own point order,
	/// into the independent matrix/skin reference. Animation and inverse binds are
	/// read from this Draco variant's source bytes. This shares codec ancestry with
	/// production Draco, but invokes no Sandbox.Gltf/Evergine decoder or evaluator.
	/// </summary>
	public static RiggedSimpleReference ReadDraco( string sourcePath, string decodedOraclePath )
	{
		var sourceBytes = File.ReadAllBytes( sourcePath );
		using var decoded = JsonDocument.Parse( File.ReadAllBytes( decodedOraclePath ) );
		var oracle = decoded.RootElement;
		Require( oracle.GetProperty( "version" ).GetInt32() == 1, "Unsupported Draco reference schema." );
		Require( oracle.GetProperty( "pin" ).GetString() == "90d7ede14c7e280af263824604b427a1ca02cb66", "Draco reference corpus pin changed." );
		var identity = oracle.GetProperty( "source" );
		Require( identity.GetProperty( "relative" ).GetString() == "Models/RiggedSimple/glTF-Draco/RiggedSimple.gltf", "Reference belongs to a different source variant." );
		Require( identity.GetProperty( "sha256" ).GetString() == Hash( sourceBytes ), "Source changed after separate decoder reference was authored." );
		var decoder = oracle.GetProperty( "decoder" );
		Require( decoder.GetProperty( "version" ).GetString() == "1.5.7" && decoder.GetProperty( "commit" ).GetString() == "8786740086a9f4d83f44aa83badfbea4dce7a1b5", "Unexpected separate decoder version." );
		Require( decoder.GetProperty( "js_sha256" ).GetString() == "8bb2952d2ba7d67e1414f8df819410cb0434a666be53f671fff75f68843d76f6" &&
			decoder.GetProperty( "wasm_sha256" ).GetString() == "712db3449ae2041d6e8a224c395bda6cedb49e51322fae38b7db9beb8b381889", "Official separate decoder file identity changed." );
		Require( decoder.GetProperty( "license" ).GetString() == "Apache-2.0" && decoder.GetProperty( "independence" ).GetString().Contains( "shared codec ancestry", StringComparison.Ordinal ), "Decoder license/independence statement is absent." );
		var root = JsonNode.Parse( sourceBytes ).AsObject();
		Require( root["buffers"].AsArray().Count == 1, "Changed model buffer profile requires review." );
		var sourceBuffer = File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( sourcePath ), root["buffers"][0]["uri"].GetValue<string>() ) );
		Require( identity.GetProperty( "buffer_sha256" ).GetString() == Hash( sourceBuffer ), "Source buffer changed after separate reference was authored." );
		var primitive = root["meshes"][0]["primitives"][0]; var views = root["bufferViews"].AsArray(); var accessors = root["accessors"].AsArray();
		var extension = primitive["extensions"]["KHR_draco_mesh_compression"];
		var compressedView = views[extension["bufferView"].GetValue<int>()];
		Require( identity.GetProperty( "compressed_sha256" ).GetString() == Hash( sourceBuffer.AsSpan( compressedView["byteOffset"]?.GetValue<int>() ?? 0, compressedView["byteLength"].GetValue<int>() ) ), "Compressed view changed after reference authoring." );
		var vertexCount = oracle.GetProperty( "vertex_count" ).GetInt32();
		Require( vertexCount is > 0 and < 100000, "Reference vertex count is outside this model budget." );
		using var buffer = new MemoryStream(); buffer.Write( sourceBuffer );
		int Append( byte[] bytes, int? target )
		{
			while ( buffer.Length % 4 != 0 ) buffer.WriteByte( 0 );
			var index = views.Count;
			var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = checked((int)buffer.Length), ["byteLength"] = bytes.Length };
			if ( target.HasValue ) view["target"] = target.Value;
			views.Add( view ); buffer.Write( bytes ); return index;
		}
		var attributes = primitive["attributes"].AsObject(); var decodedAttributes = oracle.GetProperty( "attributes" );
		Require( decodedAttributes.EnumerateObject().Count() == attributes.Count, "Decoded reference attributes are incomplete." );
		foreach ( var attribute in attributes )
		{
			var accessor = accessors[attribute.Value.GetValue<int>()]; var value = decodedAttributes.GetProperty( attribute.Key );
			var componentType = accessor["componentType"].GetValue<int>();
			var width = componentType switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw new InvalidDataException( "Unknown reference component type." ) };
			var components = accessor["type"].GetValue<string>() switch { "VEC3" => 3, "VEC4" => 4, _ => throw new InvalidDataException( "Changed reference attribute shape." ) };
			Require( value.GetProperty( "component_type" ).GetInt32() == componentType, "Separate decoder raw type differs from authored accessor: " + attribute.Key );
			Require( value.GetProperty( "components" ).GetInt32() == components && value.GetProperty( "count" ).GetInt32() == vertexCount && accessor["count"].GetValue<int>() == vertexCount, "Decoded reference extent differs: " + attribute.Key );
			Require( value.GetProperty( "normalized" ).GetBoolean() == (accessor["normalized"]?.GetValue<bool>() ?? false), "Reference must preserve glTF accessor normalization: " + attribute.Key );
			var bytes = Convert.FromBase64String( value.GetProperty( "raw_base64" ).GetString() );
			Require( bytes.Length == checked(vertexCount * components * width) && Hash( bytes ) == value.GetProperty( "raw_sha256" ).GetString(), "Decoded raw bytes changed: " + attribute.Key );
			accessor["bufferView"] = Append( bytes, 34962 ); accessor["byteOffset"] = 0;
		}
		var sourceIndices = oracle.GetProperty( "indices" ); var indexAccessor = accessors[primitive["indices"].GetValue<int>()];
		Require( indexAccessor["componentType"].GetValue<int>() == 5123 && indexAccessor["count"].GetValue<int>() == sourceIndices.GetArrayLength(), "Changed reference index representation requires review." );
		var indexBytes = new byte[sourceIndices.GetArrayLength() * 2];
		for ( var index = 0; index < sourceIndices.GetArrayLength(); index++ )
		{
			var value = sourceIndices[index].GetUInt32(); Require( value < vertexCount, "Decoded index escapes variant-local vertex count." );
			BinaryPrimitives.WriteUInt16LittleEndian( indexBytes.AsSpan( index * 2, 2 ), checked((ushort)value) );
		}
		indexAccessor["bufferView"] = Append( indexBytes, 34963 ); indexAccessor["byteOffset"] = 0;
		var bytesOwned = buffer.ToArray(); root["buffers"][0]["byteLength"] = bytesOwned.Length;
		// This JsonDocument is private reference input, never written back or fed
		// to the producer. The original extension and source files stay untouched.
		var json = JsonDocument.Parse( JsonSerializer.SerializeToUtf8Bytes( root ) );
		try { return new RiggedSimpleReference( json, new[] { bytesOwned } ); }
		catch { json.Dispose(); throw; }
	}

	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidDataException( message ); }
	private static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
