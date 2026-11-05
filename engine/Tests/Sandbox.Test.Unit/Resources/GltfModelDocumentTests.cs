using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Gltf;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Tests;

/// <summary>Exercises the loader's actual document policy before native allocation.</summary>
[TestClass]
public class GltfModelDocumentTests
{
	private const string Extension = "KHR_mesh_quantization";
	// Three SHORT positions, normalized BYTE directions, aligned strided views,
	// and an authored scale of 0.5. Expected geometry below is independent of
	// the accessor decoder and coordinate-conversion helpers.
	private const string QuantizedSource = """
		{"asset":{"version":"2.0"},"extensionsUsed":["KHR_mesh_quantization"],"extensionsRequired":["KHR_mesh_quantization"],"buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAACAAAAAAAAAAAAAgAAAAAAAAB/AAAAfwAAAH8AfwAAf38AAH9/AAB/AAABAAIA","byteLength":54}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":24,"byteStride":8},{"buffer":0,"byteOffset":24,"byteLength":12,"byteStride":4},{"buffer":0,"byteOffset":36,"byteLength":12,"byteStride":4},{"buffer":0,"byteOffset":48,"byteLength":6}],"accessors":[{"bufferView":0,"componentType":5122,"count":3,"type":"VEC3","min":[0,0,0],"max":[2,2,0]},{"bufferView":1,"componentType":5120,"normalized":true,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5120,"normalized":true,"count":3,"type":"VEC4"},{"bufferView":3,"componentType":5123,"count":3,"type":"SCALAR"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TANGENT":2},"indices":3}]}],"nodes":[{"mesh":0,"scale":[0.5,0.5,0.5]}],"scenes":[{"nodes":[0]}],"scene":0}
		""";

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void LoaderPolicyStagesRequiredQuantizedSourceAndCompiledGeometry( bool compiled )
	{
		var bytes = Encoding.UTF8.GetBytes( QuantizedSource );
		if ( compiled ) bytes = GltfCompiler.Compile( bytes, supportedExtensions: new HashSet<string>( StringComparer.Ordinal ) { Extension } );
		using var document = GltfModelLoader.ReadDocument( bytes );
		Assert.AreEqual( 0, document.UnsupportedOptionalExtensions.Count );
		var scene = GltfStaticSceneAssembler.Assemble( document );
		Assert.AreEqual( 1, scene.Primitives.Count );
		Assert.IsTrue( scene.HasBounds );
		const float unit = 1f / .0254f;
		AssertVector( NVector3.Zero, scene.BoundsMin );
		AssertVector( new NVector3( 0, unit, unit ), scene.BoundsMax );
		var data = scene.Primitives[0].Data;
		Assert.AreEqual( 3, data.Positions.Length );
		AssertVector( NVector3.Zero, data.Positions.Span[0] );
		AssertVector( new NVector3( 0, unit, 0 ), data.Positions.Span[1] );
		AssertVector( new NVector3( 0, 0, unit ), data.Positions.Span[2] );
		foreach ( var normal in data.Normals.Span ) AssertVector( NVector3.UnitX, normal );
		foreach ( var tangent in data.Tangents.Span )
		{
			AssertVector( NVector3.UnitY, new NVector3( tangent.X, tangent.Y, tangent.Z ) );
			Assert.AreEqual( 1f, tangent.W );
		}
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, data.Indices.ToArray() );
		// Exercise the real native-buffer staging adapter without allocating one.
		var prepared = GltfModelPrimitiveBuilder.Prepare( data );
		Assert.AreEqual( 3, prepared.Vertices.Length );
		Assert.AreEqual( MeshPrimitiveType.Triangles, prepared.PrimitiveType );
		foreach ( var vertex in prepared.Vertices ) Assert.AreEqual( Vector3.Forward, vertex.Normal );
	}

	[TestMethod]
	public void LoaderCapabilityDoesNotLegalizeUndeclaredQuantizedGeometry()
	{
		var json = QuantizedSource.Replace( "\"extensionsUsed\":[\"KHR_mesh_quantization\"],\"extensionsRequired\":[\"KHR_mesh_quantization\"],", "", StringComparison.Ordinal );
		using var document = GltfModelLoader.ReadDocument( Encoding.UTF8.GetBytes( json ) );
		Assert.ThrowsException<GltfException>( () => GltfStaticSceneAssembler.Assemble( document ) );
	}

	[TestMethod]
	public void LoaderPolicyStillRejectsUnknownRequiredExtensions()
	{
		var bytes = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extensionsUsed":["VENDOR_unknown"],"extensionsRequired":["VENDOR_unknown"]}
			""" );
		var error = Assert.ThrowsException<GltfException>( () => { using var ignored = GltfModelLoader.ReadDocument( bytes ); } );
		StringAssert.Contains( error.Message, "VENDOR_unknown" );
	}

	[TestMethod]
	public void LoaderPolicyReportsUnknownOptionalExtensionsForFallback()
	{
		using var document = GltfModelLoader.ReadDocument( Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extensionsUsed":["VENDOR_unknown"]}
			""" ) );
		CollectionAssert.AreEqual( new[] { "VENDOR_unknown" }, document.UnsupportedOptionalExtensions.ToArray() );
	}

	private static void AssertVector( NVector3 expected, NVector3 actual )
	{
		Assert.AreEqual( expected.X, actual.X, .00001f );
		Assert.AreEqual( expected.Y, actual.Y, .00001f );
		Assert.AreEqual( expected.Z, actual.Z, .00001f );
	}
}
