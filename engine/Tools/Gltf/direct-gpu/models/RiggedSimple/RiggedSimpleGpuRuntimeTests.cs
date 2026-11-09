using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Tests;

/// <summary>Actual GPU readback against the independent model oracle, not visual approval.</summary>
public static class RiggedSimpleGpuRuntimeTests
{
	public sealed record Result( string Name, bool Passed, string Error, int Samples, int Vertices,
		double MaxPositionInches, double MaxNormalDistance, double MaxNormalAngleDegrees, int EmptyCoverage, int PositiveCoverage,
		int Strict32UlpExceedances, double WrongPoseMaximumInches );
	public static async Task<Result[]> RunAll( string checkout, string compiledRoot, string output )
	{
		var forms = new[] { ("glTF", "RiggedSimple.gltf"), ("glTF-Embedded", "RiggedSimple.gltf"),
			("glTF-Binary", "RiggedSimple.glb"), ("glTF-Draco", "RiggedSimple.gltf") };
		Directory.CreateDirectory( output ); var results = new List<Result>();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output,
			new global::Vector3( -565.84424f, 0, 28.292212f ), global::Vector3.Zero, 450.30286f );
		capture.Camera.ZFar = 2100;
		// Match the approved camera-directed light; the stock capture helper's
		// fixed point light is behind this 180-degree camera view.
		var light = new SceneDirectionalLight( capture.Scene.SceneWorld,
			Rotation.LookAt( new global::Vector3( .99875234f, 0, -.049937617f ), global::Vector3.Up ), global::Color.White * 2 );
		try
		{
		var compute = Material.FromShader( "shaders/gltf_deform_cs.shader" );
		if ( compute is null || !compute.IsValid || compute.IsError ) throw new InvalidOperationException( "Compiled deformation material is unavailable." );
		var sourceRoot = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/RiggedSimple" );
		var ordinary = Path.Combine( sourceRoot, "glTF/RiggedSimple.gltf" );
		var draco = Path.Combine( sourceRoot, "glTF-Draco/RiggedSimple.gltf" );
		var oracle = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/RiggedSimple/draco-reference.json" );
		foreach ( var (variant, file) in forms ) foreach ( var compiled in new[] { false, true } )
		{
			var name = variant + (compiled ? "/compiled" : "/source"); var label = variant.Replace( '-', '_' ) + (compiled ? "_compiled" : "_source");
			var samples = 0; var vertices = 0; var positive = 0; var strict = 0; var positiveCaptured = false; double pos = 0, normal = 0, angle = 0, wrongPose = 0;
			try
			{
				var source = compiled ? Path.Combine( compiledRoot, "Models/RiggedSimple", variant, "RiggedSimple.glb_c" ) : Path.Combine( sourceRoot, variant, file );
				byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( source ), Uri.UnescapeDataString( uri ) ) );
				using var reference = variant == "glTF-Draco" ? RiggedSimpleReference.ReadDraco( draco, oracle ) : RiggedSimpleReference.Read( ordinary );
				using var document = GltfReader.Read( File.ReadAllBytes( source ), Resolve, new HashSet<string> { "KHR_draco_mesh_compression" } );
				var empty = await capture.Take( label + "_empty_before" ); Require( empty.Coverage == 0, "Initial target is not empty." );
				async Task Wait()
				{
					await capture.WaitFrames();
					if ( !positiveCaptured ) { var image = await capture.Take( label + "_positive" ); positive = image.Coverage; positiveCaptured = true; }
				}
				var rows = await RiggedSimpleGpuProbe.Run( document, reference, capture.Scene.SceneWorld, compute, Wait );
				Require( rows.Count == 108, "Expected 108 real GPU measurement rows." );
				samples = rows.Count; vertices = rows.Sum( x => x.Comparison.VertexCount );
				pos = rows.Max( x => x.Comparison.PositionMaximumInches ); normal = rows.Max( x => x.Comparison.NormalMaximumDistance );
				angle = rows.Max( x => x.Comparison.NormalMaximumAngleDegrees );
				foreach ( var row in rows ) for ( var i = 0; i < row.Comparison.VertexCount; i++ )
				{
					var expected = row.Comparison.ExpectedPositions[i]; var actual = row.Comparison.ActualPositions[i];
					var expectedNormal = row.Comparison.ExpectedNormals[i]; var actualNormal = row.Comparison.ActualNormals[i];
					strict += ExceedsCpuBound( expected.X, actual.X ) + ExceedsCpuBound( expected.Y, actual.Y ) + ExceedsCpuBound( expected.Z, actual.Z );
					strict += ExceedsCpuBound( expectedNormal.X, actualNormal.X ) + ExceedsCpuBound( expectedNormal.Y, actualNormal.Y ) + ExceedsCpuBound( expectedNormal.Z, actualNormal.Z );
				}
				// Deliberately compare the actual middle-key draw to the first-key
				// independent pose. It must be rejected by the same GPU bound.
				var middle = rows.Single( x => x.Name == "pose_29" ).Comparison;
				var correct = reference.ToEngine( reference.Evaluate( reference.KeyTimes[25] ), System.Numerics.Matrix4x4.Identity );
				var wrong = reference.ToEngine( reference.Evaluate( reference.KeyTimes[0] ), System.Numerics.Matrix4x4.Identity );
				Require( middle.VertexCount == reference.VertexCount, "Negative control requires one output per authored vertex." );
				for ( var i = 0; i < middle.VertexCount; i++ )
				{
					Require( System.Numerics.Vector3.Distance( middle.ExpectedPositions[i], correct.Positions[i] ) < 1e-4f, "Negative-control source vertex mapping differs." );
					wrongPose = Math.Max( wrongPose, System.Numerics.Vector3.Distance( middle.ActualPositions[i], wrong.Positions[i] ) );
				}
				Require( wrongPose > 1e-4, "Wrong-pose negative control did not fail the GPU position gate." );
				// Existing accepted public SimpleSkin GPU gates, recorded in EXECUTION.md.
				Require( pos <= 1e-4 && normal <= 1e-5, $"GPU errors exceed position/normal gates: {pos:R}, {normal:R}." );
				Require( positive > 64, "No positive visible geometry." );
				var deleted = await capture.Take( label + "_empty_after" ); Require( deleted.Coverage == 0, "Owned objects left visible geometry after disposal." );
				results.Add( new( name, true, null, samples, vertices, pos, normal, angle, deleted.Coverage, positive, strict, wrongPose ) );
			}
			catch ( Exception error )
			{
				var empty = -1; string cleanup = null;
				try { empty = (await capture.Take( label + "_empty_on_failure" )).Coverage; }
				catch ( Exception cleanupError ) { cleanup = cleanupError.ToString(); }
				results.Add( new( name, false, error + (cleanup is null ? "" : "\nCleanup observation: " + cleanup), samples, vertices, pos, normal, angle, empty, positive, strict, wrongPose ) );
			}
		}
		return results.ToArray();
		}
		finally { if ( light.IsValid() ) light.Delete(); }
	}
	private static int ExceedsCpuBound( float expected, float actual )
	{
		// Retain the stricter CPU-style result as a diagnostic, not the GPU gate.
		var limit = 32d * 1.1920928955078125e-7 * Math.Max( 1d, Math.Abs( expected ) );
		return !float.IsFinite( actual ) || Math.Abs( (double)actual - expected ) > limit ? 1 : 0;
	}
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
