using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Sandbox.Gltf;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Tests;

/// <summary>Exercises the loader's actual document policy before native allocation.</summary>
[TestClass]
public class GltfModelDocumentTests
{
	[TestMethod]
	public void CompiledAliasesKeepGltfAndGlbSourcesDistinct()
	{
		Assert.AreEqual( "models/box.gltf.glb_c", GltfModelLoader.CompiledPath( "models/box.gltf" ) );
		Assert.AreEqual( "models/box.glb_c", GltfModelLoader.CompiledPath( "models/box.glb" ) );
		Assert.AreEqual( "models/BOX.GLB_c", GltfModelLoader.CompiledPath( "models/BOX.GLB" ) );
		Assert.AreEqual( "models/box.glb_c", GltfModelLoader.CompiledPath( "models/box.glb_c" ) );
	}

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

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void LoaderPolicyStagesRequiredDracoWithNoOrdinaryFallback( bool compiled, bool ignoredInvalidFallback )
	{
		var source = DracoSource( ignoredInvalidFallback );
		var bytes = compiled ? GltfCompiler.Compile( source, supportedExtensions: CodecExtensions() ) : source;
		using var document = GltfModelLoader.ReadDocument( bytes );
		Assert.AreEqual( 0, document.UnsupportedOptionalExtensions.Count );
		var scene = GltfStaticSceneAssembler.Assemble( document );
		var primitive = scene.Primitives.Single();
		Assert.AreEqual( 24, primitive.Data.Positions.Length );
		Assert.AreEqual( 36, primitive.Data.Indices.Length );
		var upload = document.MaterializePrimitiveUpload( primitive.Mesh, primitive.Primitive );
		Assert.IsTrue( upload.Layout.RequiresDracoIndices );
		Assert.IsTrue( upload.Attributes["POSITION"].IsMaterialized );
		Assert.IsNull( upload.Attributes["POSITION"].Binding.SourceBufferView );
		var prepared = GltfModelPrimitiveBuilder.Prepare( primitive.Data, upload: upload );
		Assert.AreEqual( 24, prepared.VertexCount );
		Assert.AreEqual( MeshPrimitiveType.Triangles, prepared.PrimitiveType );
		const float halfSide = .5f / .0254f;
		foreach ( var position in primitive.Data.Positions.Span )
		{
			// The fixture's quantization can shift a face by 0.000489 metres.
			Assert.AreEqual( halfSide, MathF.Abs( position.X ), .03f );
			Assert.AreEqual( halfSide, MathF.Abs( position.Y ), .03f );
			Assert.AreEqual( halfSide, MathF.Abs( position.Z ), .03f );
		}
		for ( var i = 0; i < primitive.Data.Indices.Length; i += 3 )
		{
			var a = (int)primitive.Data.Indices.Span[i]; var b = (int)primitive.Data.Indices.Span[i + 1]; var c = (int)primitive.Data.Indices.Span[i + 2];
			var normal = NVector3.Normalize( NVector3.Cross( primitive.Data.Positions.Span[b] - primitive.Data.Positions.Span[a], primitive.Data.Positions.Span[c] - primitive.Data.Positions.Span[a] ) );
			Assert.IsTrue( NVector3.Dot( normal, primitive.Data.Normals.Span[a] ) > .99f, "Decoded triangle winding and supplied face normals must agree." );
		}
	}

