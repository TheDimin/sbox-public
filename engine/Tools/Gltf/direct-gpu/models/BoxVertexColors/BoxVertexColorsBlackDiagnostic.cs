using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

using Frame = BoxVertexColorsImageProbe.Frame;

/// <summary>
/// Bounded diagnosis of a public ModelRenderer already placed in a private
/// scene with the recorded BoxVertexColors camera and lighting. The caller
/// owns the renderer, camera, frame pump, white texture and all capture files.
/// This helper temporarily assigns only copies of the observed source material.
/// </summary>
internal static class BoxVertexColorsBlackDiagnostic
{
	public sealed record TextureState( string Parameter, bool Declared, bool Valid, int? TextureIndex );
	public sealed record MaterialState( string Shader, int LinearColorFeature, int MetalnessTextureFeature, float[] ColorTint,
		float[] ModelTintAmount, float[] Metalness, float[] RoughnessScale, float[] RendererTint, float[] InstanceTint, TextureState[] Textures );
	public sealed record FrameDelta( int ChangedPixels, int MaximumRgbError, double MeanRgbError );
	public sealed record Result( string Scope, MaterialState Material, MaterialState MetalMaterial, MaterialState WhiteMaterial, MaterialState BothMaterial, int WhiteTextureIndex, BoxVertexColorsGpuChecks.Measurement Gpu,
		bool ColorCheckPassed, string ColorCheckError, Frame Baseline,
		Frame MetalZero, Frame WhiteTexture,
		Frame MetalZeroWhiteTexture, Frame Restored,
		FrameDelta MetalDelta, FrameDelta WhiteDelta, FrameDelta BothDelta, FrameDelta RestoreDelta );

