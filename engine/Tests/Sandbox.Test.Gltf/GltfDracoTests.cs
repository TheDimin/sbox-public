using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

// Khronos Sample Assets Box, copyright 2017 Cesium, CC-BY-4.0.
// See Fixtures/KhronosBox for attribution, upstream notice, and the full license.
[TestClass]
public class GltfDracoTests
{
	private const string DracoBuffer = "RFJBQ08CAgEBAAAACAwBCwAAA19bCgEBEFUEXOONRgL/AAAAAQABAAkDAAECAQEJAwAAAwEBAQADAwEwARADACSWEwokBAAAAAD/BwAAAAAAvwAAAL8AAAC/AACAPwsGAwEBAQEBQAEA/wAAAH8AAAD/AqFBCAAA";
	private const string RawBuffer = "AAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AAAAAAAAgL8AAAAAAAAAAAAAgL8AAAAAAAAAAAAAgL8AAAAAAAAAAAAAgL8AAAAAAACAPwAAAAAAAAAAAACAPwAAAAAAAAAAAACAPwAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAAAAgD8AAAAAAAAAAAAAgD8AAAAAAAAAAAAAgD8AAAAAAACAvwAAAAAAAAAAAACAvwAAAAAAAAAAAACAvwAAAAAAAAAAAACAvwAAAAAAAAAAAAAAAAAAAAAAAIC/AAAAAAAAAAAAAIC/AAAAAAAAAAAAAIC/AAAAAAAAAAAAAIC/AAAAvwAAAL8AAAA/AAAAPwAAAL8AAAA/AAAAvwAAAD8AAAA/AAAAPwAAAD8AAAA/AAAAPwAAAL8AAAA/AAAAvwAAAL8AAAA/AAAAPwAAAL8AAAC/AAAAvwAAAL8AAAC/AAAAPwAAAD8AAAA/AAAAPwAAAL8AAAA/AAAAPwAAAD8AAAC/AAAAPwAAAL8AAAC/AAAAvwAAAD8AAAA/AAAAPwAAAD8AAAA/AAAAvwAAAD8AAAC/AAAAPwAAAD8AAAC/AAAAvwAAAL8AAAA/AAAAvwAAAD8AAAA/AAAAvwAAAL8AAAC/AAAAvwAAAD8AAAC/AAAAvwAAAL8AAAC/AAAAvwAAAD8AAAC/AAAAPwAAAL8AAAC/AAAAPwAAAD8AAAC/AAABAAIAAwACAAEABAAFAAYABwAGAAUACAAJAAoACwAKAAkADAANAA4ADwAOAA0AEAARABIAEwASABEAFAAVABYAFwAWABUA";

	[TestMethod]
	public void KhronosBoxDracoMatchesRawOrientedTrianglesAndNormals()
	{
		using var raw = Box( false );
		using var compressed = Box( true );
		var rawTriangles = Triangles( raw );
		var compressedTriangles = Triangles( compressed );
		Assert.AreEqual( 12, rawTriangles.Length );
		CollectionAssert.AreEqual( rawTriangles, compressedTriangles );
	}

	[TestMethod]
	public void DracoAccessorsDecodeBeforePrimitiveRead()
	{
		using var document = Box( true );
		var positions = document.ReadFloatAccessor( 2 );
		var normals = document.ReadFloatAccessor( 1 );
		var indices = document.ReadIndexAccessor( 0 );
		Assert.AreEqual( 72, positions.Length );
		Assert.AreEqual( 72, normals.Length );
		Assert.AreEqual( 36, indices.Length );
		CollectionAssert.AreEqual( indices, document.ReadPrimitive( 0, 0 ).Indices );
	}

	[TestMethod]
	public void SelectedDracoIgnoresAccessorFallbackBufferViews()
	{
		using var raw = Box( false );
		using var compressed = Box( true, invalidFallback: true );
		CollectionAssert.AreEqual( Triangles( raw ), Triangles( compressed ) );
	}

	[TestMethod]
	public void UnsupportedOptionalDracoUsesTheUncompressedFallback()
	{
		using var raw = Box( false );
		using var fallback = Box( false, optionalFallback: true );
		CollectionAssert.AreEqual( Triangles( raw ), Triangles( fallback ) );
	}

