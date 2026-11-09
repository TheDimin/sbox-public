using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;
using NativeFrame = Sandbox.Gltf.DirectGpu.GltfGpuTriangleGateTests.NativeCapture.Frame;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

/// <summary>
/// Public Model.Load and ModelRenderer probe. This compiles only a test helper
/// against the actual staged Engine; no implementation source is included.
/// </summary>
internal static class TextureTransformPublicRegression
{
	public sealed record Result( bool Compiled, string ResourcePath, TextureTransformGpuChecks.Measurement DefaultUv,
		TextureTransformGpuChecks.Measurement RawNegative, TextureTransformGpuChecks.Measurement OverrideUv, TextureTransformGpuChecks.Measurement RestoredUv,
		BoxTexturedValidation.BoxTexturedImageChecks.ImageMeasurement ActualVsReference,
		BoxTexturedValidation.BoxTexturedImageChecks.ImageMeasurement MissingTransformVsReference,
		string ReferenceCapture, string MissingTransformCapture, string DefaultCapture, string OverrideCapture, string RestoredCapture, string EmptyCapture, bool Passed, string Error );

	public static async Task<Result> Run( string checkoutRoot, Scene scene, CameraComponent camera,
		Func<Task> waitFrames, Func<string, Task<NativeFrame>> capture, bool compiled )
	{
		ArgumentNullException.ThrowIfNull( scene ); ArgumentNullException.ThrowIfNull( camera );
		ArgumentNullException.ThrowIfNull( waitFrames ); ArgumentNullException.ThrowIfNull( capture );
		if ( !ReferenceEquals( scene, camera.Scene ) ) throw new InvalidOperationException( "The private scene and camera must match." );
		using var inputs = new TextureTransformReference.Inputs( checkoutRoot, compiled );
		using var mount = new PublicMount( inputs, compiled );
		ModelRenderer renderer = null; string resourcePath = null, referenceCapture = null, negativeCapture = null, defaultCapture = null, overrideCapture = null, restoredCapture = null, emptyCapture = null, failure = null;
		TextureTransformGpuChecks.Measurement actual = null, rawNegative = null, rawOverride = null, restored = null;
		BoxTexturedValidation.BoxTexturedImageChecks.ImageMeasurement actualImage = null, negativeImage = null;
		try
		{
			await MainThread.Wait();
			Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Requires the coordinator's idle editor and private scene." );
			camera.WorldPosition = new Vector3( 160, 0, 0 ); camera.WorldRotation = Rotation.LookAt( -camera.WorldPosition, Vector3.Up );
			camera.Orthographic = true; camera.OrthographicHeight = 150; camera.ZNear = 1; camera.ZFar = 1000;
			camera.CustomSize = new Vector2( 192, 192 ); camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All;
			camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
			var initialEmpty = await Frame( "empty_before" );
			BoxTexturedValidation.BoxTexturedImageChecks.RequireEmpty( initialEmpty.Pixels, initialEmpty.Width, initialEmpty.Height, initialEmpty.Path );
			var model = mount.Load();
			Require( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null, "Public Model.Load did not select the managed GPU path." );
			resourcePath = model.ResourcePath;
			using var referenceDocument = TextureTransformIdentityMaterialReference.CreateDocument( inputs, compiled );
			using var referenceAsset = GltfGpuAsset.Create( referenceDocument, GltfManagedModelResource.ComputeShaderPath );
			var identityMaterials = referenceAsset.GetMaterials();
			Material ReferenceMaterial( int? index )
			{
				var slot = referenceAsset.MaterialSlotSourceIndices.ToList().IndexOf( index );
				if ( slot < 0 ) throw new InvalidOperationException( "Independent reference material slot is missing." );
				return identityMaterials[slot];
			}
			NativeFrame expected;
			using ( var reference = new TextureTransformReferenceScene( scene.SceneWorld, NMatrix.Identity, ReferenceMaterial ) )
			{
				expected = await Frame( "independent_uv_reference" ); referenceCapture = expected.Path;
				Require( reference.DrawCalls > 0 && reference.CallbackErrors.Length == 0, "Independent transformed-UV reference did not draw cleanly." );
				BoxTexturedValidation.BoxTexturedImageChecks.RequirePositive( expected.Pixels, expected.Width, expected.Height, expected.Path );
			}
			var emptyAfterReference = await Frame( "empty_after_reference" );
			BoxTexturedValidation.BoxTexturedImageChecks.RequireEmpty( emptyAfterReference.Pixels, emptyAfterReference.Width, emptyAfterReference.Height, emptyAfterReference.Path );
			using ( var negative = new TextureTransformReferenceScene( scene.SceneWorld, NMatrix.Identity, ReferenceMaterial, omitTransforms: true ) )
			{
				var wrong = await Frame( "missing_transform_negative" ); negativeCapture = wrong.Path;
				Require( negative.DrawCalls > 0 && negative.CallbackErrors.Length == 0 && expected.CameraStateJson == wrong.CameraStateJson, "Missing-transform control did not draw with the reference camera." );
				negativeImage = BoxTexturedValidation.BoxTexturedImageChecks.Compare( expected.Pixels, wrong.Pixels, expected.Width, expected.Height );
				Require( negativeImage.DifferentRgbPixels > 64 && negativeImage.MeanAbsoluteRgbError > 0, "Missing-transform image control has no detectable effect." );
			}
			var emptyAfterNegative = await Frame( "empty_after_negative" );
			BoxTexturedValidation.BoxTexturedImageChecks.RequireEmpty( emptyAfterNegative.Pixels, emptyAfterNegative.Width, emptyAfterNegative.Height, emptyAfterNegative.Path );
			using ( scene.Push() )
			{
				renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
				renderer.WorldTransform = Transform.Zero; renderer.Model = model; renderer.Enabled = true; scene.EditorTick( 0, 0 );
			}
			var instance = renderer.GltfInstance;
			Require( instance is not null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null, "ModelRenderer did not use the public managed drawing path." );
			var defaultFrame = await Frame( "default" ); defaultCapture = defaultFrame.Path;
			BoxTexturedValidation.BoxTexturedImageChecks.RequirePositive( defaultFrame.Pixels, defaultFrame.Width, defaultFrame.Height, defaultFrame.Path );
			Require( expected.CameraStateJson == defaultFrame.CameraStateJson, "Public and reference capture cameras differ." );
			actualImage = BoxTexturedValidation.BoxTexturedImageChecks.Compare( expected.Pixels, defaultFrame.Pixels, expected.Width, expected.Height );
			Require( actualImage.MeanAbsoluteRgbError < negativeImage.MeanAbsoluteRgbError, "Public sampled image is no closer to the independent transformed-UV reference than the no-transform control." );
			actual = TextureTransformGpuChecks.MeasureCurrent( instance, NMatrix.Identity, TextureTransformUvRepresentation.BakedBaseColor );
			TextureTransformGpuChecks.RequireWithin( actual, 1e-4f, 1e-5f, 2e-6 );
			rawNegative = TextureTransformGpuChecks.MeasureCurrent( instance, NMatrix.Identity, TextureTransformUvRepresentation.RawSource );
			Require( rawNegative.MaximumUvError > .25, "Raw-source negative control did not distinguish the authored transforms." );
			// A native material override must see unbaked original UV0, not the
			// source material's transformed stream. Colour is irrelevant here.
			renderer.MaterialOverride = model.Materials[6];
			using ( scene.Push() ) scene.EditorTick( 0, 0 );
			var overrideFrame = await Frame( "native_override_raw_uv" ); overrideCapture = overrideFrame.Path;
			BoxTexturedValidation.BoxTexturedImageChecks.RequirePositive( overrideFrame.Pixels, overrideFrame.Width, overrideFrame.Height, overrideFrame.Path );
			rawOverride = TextureTransformGpuChecks.MeasureCurrent( instance, NMatrix.Identity, TextureTransformUvRepresentation.RawSource );
			TextureTransformGpuChecks.RequireWithin( rawOverride, 1e-4f, 1e-5f, 0 );
			renderer.MaterialOverride = null;
			using ( scene.Push() ) scene.EditorTick( 0, 0 );
			var restoredFrame = await Frame( "source_material_restored" ); restoredCapture = restoredFrame.Path;
			BoxTexturedValidation.BoxTexturedImageChecks.RequirePositive( restoredFrame.Pixels, restoredFrame.Width, restoredFrame.Height, restoredFrame.Path );
			restored = TextureTransformGpuChecks.MeasureCurrent( instance, NMatrix.Identity, TextureTransformUvRepresentation.BakedBaseColor );
			TextureTransformGpuChecks.RequireWithin( restored, 1e-4f, 1e-5f, 2e-6 );
			using ( scene.Push() ) { renderer.GameObject.Destroy(); scene.EditorTick( 0, 0 ); scene.ProcessDeletes(); renderer = null; }
			var calls = instance.DrawCalls; var deletedFrame = await Frame( "deleted" ); emptyCapture = deletedFrame.Path;
			BoxTexturedValidation.BoxTexturedImageChecks.RequireEmpty( deletedFrame.Pixels, deletedFrame.Width, deletedFrame.Height, deletedFrame.Path );
			Require( instance.DrawCalls == calls, "Deleted ModelRenderer continued submitting draws." );
		}
		catch ( Exception error ) { failure = error.ToString(); }
		finally
		{
			await MainThread.Wait();
			if ( renderer is not null ) using ( scene.Push() ) { renderer.GameObject.Destroy(); scene.EditorTick( 0, 0 ); scene.ProcessDeletes(); }
		}
		return new( compiled, resourcePath, actual, rawNegative, rawOverride, restored, actualImage, negativeImage,
			referenceCapture, negativeCapture, defaultCapture, overrideCapture, restoredCapture, emptyCapture, failure is null, failure );

		async Task<NativeFrame> Frame( string name )
		{
			await waitFrames(); await MainThread.Wait();
			var frame = await capture( "TextureTransform_public_" + (compiled ? "compiled_" : "source_") + name ); await MainThread.Wait();
			Require( frame is not null && frame.Width > 0 && frame.Height > 0 && frame.Pixels.Length == frame.Width * frame.Height && !string.IsNullOrEmpty( frame.Path ), "Capture is incomplete." );
			return frame with { Pixels = (Color[])frame.Pixels.Clone() };
		}
	}