	/// <summary>
	/// Five completed frames: baseline, metallic=0, neutral white texture,
	/// both controls, restored baseline. No diagnostic frame is a source-material
	/// pass. Await callback must pump real native frames and return on main thread.
	/// </summary>
	public static async Task<Result> RunWithRenderer( string checkoutRoot, ModelRenderer renderer, Texture white,
		Func<Task<Frame>> captureCompletedNativeFrame )
	{
		ArgumentNullException.ThrowIfNull( renderer ); ArgumentNullException.ThrowIfNull( white ); ArgumentNullException.ThrowIfNull( captureCompletedNativeFrame );
		if ( !Application.IsEditor || Graphics.IsActive || renderer.MaterialOverride is not null || !white.IsValid || white.Width != 1 || white.Height != 1 || white.Depth != 1 )
			throw new InvalidOperationException( "Requires an isolated public renderer, no preexisting override, a valid white texture and idle main thread." );
		ThreadSafe.AssertIsMainThread();
		// Guard the white control with actual GPU bytes; no synthetic parameter
		// write alone establishes that the bound texture is neutral.
		Graphics.FlushGPU(); var whitePixels = white.GetPixels();
		if ( whitePixels.Length is < 1 or > 4096 || whitePixels.Any( p => p.r != 255 || p.g != 255 || p.b != 255 || p.a != 255 ) )
			throw new InvalidOperationException( "White control texture is not a bounded actual all-white GPU image." );
		var reference = new BoxVertexColorsReference( checkoutRoot );
		var form = reference.Forms.First( x => x.Name == "glTF/source" );
		var initialInstance = renderer.GltfInstance ?? throw new InvalidOperationException( "The public ModelRenderer has no realized glTF instance." );
		var baseline = await captureCompletedNativeFrame(); Validate( baseline );
		var instance = renderer.GltfInstance ?? throw new InvalidOperationException( "Public instance disappeared." );
		if ( !ReferenceEquals( instance, initialInstance ) || instance.DrawCalls == 0 || instance.SubmittedRevision != instance.PoseRevision || instance.CallbackErrors.Length != 0 )
			throw new InvalidOperationException( "Baseline glTF instance did not submit cleanly." );
		if ( instance.Placement != System.Numerics.Matrix4x4.Identity ) throw new InvalidOperationException( "The literal GPU position oracle requires identity instance placement." );
		var rows = instance.Readback(); var row = rows.Single();
		var actual = row.SubmittedMaterial ?? throw new InvalidOperationException( "Submitted source material was not recorded." );
		if ( !actual.IsValid || actual.IsError || !actual.ShaderName.Contains( "gltf_stock_sampler", StringComparison.OrdinalIgnoreCase ) || actual.GetFeature( "F_GLTF_LINEAR_VERTEX_COLOR" ) != 1 )
			throw new InvalidOperationException( "Public baseline did not submit the expected linear vertex-color material." );
		var material = Observe( actual, renderer, instance );
		var gpu = BoxVertexColorsGpuChecks.Measure( reference, form, rows, System.Numerics.Matrix4x4.Identity );
		var gpuPassed = true; string gpuError = null;
		try { BoxVertexColorsGpuChecks.RequireWithin( gpu ); } catch ( Exception error ) { gpuPassed = false; gpuError = error.Message; }
		Material metal = null, whiteOnly = null, both = null;
		try
		{
			metal = actual.CreateCopy(); whiteOnly = actual.CreateCopy(); both = actual.CreateCopy();
			if ( metal is not { IsValid: true, IsError: false } || whiteOnly is not { IsValid: true, IsError: false } || both is not { IsValid: true, IsError: false } )
				throw new InvalidOperationException( "Could not create isolated material controls." );
			metal.Set( "g_flMetalness", 0f ); both.Set( "g_flMetalness", 0f );
			foreach ( var name in new[] { "g_tColor", "g_tColor1", "g_tColor2" } ) { whiteOnly.Set( name, white ); both.Set( name, white ); }
			if ( metal.GetVector4( "g_flMetalness" ).x != 0 || both.GetVector4( "g_flMetalness" ).x != 0 )
				throw new InvalidOperationException( "Metalness controls did not retain zero." );
			var metalFrame = await Capture( metal );
			var whiteFrame = await Capture( whiteOnly );
			var bothFrame = await Capture( both );
			var restored = await Capture( null );
			return new( "public_ModelRenderer; actual_COLOR0_GPU_readback; source_material_metadata; diagnostic_overrides_not_source_material_acceptance",
				material, Observe( metal, renderer, instance ), Observe( whiteOnly, renderer, instance ), Observe( both, renderer, instance ), white.Index, gpu, gpuPassed, gpuError, baseline, metalFrame, whiteFrame, bothFrame, restored,
				Difference( baseline, metalFrame ), Difference( baseline, whiteFrame ), Difference( baseline, bothFrame ), Difference( baseline, restored ) );

			async Task<Frame> Capture( Material selected )
			{
				renderer.MaterialOverride = selected; var startingFrame = Application.FrameCount;
				var frame = await captureCompletedNativeFrame(); Validate( frame );
				var current = renderer.GltfInstance ?? throw new InvalidOperationException( "Public instance disappeared during control." );
				if ( !ReferenceEquals( current, initialInstance ) || current.CallbackErrors.Length != 0 ) throw new InvalidOperationException( "Public instance changed or reported a callback error." );
				var submitted = current.Readback().Single(); var expected = selected ?? actual;
				if ( !ReferenceEquals( submitted.SubmittedMaterial, expected ) || submitted.SubmittedMaterialOverride != (selected is not null) || submitted.SubmittedMaterialFrame < startingFrame )
					throw new InvalidOperationException( "Control capture did not submit the requested fresh effective material." );
				return frame;
			}
		}
		finally
		{
			renderer.MaterialOverride = null;
			metal?.Destroy(); whiteOnly?.Destroy(); both?.Destroy();
		}
	}