	[TestMethod]
	[DataRow( 23, 36 )]
	[DataRow( 24, 35 )]
	public void DecompressedCountsMustMatchDeclaredAccessors( int vertexCount, int indexCount )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Box( true, vertexCount: vertexCount, indexCount: indexCount );
			document.ReadPrimitive( 0, 0 );
		} );
	}

	[TestMethod]
	public void UnknownDracoAttributeIdIsRejected()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Box( true, positionId: 999 );
			document.ReadPrimitive( 0, 0 );
		} );
	}

	[TestMethod]
	public void CorruptNativePayloadIsRejected()
	{
		Assert.ThrowsException<GltfException>( () => GltfDraco.Decode( [1, 2, 3, 4] ) );
	}

	[TestMethod]
	public void DecompressedOutputBudgetIsEnforced()
	{
		var data = Convert.FromBase64String( DracoBuffer );
		Assert.ThrowsException<GltfException>( () => GltfDraco.Decode( data, maxDecodedBytes: 1 ) );
	}

	[TestMethod]
	public void DecodedDataIsOwnedByManagedResult()
	{
		var data = Convert.FromBase64String( DracoBuffer );
		var mesh = GltfDraco.Decode( data );
		Array.Clear( data );
		Assert.AreEqual( 24, mesh.VertexCount );
		Assert.AreEqual( 36, mesh.Indices.Length );
		Assert.IsTrue( mesh.Attributes.ContainsKey( 1 ) );
		Assert.AreEqual( 5126, mesh.Attributes[1].ComponentType );
		Assert.AreEqual( 3, mesh.Attributes[1].Components );
		Assert.AreEqual( 24 * 3 * sizeof( float ), mesh.Attributes[1].Bytes.Length );
	}

	private static GltfDocument Box( bool draco, int positionId = 1, bool invalidFallback = false, bool optionalFallback = false, int vertexCount = 24, int indexCount = 36 )
	{
		var base64 = draco ? DracoBuffer : RawBuffer;
		var bufferLength = draco ? 120 : 648;
		var views = draco
			? "[{\"buffer\":0,\"byteLength\":120}]"
			: "[{\"buffer\":0,\"byteOffset\":576,\"byteLength\":72},{\"buffer\":0,\"byteLength\":576,\"byteStride\":12}]";
		var indexView = draco ? (invalidFallback ? "\"bufferView\":999," : "") : "\"bufferView\":0,";
		var normalView = draco ? (invalidFallback ? "\"bufferView\":999," : "") : "\"bufferView\":1,";
		var positionView = draco ? (invalidFallback ? "\"bufferView\":999," : "") : "\"bufferView\":1,\"byteOffset\":288,";
		var extensions = draco ? "\"extensionsUsed\":[\"KHR_draco_mesh_compression\"],\"extensionsRequired\":[\"KHR_draco_mesh_compression\"]," : optionalFallback ? "\"extensionsUsed\":[\"KHR_draco_mesh_compression\"]," : "";
		var primitiveExtension = draco || optionalFallback ? ",\"extensions\":{\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"NORMAL\":0,\"POSITION\":" + positionId + "}}}" : "";
		var json = $$$"""
			{"asset":{"version":"2.0"},{{{extensions}}}
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{base64}}}","byteLength":{{{bufferLength}}} }],
			 "bufferViews":{{{views}}},
			 "accessors":[{ {{{indexView}}}"componentType":5123,"count":{{{indexCount}}},"type":"SCALAR"},
			              { {{{normalView}}}"componentType":5126,"count":{{{vertexCount}}},"type":"VEC3"},
			              { {{{positionView}}}"componentType":5126,"count":{{{vertexCount}}},"type":"VEC3","min":[-0.501,-0.501,-0.501],"max":[0.501,0.501,0.501]}],
			 "meshes":[{"primitives":[{"attributes":{"NORMAL":1,"POSITION":2},"indices":0,"mode":4{{{primitiveExtension}}} }]}]}
			""";
		var supported = optionalFallback ? new HashSet<string>( StringComparer.Ordinal ) : new HashSet<string>( StringComparer.Ordinal ) { "KHR_draco_mesh_compression" };
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: supported );
	}

	private static string[] Triangles( GltfDocument document )
	{
		var primitive = document.ReadPrimitive( 0, 0 );
		var position = document.ReadFloatAccessor( primitive.Attributes["POSITION"] );
		var normal = document.ReadFloatAccessor( primitive.Attributes["NORMAL"] );
		var result = new List<string>();
		for ( var i = 0; i < primitive.Indices.Length; i += 3 )
		{
			var a = Vertex( primitive.Indices[i], position, normal );
			var b = Vertex( primitive.Indices[i + 1], position, normal );
			var c = Vertex( primitive.Indices[i + 2], position, normal );
			var rotations = new[] { $"{a}|{b}|{c}", $"{b}|{c}|{a}", $"{c}|{a}|{b}" };
			Array.Sort( rotations, StringComparer.Ordinal );
			result.Add( rotations[0] );
		}
		return result.OrderBy( x => x, StringComparer.Ordinal ).ToArray();
	}

	private static string Vertex( uint index, float[] position, float[] normal )
	{
		var i = checked((int)index * 3);
		var components = new int[6];
		for ( var axis = 0; axis < 3; axis++ )
		{
			var p = position[i + axis];
			var n = normal[i + axis];
			var roundedPosition = (int)MathF.Round( p * 2 );
			var roundedNormal = (int)MathF.Round( n );
			Assert.AreEqual( roundedPosition / 2f, p, 0.02f );
			Assert.AreEqual( (float)roundedNormal, n, 0.02f );
			components[axis] = roundedPosition;
			components[axis + 3] = roundedNormal;
		}
		return string.Join( ",", components );
	}
}
