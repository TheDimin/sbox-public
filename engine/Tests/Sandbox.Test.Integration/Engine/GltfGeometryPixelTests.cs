using Sandbox.Gltf;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace EngineTests;

/// <summary>Small GPU construction proof. This is not a material or corpus appearance-parity test.</summary>
[TestClass]
public class GltfGeometryPixelTests
{
	[TestInitialize]
	public void RequireGraphics()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" || !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires an authorized graphics test session with SBOX_TEST_GRAPHICS=1." );
	}

	[TestMethod]
	[DataRow( "glTF", false )]
	[DataRow( "glTF-Embedded", false )]
	[DataRow( "glTF", true )]
	[DataRow( "glTF-Embedded", true )]
	public void KhronosTriangleUsesManagedVertexAndIndexBuffers( string variant, bool compile )
	{
		var corpus = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
		if ( string.IsNullOrWhiteSpace( corpus ) ) Assert.Inconclusive( "Set SBOX_GLTF_CORPUS to the pinned Khronos Sample Assets checkout." );
		var directory = Path.Combine( corpus, "Models", "Triangle", variant );
		var bytes = File.ReadAllBytes( Path.Combine( directory, "Triangle.gltf" ) );
		var expectedHash = variant == "glTF" ? "6f4a8a1fbefbac2d0adde0800864279fd98c3f119077380e6840810ed62b6cfc" : "6bf10f2d1e07643cbda968fc6941929d52d4dfd4250182c852103668997f706a";
		Assert.AreEqual( expectedHash, Convert.ToHexStringLower( SHA256.HashData( bytes ) ), "Reference asset differs from the pinned corpus." );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( directory, uri ) );
		if ( compile ) bytes = GltfCompiler.Compile( bytes, Resolve );
		using var document = GltfReader.Read( bytes, Resolve );
		var data = document.CreateDrawData( 0, 0 );
		using var geometry = new GltfGeometryBuffer( data );
		var uploaded = new GltfGeometryBuffer.GltfVertex[data.Positions.Length];
		geometry.Vertices.GetData( uploaded.AsSpan() );
		var indices = new uint[data.Indices.Length];
		geometry.Indices.GetData( indices.AsSpan() );
		CollectionAssert.AreEqual( data.Indices.ToArray(), indices );
		for ( var i = 0; i < uploaded.Length; i++ ) Assert.AreEqual( (Vector3)data.Positions.Span[i], uploaded[i].Position );

		var material = Material.Create( "gltf-geometry-probe", "shaders/gltf_geometry_probe.shader" );
		Assert.IsTrue( material.IsValid );
		Assert.AreEqual( "shaders/gltf_geometry_probe.shader", material.Shader.native.GetFilename(), "Probe shader must be compiled and mounted." );
		try
		{
			using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).WithFormat( ImageFormat.RGBA32323232F ).Create();
			using ( var scope = Graphics.Scope.Create() )
			{
				Graphics.RenderTarget = RenderTarget.From( target );
				scope.Context.Clear( Color.Transparent, true, false, false );
				Graphics.Attributes.Set( "ProbeColor", new Vector4( 0.123456f, 0.25f, 0.75f, 1 ) );
				geometry.Draw( material );
			}
			g_pRenderDevice.ForceFlushGPU( default );
			var pixels = new float[64 * 64 * 4];
			target.GetPixels( (0, 0, 64, 64), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
			var covered = 0;
			for ( var i = 0; i < pixels.Length; i += 4 )
			{
				if ( pixels[i + 3] < 0.5f ) continue;
				covered++;
				Assert.AreEqual( 0.123456f, pixels[i], 0.0001f, "Probe output must remain linear." );
				Assert.AreEqual( 0.25f, pixels[i + 1], 0.0001f );
				Assert.AreEqual( 0.75f, pixels[i + 2], 0.0001f );
			}
			Assert.IsTrue( covered is > 1080 and < 1220, $"Expected the projected triangle, got {covered} covered pixels." );
		}
		finally { material.Destroy(); }
	}
}