	/// <summary>
	/// Self-contained audited-runner entrypoint. Uses the existing native capture
	/// helper, a task-owned temporary public filesystem mount and the recorded
	/// BoxVertexColors view. NativeCapture is 192px square instead of the original
	/// 512px; no capture is published or substituted for the corpus recording.
	/// </summary>
	public static Task<object[]> Run( string checkoutRoot ) => RunCore( checkoutRoot, false, false );
	public static Task<object[]> RunRoughness( string checkoutRoot ) => RunCore( checkoutRoot, true, false );
	public static Task<object[]> RunRegression( string checkoutRoot ) => RunCore( checkoutRoot, true, true );
	private static async Task<object[]> RunCore( string checkoutRoot, bool checkRoughness, bool regression )
	{
		await MainThread.Wait();
		if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Requires an idle editor main thread." );
		var reference = new BoxVertexColorsReference( checkoutRoot );
		var form = reference.Forms.First( x => x.Name == "glTF/source" );
		var output = Path.Combine( checkoutRoot, "Temp/gltf-validation/direct-gpu/black-box-diagnostic", Guid.NewGuid().ToString( "N" ) );
		var directory = Path.Combine( Path.GetTempPath(), "gltf_box_vertex_colors_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output ); Directory.CreateDirectory( directory );
		var resourcePath = "box_vertex_colors_" + Guid.NewGuid().ToString( "N" ) + ".gltf";
		File.WriteAllBytes( Path.Combine( directory, resourcePath ), form.Bytes ); File.WriteAllBytes( Path.Combine( directory, "buffer.bin" ), form.Resolve( "buffer.bin" ) );
		var aggregate = FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
		LocalFileSystem mount = null; GltfGpuTriangleGateTests.NativeCapture capture = null;
		Model model = null; ModelRenderer renderer = null; Texture white = null; SceneDirectionalLight directional = null; SceneObject positiveObject = null;
		try
		{
			mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
			capture = new GltfGpuTriangleGateTests.NativeCapture( output );
			var camera = capture.Camera; camera.WorldPosition = new Vector3( 85.514946f, 85.514946f, 62.474476f );
			camera.WorldRotation = Rotation.LookAt( new Vector3( 19.68504f, 19.68504f, 19.68504f ) - camera.WorldPosition, Vector3.Up );
			camera.Orthographic = false; camera.FovAxis = CameraComponent.Axis.Vertical; camera.FieldOfView = 45; camera.ZNear = .01f; camera.ZFar = 1000;
			camera.BackgroundColor = new Color( .35f, .35f, .35f, 1 ); camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
			camera.AutoExposure.Enabled = true; camera.AutoExposure.MinimumExposure = 1; camera.AutoExposure.MaximumExposure = 1; camera.AutoExposure.Compensation = 0;
			var point = typeof( GltfGpuTriangleGateTests.NativeCapture ).GetField( "light", BindingFlags.Instance | BindingFlags.NonPublic )?.GetValue( capture ) as ScenePointLight;
			if ( point is null || !point.IsValid() ) throw new InvalidOperationException( "Owned capture point light could not be neutralized." );
			point.LightColor = Color.Black;
			var direction = new Vector3( -.6424926f, -.6424926f, -.41762018f ).Normal;
			directional = new SceneDirectionalLight( capture.Scene.SceneWorld, Rotation.LookAt( direction, Vector3.Up ), Color.White * 2 );
			var empty = await capture.Take( "empty_before" );
			if ( Model.Sphere is not { IsValid: true, IsError: false } ) throw new InvalidOperationException( "Independent stock-sphere control is unavailable." );
			positiveObject = new SceneObject( capture.Scene.SceneWorld, Model.Sphere, new Transform( new Vector3( 19.68504f, 19.68504f, 19.68504f ), Rotation.Identity, 1 ) );
			positiveObject.SetMaterialOverride( capture.Material );
			var positive = await capture.Take( "positive_stock_sphere" );
			positiveObject.Delete(); positiveObject = null; using ( capture.Scene.Push() ) capture.Scene.ProcessDeletes();
			var emptyAfterPositive = await capture.Take( "empty_after_positive" );
			var positiveDifference = Difference( ConvertFrame( empty ), ConvertFrame( positive ) );
			if ( positiveDifference.ChangedPixels < 256 || Difference( ConvertFrame( empty ), ConvertFrame( emptyAfterPositive ) ).ChangedPixels > 16 )
				throw new InvalidOperationException( "Fresh native positive/deletion controls are not reliable." );
			model = Model.Load( resourcePath );
			if ( model is not { IsValid: true, IsError: false } || model.GltfResource is null || !model.native.IsNull ) throw new InvalidOperationException( "Public Model.Load did not realize the managed glTF source." );
			using ( capture.Scene.Push() )
			{
				renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>( false ); renderer.WorldTransform = Transform.Zero; renderer.Model = model; renderer.Enabled = true;
				capture.Scene.EditorTick( 0, 0 );
			}
			await capture.WaitFrames();
			if ( renderer.GltfInstance is null || !ReferenceEquals( renderer.SceneObject, renderer.GltfInstance.SceneObject ) ) throw new InvalidOperationException( "Public renderer did not create the managed direct-GPU instance." );
			white = Texture.Create( 1, 1, ImageFormat.RGBA8888 ).WithMips( 1 ).WithData( new byte[] { 255, 255, 255, 255 } ).WithStaticUsage().Finish();
			var labels = new[] { "public_baseline", "metal_zero", "white_texture", "metal_zero_white_texture", "public_restored" }; var index = 0;
			var result = await RunWithRenderer( checkoutRoot, renderer, white, async () => ConvertFrame( await capture.Take( labels[index++] ) ) );
			if ( index != labels.Length ) throw new InvalidOperationException( "Expected five diagnostic frames." );
			object roughness = null, aliases = null; var roughnessValid = true; var regressionValid = true;
			var chromaticPixels = 0;
			for ( var i = 0; i < result.MetalZero.Rgba.Length; i += 4 )
			{
				var r = result.MetalZero.Rgba[i]; var g = result.MetalZero.Rgba[i+1]; var b = result.MetalZero.Rgba[i+2];
				if ( Math.Max( r, Math.Max( g, b ) ) - Math.Min( r, Math.Min( g, b ) ) > 12 ) chromaticPixels++;
			}
			if ( regression ) regressionValid = chromaticPixels >= 256 && result.BothDelta.ChangedPixels >= 256 && Difference( result.MetalZero, result.MetalZeroWhiteTexture ).ChangedPixels <= 16;
			if ( checkRoughness ) { aliases = await CaptureAliases(); roughness = await CaptureRoughness(); }
			using ( capture.Scene.Push() ) { renderer.GameObject.Destroy(); capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); renderer = null; }
			var deleted = await capture.Take( "empty_after_deletion" );
			var emptyDifference = Difference( ConvertFrame( empty ), ConvertFrame( deleted ) );
			if ( emptyDifference.ChangedPixels > 16 ) throw new InvalidOperationException( "Deleted public renderer did not restore the fresh empty scene." );
			object vmdlControl = null;
			if ( regression ) vmdlControl = await CaptureDefaultVmdl();
			return new object[] { new { Passed = result.ColorCheckPassed && roughnessValid && regressionValid, result.ColorCheckError, result.Material, result.MetalMaterial, result.WhiteMaterial, result.BothMaterial, result.WhiteTextureIndex, result.Gpu,
				baseline = FrameInfo( result.Baseline ), metalZero = FrameInfo( result.MetalZero ), whiteTexture = FrameInfo( result.WhiteTexture ), both = FrameInfo( result.MetalZeroWhiteTexture ), restored = FrameInfo( result.Restored ),
				result.MetalDelta, result.WhiteDelta, result.BothDelta, result.RestoreDelta, chromaticPixels, regressionValid, aliases, roughness, vmdlControl,
				emptyBefore = FrameInfo( ConvertFrame( empty ) ), positive = FrameInfo( ConvertFrame( positive ) ), emptyAfterPositive = FrameInfo( ConvertFrame( emptyAfterPositive ) ), positiveDifference, emptyAfter = FrameInfo( ConvertFrame( deleted ) ), emptyDifference, resourcePath, output,
				view = new { position = camera.WorldPosition.ToString(), rotation = camera.WorldRotation.ToString(), target = "19.68504 19.68504 19.68504", camera.FieldOfView, camera.FovAxis, camera.Orthographic, camera.ZNear, camera.ZFar, background = camera.BackgroundColor.ToString(), camera.DebugMode, camera.EnablePostProcessing,
					exposureEnabled = camera.AutoExposure.Enabled, minimumExposure = camera.AutoExposure.MinimumExposure, maximumExposure = camera.AutoExposure.MaximumExposure, exposureCompensation = camera.AutoExposure.Compensation, directionalForward = direction.ToString(), directionalColor = directional.LightColor.ToString(), pointColor = point.LightColor.ToString() },
				engineMvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId, gltfMvid = typeof( GltfReader ).Assembly.ManifestModule.ModuleVersionId,
				scope = regression ? "Twenty-two native frames: glTF color/roughness controls, default-off material controls, and native vmdl controls with final empty scene; 192px, not the 512px corpus capture. Chromaticity is a positive rendering gate, not full pixel parity." : checkRoughness ? "Seventeen native frames: original nine controls, three isolated color-alias controls and five roughness-view material controls; 192px, not the 512px corpus capture. No shared material or shader mutation." : "Nine 192px native frames: empty, independent stock-sphere positive, deleted-empty, five public-model diagnostic controls, deleted-empty. Same recorded perspective, target, directional lighting and background; diagnostic resolution differs from 512px corpus capture. No shader or shared material mutation." } };

			async Task<object> CaptureDefaultVmdl()
			{
				Texture zero = null, one = null; Material m0 = null, m1 = null; SceneObject sphere = null;
				try
				{
					var template = Material.Load( "materials/default/vertex_color.vmat" );
					if ( template is not { IsValid: true, IsError: false } || template.GetFeature( "F_GLTF_LINEAR_VERTEX_COLOR" ) != 0 || template.GetFeature( "F_GLTF_METALLIC_TEXTURE" ) != 0 ) throw new InvalidOperationException( "Default vertex-color vmat glTF features are not disabled." );
					m0 = template.CreateCopy(); m1 = template.CreateCopy();
					if ( m0 is not { IsValid: true, IsError: false } || m1 is not { IsValid: true, IsError: false } ) throw new InvalidOperationException( "Default vmdl material copies are unavailable." );
					zero = Texture.Create( 1, 1, ImageFormat.RGBA8888 ).WithMips( 1 ).WithData( new byte[] { 128, 128, 0, 255 } ).WithStaticUsage().Finish();
					one = Texture.Create( 1, 1, ImageFormat.RGBA8888 ).WithMips( 1 ).WithData( new byte[] { 128, 128, 255, 255 } ).WithStaticUsage().Finish();
					Graphics.FlushGPU(); if ( zero.GetPixels().Single().b != 0 || one.GetPixels().Single().b != 255 ) throw new InvalidOperationException( "Default vmdl endpoint textures differ on GPU readback." );
					m0.Set( "g_tNormal", zero ); m1.Set( "g_tNormal", one ); camera.DebugMode = SceneCameraDebugMode.Roughness;
					sphere = new SceneObject( capture.Scene.SceneWorld, Model.Sphere, new Transform( new Vector3( 19.68504f, 19.68504f, 19.68504f ), Rotation.Identity, 1 ) );
					sphere.SetMaterialOverride( m0 ); var a = ConvertFrame( await capture.Take( "vmdl_default_off_zero" ) );
					sphere.SetMaterialOverride( m1 ); var b = ConvertFrame( await capture.Take( "vmdl_default_off_one" ) );
					var sensitivity = Difference( a, b ); var positive = Difference( ConvertFrame( empty ), a );
					sphere.Delete(); sphere = null; using ( capture.Scene.Push() ) capture.Scene.ProcessDeletes(); camera.DebugMode = SceneCameraDebugMode.Normal;
					var after = ConvertFrame( await capture.Take( "vmdl_deleted_empty" ) ); var cleanup = Difference( ConvertFrame( empty ), after );
					regressionValid &= positive.ChangedPixels >= 256 && sensitivity.ChangedPixels <= 16 && cleanup.ChangedPixels <= 16;
					return new { model = "models/dev/sphere.vmdl", material = "materials/default/vertex_color.vmat", zero = FrameInfo( a ), one = FrameInfo( b ), deleted = FrameInfo( after ), positive, sensitivity, cleanup,
						scope = "Native vmdl with a task-owned copy of the default-off stock vertex-color vmat; endpoint variation must not enable glTF normal mapping." };
				}
				finally { sphere?.Delete(); camera.DebugMode = SceneCameraDebugMode.Normal; m0?.Destroy(); m1?.Destroy(); zero?.Dispose(); one?.Dispose(); }
			}

			async Task<object> CaptureAliases()
			{
				var instance = renderer.GltfInstance; var source = instance.Readback().Single().SubmittedMaterial;
				var names = new[] { "g_tColor", "g_tColor1", "g_tColor2" }; var copies = new List<Material>(); var observations = new List<object>();
				try
				{
					foreach ( var name in names )
					{
						var copy = source.CreateCopy(); copies.Add( copy );
						if ( copy is not { IsValid: true, IsError: false } ) throw new InvalidOperationException( "Alias control material copy failed." );
						copy.Set( "g_flMetalness", 0f ); copy.Set( name, white );
						var bound = copy.GetTexture( name );
						if ( bound is not { IsValid: true } || bound.Index != white.Index || copy.GetVector4( "g_flMetalness" ).x != 0 ) throw new InvalidOperationException( "Alias control did not retain its isolated binding." );
						renderer.MaterialOverride = copy; var start = Application.FrameCount;
						var frame = ConvertFrame( await capture.Take( "alias_" + name ) ); var submitted = instance.Readback().Single();
						if ( !ReferenceEquals( submitted.SubmittedMaterial, copy ) || !submitted.SubmittedMaterialOverride || submitted.SubmittedMaterialFrame < start ) throw new InvalidOperationException( "Alias control did not submit its fresh material." );
						observations.Add( new { name, material = Observe( copy, renderer, instance ), frame = FrameInfo( frame ), versusMetalZero = Difference( result.MetalZero, frame ), versusAllAliases = Difference( result.MetalZeroWhiteTexture, frame ) } );
					}
					return new { observations, scope = "Each copy binds white to exactly one color parameter at metallic zero; compare against the unbound and all-three-alias controls." };
				}
				finally { renderer.MaterialOverride = null; foreach ( var copy in copies ) copy?.Destroy(); }
			}

			async Task<object> CaptureRoughness()
			{
				Texture zero = null, one = null; Material sourceZero = null, sourceOne = null, stockZero = null, stockOne = null, defaultZero = null, defaultOne = null;
				try
				{
					await capture.WaitFrames();
					var instance = renderer.GltfInstance; var restoredRow = instance.Readback().Single();
					if ( restoredRow.SubmittedMaterialOverride || restoredRow.SubmittedMaterial is not { IsValid: true, IsError: false } ) throw new InvalidOperationException( "Source material was not restored after alias controls." );
					var source = restoredRow.SubmittedMaterial;
					zero = Texture.Create( 1, 1, ImageFormat.RGBA8888 ).WithMips( 1 ).WithData( new byte[] { 128, 128, 0, 255 } ).WithStaticUsage().Finish();
					one = Texture.Create( 1, 1, ImageFormat.RGBA8888 ).WithMips( 1 ).WithData( new byte[] { 128, 128, 255, 255 } ).WithStaticUsage().Finish();
					Graphics.FlushGPU();
					if ( zero.GetPixels().Single().b != 0 || one.GetPixels().Single().b != 255 ) throw new InvalidOperationException( "Roughness control texture GPU readback differs." );
					sourceZero = source.CreateCopy(); sourceOne = source.CreateCopy(); stockZero = capture.Material.CreateCopy(); stockOne = capture.Material.CreateCopy();
					var materials = new[] { sourceZero, sourceOne, stockZero, stockOne };
					if ( materials.Any( m => m is not { IsValid: true, IsError: false } ) ) throw new InvalidOperationException( "Roughness control material copies failed." );
					sourceZero.Set( "g_tNormal", zero ); sourceOne.Set( "g_tNormal", one ); stockZero.Set( "g_tNormal", zero ); stockOne.Set( "g_tNormal", one );
					foreach ( var m in materials ) { m.Set( "g_flRoughnessScaleFactor", 1f ); m.Set( "g_flMetalness", 0f ); }
					camera.DebugMode = SceneCameraDebugMode.Roughness;
					var baseline = await Take( null, "roughness_source_baseline" );
					var s0 = await Take( sourceZero, "roughness_source_zero" ); var s1 = await Take( sourceOne, "roughness_source_one" );
					var c0 = await Take( stockZero, "roughness_stock_zero" ); var c1 = await Take( stockOne, "roughness_stock_one" );
					var stockDelta = Difference( c0, c1 ); var sourceDelta = Difference( s0, s1 );
					roughnessValid = stockDelta.ChangedPixels >= 256 && stockDelta.MeanRgbError >= 10;
					object defaultOff = null;
					if ( regression )
					{
						var defaultTemplate = Material.Load( "materials/default/vertex_color.vmat" );
						if ( defaultTemplate is not { IsValid: true, IsError: false } || defaultTemplate.GetFeature( "F_GLTF_LINEAR_VERTEX_COLOR" ) != 0 || defaultTemplate.GetFeature( "F_GLTF_METALLIC_TEXTURE" ) != 0 ) throw new InvalidOperationException( "Default shader features are not disabled." );
						defaultZero = defaultTemplate.CreateCopy(); defaultOne = defaultTemplate.CreateCopy();
						if ( defaultZero is not { IsValid: true, IsError: false } || defaultOne is not { IsValid: true, IsError: false } ) throw new InvalidOperationException( "Default-off control copies are unavailable." );
						defaultZero.Set( "g_tNormal", zero ); defaultOne.Set( "g_tNormal", one );
						var d0 = await Take( defaultZero, "roughness_default_off_zero" ); var d1 = await Take( defaultOne, "roughness_default_off_one" );
						var difference = Difference( d0, d1 ); var baselineVsOne = Difference( baseline, s1 );
						regressionValid &= sourceDelta.ChangedPixels >= 256 && sourceDelta.MeanRgbError >= 10 && baselineVsOne.ChangedPixels <= 16 && difference.ChangedPixels <= 16;
						defaultOff = new { zero = FrameInfo( d0 ), one = FrameInfo( d1 ), difference, baselineVsOne, scope = "Shader feature defaults remain off; roughness blue endpoints have no effect under the legacy shader variant." };
					}
					return new { baseline = FrameInfo( baseline ), sourceZero = FrameInfo( s0 ), sourceOne = FrameInfo( s1 ), stockZero = FrameInfo( c0 ), stockOne = FrameInfo( c1 ), stockDelta, sourceDelta,
						stockSensitivityPassed = roughnessValid, defaultOff,
						baselineForegroundMean = ForegroundMean( baseline ), sourceZeroForegroundMean = ForegroundMean( s0 ), sourceOneForegroundMean = ForegroundMean( s1 ), stockZeroForegroundMean = ForegroundMean( c0 ), stockOneForegroundMean = ForegroundMean( c1 ), zeroTextureIndex = zero.Index, oneTextureIndex = one.Index,
						scope = "Actual native roughness debug view with verified GPU control texture blue endpoints and positive stock complex shader sensitivity; foreground uses original public silhouette. Read control colors and variant behavior before attributing a cause." };

					async Task<Frame> Take( Material selected, string name )
					{
						renderer.MaterialOverride = selected; var start = Application.FrameCount; var frame = ConvertFrame( await capture.Take( name ) ); var submitted = instance.Readback().Single();
						if ( !ReferenceEquals( submitted.SubmittedMaterial, selected ?? source ) || submitted.SubmittedMaterialOverride != (selected is not null) || submitted.SubmittedMaterialFrame < start ) throw new InvalidOperationException( "Roughness control did not submit the expected fresh material." );
						return frame;
					}
					double ForegroundMean( Frame frame )
					{
						var bg = ConvertFrame( empty ); var original = result.Baseline; long total = 0; var count = 0;
						for ( var i = 0; i < frame.Rgba.Length; i += 4 ) if ( Math.Abs( original.Rgba[i] - bg.Rgba[i] ) > 4 || Math.Abs( original.Rgba[i+1] - bg.Rgba[i+1] ) > 4 || Math.Abs( original.Rgba[i+2] - bg.Rgba[i+2] ) > 4 ) { total += frame.Rgba[i] + frame.Rgba[i+1] + frame.Rgba[i+2]; count += 3; }
						if ( count == 0 ) throw new InvalidOperationException( "Baseline silhouette is empty." ); return total / (double)count;
					}
				}
				finally
				{
					renderer.MaterialOverride = null; camera.DebugMode = SceneCameraDebugMode.Normal;
					sourceZero?.Destroy(); sourceOne?.Destroy(); stockZero?.Destroy(); stockOne?.Destroy(); defaultZero?.Destroy(); defaultOne?.Destroy(); zero?.Dispose(); one?.Dispose();
				}
			}
		}
		finally
		{
			await MainThread.Wait();
			if ( renderer is not null ) { renderer.MaterialOverride = null; using ( capture.Scene.Push() ) { renderer.GameObject.Destroy(); capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); } }
			positiveObject?.Delete(); white?.Dispose(); directional?.Delete(); capture?.Dispose(); model?.Destroy();
			if ( mount is not null ) { aggregate.UnMount( mount ); mount.Dispose(); }
			if ( Directory.Exists( directory ) ) Directory.Delete( directory, true );
		}

