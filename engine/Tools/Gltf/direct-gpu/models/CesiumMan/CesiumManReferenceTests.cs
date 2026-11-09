using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.CesiumMan;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NV3 = System.Numerics.Vector3;

/// <summary>
/// Compile these tests against the editor's loaded producer assembly. The JSON
/// oracle is authored independently from pinned source bytes by author_reference.py.
/// These are source/CPU contracts, not proof of GPU rendering or Model.Load support.
/// </summary>
[TestClass]
public sealed class CesiumManReferenceTests
{
	public static string Checkout { get; set; }
	public static string OraclePath { get; set; }
	private const string Pin = "90d7ede14c7e280af263824604b427a1ca02cb66";
	private const float PositionTolerance = .00002f;
	private const float MatrixTolerance = .00001f;
	private static readonly IReadOnlySet<string> Draco = new HashSet<string>( StringComparer.Ordinal ) { "KHR_draco_mesh_compression" };

	public static void Configure( string checkoutRoot, string oraclePath ) => (Checkout, OraclePath) = (checkoutRoot, oraclePath);

	[DataTestMethod]
	[DataRow( "glTF" )]
	[DataRow( "glTF-Embedded" )]
	[DataRow( "glTF-Binary" )]
	[DataRow( "glTF-Draco" )]
	public void SourceIdentityAndSceneInventoryMatchThePinnedInput( string variant )
	{
		using var oracle = Oracle();
		var input = oracle.RootElement.GetProperty( "assets" ).GetProperty( variant );
		var path = AssetPath( input );
		Assert.AreEqual( input.GetProperty( "sha256" ).GetString(), Hash( File.ReadAllBytes( path ) ), "Source changed after reference authoring." );
		foreach ( var dependency in input.GetProperty( "dependencies" ).EnumerateObject() )
			Assert.AreEqual( dependency.Value.GetString(), Hash( File.ReadAllBytes( Path.Combine( Corpus(), dependency.Name ) ) ), dependency.Name );
		using var document = Read( path );
		Assert.AreEqual( 22, document.Root.GetProperty( "nodes" ).GetArrayLength() );
		Assert.AreEqual( 1, document.Root.GetProperty( "meshes" ).GetArrayLength() );
		Assert.AreEqual( 1, document.Root.GetProperty( "skins" ).GetArrayLength() );
		Assert.AreEqual( 1, document.Root.GetProperty( "animations" ).GetArrayLength() );
		Assert.AreEqual( 57, document.Root.GetProperty( "animations" )[0].GetProperty( "channels" ).GetArrayLength() );
		var skin = document.ReadSkin( 0 );
		CollectionAssert.AreEqual( new[] { 3, 12, 13, 20, 21, 17, 14, 18, 15, 19, 16, 8, 4, 9, 5, 10, 6, 11, 7 }, skin.Joints );
		Assert.AreEqual( 19, skin.InverseBindMatrices.Length );
		Assert.AreEqual( 2, input.GetProperty( "mesh_node" ).GetInt32() );
	}

	[DataTestMethod]
	[DataRow( "glTF" )]
	[DataRow( "glTF-Embedded" )]
	[DataRow( "glTF-Binary" )]
	public void SourceUploadRetainsTwoSharedViewsAndNonzeroAccessorOffsets( string variant )
	{
		using var oracle = Oracle();
		using var document = Read( AssetPath( oracle.RootElement.GetProperty( "assets" ).GetProperty( variant ) ) );
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var position = upload.Attributes["POSITION"]; var normal = upload.Attributes["NORMAL"];
		var joints = upload.Attributes["JOINTS_0"]; var uv = upload.Attributes["TEXCOORD_0"];
		Assert.AreSame( position.Storage, normal.Storage, "The source shares the two float3 streams' view." );
		Assert.AreSame( joints.Storage, uv.Storage, "The source shares U16x4 and FLOAT2 storage." );
		Assert.AreEqual( 39276, position.ByteOffset ); Assert.AreEqual( 0, normal.ByteOffset );
		Assert.AreEqual( 26184, uv.ByteOffset ); Assert.AreEqual( 0, joints.ByteOffset );
		Assert.AreEqual( 12, position.ByteStride ); Assert.AreEqual( 12, normal.ByteStride );
		Assert.AreEqual( 8, joints.ByteStride ); Assert.AreEqual( 8, uv.ByteStride );
		Assert.AreEqual( 5123, joints.ComponentType ); Assert.IsFalse( joints.Normalized );
		Assert.AreEqual( 5126, upload.Attributes["WEIGHTS_0"].ComponentType );
		Assert.AreEqual( 16, upload.Attributes["WEIGHTS_0"].ByteStride );
		Assert.AreEqual( 3273, position.Count ); Assert.AreEqual( 14016, upload.Indices.Count );
		Assert.AreEqual( 5123, upload.Indices.ComponentType );
	}

