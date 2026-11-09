using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraReaderTests
{
	static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; Measurements.Clear(); }
	internal static string AssetDirectory => Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/Cameras/glTF" );
	internal static byte[] Source()
	{
		var bytes = File.ReadAllBytes( Path.Combine( AssetDirectory, "Cameras.gltf" ) );
		Assert.AreEqual( "5ca6b0ebf70411edd8a11427adeff23b1a2211ea7145f0964a95ac6573df9abc", Hash( bytes ) );
		Assert.AreEqual( "2d73fc484aab11784e8773862e69b28cb7e0d7d1aadac6032ceea7c490cfd15e", Hash( Resolve( "Cameras.bin" ) ) );
		return bytes;
	}
	internal static byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( AssetDirectory, uri ) );
	internal static string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
	internal static byte[] Canonical() => GltfCompiler.Compile( Source(), Resolve );
	internal static GltfDocument Fixture( string camera ) => GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"cameras\":[" + camera + "]}" ) );

	[DataTestMethod]
	[DataRow( false )] [DataRow( true )]
	public void PinnedSourceAndOneCanonicalPackage( bool compiled )
	{
		var bytes = compiled ? Canonical() : Source();
		using var document = GltfReader.Read( bytes, Resolve );
		CheckPerspective( document.ReadCamera( 0 ) );
		CheckOrthographic( document.ReadCamera( 1 ) );
		Assert.AreEqual( 0, document.GetDefaultScene() );
		var matrix = document.GetWorldTransform( 1 );
		Assert.AreEqual( .5f, matrix.M41 ); Assert.AreEqual( .5f, matrix.M42 ); Assert.AreEqual( 3f, matrix.M43 );
		// Literal square data, independently decoded offsets from the pinned JSON.
		CollectionAssert.AreEqual( new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 1, 0 }, document.ReadFloatAccessor( 1 ) );
		CollectionAssert.AreEqual( new float[] { 0, 1, 2, 1, 3, 2 }, document.ReadFloatAccessor( 0 ) );
		Measurements.Add( new { form = compiled ? "canonical-glb_c" : "source-gltf", input_sha256 = Hash( bytes ), cameras = 2, scope = "Reader metadata and accessor bytes; not camera scene integration." } );
	}
	internal static void CheckPerspective( GltfCamera camera )
	{
		Assert.AreEqual( "perspective", camera.Type ); Assert.AreEqual( .7f, camera.YFov );
		Assert.AreEqual( .01f, camera.ZNear ); Assert.AreEqual( 100f, camera.ZFar ); Assert.AreEqual( 1f, camera.AspectRatio );
	}
	internal static void CheckOrthographic( GltfCamera camera )
	{
		Assert.AreEqual( "orthographic", camera.Type ); Assert.AreEqual( 1f, camera.XMagnitude ); Assert.AreEqual( 1f, camera.YMagnitude );
		Assert.AreEqual( .01f, camera.ZNear ); Assert.AreEqual( 100f, camera.ZFar );
	}
	[TestMethod]
	public void OptionalPerspectiveFarAndAspectRemainAbsent()
	{
		using var document = Fixture( "{\"type\":\"perspective\",\"perspective\":{\"yfov\":0.9,\"znear\":0.125}}" );
		var camera = document.ReadCamera( 0 ); Assert.IsNull( camera.ZFar ); Assert.IsNull( camera.AspectRatio );
		Assert.AreEqual( .9f, camera.YFov ); Assert.AreEqual( .125f, camera.ZNear );
	}
	[TestMethod]
	public void ValidNoDefaultSceneAndSignedOrthographicMetadataRemainResources()
	{
		using var document = Fixture( "{\"type\":\"orthographic\",\"orthographic\":{\"xmag\":-2,\"ymag\":3,\"znear\":0,\"zfar\":7}}" );
		Assert.IsNull( document.GetDefaultScene() ); var camera = document.ReadCamera( 0 );
		Assert.AreEqual( -2f, camera.XMagnitude ); Assert.AreEqual( 3f, camera.YMagnitude ); Assert.AreEqual( 0f, camera.ZNear ); Assert.AreEqual( 7f, camera.ZFar );
	}
	[DataTestMethod]
	[DataRow( "yfov", "0" )] [DataRow( "znear", "0" )] [DataRow( "zfar", "0.005" )] [DataRow( "aspectRatio", "-1" )]
	public void InvalidPerspectiveParametersAreRejected( string parameter, string value )
	{
		var model = JsonNode.Parse( Encoding.UTF8.GetString( Source() ) ); model["cameras"][0]["perspective"][parameter] = JsonNode.Parse( value );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( model.ToJsonString() ), Resolve );
		Assert.ThrowsException<GltfException>( () => document.ReadCamera( 0 ) );
	}
	[DataTestMethod]
	[DataRow( "xmag", "0" )] [DataRow( "ymag", "0" )] [DataRow( "znear", "-1" )] [DataRow( "zfar", "0.005" )]
	public void InvalidOrthographicParametersAreRejected( string parameter, string value )
	{
		var model = JsonNode.Parse( Encoding.UTF8.GetString( Source() ) ); model["cameras"][1]["orthographic"][parameter] = JsonNode.Parse( value );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( model.ToJsonString() ), Resolve );
		Assert.ThrowsException<GltfException>( () => document.ReadCamera( 1 ) );
	}
	[TestMethod]
	public void LiteralOracleRejectsPlausibleWrongParameter()
	{
		Assert.ThrowsException<AssertFailedException>( () => CheckPerspective( new GltfCamera { Type = "perspective", YFov = .8f, ZNear = .01f, ZFar = 100, AspectRatio = 1 } ) );
		Assert.ThrowsException<AssertFailedException>( () => CheckOrthographic( new GltfCamera { Type = "orthographic", XMagnitude = 2, YMagnitude = 1, ZNear = .01f, ZFar = 100 } ) );
	}
}
