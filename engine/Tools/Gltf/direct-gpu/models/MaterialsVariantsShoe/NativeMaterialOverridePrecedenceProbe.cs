using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;

using Capture = GltfGpuTriangleGateTests.NativeCapture;
using Frame = GltfGpuTriangleGateTests.NativeCapture.Frame;
using static MaterialsVariantsShoeReference;

/// <summary>
/// Tests the established native ModelRenderer component, using only cached
/// Model.Cube and two owned stock material copies. Never creates a Model/Mesh.
/// Combination winners are measured against single-override image controls.
/// </summary>
public static class NativeMaterialOverridePrecedenceProbe
{
	public sealed record Distance( string Reference, double MeanRgbError, double MaskIoU );
	public sealed record Classification( string Winner, Distance[] Distances );
	public sealed record Result( string Name, bool Passed, string Error );
	private const double MaximumMeanRgbError = .02;
	private const double MinimumMaskIoU = .995;
	private const double MinimumWinnerMargin = .03;

	public static Classification Classify( Frame actual, IReadOnlyDictionary<string, Frame> references )
	{
		Require( actual.Coverage > 64 && references.Count == 3, "Visible native cube and all three reference controls are required." );
		var distances = references.Select( pair => Measure( pair.Key, pair.Value, actual ) ).OrderBy( x => x.MeanRgbError ).ToArray();
		Require( distances[0].MeanRgbError <= MaximumMeanRgbError && distances[0].MaskIoU >= MinimumMaskIoU && distances[1].MeanRgbError - distances[0].MeanRgbError >= MinimumWinnerMargin, "Image does not uniquely match default/whole-A/slot-B control: " + JsonSerializer.Serialize( distances ) );
		return new Classification( distances[0].Reference, distances );
	}

	public static Result[] RunClassificationControls()
	{
		var refs = new Dictionary<string, Frame> { ["default"] = Synthetic( Color.White ), ["whole_A"] = Synthetic( new Color( .85f, .02f, .02f ) ), ["slot_B"] = Synthetic( new Color( .02f, .85f, .85f ) ) };
		var results = new List<Result>();
		foreach ( var pair in refs ) Run( "classifier_accepts_" + pair.Key, () => Require( Classify( pair.Value, refs ).Winner == pair.Key, "Wrong literal reference classification." ) );
		Run( "classifier_rejects_partial_override_image", () => { var pixels = refs["whole_A"].Pixels.ToArray(); Array.Copy( refs["slot_B"].Pixels, 128, pixels, 128, 128 ); Reject( () => Classify( refs["whole_A"] with { Pixels = pixels }, refs ) ); } );
		Run( "classifier_rejects_unmatched_camera", () => Reject( () => Classify( refs["whole_A"] with { CameraStateJson = "different camera" }, refs ) ) );
		Run( "classifier_rejects_empty_capture", () => Reject( () => Classify( Synthetic( Color.Black ) with { Coverage = 0 }, refs ) ) );
		Run( "classifier_rejects_wrong_target_extent", () => Reject( () => Classify( refs["whole_A"] with { Width = 32 }, refs ) ) );
		return results.ToArray();
		Frame Synthetic( Color color ) => new( Enumerable.Repeat( color, 256 ).ToArray(), 256, "synthetic-checker-only", new string( '0', 64 ), 16, 16, 1, 0, "literal-camera" );
		void Run( string name, Action body ) { try { body(); results.Add( new( name, true, null ) ); } catch ( Exception error ) { results.Add( new( name, false, error.ToString() ) ); } }
		void Reject( Action body ) { try { body(); } catch ( InvalidOperationException ) { return; } throw new Exception( "Invalid classification control was accepted." ); }
	}