	[DataTestMethod]
	[DataRow( "glTF" )]
	[DataRow( "glTF-Embedded" )]
	[DataRow( "glTF-Binary" )]
	public void EveryOwnedAttributeMatchesIndependentSourceBytes( string variant )
	{
		using var oracle = Oracle();
		var input = oracle.RootElement.GetProperty( "assets" ).GetProperty( variant );
		using var document = Read( AssetPath( input ) );
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		foreach ( var attribute in upload.Attributes )
		{
			var expected = input.GetProperty( "attributes" ).GetProperty( attribute.Key );
			Assert.AreEqual( expected.GetProperty( "raw_sha256" ).GetString(), Hash( TightBytes( attribute.Value ) ), attribute.Key );
			Assert.AreEqual( expected.GetProperty( "offset" ).GetInt32(), attribute.Value.ByteOffset );
			Assert.AreEqual( expected.GetProperty( "stride" ).GetInt32(), attribute.Value.ByteStride );
		}
		Assert.AreEqual( input.GetProperty( "indices_raw_sha256" ).GetString(), Hash( TightBytes( upload.Indices ) ) );
		// A zeroed POSITION accessor offset must not be another passing binding.
		var position = upload.Attributes["POSITION"];
		Assert.AreNotEqual( input.GetProperty( "attributes" ).GetProperty( "POSITION" ).GetProperty( "raw_sha256" ).GetString(),
			Hash( position.Storage.Bytes.Span[..(position.Count * 12)].ToArray() ), "Oracle is insensitive to the reported source-offset regression." );
	}

	[DataTestMethod]
	[DataRow( "glTF" )]
	[DataRow( "glTF-Embedded" )]
	[DataRow( "glTF-Binary" )]
	public void EvaluatedWorldsAndSelectedVerticesMatchIndependentReference( string variant )
	{
		using var oracle = Oracle();
		using var document = Read( AssetPath( oracle.RootElement.GetProperty( "assets" ).GetProperty( variant ) ) );
		AssertPoses( document, oracle.RootElement.GetProperty( "variants" ).GetProperty( variant ) );
	}

