using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.CesiumMan;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NV3 = System.Numerics.Vector3;

/// <summary>Own-variant decoded geometry/animation oracle; never uses core vertex correspondence.</summary>
[TestClass]
public sealed class CesiumManDracoPoseTests
{
	private static string checkout;
	private static string poseReferencePath;
	public static void Configure( string checkoutRoot, string oraclePath ) => (checkout, poseReferencePath) = (checkoutRoot, oraclePath);

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DecodedVariantPoseMatchesItsOwnIndependentGeometryAndAnimation( bool compiled )
	{
		using var reference = JsonDocument.Parse( File.ReadAllBytes( poseReferencePath ) );
		var root = reference.RootElement;
		Assert.AreEqual( 1, root.GetProperty( "version" ).GetInt32() );
		Assert.AreEqual( "glTF-Draco", root.GetProperty( "variant" ).GetString() );
		Assert.AreEqual( "90d7ede14c7e280af263824604b427a1ca02cb66", root.GetProperty( "pin" ).GetString() );
		var source = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/CesiumMan/glTF-Draco/CesiumMan.gltf" );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( source ), Uri.UnescapeDataString( uri ) ) );
		IReadOnlySet<string> extensions = new HashSet<string>( StringComparer.Ordinal ) { "KHR_draco_mesh_compression" };
		var bytes = File.ReadAllBytes( source );
		if ( compiled ) bytes = GltfCompiler.Compile( bytes, Resolve, extensions );
		using var document = GltfReader.Read( bytes, Resolve, extensions );
		foreach ( var sample in root.GetProperty( "samples" ).EnumerateArray() )
		{
			var time = sample.GetProperty( "time" );
			var pose = time.ValueKind == JsonValueKind.Null ? document.EvaluatePose() : document.EvaluatePose( 0, time.GetSingle() );
			var worlds = sample.GetProperty( "world_row_transform" );
			for ( var node = 0; node < worlds.GetArrayLength(); node++ )
			{
				var value = pose.GetWorldTransform( node );
				var actual = new[] { value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24,
					value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44 };
				for ( var component = 0; component < actual.Length; component++ ) Assert.AreEqual( worlds[node][component].GetSingle(), actual[component], .00001f, $"node={node},sample={sample.GetProperty( "label" ).GetString()}" );
			}
			foreach ( var item in root.GetProperty( "checked_vertices" ).EnumerateArray() )
			{
				var vertex = item.GetInt32(); var expected = sample.GetProperty( "positions_gltf" )[vertex];
				var actual = document.EvaluateVertexPosition( 2, 0, vertex, pose );
				Assert.IsTrue( NV3.Distance( actual, new NV3( expected[0].GetSingle(), expected[1].GetSingle(), expected[2].GetSingle() ) ) <= .00002f,
					$"Draco own-order vertex {vertex} differs from its independent pose source at {sample.GetProperty( "label" ).GetString()}." );
			}
		}
	}
}
