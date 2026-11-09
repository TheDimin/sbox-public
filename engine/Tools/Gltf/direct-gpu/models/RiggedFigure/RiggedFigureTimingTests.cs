using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.RiggedFigureValidation;

[TestClass]
public sealed class RiggedFigureTimingTests
{
	public static string CheckoutRoot { get; set; }
	public static string FixtureRoot { get; set; } = "Temp/gltf-validation/riggedfigure-review/compiled-input";
	private static string Folder => Path.Combine( CheckoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/RiggedFigure/glTF" );
	private static byte[] ReadChecked( string path, string hash )
	{
		var bytes = File.ReadAllBytes( path );
		Assert.AreEqual( hash, Convert.ToHexStringLower( SHA256.HashData( bytes ) ) );
		return bytes;
	}
	private static byte[] Source() => ReadChecked( Path.Combine( Folder, "RiggedFigure.gltf" ), "ca84ebbf6084d755ff7ac81b3b7c8bc388da68c8aa4b2cf236503b07d3858cc6" );
	private static byte[] Resolve( string uri )
	{
		Assert.AreEqual( "RiggedFigure0.bin", uri );
		return ReadChecked( Path.Combine( Folder, uri ), "28baa193dbc2f8bfad9cb8c13ae16191c592725ea3b2b22b7f05ab701618ce27" );
	}

	[TestMethod]
	public void SourceAndCompiledPreserveAuthoredTrackKeysAndIndependentMidpoint()
	{
		var source = Source();
		foreach ( var bytes in new[] { source, GltfCompiler.Compile( source, Resolve ) } )
		{
			using var doc = GltfReader.Read( bytes, Resolve );
			var animation = doc.Root.GetProperty( "animations" )[0];
			var channel = animation.GetProperty( "channels" ).EnumerateArray().Single( c => c.GetProperty( "target" ).GetProperty( "node" ).GetInt32() == 19 && c.GetProperty( "target" ).GetProperty( "path" ).GetString() == "rotation" );
			var sampler = animation.GetProperty( "samplers" )[channel.GetProperty( "sampler" ).GetInt32()];
			CollectionAssert.AreEqual( new[] { 0f, 1.25f }, doc.ReadFloatAccessor( sampler.GetProperty( "input" ).GetInt32() ) );
			var values = doc.ReadFloatAccessor( sampler.GetProperty( "output" ).GetInt32() );
			var actual = GltfAnimationSampler.Sample( [0, 1.25f], values, 4, "LINEAR", .625f, rotation: true );
			float[] expected = [-.68263453415f, 0, 0, -.73075994197f];
			for ( var i = 0; i < 4; i++ ) Assert.AreEqual( expected[i], actual[i], 1e-6f, $"midpoint component {i}" );
		}
	}

	[TestMethod]
	public void SourceAndCompiledProduceSameDistinctAuthoredPoses()
	{
		var source = Source();
		using var original = GltfReader.Read( source, Resolve );
		using var compiled = GltfReader.Read( GltfCompiler.Compile( source, Resolve ), Resolve );
		var positions = new Matrix4x4[3];
		var times = new[] { 0f, .625f, 1.25f };
		for ( var i = 0; i < times.Length; i++ )
		{
			positions[i] = original.EvaluatePose( 0, times[i] ).GetWorldTransform( 19 );
			Assert.AreEqual( positions[i], compiled.EvaluatePose( 0, times[i] ).GetWorldTransform( 19 ) );
		}
		Assert.AreNotEqual( positions[0], positions[1] );
		Assert.AreNotEqual( positions[1], positions[2] );
	}

	[TestMethod]
	public void BuildIsolatedCompiledInput()
	{
		var output = Path.Combine( CheckoutRoot, FixtureRoot );
		Directory.CreateDirectory( output );
		var path = Path.Combine( output, "RiggedFigure.glb_c" );
		Assert.IsFalse( File.Exists( path ), "Refusing to replace an existing compiled fixture." );
		var bytes = GltfCompiler.Compile( Source(), Resolve );
		File.WriteAllBytes( path, bytes );
		using var document = GltfReader.Read( bytes );
		Assert.AreEqual( 1, document.Root.GetProperty( "animations" ).GetArrayLength() );
	}
}