		static Frame ConvertFrame( GltfGpuTriangleGateTests.NativeCapture.Frame source )
		{
			var rgba = new byte[checked(source.Width * source.Height * 4)];
			for ( var i = 0; i < source.Pixels.Length; i++ )
			{
				Color32 p = source.Pixels[i]; rgba[i*4] = p.r; rgba[i*4+1] = p.g; rgba[i*4+2] = p.b; rgba[i*4+3] = p.a;
			}
			return new( source.Width, source.Height, rgba, source.Path, source.ObservedWallclockSeconds,
				new Dictionary<string, string> { ["sha256"] = source.Sha256, ["camera"] = source.CameraStateJson, ["engineFrame"] = source.EngineFrame.ToString() } );
		}
		static object FrameInfo( Frame frame ) => new { frame.Width, frame.Height, frame.ImagePath, frame.MonotonicSeconds, frame.Metadata };
	}

	private static MaterialState Observe( Material material, ModelRenderer renderer, GltfGpuInstance instance ) => new( material.ShaderName, material.GetFeature( "F_GLTF_LINEAR_VERTEX_COLOR" ), material.GetFeature( "F_GLTF_METALLIC_TEXTURE" ),
		Components( material.GetVector4( "g_vColorTint" ) ), Components( material.GetVector4( "g_flModelTintAmount" ) ), Components( material.GetVector4( "g_flMetalness" ) ), Components( material.GetVector4( "g_flRoughnessScaleFactor" ) ), Components( renderer.Tint ), Components( instance.SceneObject.ColorTint ),
		new[] { "g_tColor", "g_tColor1", "g_tColor2", "g_tNormal", "g_tAmbientOcclusion" }.Select( name =>
		{
			var texture = material.GetTexture( name ); var valid = texture is { IsValid: true }; return new TextureState( name, material.HasParameter( name ), valid, valid ? texture.Index : null );
		} ).ToArray() );
	private static float[] Components( Vector4 value ) => new[] { value.x, value.y, value.z, value.w };
	private static float[] Components( Color value ) => new[] { value.r, value.g, value.b, value.a };
	private static void Validate( Frame frame )
	{
		if ( frame is null || frame.Width <= 0 || frame.Height <= 0 || frame.Rgba is null || frame.Rgba.Length != checked(frame.Width * frame.Height * 4) || frame.Metadata is null )
			throw new InvalidOperationException( "Missing or malformed completed native frame." );
	}
	private static FrameDelta Difference( Frame baseline, Frame other )
	{
		if ( baseline.Width != other.Width || baseline.Height != other.Height ) throw new InvalidOperationException( "Diagnostic frame dimensions changed." );
		// Reuse the existing image probe's byte comparison; its foreground
		// classification assumes a black background, so do not use its IoU here.
		var measured = BoxVertexColorsImageProbe.Compare( baseline, other );
		int changed = 0;
		for ( var i = 0; i < baseline.Rgba.Length; i += 4 )
		{
			var pixelChanged = false;
			for ( var c = 0; c < 3; c++ ) pixelChanged |= Math.Abs( baseline.Rgba[i+c] - other.Rgba[i+c] ) > 4;
			if ( pixelChanged ) changed++;
		}
		return new( changed, measured.MaximumRgbByteError, measured.MeanAbsoluteRgbByteError );
	}
}