	[TestMethod]
	[DataRow( "EXT_meshopt_compression", false, false )]
	[DataRow( "EXT_meshopt_compression", false, true )]
	[DataRow( "KHR_meshopt_compression", false, false )]
	[DataRow( "KHR_meshopt_compression", false, true )]
	[DataRow( "KHR_meshopt_compression", true, false )]
	[DataRow( "KHR_meshopt_compression", true, true )]
	public void LoaderPolicyStagesRequiredMeshoptFromPlaceholderBufferAndStridedNormalizedAttributes( string extension, bool versionOne, bool compiled )
	{
		var source = MeshoptSource( extension, versionOne );
		var bytes = compiled ? GltfCompiler.Compile( source, supportedExtensions: CodecExtensions() ) : source;
		using var document = GltfModelLoader.ReadDocument( bytes );
		Assert.AreEqual( 0, document.UnsupportedOptionalExtensions.Count );
		var primitive = GltfStaticSceneAssembler.Assemble( document ).Primitives.Single();
		const float side = 300f * (1f / .0254f);
		NVector3[] expected = [NVector3.Zero, new( 0, side, 0 ), new( 0, 0, side ), new( 0, side, side )];
		for ( var i = 0; i < expected.Length; i++ ) AssertVector( expected[i], primitive.Data.Positions.Span[i] );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 2, 1, 3 }, primitive.Data.Indices.ToArray() );
		var upload = document.MaterializePrimitiveUpload( primitive.Mesh, primitive.Primitive );
		Assert.IsTrue( upload.Attributes["POSITION"].IsMaterialized );
		Assert.AreSame( upload.Attributes["POSITION"].Storage, upload.Attributes["TEXCOORD_0"].Storage );
		Assert.AreEqual( 12, upload.Attributes["TEXCOORD_0"].ByteStride );
		Assert.AreEqual( 8, upload.Attributes["TEXCOORD_0"].ByteOffset );
		var prepared = GltfModelPrimitiveBuilder.Prepare( primitive.Data, upload: upload );
		Assert.AreEqual( 6, prepared.VertexCount );
		var uv = prepared.VertexFields.Single( field => field.Semantic == "texcoord" && field.SemanticIndex == 0 );
		Assert.AreEqual( NativeEngine.ColorFormat.COLOR_FORMAT_R16G16_UNORM, uv.Format );
		for ( var corner = 0; corner < prepared.VertexCount; corner++ )
		{
			var sourceVertex = (int)primitive.Data.Indices.Span[corner];
			var expectedU = sourceVertex is 1 or 3 ? 500 : 0; var expectedV = sourceVertex is 2 or 3 ? 500 : 0;
			var offset = corner * prepared.VertexStride + uv.Offset;
			Assert.AreEqual( expectedU, (int)BinaryPrimitives.ReadUInt16LittleEndian( prepared.VertexBytes.AsSpan( offset, 2 ) ) );
			Assert.AreEqual( expectedV, (int)BinaryPrimitives.ReadUInt16LittleEndian( prepared.VertexBytes.AsSpan( offset + 2, 2 ) ) );
		}
	}

	[TestMethod]
	[DataRow( "KHR_draco_mesh_compression" )]
	[DataRow( "EXT_meshopt_compression" )]
	[DataRow( "KHR_meshopt_compression" )]
	public void SelectedRequiredGeometryCodecRejectsCorruptionInsteadOfUsingFallback( string extension )
	{
		var source = extension == "KHR_draco_mesh_compression" ? DracoSource( false, corrupt: true ) : MeshoptSource( extension, false, corrupt: true );
		using var document = GltfModelLoader.ReadDocument( source );
		var error = Assert.ThrowsException<GltfException>( () => GltfStaticSceneAssembler.Assemble( document ) );
		Assert.IsTrue( error.Message.Contains( "Draco", StringComparison.OrdinalIgnoreCase ) || error.Message.Contains( "meshopt", StringComparison.OrdinalIgnoreCase ), error.Message );
	}

	[TestMethod]
	[DataRow( "KHR_texture_basisu" )]
	[DataRow( "KHR_materials_unlit" )]
	[DataRow( "KHR_materials_variants" )]
	public void PublicLoaderDocumentPolicyAcceptsSelectedRequiredExtension( string extension )
	{
		var bytes = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"" + extension + "\"],\"extensionsRequired\":[\"" + extension + "\"]}" );
		using var document = GltfModelLoader.ReadDocument( bytes );
		Assert.AreEqual( 0, document.UnsupportedOptionalExtensions.Count );
	}

	[TestMethod]
	[DataRow( "KHR_materials_transmission" )]
	[DataRow( "EXT_texture_webp" )]
	public void PublicLoaderDocumentPolicyRejectsUnsupportedRequiredExtension( string extension )
	{
		var bytes = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"" + extension + "\"],\"extensionsRequired\":[\"" + extension + "\"]}" );
		var error = Assert.ThrowsException<GltfException>( () => { using var ignored = GltfModelLoader.ReadDocument( bytes ); } );
		StringAssert.Contains( error.Message, "Unsupported required" );
		StringAssert.Contains( error.Message, extension );
	}

	[TestMethod]
	public void PublicLoaderDocumentPolicyUsesCoreImageForUnsupportedOptionalExtension()
	{
		var bytes = Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"extensionsUsed":["EXT_texture_webp"],"images":[{"uri":"fallback.png"},{"uri":"extension.webp"}],"textures":[{"source":0,"extensions":{"EXT_texture_webp":{"source":1}}}]}""" );
		var resolved = new List<string>();
		using var document = GltfModelLoader.ReadDocument( bytes, uri => { resolved.Add( uri ); return new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }; } );
		CollectionAssert.AreEqual( new[] { "EXT_texture_webp" }, document.UnsupportedOptionalExtensions.ToArray() );
		Assert.AreEqual( "image/png", document.ReadTextureImage( 0 ).MimeType );
		CollectionAssert.AreEqual( new[] { "fallback.png" }, resolved );
	}

	[TestMethod]
	public void PublicLoaderDocumentPolicyRejectsCorruptRequiredBasisImageInsteadOfFallback()
	{
		var bytes = Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_basisu"],"extensionsRequired":["KHR_texture_basisu"],"images":[{"uri":"fallback.png"},{"uri":"corrupt.ktx2","mimeType":"image/ktx2"}],"textures":[{"source":0,"extensions":{"KHR_texture_basisu":{"source":1}}}]}""" );
		var resolved = new List<string>();
		using var document = GltfModelLoader.ReadDocument( bytes, uri => { resolved.Add( uri ); return uri == "fallback.png" ? new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } : new byte[] { 0 }; } );
		Assert.ThrowsException<GltfException>( () => document.ReadTextureImage( 0 ) );
		CollectionAssert.AreEqual( new[] { "corrupt.ktx2" }, resolved );
	}

	private static HashSet<string> CodecExtensions() => new( StringComparer.Ordinal ) { "KHR_mesh_quantization", "KHR_draco_mesh_compression", "KHR_meshopt_compression", "EXT_meshopt_compression" };

	// Khronos Sample Assets Box, copyright 2017 Cesium, CC-BY-4.0. The
	// attribution, upstream notice and full license are in Test.Gltf/Fixtures/KhronosBox.
	private static byte[] DracoSource( bool ignoredInvalidFallback, bool corrupt = false )
	{
		var encoded = Convert.FromBase64String( "RFJBQ08CAgEBAAAACAwBCwAAA19bCgEBEFUEXOONRgL/AAAAAQABAAkDAAECAQEJAwAAAwEBAQADAwEwARADACSWEwokBAAAAAD/BwAAAAAAvwAAAL8AAAC/AACAPwsGAwEBAQEBQAEA/wAAAH8AAAD/AqFBCAAA" );
		if ( corrupt ) Array.Clear( encoded );
		var ignored = ignoredInvalidFallback ? "\"bufferView\":999,\"byteOffset\":999," : "";
		return Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_draco_mesh_compression\"],\"extensionsRequired\":[\"KHR_draco_mesh_compression\"],"
			+ "\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( encoded ) + "\",\"byteLength\":" + encoded.Length + "}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":" + encoded.Length + "}],"
			+ "\"accessors\":[{" + ignored + "\"componentType\":5123,\"count\":36,\"type\":\"SCALAR\"},{" + ignored + "\"componentType\":5126,\"count\":24,\"type\":\"VEC3\"},{" + ignored + "\"componentType\":5126,\"count\":24,\"type\":\"VEC3\",\"min\":[-0.501,-0.501,-0.501],\"max\":[0.501,0.501,0.501]}],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"NORMAL\":1,\"POSITION\":2},\"indices\":0,\"extensions\":{\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"NORMAL\":0,\"POSITION\":1}}}}]}],\"nodes\":[{\"mesh\":0}],\"scenes\":[{\"nodes\":[0]}],\"scene\":0}" );
	}

	// Independent known-answer byte stream from meshoptimizer demo/tests.cpp,
	// ce321f27536cdbc61ef86622d8ef11fedf5492a8, MIT; Test.Gltf/Fixtures/meshoptimizer-LICENSE.md.
	private static byte[] MeshoptSource( string extension, bool versionOne, bool corrupt = false )
	{
		var encoded = Convert.FromHexString( versionOne ? "a1eeaaee004b4b4b00004b00007d7d7d00007d00000000000000000000000000000000000000000000620062" : "a0013f0000005857580126000000010c00000058010800000000000000013f0000001718170126000000010c0000001701080000000000000000000000000000000000000000000000000000000000000000000000" );
		if ( corrupt ) Array.Clear( encoded );
		var prefix = new byte[encoded.Length + 3]; encoded.CopyTo( prefix, 3 );
		return Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_mesh_quantization\",\"" + extension + "\"],\"extensionsRequired\":[\"KHR_mesh_quantization\",\"" + extension + "\"],"
			+ "\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( prefix ) + "\",\"byteLength\":" + prefix.Length + "},{\"byteLength\":48}],"
			+ "\"bufferViews\":[{\"buffer\":1,\"byteLength\":48,\"byteStride\":12,\"extensions\":{\"" + extension + "\":{\"buffer\":0,\"byteOffset\":3,\"byteLength\":" + encoded.Length + ",\"count\":4,\"byteStride\":12,\"mode\":\"ATTRIBUTES\"}}}],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5123,\"count\":4,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[300,300,0]},{\"bufferView\":0,\"byteOffset\":8,\"componentType\":5123,\"normalized\":true,\"count\":4,\"type\":\"VEC2\"}],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"TEXCOORD_0\":1},\"mode\":5}]}],\"nodes\":[{\"mesh\":0}],\"scenes\":[{\"nodes\":[0]}],\"scene\":0}" );
	}

	private static void AssertVector( NVector3 expected, NVector3 actual )
	{
		Assert.AreEqual( expected.X, actual.X, .00001f );
		Assert.AreEqual( expected.Y, actual.Y, .00001f );
		Assert.AreEqual( expected.Z, actual.Z, .00001f );
	}
}
