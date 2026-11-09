using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.InterpolationTestValidation;

[TestClass]
public sealed class InterpolationCompositionTests
{
	public static string CheckoutRoot { get; private set; }
	public static void Configure( string checkoutRoot ) => CheckoutRoot = Path.GetFullPath( checkoutRoot );

	[TestMethod]
	public void NineIndependentClipsComposeAgainstSourceOracle()
	{
		var source = Source();
		using var reference = new InterpolationReference( source, Resolve );
		using var document = GltfReader.Read( source, Resolve );
		var clips = Enumerable.Range( 0, 9 ).ToArray();
		var targets = clips.Select( clip => reference.Channel( clip ).Node ).ToArray();
		Assert.AreEqual( 9, targets.Distinct().Count() );
		foreach ( var time in new[] { 0f, .25f, .5f, .75f, 1f, 1.5f, 2f } )
		{
			var pose = document.EvaluatePoses( clips, time );
			for ( var node = 0; node < reference.NodeCount; node++ )
			{
				var index = Array.IndexOf( targets, node );
				var expected = reference.World( node, index < 0 ? null : index, time );
				var actual = Matrix( pose.GetWorldTransform( node ) );
				for ( var i = 0; i < actual.Length; i++ )
					Assert.AreEqual( expected[i], actual[i], 8 * 1.1920928955078125e-7 * Math.Max( 1, Math.Abs( expected[i] ) ), $"node {node} matrix {i}, time {time}" );
			}
		}
	}

	[TestMethod]
	public void DistinctPropertiesOfOneNodeComposeBeforeWorldTransform()
	{
		var source = Source();
		using var reference = new InterpolationReference( source, Resolve );
		var root = JsonNode.Parse( source );
		root["animations"][1]["channels"][0]["target"]["node"] = 0;
		root["animations"][5]["channels"][0]["target"]["node"] = 0;
		using var document = GltfReader.Read( System.Text.Encoding.UTF8.GetBytes( root.ToJsonString() ), Resolve );
		const float time = .25f;
		var scaleChannel = reference.Channel( 1 );
		var rotationChannel = reference.Channel( 5 );
		var scale = InterpolationReference.Sample( scaleChannel.Times, scaleChannel.Values, scaleChannel.Mode, time, false );
		var rotation = InterpolationReference.Sample( rotationChannel.Times, rotationChannel.Values, rotationChannel.Mode, time, true );
		var expected = Matrix4x4.CreateScale( (float)scale[0], (float)scale[1], (float)scale[2] ) *
			Matrix4x4.CreateFromQuaternion( new Quaternion( (float)rotation[0], (float)rotation[1], (float)rotation[2], (float)rotation[3] ) );
		var actual = Matrix( document.EvaluatePoses( [1, 5], time ).GetWorldTransform( 0 ) );
		var expectedValues = Matrix( expected );
		for ( var i = 0; i < actual.Length; i++ ) Assert.AreEqual( expectedValues[i], actual[i], 1e-6, $"matrix {i}" );
	}

	[TestMethod]
	public void DuplicateOrOverlappingSelectionsFailWithoutMutatingPriorPose()
	{
		var root = JsonNode.Parse( Source() );
		var animations = root["animations"].AsArray();
		animations.Add( animations[0].DeepClone() );
		using var document = GltfReader.Read( System.Text.Encoding.UTF8.GetBytes( root.ToJsonString() ), Resolve );
		var before = document.EvaluatePose( 0, .25f );
		var expected = before.GetWorldTransform( 0 );
		Assert.ThrowsException<GltfException>( () => document.EvaluatePoses( [0, 0], .25f ) );
		Assert.ThrowsException<GltfException>( () => document.EvaluatePoses( [0, 9], .25f ) );
		Assert.AreEqual( expected, before.GetWorldTransform( 0 ) );
		Assert.AreEqual( expected, document.EvaluatePoses( [0], .25f ).GetWorldTransform( 0 ) );
	}

	private static byte[] Source()
	{
		var source = File.ReadAllBytes( Path.Combine( Folder(), "InterpolationTest.gltf" ) );
		Assert.AreEqual( "4357b9e260f2e7ffb153255da37c20d6c20a03c76cbebf959a0a4f144a873028", Convert.ToHexStringLower( SHA256.HashData( source ) ) );
		return source;
	}
	private static string Folder() => Path.Combine( CheckoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/InterpolationTest/glTF" );
	private static byte[] Resolve( string name )
	{
		var hash = name switch
		{
			"InterpolationTest_data.bin" => "f4ab866b1b34a0b0ae345a4ac37f2b13b49f6f4e4068ef345880bfcdcdd4d961",
			"InterpolationTest_img0.png" => "dc89eecc149440e13448baf1434b313b1c81ecd027f6f68f8ba20e32eefcc0b4",
			_ => throw new InvalidDataException( "Unexpected source dependency: " + name )
		};
		var bytes = File.ReadAllBytes( Path.Combine( Folder(), name ) );
		Assert.AreEqual( hash, Convert.ToHexStringLower( SHA256.HashData( bytes ) ) );
		return bytes;
	}
	private static double[] Matrix( Matrix4x4 m ) => [m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44];
}
