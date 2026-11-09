using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.Tests;

using NM = System.Numerics.Matrix4x4;
using NV3 = System.Numerics.Vector3;

public static class FoxCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error, double MaximumMatrixError, double MaximumVertexError );
	public sealed record Form( string Name, byte[] Bytes, Func<string, byte[]> Resolve );
	public static IEnumerable<Form> OpenForms( string checkoutRoot )
	{
		var root = Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/Fox" );
		foreach ( var item in new[] { ("glTF", "Fox.gltf"), ("glTF-Binary", "Fox.glb") } )
		{
			var folder = Path.Combine( root, item.Item1 ); var bytes = File.ReadAllBytes( Path.Combine( folder, item.Item2 ) );
			byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( folder, Uri.UnescapeDataString( uri ) ) );
			yield return new Form( item.Item1 + "/source", bytes, Resolve );
			yield return new Form( item.Item1 + "/compiled", GltfCompiler.Compile( bytes, Resolve ), Resolve );
		}
	}

	public static Result[] RunAll( string checkoutRoot )
	{
		var result = new List<Result>();
		foreach ( var form in OpenForms( checkoutRoot ) )
		{
			Run( "pinned_inventory_and_source_formats", () => Inventory( form ) );
			for ( var clip = 0; clip < 3; clip++ ) { var selected = clip; Run( "clip_" + clip + "_every_key_midpoint_and_clamps", () => Clip( form, selected ) ); }
			Run( "rest_and_evaluated_pose_instance_isolation", () => Isolation( form ) );
			void Run( string name, Func<(int Assertions, double Matrix, double Vertex)> test )
			{
				try { var row = test(); result.Add( new Result( name, form.Name, row.Assertions, true, null, row.Matrix, row.Vertex ) ); }
				catch ( Exception exception ) { result.Add( new Result( name, form.Name, 0, false, exception.ToString(), 0, 0 ) ); }
			}
		}
		return result.ToArray();
	}
	private static (int, double, double) Inventory( Form form )
	{
		using var reference = FoxReference.Read( form.Bytes, form.Resolve ); using var document = GltfReader.Read( form.Bytes, form.Resolve );
		Require( reference.VertexCount == 1728 && reference.JointCount == 24 && reference.NodeCount == 26, "Fox independent source inventory changed." );
		Require( new[] { "Survey", "Walk", "Run" }.SequenceEqual( Enumerable.Range( 0, 3 ).Select( reference.ClipName ) ), "Fox authored clip identity changed." );
		Require( MathF.Abs( reference.Duration( 0 ) - 3.4166667461395264f ) == 0 && MathF.Abs( reference.Duration( 1 ) - .7083333134651184f ) == 0 && MathF.Abs( reference.Duration( 2 ) - 1.1583333015441895f ) == 0, "Fox duration was quantized or resampled." );
		var upload = document.MaterializePrimitiveUpload( reference.Mesh, 0 );
		Require( upload.Attributes.Count == 4 && !upload.Attributes.ContainsKey( "NORMAL" ) && !upload.Attributes.ContainsKey( "TANGENT" ), "Fox authored attributes changed; normals must be explicitly generated." );
		Require( upload.Layout.RequiresGeneratedIndices && upload.Indices is null, "Fox source is non-indexed." );
		Require( upload.Attributes["POSITION"].ComponentType == 5126 && upload.Attributes["POSITION"].Components == 3 && upload.Attributes["POSITION"].ByteStride == 12, "Fox source POSITION layout changed." );
		Require( upload.Attributes["JOINTS_0"].ComponentType == 5123 && !upload.Attributes["JOINTS_0"].Normalized && upload.Attributes["JOINTS_0"].Components == 4, "Fox joint storage must not be truncated to U8." );
		Require( upload.Attributes["WEIGHTS_0"].ComponentType == 5126 && upload.Attributes["WEIGHTS_0"].Components == 4 && !upload.Attributes["WEIGHTS_0"].Normalized, "Fox FLOAT weights must not be quantized." );
		var skin = document.ReadSkin( 0 ); Require( skin.Joints.SequenceEqual( Enumerable.Range( 2, 24 ) ), "Fox authored joint order changed." );
		return (10, 0, 0);
	}
	private static (int, double, double) Clip( Form form, int clip )
	{
		using var reference = FoxReference.Read( form.Bytes, form.Resolve ); using var document = GltfReader.Read( form.Bytes, form.Resolve );
		var keys = reference.KeyTimes( clip ); var times = keys.Concat( keys.Zip( keys.Skip( 1 ), (a, b) => (a + b) * .5f ) ).Append( keys[0] - .01f ).Append( keys[^1] + .01f ).Distinct().Order();
		var maximumMatrix = 0d; var maximumVertex = 0d; var assertions = 0;
		var vertices = new[] { 0, 1, 2, 37, 122, 437, 701, 998, 1220, 1500, 1725, 1726, 1727 };
		foreach ( var time in times )
		{
			var expected = reference.Evaluate( clip, time ); var actual = document.EvaluatePose( clip, time );
			for ( var node = 0; node < reference.NodeCount; node++ )
			{
				var a = Values( actual.GetWorldTransform( node ) ); var e = Values( expected.Worlds[node] );
				for ( var component = 0; component < 16; component++ ) { maximumMatrix = Math.Max( maximumMatrix, Math.Abs( (double)a[component] - e[component] ) ); assertions++; }
			}
			foreach ( var vertex in vertices ) { maximumVertex = Math.Max( maximumVertex, Distance( document.EvaluateVertexPosition( reference.MeshNode, 0, vertex, actual ), expected.Positions[vertex] ) ); assertions++; }
		}
		// Float arithmetic comparison gates for this source evaluator. GPU normal,
		// rendered fidelity and animation timing acceptance remain separate.
		Require( maximumMatrix <= 4e-6 && maximumVertex <= 2e-5, $"Fox source evaluator differs from independent reference: matrix={maximumMatrix:R}, vertex={maximumVertex:R}." );
		return (assertions, maximumMatrix, maximumVertex);
	}
	private static (int, double, double) Isolation( Form form )
	{
		using var reference = FoxReference.Read( form.Bytes, form.Resolve ); using var document = GltfReader.Read( form.Bytes, form.Resolve );
		var a = document.EvaluatePose( 0, .75f ); var before = a.GetWorldTransform( 10 ); var beforeVertex = document.EvaluateVertexPosition( reference.MeshNode, 0, 437, a );
		_ = document.EvaluatePose( 2, .5f ); Require( a.GetWorldTransform( 10 ) == before && document.EvaluateVertexPosition( reference.MeshNode, 0, 437, a ) == beforeVertex, "Evaluating Fox instance B mutated A's pose." );
		var rest = document.EvaluatePose(); var expected = reference.Evaluate( null, 0 ); var maximum = 0d;
		foreach ( var vertex in new[] { 0, 37, 437, 998, 1727 } ) maximum = Math.Max( maximum, Distance( document.EvaluateVertexPosition( reference.MeshNode, 0, vertex, rest ), expected.Positions[vertex] ) );
		Require( maximum <= 2e-5, "Fox authored rest differs from independent source." );
		return (6, 0, maximum);
	}
	private static float[] Values( NM m ) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
	private static double Distance( NV3 a, NV3 b ) => Math.Sqrt( Math.Pow( (double)a.X - b.X, 2 ) + Math.Pow( (double)a.Y - b.Y, 2 ) + Math.Pow( (double)a.Z - b.Z, 2 ) );
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidDataException( message ); }
}
