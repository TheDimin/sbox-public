using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using NMatrix = System.Numerics.Matrix4x4;
using Capture = Sandbox.Gltf.DirectGpu.GltfGpuTriangleGateTests.NativeCapture;
using CaptureTests = Sandbox.Gltf.DirectGpu.GltfGpuTriangleGateTests;
using Reference = global::Sandbox.Gltf.DirectGpu.SimpleInstancingValidation.SimpleInstancingReference;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>
/// Two execution adapters for six existing synthetic affine states and four
/// existing public-renderer forms. Existing numeric gates remain unchanged.
/// Native images are diagnostic controls, not image/material parity or timing evidence.
/// </summary>
[TestClass]
public sealed class SimpleInstancingCanonicalGpuExecutionTests
{
	public const int ExpectedTestCases = 2;
	public const int ExpectedMeasurementStates = 8;
	private const string ComputeSourceHash = "5f02c8ca37cfbb0296f5deb3c26d4c6f431641c0145251314c5ec7afcae9968d";
	private const string ComputeCompiledHash = "4e5f9c9da78a82fcdef7ae72d0280a454697a0bf1eea6e46a83ca5a94c259c65";
	private const string ReferenceHash = "f213b1b9b2105a8eccfb00a472a1fb97581480371060547bd66266aae2cc0839";
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkoutRoot )
	{
		checkout = Path.GetFullPath( checkoutRoot ); Measurements.Clear();
	}

	[TestMethod]
	public async Task ExistingAffineProbeCompletesSixSourceAndCompiledStates()
	{
		await MainThread.Wait(); ValidateInputs();
		var reference = new Reference( checkout ); var fit = Fit( AffineExpectedPositions( reference ) );
		var output = OutputDirectory( "affine-six" ); CaptureTests.Measurements.Clear(); var completed = 0;
		RecordFit( "affine_camera", fit, output, "Independent double affine oracle over literal synthetic fixture transforms; six rows, 24 source vertices, three placements. No producer bounds used." );
		using var capture = CreateCapture( output, fit );
		try
		{
			Assert.AreEqual( 0, (await capture.Take( "empty_before_affine" )).Coverage, "Affine scene must begin empty." );
			var compute = NamedCompute(); var waits = 0;
			var rows = await SimpleInstancingAffineProbe.Run( checkout, capture.Scene.SceneWorld, compute, async () =>
			{
				await capture.WaitFrames();
				if ( waits++ == 0 )
				{
					// The shared capture light may not illuminate this distant synthetic
					// fixture. Readback is the numeric gate; this image is retained only
					// as camera/presentation context, with no pixel acceptance claim.
					var frame = await capture.Take( "affine_initial_identity_diagnostic" );
					RecordFrame( "affine_diagnostic", frame );
				}
			} );
			completed = rows.Count;
			Measurements.Add( new { phase = "existing_affine_probe_results", planned_states = 6, actual_states = rows.Count, actual_waits = waits, rows = rows.ToArray(), scope = "Actual GPU readback through the unchanged affine probe and original thresholds; singular normal direction remains unspecified." } );
			Assert.AreEqual( 6, rows.Count ); Assert.AreEqual( 6, waits );
			var expected = new[] { "synthetic_affine/source", "synthetic_affine/compiled_glb_c" }
				.SelectMany( form => new[] { "identity", "affine_shear", "mirrored_affine_shear" }.Select( placement => form + "|" + placement ) ).OrderBy( value => value, StringComparer.Ordinal ).ToArray();
			var actual = rows.Select( row => row.Form + "|" + row.Placement ).OrderBy( value => value, StringComparer.Ordinal ).ToArray();
			Assert.IsTrue( actual.SequenceEqual( expected ), "All six exact form/placement pairs are required." );
			Assert.IsTrue( rows.All( row => row.Positions == 144 && row.DefinedNormalTangentFrames == 96 && row.SingularFramesWithFiniteOutput == 48 ), "Every affine state must retain all six tiny/reflected/singular rows." );
			Assert.AreEqual( 0, (await capture.Take( "empty_after_affine_disposal" )).Coverage, "Disposed affine instances must leave an empty native scene." );
		}
		finally
		{
			Measurements.Add( new { phase = "affine_execution_summary", planned_states = 6, completed_states = completed, retained_native_captures = CaptureTests.Measurements.ToArray(), scope = "Six synthetic GPU states; not public loading, matched image/material parity, hardware instancing or performance acceptance." } );
		}
	}

	[TestMethod]
	public async Task CanonicalPublicRendererProbeCompletesTwoActualForms()
	{
		await MainThread.Wait(); ValidateInputs();
		var reference = new Reference( checkout ); var fit = Fit( PublicExpectedPositions( reference ) );
		var forms = new[] { "glTF/source", "glTF/compiled_glb_c" };
		Assert.IsTrue( forms.All( name => reference.Forms.Any( form => form.Name == name ) ), "Actual pinned source/compiled form inventory changed." );
		var output = OutputDirectory( "public-canonical-two" ); CaptureTests.Measurements.Clear(); var rows = new List<SimpleInstancingPublicTests.DrawResult>();
		RecordFit( "public_camera", fit, output, "Independent original sample oracle: all125 instance rows and all24 source vertices at identity public placement; no producer bounds used." );
		try
		{
			for ( var index = 0; index < forms.Length; index++ )
			{
				var form = forms[index]; var folder = Path.Combine( output, index.ToString( "D2" ) + "_" + form.Replace( '/', '_' ) ); Directory.CreateDirectory( folder );
				using var capture = CreateCapture( folder, fit ); var observed = new List<string>();
				var result = await SimpleInstancingPublicTests.RunRenderer( checkout, form, capture.Scene, capture.WaitFrames, async phase =>
				{
					var frame = await capture.Take( phase ); observed.Add( phase ); RecordFrame( form + ":" + phase, frame );
					if ( phase is "empty_before_public_instancing" or "empty_after_public_instancing" ) Assert.AreEqual( 0, frame.Coverage, "Public empty/deletion control retained geometry." );
					else if ( phase == "actual_public_instancing" ) Assert.IsTrue( frame.Coverage > 64, "Public source-material scene must visibly submit geometry; this is a positive control, not pixel parity." );
					else Assert.Fail( "Unexpected public observation checkpoint: " + phase );
				} );
				rows.Add( result ); Measurements.Add( new { phase = "existing_public_renderer_result", form, result, observed_checkpoints = observed.ToArray() } );
				Assert.IsTrue( result.Passed, result.Error ); Assert.IsNotNull( result.Gpu, "Public success requires an actual GPU readback measurement." );
				Assert.IsTrue( result.Gpu.Instances == 125 && result.Gpu.Vertices == 3000 && result.Gpu.Indices == 4500 && result.ComponentDeleted, "Public result omitted source instances/readback/deletion evidence." );
				Assert.IsTrue( observed.SequenceEqual( new[] { "empty_before_public_instancing", "actual_public_instancing", "empty_after_public_instancing" } ), "Every public form needs its exact fresh empty/positive/deleted checkpoints." );
			}
			Assert.AreEqual( 2, rows.Count ); Assert.IsTrue( rows.Select( row => row.Form ).SequenceEqual( forms ) );
		}
		finally
		{
			Measurements.Add( new { phase = "public_execution_summary", planned_states = 2, returned_states = rows.Count, passed_states = rows.Count( row => row.Passed ), rows = rows.ToArray(), retained_native_captures = CaptureTests.Measurements.ToArray(), scope = "Existing public Model.Load/ModelRenderer gates and native coverage controls. No helper Asset.Create/SetPose route, independent image/material parity, hardware instancing or performance claim." } );
		}
	}

	private sealed record CameraFit( double[] Minimum, double[] Maximum, global::Vector3 Center, float Radius );
	private static CameraFit Fit( IEnumerable<double[]> points )
	{
		var minimum = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity }; var maximum = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity }; var count = 0;
		foreach ( var point in points )
		{
			Assert.IsTrue( point.Length == 3 && point.All( double.IsFinite ), "Independent camera point must be finite." ); count++;
			for ( var axis = 0; axis < 3; axis++ ) { minimum[axis] = Math.Min( minimum[axis], point[axis] ); maximum[axis] = Math.Max( maximum[axis], point[axis] ); }
		}
		Assert.IsTrue( count > 0 );
		var center = new global::Vector3( (float)((minimum[0] + maximum[0]) * .5), (float)((minimum[1] + maximum[1]) * .5), (float)((minimum[2] + maximum[2]) * .5) );
		var radius = (float)(.5 * Math.Sqrt( minimum.Select( (value, axis) => Math.Pow( maximum[axis] - value, 2 ) ).Sum() ));
		Assert.IsTrue( float.IsFinite( radius ) && radius > 0 ); return new( minimum, maximum, center, radius );
	}
	private static Capture CreateCapture( string output, CameraFit fit )
	{
		// Orthographic height exceeds the fitted sphere's diameter by15%; camera
		// distance sqrt(22)*R and far12*R enclose its complete depth range.
		var capture = new Capture( output, fit.Center + new global::Vector3( fit.Radius * 3, fit.Radius * 3, fit.Radius * 2 ), fit.Center, fit.Radius * 2.3f );
		try { capture.Camera.ZNear = Math.Max( .1f, fit.Radius * .01f ); capture.Camera.ZFar = fit.Radius * 12; return capture; }
		catch { capture.Dispose(); throw; }
	}
	private static IEnumerable<double[]> PublicExpectedPositions( Reference reference )
	{
		for ( var instance = 0; instance < Reference.InstanceCount; instance++ ) for ( var vertex = 0; vertex < Reference.VertexCount; vertex++ ) yield return reference.Engine( instance, vertex, NMatrix.Identity );
	}
	private static IEnumerable<double[]> AffineExpectedPositions( Reference reference )
	{
		// Literal authored fixture values are used only to frame its camera. The
		// unchanged AffineProbe supplies the separate GPU numeric acceptance.
		var scales = new[] { new[] { 1f, 1.25f, 2f }, new[] { -1f, 1.25f, 2f }, new[] { 1e-30f, 1e-30f, 1e-30f }, new[] { -1e-30f, 2e-20f, 3e-10f }, new[] { 0f, 1.25f, 2f }, new[] { 0f, 0f, 0f } };
		var nodeWorld = new NMatrix( .6f * 2, .8f * 3, 0, 0, -.8f * 2, .6f * 3, 0, 0, 0, 0, 4, 0, 3, -5, 7, 1 );
		foreach ( var placement in new[] { NMatrix.Identity, Reference.AffinePlacement, Reference.MirroredPlacement } ) foreach ( var scale in scales )
		{
			// Authored q=(.5,.5,.5,.5) cycles X->Y->Z->X after scale.
			var local = new NMatrix( 0, scale[0], 0, 0, 0, 0, scale[1], 0, scale[2], 0, 0, 0, 2.5f, 5, 7.5f, 1 );
			for ( var vertex = 0; vertex < Reference.VertexCount; vertex++ )
			{
				var normal = reference.Value( 2, vertex ); var tangent = normal[2] != 0 ? new double[] { 1, 0, 0 } : new double[] { 0, 0, 1 };
				yield return SimpleInstancingAffineReference.Transform( reference.Value( 1, vertex ), normal, tangent, 1, local, nodeWorld, placement ).P;
			}
		}
	}
	private static Material NamedCompute()
	{
		var shader = Shader.Load( GltfManagedModelResource.ComputeShaderPath ); Assert.IsTrue( shader is { IsValid: true, IsError: false, IsAnonymous: false }, "Named default compiled deformation shader is required." );
		var compute = Material.FromShader( GltfManagedModelResource.ComputeShaderPath ); Assert.IsTrue( compute is { IsValid: true, IsError: false } && compute.Shader is { IsValid: true, IsError: false, IsAnonymous: false } );
		Measurements.Add( new { phase = "borrowed_named_compute", shader = compute.ShaderName, source_sha256 = ComputeSourceHash, compiled_sha256 = ComputeCompiledHash, ownership = "engine cache borrowed; never destroyed by adapter" } ); return compute;
	}
	private static void ValidateInputs()
	{
		Assert.IsTrue( !string.IsNullOrWhiteSpace( checkout ) && Directory.Exists( checkout ), "Configure(checkoutRoot) before executing." );
		Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires idle editor main thread outside rendering." );
		Assert.AreEqual( ComputeSourceHash, Hash( "game/core/shaders/gltf_deform_cs.shader" ) ); Assert.AreEqual( ComputeCompiledHash, Hash( "game/core/shaders/gltf_deform_cs.shader_c" ) );
		Assert.AreEqual( ReferenceHash, Hash( "research/gltf-validation/direct-gpu/models/SimpleInstancing/reference.json" ) );
	}
	private static string OutputDirectory( string phase ) { var path = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/simple-instancing-current-gpu", phase, Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( path ); return path; }
	private static string Hash( string relative ) => Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( Path.Combine( checkout, relative ) ) ) );
	private static void RecordFit( string phase, CameraFit fit, string output, string provenance ) => Measurements.Add( new { phase, output, minimum_inches = fit.Minimum, maximum_inches = fit.Maximum, center_inches = fit.Center.ToString(), radius_inches = fit.Radius, orthographic_height_inches = fit.Radius * 2.3f, provenance } );
	private static void RecordFrame( string phase, Capture.Frame frame ) => Measurements.Add( new { phase, frame.Path, frame.Sha256, frame.Coverage, frame.Width, frame.Height, frame.EngineFrame, frame.ObservedWallclockSeconds, scope = "Native capture checkpoint; coverage controls only, no matched-image/material parity." } );
}
