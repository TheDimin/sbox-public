using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;
using NativeFrame = Sandbox.Gltf.DirectGpu.GltfGpuTriangleGateTests.NativeCapture.Frame;

namespace Sandbox.Gltf.DirectGpu.BoxTexturedValidation;

/// <summary>Public Model.Load/ordinary ModelRenderer, with coordinator-owned native capture controls.</summary>
internal static class BoxTexturedPublicCapture
{
	public sealed record FrameResult( string Label, string Path, string Sha256, int Width, int Height, int Coverage,
		ulong EngineFrame, double WallSeconds, string CameraState );
	public sealed record Result( string Form, string InputSha256, string PublicResourcePath,
		BoxTexturedGpuChecks.Row GpuAndMaterial, BoxTexturedImageChecks.ImageMeasurement Image,
		BoxTexturedImageChecks.ImageMeasurement FlippedVImage, FrameResult[] Captures,
		bool? NativeWrapperIsNull, bool? NativeStrongHandleValid, string ComputeShader,
		bool Passed, string Scope, string Error );

	public static async Task<Result> Run( string checkoutRoot, Scene scene, CameraComponent camera,
		Func<Task> waitFrames, Func<string, Task<NativeFrame>> capture,
		float positionToleranceInches, float normalTolerance, double minimumIoU,
		double maximumMeanRgbError, double maximumRgbError, string formName )
	{
		ArgumentNullException.ThrowIfNull( scene ); ArgumentNullException.ThrowIfNull( camera );
		ArgumentNullException.ThrowIfNull( waitFrames ); ArgumentNullException.ThrowIfNull( capture );
		Require( ReferenceEquals( scene, camera.Scene ), "Use the coordinator's private scene/camera." );
		var source = BoxTexturedReference.Open( checkoutRoot );
		var form = source.Forms().Single( row => row.Name == formName );
		var frames = new List<FrameResult>();
		BoxTexturedModelMount mount = null; ModelRenderer renderer = null;
		BoxTexturedImageChecks.ReferenceDraw literal = null;
		BoxTexturedGpuChecks.Row gpu = null;
		BoxTexturedImageChecks.ImageMeasurement comparison = null, flippedComparison = null;
		string resourcePath = null, failure = null, computeShader = null;
		bool? wrapperNull = null, strongValid = null;
		try
		{
			await MainThread.Wait();
			Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Requires idle editor main thread outside rendering." );
			camera.WorldPosition = new Vector3( 140, 160, 100 );
			camera.WorldRotation = Rotation.LookAt( -camera.WorldPosition, Vector3.Up );
			camera.Orthographic = true; camera.OrthographicHeight = 85;
			camera.ZNear = 1; camera.ZFar = 1000; camera.CustomSize = new Vector2( 192, 192 );
			camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All;
			camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
			mount = new BoxTexturedModelMount( form ); var model = mount.Load();
			Require( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null, "Public Model.Load must return a valid managed model without native IModel." );
			resourcePath = model.ResourcePath;
			var materials = model.Materials;
			Require( materials.Length == 1 && model.GltfResource.MaterialSlotSourceIndices.SequenceEqual( new int?[] { 0 } ), "Authored single material slot changed." );
			var namedShader = Shader.Load( GltfManagedModelResource.ComputeShaderPath );
			Require( namedShader is { IsValid: true, IsError: false, IsAnonymous: false }, "Public loading must use the named compiled deformation shader." );
			Empty( await Take( "empty_before" ) );
			literal = new( scene.SceneWorld, form.Independent, NMatrix.Identity, materials[0] );
			var reference = await Take( "independent_reference_source_material" ); Positive( reference );
			Require( literal.DrawCalls > 0 && literal.CallbackErrors.Length == 0, "Independent reference did not draw." );
			var retainedReference = literal; RemoveReference(); var deletedReferenceCalls = retainedReference.DrawCalls;
			Empty( await Take( "empty_after_reference" ) );
			Require( retainedReference.DrawCalls == deletedReferenceCalls, "Deleted reference continued drawing." );
			literal = new( scene.SceneWorld, form.Independent, NMatrix.Identity, materials[0], deliberatelyFlipImageV: true );
			var flipped = await Take( "flipped_v_negative" ); Positive( flipped );
			Require( literal.DrawCalls > 0 && literal.CallbackErrors.Length == 0, "V-flip reference did not draw." );
			var retainedFlipped = literal; RemoveReference(); var deletedFlippedCalls = retainedFlipped.DrawCalls;
			Empty( await Take( "empty_after_negative" ) );
			Require( retainedFlipped.DrawCalls == deletedFlippedCalls, "Deleted negative reference continued drawing." );
			using ( scene.Push() )
			{
				renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
				renderer.WorldTransform = Transform.Zero; renderer.Model = model; renderer.Enabled = true; scene.EditorTick( 0, 0 );
			}
			var instance = renderer.GltfInstance;
			computeShader = model.GltfResource.Asset.ComputeMaterial?.ShaderName;
			Require( computeShader is not null && computeShader.Contains( "gltf_deform_cs", StringComparison.OrdinalIgnoreCase ), "The public instance did not realize the named deformation shader." );
			Require( instance is not null && renderer.SceneObject is SceneCustomObject && ReferenceEquals( renderer.SceneObject, instance.SceneObject ) && renderer.SceneObject.Model is null, "Ordinary ModelRenderer must own the managed buffer path without a native model." );
			Require( instance.Placement == NMatrix.Identity && renderer.SceneObject.Transform == Transform.Zero, "Placement was applied more than once." );
			Require( scene.SceneWorld.SceneObjects.Count( row => row.IsValid() && ReferenceEquals( row.Component, renderer ) && row is SceneCustomObject ) == 1, "Expected exactly one primary public draw object." );
			var handle = renderer.SceneObject.native.GetModelHandle(); wrapperNull = handle.IsNull;
			try { strongValid = !handle.IsNull && handle.IsStrongHandleValid(); Require( strongValid == false, "Public custom object contains a valid native model placeholder." ); }
			finally { if ( !handle.IsNull ) handle.DestroyStrongHandle(); }
			var actual = await Take( "actual_public_Model_Load_ModelRenderer" ); Positive( actual );
			var measurement = BoxTexturedGpuChecks.Measure( instance, form.Independent, NMatrix.Identity );
			var residency = await BoxTexturedGpuChecks.ObserveMaterial( materials[0], source.Rgba, waitFrames );
			gpu = new( form.Name, "authored_rest_public_component", NMatrix.Identity, measurement, residency );
			BoxTexturedGpuChecks.RequireWithin( gpu, positionToleranceInches, normalTolerance );
			Require( reference.CameraStateJson == actual.CameraStateJson && reference.CameraStateJson == flipped.CameraStateJson, "Matched capture camera changed." );
			Require( reference.Width == actual.Width && reference.Height == actual.Height && reference.Width == flipped.Width && reference.Height == flipped.Height, "Matched capture dimensions changed." );
			comparison = BoxTexturedImageChecks.Compare( reference.Pixels, actual.Pixels, reference.Width, reference.Height );
			flippedComparison = BoxTexturedImageChecks.Compare( reference.Pixels, flipped.Pixels, reference.Width, reference.Height );
			DestroyRenderer(); var deletedCalls = instance.DrawCalls;
			Empty( await Take( "empty_after_public_renderer" ) );
			Require( instance.DrawCalls == deletedCalls, "Deleted public renderer continued drawing." );
			Require( model.IsValid && ReferenceEquals( model, mount.Load() ), "Deleting renderer invalidated its cached resource." );
			BoxTexturedImageChecks.RequireBindingComparison( comparison, flippedComparison, minimumIoU, maximumMeanRgbError, maximumRgbError );
			Require( frames.Count == 7, "Capture inventory changed." );
		}
		catch ( Exception error ) { failure = error.ToString(); }
		finally
		{
			await MainThread.Wait(); Clean( RemoveReference, "reference" ); Clean( DestroyRenderer, "renderer" ); Clean( () => mount?.Dispose(), "mount" );
		}
		return new( form.Name, Convert.ToHexStringLower( SHA256.HashData( form.Bytes ) ), resourcePath, gpu, comparison, flippedComparison, frames.ToArray(), wrapperNull, strongValid, computeShader, failure is null,
			"Public Model.Load and ordinary ModelRenderer; actual GPU positions/normals/UVs/indices, exact decoded source base-color texture, source stock complex material and same-material independent literal geometry images. V-flip negative and fresh empty/deletion controls. Not independent sampler/sRGB/PBR fidelity, full affine coverage, editor parity, Windows or performance.", failure );

		async Task<NativeFrame> Take( string label )
		{
			await waitFrames(); await MainThread.Wait();
			var frame = await capture( "BoxTextured_public_" + formName.Replace( '/', '_' ) + "_" + label ); await MainThread.Wait();
			Require( frame is not null && frame.Width > 0 && frame.Height > 0 && frame.Pixels.Length == frame.Width * frame.Height && frame.Sha256?.Length == 64 && !string.IsNullOrEmpty( frame.Path ) && !string.IsNullOrEmpty( frame.CameraStateJson ), "Capture lacks pixels/provenance." );
			frame = frame with { Pixels = (Color[])frame.Pixels.Clone() };
			frames.Add( new( label, frame.Path, frame.Sha256, frame.Width, frame.Height, frame.Coverage, frame.EngineFrame, frame.ObservedWallclockSeconds, frame.CameraStateJson ) ); return frame;
		}
		void Empty( NativeFrame frame ) => BoxTexturedImageChecks.RequireEmpty( frame.Pixels, frame.Width, frame.Height, frame.Path );
		void Positive( NativeFrame frame )
		{
			BoxTexturedImageChecks.RequirePositive( frame.Pixels, frame.Width, frame.Height, frame.Path );
			Require( !frame.Pixels.Any( pixel => pixel.r > .8f && pixel.g < .2f && pixel.b > .8f ), "Positive capture contains magenta target sentinel." );
		}
		void RemoveReference() { literal?.Dispose(); literal = null; using var scope = scene.Push(); scene.ProcessDeletes(); }
		void DestroyRenderer() { if ( renderer is null ) return; using var scope = scene.Push(); renderer.GameObject.Destroy(); scene.EditorTick( 0, 0 ); scene.ProcessDeletes(); renderer = null; }
		void Clean( Action cleanup, string label ) { try { cleanup(); } catch ( Exception error ) { failure += "\n" + label + " cleanup: " + error; } }
	}
	private static void Require( bool condition, string message ) => BoxTexturedCoreTests.Require( condition, message );
}