	private sealed class PublicMount : IDisposable
	{
		private readonly string directory;
		private readonly BaseFileSystem aggregate;
		private readonly string path;
		private LocalFileSystem mount;
		private Model model;
		internal PublicMount( TextureTransformReference.Inputs inputs, bool compiled )
		{
			directory = Path.Combine( Path.GetTempPath(), "gltf_transform_" + Guid.NewGuid().ToString( "N" ) );
			try
			{
				Directory.CreateDirectory( directory ); path = "texture_transform_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				File.WriteAllBytes( Path.Combine( directory, path ), inputs.Bytes );
				foreach ( var (name, bytes) in inputs.Dependencies )
					if ( name != "TextureTransformTest.gltf" ) File.WriteAllBytes( Path.Combine( directory, name ), bytes );
				aggregate = FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
				mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
			}
			catch { Dispose(); throw; }
		}
		internal Model Load() => model = Model.Load( path );
		public void Dispose()
		{
			var errors = new List<Exception>();
			void Clean( Action action ) { try { action(); } catch ( Exception error ) { errors.Add( error ); } }
			if ( model is not null && model.ResourcePath == Resource.FixPath( path ) ) Clean( model.Destroy ); model = null;
			if ( mount is not null ) { Clean( () => aggregate.UnMount( mount ) ); Clean( mount.Dispose ); mount = null; }
			Clean( () => { if ( Directory.Exists( directory ) ) Directory.Delete( directory, true ); } );
			if ( errors.Count != 0 ) throw new AggregateException( "Public texture-transform probe cleanup failed.", errors );
		}
	}
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