	/// <summary>
	/// Coordinator entry. NativeCapture supplies the accepted Normal+postprocessing,
	/// owned-light, fresh-target, exact-size route and a visible widget. No shaders
	/// compile here; normal component setters, EditorTick and real frames run first.
	/// </summary>
	public static async Task<object> Run( string checkoutRoot )
	{
		await MainThread.Wait(); Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Run only in the authorized idle physical editor." );
		var output = Path.Combine( checkoutRoot, "Temp/gltf-validation/direct-gpu/native-material-precedence", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		var cube = Model.Cube; Require( cube is { IsValid: true, IsError: false } && !cube.native.IsNull && cube.Materials.Length == 1, "This bounded probe requires the existing cached native Model.Cube with exactly one material slot." );
		var original = cube.Materials[0]; var originalTint = original.GetVector4( "g_vColorTint" ); var groups = Enumerable.Range( 0, cube.MaterialGroupCount ).Select( cube.GetMaterialGroupName ).ToArray();
		var capture = new Capture( output, new Vector3( 140, 80, 60 ), Vector3.Zero, 90 );
		var materialA = capture.Material; Material materialB = null; ModelRenderer renderer = null;
		var references = new Dictionary<string, Frame>(); var rows = new List<object>(); var captures = new List<object>(); var cleanupErrors = new List<string>();
		string failure = null; var phase = "initialize";
		try
		{
			materialB = Material.Load( "materials/default/white.vmat" ).CreateCopy( "native_override_slot_B_" + Guid.NewGuid().ToString( "N" ) );
			Require( materialB is { IsValid: true, IsError: false }, "Second stock material copy unavailable." );
			materialA.Set( "g_vColorTint", new Color( .85f, .02f, .02f ) ); materialB.Set( "g_vColorTint", new Color( .02f, .85f, .85f ) ); materialB.SetFeature( "F_RENDER_BACKFACES", 0 );
			phase = "empty_before"; Empty( await Take( phase ) );
			foreach ( var name in new[] { "default", "whole_A", "slot_B" } )
			{
				Create(); using ( capture.Scene.Push() ) { if ( name == "whole_A" ) renderer.MaterialOverride = materialA; if ( name == "slot_B" ) renderer.Materials.SetOverride( 0, materialB ); Tick(); }
				phase = "reference_" + name; var image = await Take( phase ); Require( image.Coverage > 64, "Single-override native positive control is empty." ); references.Add( name, image ); Delete();
			}
			phase = "reference_deleted_empty"; Empty( await Take( phase ) );
			foreach ( var pair in references ) Require( Classify( pair.Value, references ).Winner == pair.Key, "Single-override controls are not uniquely distinguishable." );
			var matrix = new (string Name, Action<ModelRenderer> Apply)[]
			{
				("whole_A_then_slot_B", r => { r.MaterialOverride = materialA; r.Materials.SetOverride( 0, materialB ); }),
				("slot_B_then_whole_A", r => { r.Materials.SetOverride( 0, materialB ); r.MaterialOverride = materialA; }),
				("both_then_clear_slot", r => { r.MaterialOverride = materialA; r.Materials.SetOverride( 0, materialB ); r.Materials.SetOverride( 0, null ); }),
				("both_then_clear_whole", r => { r.MaterialOverride = materialA; r.Materials.SetOverride( 0, materialB ); r.MaterialOverride = null; }),
				("both_then_clear_all", r => { r.MaterialOverride = materialA; r.Materials.SetOverride( 0, materialB ); r.ClearMaterialOverrides(); }),
				("clear_all_then_component_reenable", r => { r.MaterialOverride = materialA; r.Materials.SetOverride( 0, materialB ); r.ClearMaterialOverrides(); Reenable( r ); }),
				("both_then_component_reenable", r => { r.MaterialOverride = materialA; r.Materials.SetOverride( 0, materialB ); Reenable( r ); }),
				("whole_only_then_clear_whole", r => { r.MaterialOverride = materialA; r.MaterialOverride = null; }),
				("slot_only_then_clear_slot", r => { r.Materials.SetOverride( 0, materialB ); r.Materials.SetOverride( 0, null ); }),
				("clear_whole_then_component_reenable", r => { r.Materials.SetOverride( 0, materialB ); r.MaterialOverride = materialA; r.MaterialOverride = null; Reenable( r ); })
			};
			foreach ( var entry in matrix )
			{
				Create(); using ( capture.Scene.Push() ) { entry.Apply( renderer ); Tick(); }
				phase = entry.Name; var image = await Take( phase ); var classification = Classify( image, references );
				rows.Add( new { name = entry.Name, classification, image.Path, image.Sha256, image.Coverage, image.EngineFrame, componentState = new { global = ReferenceEquals( renderer.MaterialOverride, materialA ) ? "A" : renderer.MaterialOverride is null ? "null" : "other", hasSlot = renderer.Materials.HasOverride( 0 ), slot = ReferenceEquals( renderer.Materials.GetOverride( 0 ), materialB ) ? "B" : renderer.Materials.GetOverride( 0 ) is null ? "null" : "other", renderer.HasMaterialGroups, renderer.MaterialGroup }, scope = "Effective native component pixels; property/slot getters are supplementary state only." } );
				Delete();
			}
			Create(); phase = "default_restored_after_matrix"; var restored = await Take( phase ); Require( Classify( restored, references ).Winner == "default", "Fresh native component did not restore the original vmdl material." ); Delete();
			phase = "deleted_empty_after"; Empty( await Take( phase ) );
			Require( cube.IsValid && !cube.native.IsNull && ReferenceEquals( Model.Cube, cube ) && ReferenceEquals( original, cube.Materials[0] ) && original.GetVector4( "g_vColorTint" ) == originalTint, "Probe mutated or destroyed the cached native model/original material." );
			phase = "complete";
		}
		catch ( Exception error ) { failure = error.ToString(); }
		finally
		{
			await MainThread.Wait(); try { Delete(); } catch ( Exception error ) { cleanupErrors.Add( error.ToString() ); }
			try { materialB?.Destroy(); } catch ( Exception error ) { cleanupErrors.Add( error.ToString() ); }
			try { capture.Dispose(); } catch ( Exception error ) { cleanupErrors.Add( error.ToString() ); }
		}
		var receipt = new { passed = failure is null && cleanupErrors.Count == 0 && rows.Count == 10, phase, failure, cleanupErrors, expectedMatrixRows = 10, rows, captures, materialGroups = groups,
			scope = "Native ModelRenderer override behavior for cached one-slot Model.Cube. No new native Model/Mesh, source glTF, managed backend, variant, tagged attribute or alternate-group parity claim.",
			unmeasured = new[] { "Tagged material overrides: cached cube's original user attributes are not mutated or invented.", "Alternate material-group precedence: this bounded one-slot cube does not supply an authored independent alternate group control." },
			thresholds = new { MaximumMeanRgbError, MinimumMaskIoU, MinimumWinnerMargin }, engineMvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId,
			sourceSha256 = Hash( File.ReadAllBytes( Path.Combine( checkoutRoot, "engine/Tools/Gltf/direct-gpu/models/MaterialsVariantsShoe/NativeMaterialOverridePrecedenceProbe.cs" ) ) ), captureHelperSha256 = Hash( File.ReadAllBytes( Path.Combine( checkoutRoot, "engine/Tools/Gltf/direct-gpu/GltfGpuTriangleGateTests.cs" ) ) ), output };
		File.WriteAllText( Path.Combine( output, "receipt.json" ), JsonSerializer.Serialize( receipt, new JsonSerializerOptions { WriteIndented = true } ) ); return receipt;
		void Create()
		{
			using var scope = capture.Scene.Push(); var go = capture.Scene.CreateObject(); var size = cube.Bounds.Size; var extent = MathF.Max( size.x, MathF.Max( size.y, size.z ) ); Require( float.IsFinite( extent ) && extent > 0, "Cube bounds are invalid." ); var scale = 32 / extent;
			go.WorldScale = Vector3.One * scale; go.WorldPosition = -cube.Bounds.Center * scale; renderer = go.Components.Create<ModelRenderer>( false ); renderer.Model = cube; renderer.Enabled = true; Tick();
			Require( renderer.SceneObject.IsValid() && renderer.SceneObject is not SceneCustomObject && renderer.SceneObject.Model is { IsValid: true } sceneModel && sceneModel.native.GetBindingPtr() == cube.native.GetBindingPtr() && renderer.Materials.Count == 1, "Ordinary native ModelRenderer lifecycle failed: valid=" + renderer.SceneObject.IsValid() + ", type=" + renderer.SceneObject?.GetType().FullName + ", model_matches=" + ReferenceEquals( renderer.SceneObject?.Model, cube ) + ", slots=" + renderer.Materials.Count );
		}
		void Tick() => capture.Scene.EditorTick( 0, 0 );
		void Reenable( ModelRenderer r ) { r.Enabled = false; Tick(); r.Enabled = true; Tick(); }
		void Delete() { if ( renderer is null ) return; using var scope = capture.Scene.Push(); renderer.GameObject.Destroy(); Tick(); capture.Scene.ProcessDeletes(); renderer = null; }
		async Task<Frame> Take( string name ) { using ( capture.Scene.Push() ) Tick(); await capture.WaitFrames(); var frame = await capture.Take( name ); Require( frame.Pixels.All( p => !(p.r > .8f && p.g < .2f && p.b > .8f) ), "Capture retained magenta initialization." ); captures.Add( new { name, frame.Path, frame.Sha256, frame.Width, frame.Height, frame.Coverage, frame.EngineFrame, frame.ObservedWallclockSeconds, frame.CameraStateJson } ); return frame; }
		void Empty( Frame frame ) => Require( frame.Coverage == 0, "Fresh empty native scene has retained geometry/pixels: " + frame.Path );
	}
	private static Distance Measure( string name, Frame expected, Frame actual )
	{
		Require( expected.Width == actual.Width && expected.Height == actual.Height && actual.Pixels.Length == actual.Width * actual.Height && expected.Pixels.Length == actual.Pixels.Length && expected.CameraStateJson == actual.CameraStateJson, "Reference and actual camera/target state differ." );
		double sum = 0; var union = 0; var intersection = 0;
		for ( var i = 0; i < actual.Pixels.Length; i++ )
		{
			var a = actual.Pixels[i]; var b = expected.Pixels[i]; Require( float.IsFinite( a.r ) && float.IsFinite( a.g ) && float.IsFinite( a.b ), "Nonfinite captured pixel." );
			var af = Capture.Foreground( a ); var bf = Capture.Foreground( b ); if ( af && bf ) intersection++; if ( !af && !bf ) continue; union++;
			sum += Math.Abs( a.r - b.r ) + Math.Abs( a.g - b.g ) + Math.Abs( a.b - b.b );
		}
		Require( union > 64, "Classification has no visible foreground." ); return new( name, sum / (union * 3), (double)intersection / union );
	}
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