	[DataTestMethod]
	[DataRow( "glTF" )]
	[DataRow( "glTF-Embedded" )]
	[DataRow( "glTF-Binary" )]
	public void LosslessPackagingRetainsIndependentPosesAndMaterialInputs( string variant )
	{
		using var oracle = Oracle();
		var path = AssetPath( oracle.RootElement.GetProperty( "assets" ).GetProperty( variant ) );
		var compiled = GltfCompiler.Compile( File.ReadAllBytes( path ), uri => Resolve( path, uri ), Draco );
		Assert.AreEqual( 0x46546c67u, BinaryPrimitives.ReadUInt32LittleEndian( compiled ) );
		Assert.AreEqual( 2u, BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( 4 ) ) );
		Assert.AreEqual( (uint)compiled.Length, BinaryPrimitives.ReadUInt32LittleEndian( compiled.AsSpan( 8 ) ) );
		using var document = GltfReader.Read( compiled, uri => Resolve( path, uri ), Draco );
		AssertPoses( document, oracle.RootElement.GetProperty( "variants" ).GetProperty( variant ) );
		var material = document.Root.GetProperty( "materials" )[0].GetProperty( "pbrMetallicRoughness" );
		Assert.AreEqual( 0f, material.GetProperty( "metallicFactor" ).GetSingle() );
		Assert.AreEqual( 0, material.GetProperty( "baseColorTexture" ).GetProperty( "index" ).GetInt32() );
	}

	[DataTestMethod]
	[DataRow( "glTF" )]
	[DataRow( "glTF-Embedded" )]
	[DataRow( "glTF-Binary" )]
	public void IndependentPosesDoNotOverwriteOtherInstancesOrTheAuthoredRest( string variant )
	{
		using var oracle = Oracle();
		using var document = Read( AssetPath( oracle.RootElement.GetProperty( "assets" ).GetProperty( variant ) ) );
		var first = document.EvaluatePose( 0, .25f );
		var retained = Enumerable.Range( 0, 22 ).Select( first.GetWorldTransform ).ToArray();
		var rest = document.EvaluatePose();
		var second = document.EvaluatePose( 0, 1.25f );
		Assert.IsTrue( Enumerable.Range( 0, 22 ).Any( i => second.GetWorldTransform( i ) != retained[i] ), "Different source times produced no joint motion." );
		for ( var i = 0; i < 22; i++ ) Assert.AreEqual( retained[i], first.GetWorldTransform( i ), "Another instance overwrote this pose." );
		for ( var i = 0; i < 22; i++ ) Assert.AreEqual( rest.GetWorldTransform( i ), document.EvaluatePose().GetWorldTransform( i ), "Animation mutated the authored bind state." );
	}

	[DataTestMethod]
	[DataRow( "glTF" )]
	[DataRow( "glTF-Embedded" )]
	[DataRow( "glTF-Binary" )]
	public void PlaybackBeforeNonzeroFirstKeyClampsToThatKey( string variant )
	{
		using var oracle = Oracle();
		using var document = Read( AssetPath( oracle.RootElement.GetProperty( "assets" ).GetProperty( variant ) ) );
		var firstTime = oracle.RootElement.GetProperty( "variants" ).GetProperty( variant ).GetProperty( "key_times" )[0].GetSingle();
		Assert.IsTrue( firstTime > 0, "This regression needs a nonzero first authored key." );
		var before = document.EvaluatePose( 0, 0 ); var first = document.EvaluatePose( 0, firstTime );
		var bind = document.EvaluatePose();
		for ( var i = 0; i < 22; i++ ) Assert.AreEqual( first.GetWorldTransform( i ), before.GetWorldTransform( i ), "Playback at zero incorrectly selected bind/rest." );
		Assert.IsTrue( Enumerable.Range( 0, 22 ).Any( i => bind.GetWorldTransform( i ) != first.GetWorldTransform( i ) ), "The sample no longer distinguishes bind from first-key animation." );
	}

	[TestMethod]
	public void RequiredDracoWithoutCapabilityIsRejectedClearly()
	{
		using var oracle = Oracle();
		var path = AssetPath( oracle.RootElement.GetProperty( "assets" ).GetProperty( "glTF-Draco" ) );
		var exception = Assert.ThrowsException<GltfException>( () => GltfReader.Read( File.ReadAllBytes( path ), uri => Resolve( path, uri ) ) );
		StringAssert.Contains( exception.Message, "KHR_draco_mesh_compression" );
		StringAssert.Contains( exception.Message, "required" );
	}

	private static void AssertPoses( GltfDocument document, JsonElement oracle )
	{
		foreach ( var sample in oracle.GetProperty( "samples" ).EnumerateArray() )
		{
			var time = sample.GetProperty( "time" );
			var pose = time.ValueKind == JsonValueKind.Null ? document.EvaluatePose() : document.EvaluatePose( 0, time.GetSingle() );
			var worlds = sample.GetProperty( "world_row_transform" );
			for ( var node = 0; node < worlds.GetArrayLength(); node++ )
			{
				var actual = pose.GetWorldTransform( node );
				var values = new[] { actual.M11, actual.M12, actual.M13, actual.M14, actual.M21, actual.M22, actual.M23, actual.M24,
					actual.M31, actual.M32, actual.M33, actual.M34, actual.M41, actual.M42, actual.M43, actual.M44 };
				for ( var component = 0; component < values.Length; component++ )
					Assert.AreEqual( worlds[node][component].GetSingle(), values[component], MatrixTolerance, $"node={node} component={component} sample={sample.GetProperty( "label" ).GetString()}" );
			}
			foreach ( var item in oracle.GetProperty( "checked_vertices" ).EnumerateArray() )
			{
				var vertex = item.GetInt32(); var expected = sample.GetProperty( "positions_gltf" )[vertex];
				var actual = document.EvaluateVertexPosition( 2, 0, vertex, pose );
				Assert.IsTrue( NV3.Distance( actual, new NV3( expected[0].GetSingle(), expected[1].GetSingle(), expected[2].GetSingle() ) ) <= PositionTolerance,
					$"Vertex {vertex} differs from independent source oracle at {sample.GetProperty( "label" ).GetString()}: {actual}." );
			}
		}
	}

	private static JsonDocument Oracle()
	{
		if ( string.IsNullOrWhiteSpace( Checkout ) || string.IsNullOrWhiteSpace( OraclePath ) )
			throw new InvalidOperationException( "Coordinator must set Checkout and OraclePath before invoking these tests." );
		var document = JsonDocument.Parse( File.ReadAllBytes( OraclePath ) );
		Assert.AreEqual( 2, document.RootElement.GetProperty( "version" ).GetInt32() );
		Assert.AreEqual( Pin, document.RootElement.GetProperty( "pin" ).GetString() );
		return document;
	}

	private static string Corpus() => Path.Combine( Checkout, "Temp/gltf-reference/glTF-Sample-Assets" );
	private static string AssetPath( JsonElement input ) => Path.Combine( Corpus(), input.GetProperty( "relative" ).GetString() );
	private static GltfDocument Read( string path ) => GltfReader.Read( File.ReadAllBytes( path ), uri => Resolve( path, uri ), Draco );
	private static byte[] Resolve( string asset, string uri )
	{
		if ( Uri.TryCreate( uri, UriKind.Absolute, out _ ) ) throw new InvalidOperationException( "External source URI must be local relative data." );
		var root = Path.GetFullPath( Corpus() ) + Path.DirectorySeparatorChar;
		var path = Path.GetFullPath( Path.Combine( Path.GetDirectoryName( asset ), Uri.UnescapeDataString( uri ) ) );
		if ( !path.StartsWith( root, StringComparison.Ordinal ) ) throw new InvalidOperationException( "Source dependency escapes the pinned corpus." );
		return File.ReadAllBytes( path );
	}
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static byte[] TightBytes( GltfUploadStream stream )
	{
		var size = stream.ComponentType switch { 5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4, _ => throw new InvalidOperationException( "Unexpected source component type." ) };
		var element = stream.Components * size;
		var output = new byte[checked(stream.Count * element)];
		for ( var i = 0; i < stream.Count; i++ ) stream.Storage.Bytes.Span.Slice( stream.ByteOffset + i * stream.ByteStride, element ).CopyTo( output.AsSpan( i * element ) );
		return output;
	}
}
