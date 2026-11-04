using System;
using System.IO;
using System.Security.Cryptography;
using NativeEngine;
using Sandbox.Diagnostics;
using Sandbox.Gltf;
using Sandbox.Rendering;

namespace EngineTests;

/// <summary>
/// Geometry-only stock-shader comparison in albedo debug mode, using a
/// fixture-owned double-sided copy. This does not prove single-sided culling,
/// glTF material fidelity, lighting, deformation, or complete model loading.
/// </summary>
[TestClass]
public sealed class GltfStockModelPixelTests
{
	[TestMethod]
	public void ControlledSingleSidedTriangleMatrix()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" ||
			Environment.GetEnvironmentVariable( "SBOX_GLTF_STOCK_RENDER" ) != "1" || !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires an isolated graphics test process with SBOX_GLTF_STOCK_RENDER=1." );
		var corpus = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
		if ( string.IsNullOrWhiteSpace( corpus ) ) Assert.Inconclusive( "Set SBOX_GLTF_CORPUS to the pinned Khronos Sample Assets checkout." );
		RequireStockLookups();
		var directory = Path.Combine( corpus, "Models", "Triangle", "glTF" );
		var input = File.ReadAllBytes( Path.Combine( directory, "Triangle.gltf" ) );
		Assert.AreEqual( "6f4a8a1fbefbac2d0adde0800864279fd98c3f119077380e6840810ed62b6cfc", Convert.ToHexStringLower( SHA256.HashData( input ) ) );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( directory, uri ) );
		var compiled = GltfCompiler.Compile( input, Resolve );
		using var sourceDocument = GltfReader.Read( input, Resolve );
		using var compiledDocument = GltfReader.Read( compiled );
		var sourceData = sourceDocument.CreateDrawData( 0, 0 );
		var compiledData = compiledDocument.CreateDrawData( 0, 0 );
		CollectionAssert.AreEqual( sourceData.Positions.ToArray(), compiledData.Positions.ToArray() );
		CollectionAssert.AreEqual( sourceData.Indices.ToArray(), compiledData.Indices.ToArray() );

		var stock = Material.Load( "materials/default/white.vmat" );
		Assert.IsTrue( stock is { IsValid: true } );
		Assert.AreEqual( "shaders/complex.shader", stock.Shader.native.GetFilename() );
		var single = stock.CreateCopy();
		var doubleSided = stock.CreateCopy();
		Assert.IsNotNull( single );
		Assert.IsNotNull( doubleSided );
		var originalOcclusion = Sandbox.Rendering.RenderPipeline.OcclusionCulling;
		var originalFrameCount = Application.FrameCount;
		var fullFrame = Environment.GetEnvironmentVariable( "SBOX_GLTF_FULL_FRAME" ) == "1";
		var sceneFrame = Environment.GetEnvironmentVariable( "SBOX_GLTF_SCENE_FRAME" ) == "1";
		var contextClear = Environment.GetEnvironmentVariable( "SBOX_GLTF_CONTEXT_CLEAR" ) == "1";
		var lit = Environment.GetEnvironmentVariable( "SBOX_GLTF_LIT" ) == "1";
		var debugMode = lit ? SceneCameraDebugMode.Normal : SceneCameraDebugMode.Albedo;
		var runFrame = fullFrame ? typeof( AppSystem ).GetMethod( "RunFrame", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ) : null;
		if ( fullFrame ) Assert.IsNotNull( runFrame, "Test app system does not expose its protected frame entry point." );
		var scene = new Scene();
		using var scope = scene.Push();
		using var target = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_matrix_target_" + Guid.NewGuid().ToString( "N" ) );
		var report = new System.Text.StringBuilder();
		var failures = new System.Collections.Generic.List<string>();
		var readbackMismatchedCaptures = 0;
		var readbackMismatchedPixels = 0;
		var callbackErrors = new System.Collections.Generic.List<string>();
		var previousPipeline = RenderPipeline.Current;
		Assert.IsNotNull( previousPipeline, "The stock native render pipeline must be installed." );
		var pipelineProbe = new PipelineProbe( previousPipeline );
		void OnMessage( LogEvent e )
		{
			if ( e.Message.Contains( "OnDisabled" ) ) callbackErrors.Add( e.Message );
		}
		Logging.OnMessage += OnMessage;
		try
		{
			RenderPipeline.Current = pipelineProbe;
			single.SetFeature( "F_RENDER_BACKFACES", 0 );
			doubleSided.SetFeature( "F_RENDER_BACKFACES", 1 );
			Assert.AreEqual( 0, single.GetFeature( "F_RENDER_BACKFACES" ) );
			Assert.AreEqual( 1, doubleSided.GetFeature( "F_RENDER_BACKFACES" ) );
			var suffix = Guid.NewGuid().ToString( "N" );
			var original = ReferenceTriangle( single, "gltf_matrix_ref_" + suffix );
			var reversed = ReferenceTriangle( single, "gltf_matrix_rev_" + suffix, reverse: true );
			var sourceModel = GltfModelPrimitiveBuilder.Build( sourceData, single, "gltf_matrix_source_" + suffix );
			var compiledModel = GltfModelPrimitiveBuilder.Build( compiledData, single, "gltf_matrix_compiled_" + suffix );
			var renderer = scene.CreateObject().Components.Create<ModelRenderer>();
			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			if ( lit ) scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White;
			camera.BackgroundColor = Color.Black;
			camera.ZNear = 1;
			camera.FieldOfView = 50;
			camera.EnablePostProcessing = false;
			camera.DebugMode = debugMode;
			camera.SceneCamera.Attributes.Set( "msaa", 0 );
			report.AppendLine( $"Camera debug mode={camera.DebugMode}, clear flags={camera.ClearFlags}, lit={lit}" );
			Assert.AreEqual( ClearFlags.Color | ClearFlags.Depth | ClearFlags.Stencil, camera.ClearFlags );
			var center = new Vector3( 0, 19.68504f, 19.68504f );
			var intendedPosition = Vector3.Zero;
			var intendedRotation = Rotation.Identity;
			var freshControlChecked = false;
			foreach ( var occlusion in new[] { originalOcclusion, false }.Distinct() )
			{
				Sandbox.Rendering.RenderPipeline.OcclusionCulling = occlusion;
				foreach ( var side in new[] { 1, -1 } )
				{
					intendedPosition = new Vector3( 80 * side, center.y, center.z );
					intendedRotation = side > 0 ? Rotation.FromYaw( 180 ) : Rotation.Identity;
					camera.WorldPosition = intendedPosition;
					camera.WorldRotation = intendedRotation;
					renderer.Enabled = false;
					var background = Capture();
					if ( !freshControlChecked ) SaveDiagnostic( "initial-background", background );
					renderer.Enabled = true;
					foreach ( var material in new[] { single, doubleSided } )
					{
						renderer.MaterialOverride = material;
						var backfaces = material.GetFeature( "F_RENDER_BACKFACES" );
						var referencePixels = Render( original, "reference", background );
						var reversePixels = Render( reversed, "reversed", background );
						var sourcePixels = Render( sourceModel, "source", background );
						var compiledPixels = Render( compiledModel, "compiled", background );
						var referenceCount = Coverage( referencePixels, background );
						var reverseCount = Coverage( reversePixels, background );
						var sourceCount = Coverage( sourcePixels, background );
						var compiledCount = Coverage( compiledPixels, background );
						var label = $"occlusion={occlusion}, side={side}, backfaces={backfaces}";
						report.AppendLine( $"{label}: ref={referenceCount}, reverse={reverseCount}, source={sourceCount}, compiled={compiledCount}" );
						if ( Math.Max( referenceCount, reverseCount ) <= 50 ) failures.Add( label + ": neither native reference winding is visible" );
						if ( backfaces == 1 && Math.Min( referenceCount, reverseCount ) <= 50 ) failures.Add( label + ": double-sided native control is missing a winding" );
						if ( backfaces == 0 && (referenceCount > 50) == (reverseCount > 50) ) failures.Add( label + ": single-sided controls did not distinguish the two windings" );
						CheckMatch( referencePixels, sourcePixels, "source" );
						CheckMatch( referencePixels, compiledPixels, "compiled" );

						void CheckMatch( Color[] expected, Color[] actual, string name )
						{
							var intersection = 0;
							var union = 0;
							for ( var i = 0; i < expected.Length; i++ )
							{
								var a = Different( expected[i], background[i] );
								var b = Different( actual[i], background[i] );
								if ( a && b ) intersection++;
								if ( a || b ) union++;
							}
							if ( referenceCount > 50 && (union == 0 || (float)intersection / union <= 0.95f) ||
								referenceCount <= 50 && Coverage( actual, background ) > 50 )
								failures.Add( $"{label}: {name} mask differs from native reference (intersection={intersection}, union={union})" );
						}
					}
				}
			}
			foreach ( var phase in new[] { "reference", "disabled", "fresh" } )
			{
				var samples = pipelineProbe.Samples.Where( s => s.Phase == phase ).ToArray();
				report.AppendLine( $"  pipeline {phase}: calls={samples.Length}; {string.Join( " | ", samples.Select( s => s.Detail ).Distinct().Take( 3 ) )}" );
			}
			report.AppendLine( $"Direct versus bitmap readback: mismatched captures={readbackMismatchedCaptures}, pixels={readbackMismatchedPixels}" );
			if ( readbackMismatchedCaptures != 0 ) failures.Add( "Direct and bitmap readbacks differ; see readback diagnostics." );
			report.AppendLine( $"Full native frame={fullFrame}, manual scene frame={sceneFrame && !fullFrame}, context clear={contextClear}; Application.FrameCount: initial={originalFrameCount}, final={Application.FrameCount}" );
			Console.WriteLine( report.ToString() );
			Assert.IsTrue( failures.Count == 0, string.Join( "\n", failures ) + "\n" + report );

			Color[] Render( Model model, string label, Color[] background )
			{
				// Re-establish an empty scene before each independent model capture.
				// A shared target or temporal history must not make the previous model
				// appear as this one when the current draw is culled.
				renderer.Enabled = false;
				var before = Capture();
				var beforeDrift = Coverage( before, background );
				renderer.Model = model;
				renderer.Enabled = true;
				if ( !freshControlChecked ) pipelineProbe.SetPhase( "reference" );
				var result = Capture();
				pipelineProbe.SetPhase( null );
				if ( !freshControlChecked ) SaveDiagnostic( "native-reference", result );
				var sceneCamera = camera.SceneCamera;
				var actualPosition = sceneCamera.Position;
				var forward = sceneCamera.Rotation.Forward;
				var facing = Vector3.Dot( center - actualPosition, forward );
				var screen = camera.PointToScreenNormal( center, out var behind );
				var sceneObject = renderer.SceneObject;
				Assert.IsTrue( sceneObject.IsValid(), $"{label}: enabled renderer has no scene object." );
				if ( behind || facing <= 0 || screen.x < 0 || screen.x > 1 || screen.y < 0 || screen.y > 1 )
					failures.Add( $"{label}: camera snapshot does not face the fixture center" );
				renderer.Enabled = false;
				CheckDisabled( sceneObject, label + " immediately" );
				if ( !freshControlChecked ) pipelineProbe.SetPhase( "disabled" );
				var after = Capture( verifyClear: !freshControlChecked );
				pipelineProbe.SetPhase( null );
				CheckDisabled( sceneObject, label + " after capture" );
				if ( !freshControlChecked ) SaveDiagnostic( "disabled-after-reference", after );
				var afterDrift = Coverage( after, background );
				if ( !freshControlChecked )
				{
					freshControlChecked = true;
					pipelineProbe.SetPhase( "fresh" );
					var fresh = CaptureFreshEmptyScene();
					pipelineProbe.SetPhase( null );
					SaveDiagnostic( "fresh-empty", fresh );
					var freshDrift = Coverage( fresh, background );
					var retainedDrift = Coverage( after, fresh );
					report.AppendLine( $"  fresh empty control: initial={freshDrift}, disabled={retainedDrift}" );
					if ( freshDrift > 5 ) failures.Add( $"{label}: fresh empty scene differs from initial background ({freshDrift} pixels)" );
				}
				if ( beforeDrift > 5 || afterDrift > 5 )
					failures.Add( $"{label}: empty-scene background changed (before={beforeDrift}, after={afterDrift})" );
				report.AppendLine( $"  {label}: disabled active={renderer.Active}, oldValid={sceneObject.IsValid()}, emptyBefore={beforeDrift}, emptyAfter={afterDrift}" );
				return result;
			}

			void CheckDisabled( SceneObject oldObject, string label )
			{
				Logging.PushQueuedMessages();
				if ( !freshControlChecked )
					report.AppendLine( $"  {label}: native world objects={scene.SceneWorld.native.GetSceneObjectCount()}, empty={scene.SceneWorld.native.IsEmpty()}" );
				Assert.IsFalse( renderer.Active, $"{label}: renderer is still active." );
				Assert.IsFalse( renderer.SceneObject.IsValid(), $"{label}: renderer retained its scene object." );
				Assert.IsFalse( oldObject.IsValid(), $"{label}: old scene object was not deleted." );
				Assert.IsTrue( callbackErrors.Count == 0, $"{label}: disable callback error: {string.Join( " | ", callbackErrors.Take( 2 ) )}" );
			}

			void SaveDiagnostic( string name, Color[] pixels )
			{
				var gameDirectory = Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" );
				Assert.IsFalse( string.IsNullOrWhiteSpace( gameDirectory ), "FACEPUNCH_ENGINE must point to the game directory." );
				var directory = Path.GetFullPath( Path.Combine( gameDirectory, "..", "Temp", "gltf-validation", "stock-diagnostic" ) );
				Directory.CreateDirectory( directory );
				using var bitmap = new Bitmap( 96, 96 );
				bitmap.SetPixels( pixels );
				File.WriteAllBytes( Path.Combine( directory, name + ".png" ), bitmap.ToPng() );
				var nonblack = pixels.Count( p => p.r > 0.01f || p.g > 0.01f || p.b > 0.01f );
				report.AppendLine( $"  {name}: nonblack={nonblack}, corner={pixels[0]}, center={pixels[48 * 96 + 48]}, " +
					$"rgba min=({pixels.Min( p => p.r ):F3},{pixels.Min( p => p.g ):F3},{pixels.Min( p => p.b ):F3},{pixels.Min( p => p.a ):F3}) " +
					$"max=({pixels.Max( p => p.r ):F3},{pixels.Max( p => p.g ):F3},{pixels.Max( p => p.b ):F3},{pixels.Max( p => p.a ):F3}) " +
					$"mean=({pixels.Average( p => p.r ):F3},{pixels.Average( p => p.g ):F3},{pixels.Average( p => p.b ):F3},{pixels.Average( p => p.a ):F3})" );
			}

			Color[] CaptureFreshEmptyScene()
			{
				var freshScene = new Scene();
				using var freshScope = freshScene.Push();
				using var freshTarget = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_matrix_fresh_target_" + Guid.NewGuid().ToString( "N" ) );
				try
				{
					var freshCamera = freshScene.CreateObject().Components.Create<CameraComponent>();
					if ( lit ) freshScene.CreateObject().Components.Create<AmbientLight>().Color = Color.White;
					Assert.AreNotEqual( camera.SceneCamera._cameraId, freshCamera.SceneCamera._cameraId, "Fresh camera shares the original camera ID." );
					Assert.AreNotEqual( target.native.GetBindingPtr(), freshTarget.native.GetBindingPtr(), "Fresh target shares the original native texture." );
					Assert.AreNotEqual( scene.SceneWorld.native.GetPointerAssertIfNull(), freshScene.SceneWorld.native.GetPointerAssertIfNull(), "Fresh scene shares the original native world." );
					report.AppendLine( $"  identity: camera={camera.SceneCamera._cameraId}, freshCamera={freshCamera.SceneCamera._cameraId}, targetsDistinct=true, worldsDistinct=true; fresh world objects={freshScene.SceneWorld.native.GetSceneObjectCount()}, empty={freshScene.SceneWorld.native.IsEmpty()}" );
					freshCamera.WorldPosition = intendedPosition;
					freshCamera.WorldRotation = intendedRotation;
					freshCamera.BackgroundColor = Color.Black;
					freshCamera.ZNear = 1;
					freshCamera.FieldOfView = 50;
					freshCamera.EnablePostProcessing = false;
					freshCamera.DebugMode = debugMode;
					Assert.AreEqual( camera.ClearFlags, freshCamera.ClearFlags, "Fresh camera must use matching clear flags." );
					Assert.AreEqual( camera.DebugMode, freshCamera.DebugMode, "Fresh camera must use matching debug mode." );
					freshCamera.SceneCamera.Attributes.Set( "msaa", 0 );
					for ( var i = 0; i < 12; i++ )
					{
						PumpFrame();
						freshScene.GameTick( 1.0 / 60.0 );
						freshCamera.ComposeView();
						freshCamera.InitializeRendering();
						Assert.AreEqual( debugMode, freshCamera.SceneCamera.DebugMode );
						Assert.AreEqual( freshCamera.ClearFlags, freshCamera.SceneCamera.ClearFlags );
						AssertCameraSnapshot( freshCamera, intendedPosition, intendedRotation );
						ClearTarget( freshTarget, contextClear && i == 0, "fresh empty" );
						Assert.IsTrue( freshCamera.RenderToTexture( freshTarget ) );
						g_pRenderDevice.Present( IntPtr.Zero );
						g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
						EngineLoop.DrainFrameEndDisposables();
						RenderTarget.EndOfFrame();
						EngineLoop.RunAsyncTasks();
					}
					return ReadChecked( freshTarget, "fresh" );
				}
				finally { freshScene.Destroy(); }
			}

			Color[] Capture( bool verifyClear = false )
			{
				for ( var i = 0; i < 12; i++ )
				{
					PumpFrame();
					scene.GameTick( 1.0 / 60.0 );
					// This fixture manually submits a camera immediately after moving it.
					// Compose and initialize its view explicitly before the submission.
					camera.ComposeView();
					camera.InitializeRendering();
					Assert.AreEqual( debugMode, camera.SceneCamera.DebugMode );
					Assert.AreEqual( camera.ClearFlags, camera.SceneCamera.ClearFlags );
					AssertCameraSnapshot( camera, intendedPosition, intendedRotation );
					ClearTarget( target, contextClear && verifyClear && i == 0, "disabled reference" );
					Assert.IsTrue( camera.RenderToTexture( target ), "Camera did not submit its render target." );
					AssertCameraSnapshot( camera, intendedPosition, intendedRotation );
					g_pRenderDevice.Present( IntPtr.Zero );
					g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
					EngineLoop.DrainFrameEndDisposables();
					RenderTarget.EndOfFrame();
					EngineLoop.RunAsyncTasks();
				}
				return ReadChecked( target, "main" );
			}

			void ClearTarget( Texture output, bool verify, string label )
			{
				if ( !contextClear )
				{
					output.Clear( Color.Black );
					return;
				}
				using ( var graphics = Graphics.Scope.Create() )
				{
					Graphics.RenderTarget = RenderTarget.From( output );
					graphics.Context.Clear( Color.Black, true, false, false );
				}
				if ( !verify ) return;
				g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
				var pixels = output.GetPixels();
				var nonblack = pixels.Count( p => p.r != 0 || p.g != 0 || p.b != 0 );
				report.AppendLine( $"  {label} direct pre-camera clear: nonblack={nonblack}" );
				Assert.AreEqual( 0, nonblack, $"{label}: direct readback after context clear is not black before camera render." );
			}

			Color[] ReadChecked( Texture source, string label )
			{
				var direct = source.GetPixels();
				using var bitmap = source.GetBitmap();
				Assert.IsNotNull( bitmap, $"{label}: bitmap readback failed." );
				var bitmapPixels = bitmap.GetPixels32();
				Assert.AreEqual( direct.Length, bitmapPixels.Length, $"{label}: readback dimensions differ." );
				var mismatches = 0;
				var first = -1;
				for ( var i = 0; i < direct.Length; i++ )
				{
					if ( direct[i].Equals( bitmapPixels[i] ) ) continue;
					if ( first < 0 ) first = i;
					mismatches++;
				}
				if ( mismatches > 0 )
				{
					readbackMismatchedCaptures++;
					readbackMismatchedPixels += mismatches;
					if ( readbackMismatchedCaptures <= 4 )
						report.AppendLine( $"  {label} readback mismatch: pixels={mismatches}, first={first}, direct={direct[first]}, bitmap={bitmapPixels[first]}, flags={source.Flags}" );
				}
				return bitmap.GetPixels();
			}

			void PumpFrame()
			{
				if ( fullFrame )
				{
					Assert.IsTrue( (bool)runFrame.Invoke( TestInit.TestAppSystem, null ), "The test app requested exit during its native frame." );
					return;
				}
				Application.FrameCount++;
				EngineGlobal.SourceEnginePanelAppFrame();
				if ( sceneFrame ) CSceneSystem.FrameUpdate();
			}
		}
		finally
		{
			RenderPipeline.Current = previousPipeline;
			Logging.OnMessage -= OnMessage;
			Application.FrameCount = originalFrameCount;
			Sandbox.Rendering.RenderPipeline.OcclusionCulling = originalOcclusion;
			scene.Destroy();
			single.Destroy();
			doubleSided.Destroy();
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void KhronosTriangleMatchesReferenceNativeModelInStockAlbedoMode( bool compiled )
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" ||
			Environment.GetEnvironmentVariable( "SBOX_GLTF_STOCK_RENDER" ) != "1" || !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires an isolated graphics test process with SBOX_GLTF_STOCK_RENDER=1." );
		var corpus = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
		if ( string.IsNullOrWhiteSpace( corpus ) ) Assert.Inconclusive( "Set SBOX_GLTF_CORPUS to the pinned Khronos Sample Assets checkout." );
		RequireStockLookups();

		var directory = Path.Combine( corpus, "Models", "Triangle", "glTF" );
		var source = File.ReadAllBytes( Path.Combine( directory, "Triangle.gltf" ) );
		Assert.AreEqual( "6f4a8a1fbefbac2d0adde0800864279fd98c3f119077380e6840810ed62b6cfc", Convert.ToHexStringLower( SHA256.HashData( source ) ) );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( directory, uri ) );
		var input = compiled ? GltfCompiler.Compile( source, Resolve ) : source;
		using var document = GltfReader.Read( input, Resolve );
		var stock = Material.Load( "materials/default/white.vmat" );
		Assert.IsNotNull( stock );
		Assert.IsTrue( stock.IsValid );
		Assert.AreEqual( "shaders/complex.shader", stock.Shader.native.GetFilename(), "Stock material must not use the error shader." );
		var material = stock.CreateCopy();
		Assert.IsNotNull( material );
		try
		{
			material.SetFeature( "F_RENDER_BACKFACES", 1 );
			Assert.AreEqual( 1, material.GetFeature( "F_RENDER_BACKFACES" ), "The geometry-only fixture must explicitly disable backface culling." );
			Assert.AreEqual( "shaders/complex.shader", material.Shader.native.GetFilename() );
			var suffix = Guid.NewGuid().ToString( "N" );
			var actual = GltfModelPrimitiveBuilder.Build( document.CreateDrawData( 0, 0 ), material, "gltf_stock_actual_" + suffix );
			var reference = ReferenceTriangle( material, "gltf_stock_reference_" + suffix );
			Assert.IsTrue( actual.IsValid && !actual.IsError && reference.IsValid && !reference.IsError );

			var scene = new Scene();
			using var scope = scene.Push();
			using var target = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create();
			try
			{
				var renderer = scene.CreateObject().Components.Create<ModelRenderer>();
				var camera = scene.CreateObject().Components.Create<CameraComponent>();
				var intendedPosition = new Vector3( 80, 19.68504f, 19.68504f );
				var intendedRotation = Rotation.FromYaw( 180 );
				camera.WorldPosition = intendedPosition;
				camera.WorldRotation = intendedRotation;
				camera.BackgroundColor = Color.Black;
				camera.ZNear = 1;
				camera.FieldOfView = 50;
				camera.EnablePostProcessing = false;
				camera.DebugMode = SceneCameraDebugMode.Albedo;
				camera.SceneCamera.Attributes.Set( "msaa", 0 );
				renderer.Enabled = false;
				var background = Capture();
				renderer.Model = reference;
				renderer.Enabled = true;
				var expected = Capture();
				renderer.Model = actual;
				var observed = Capture();
				var expectedCount = 0;
				var actualCount = 0;
				var intersection = 0;
				var union = 0;
				for ( var i = 0; i < background.Length; i++ )
				{
					var a = Different( expected[i], background[i] );
					var b = Different( observed[i], background[i] );
					if ( a ) expectedCount++;
					if ( b ) actualCount++;
					if ( a && b ) intersection++;
					if ( a || b ) union++;
				}
				var diagnostic = "";
				if ( expectedCount <= 50 || actualCount <= 50 )
				{
					// Independent controls distinguish a dead scene/camera path from
					// backface winding, without changing the accepted triangle winding.
					var sphere = Model.Sphere;
					var sphereValid = sphere is not null && sphere.IsValid && !sphere.IsError;
					var sphereCount = -1;
					if ( sphereValid )
					{
						var size = sphere.Bounds.Size;
						var largest = MathF.Max( size.x, MathF.Max( size.y, size.z ) );
						if ( float.IsFinite( largest ) && largest > 0 )
						{
							var scale = 30f / largest;
							renderer.Model = sphere;
							renderer.MaterialOverride = material;
							renderer.GameObject.WorldScale = new Vector3( scale );
							renderer.GameObject.WorldPosition = new Vector3( 0, 19.68504f, 19.68504f ) - sphere.Bounds.Center * scale;
							sphereCount = Coverage( Capture(), background );
						}
					}
					renderer.GameObject.WorldScale = Vector3.One;
					renderer.GameObject.WorldPosition = Vector3.Zero;
					renderer.MaterialOverride = null;
					renderer.Model = ReferenceTriangle( material, "gltf_stock_reverse_" + suffix, reverse: true );
					var reverseFront = Coverage( Capture(), background );
					renderer.Enabled = false;
					intendedPosition = new Vector3( -80, 19.68504f, 19.68504f );
					intendedRotation = Rotation.Identity;
					camera.WorldPosition = intendedPosition;
					camera.WorldRotation = intendedRotation;
					var backBackground = Capture();
					renderer.Enabled = true;
					renderer.Model = reference;
					var referenceBack = Coverage( Capture(), backBackground );
					renderer.Model = actual;
					var actualBack = Coverage( Capture(), backBackground );
					var sceneObject = renderer.SceneObject;
					var rendering = sceneObject.IsValid() ? sceneObject.RenderingEnabled.ToString() : "unavailable";
					diagnostic = $" Controls: stock sphere valid={sphereValid} front coverage={sphereCount}; reversed reference front={reverseFront}; " +
						$"reference back={referenceBack}; glTF back={actualBack}; renderer scene object valid={sceneObject.IsValid()}, " +
						$"rendering={rendering}; camera position={camera.WorldPosition}, rotation={camera.WorldRotation}.";
				}
				Assert.IsTrue( expectedCount > 50 && actualCount > 50, $"Both stock-shader models must cover pixels (reference {expectedCount}, glTF {actualCount}).{diagnostic}" );
				Assert.IsTrue( union > 0 && (float)intersection / union > 0.95f, $"Projected silhouettes differ: intersection {intersection}, union {union}." );

				Color[] Capture()
				{
					for ( var i = 0; i < 20; i++ )
					{
						EngineGlobal.SourceEnginePanelAppFrame();
						scene.GameTick( 1.0 / 60.0 );
						camera.ComposeView();
						camera.InitializeRendering();
						AssertCameraSnapshot( camera, intendedPosition, intendedRotation );
						target.Clear( Color.Black );
						Assert.IsTrue( camera.RenderToTexture( target ), "Camera did not submit its render target." );
						AssertCameraSnapshot( camera, intendedPosition, intendedRotation );
						g_pRenderDevice.Present( IntPtr.Zero );
						g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
						EngineLoop.DrainFrameEndDisposables();
						RenderTarget.EndOfFrame();
						EngineLoop.RunAsyncTasks();
					}
					using var bitmap = target.GetBitmap();
					return bitmap.GetPixels().ToArray();
				}
			}
			finally { scene.Destroy(); }
		}
		finally { material.Destroy(); }
	}

	private static bool Different( Color a, Color b )
		=> MathF.Abs( a.r - b.r ) > 0.03f || MathF.Abs( a.g - b.g ) > 0.03f || MathF.Abs( a.b - b.b ) > 0.03f;

	private static void AssertCameraSnapshot( CameraComponent camera, Vector3 intendedPosition, Rotation intendedRotation )
	{
		var snapshot = camera.SceneCamera;
		Assert.IsTrue( (camera.WorldPosition - intendedPosition).Length < 0.001f,
			$"Camera component position {camera.WorldPosition} differs from intended {intendedPosition}." );
		Assert.IsTrue( (snapshot.Position - intendedPosition).Length < 0.001f,
			$"Submitted camera position {snapshot.Position} differs from intended {intendedPosition}." );
		Assert.IsTrue( Vector3.Dot( snapshot.Rotation.Forward, intendedRotation.Forward ) > 0.99999f &&
			Vector3.Dot( snapshot.Rotation.Up, intendedRotation.Up ) > 0.99999f,
			$"Submitted camera rotation {snapshot.Rotation} differs from intended {intendedRotation}." );
	}

	private static int Coverage( Color[] pixels, Color[] background )
	{
		var count = 0;
		for ( var i = 0; i < pixels.Length; i++ ) if ( Different( pixels[i], background[i] ) ) count++;
		return count;
	}

	private sealed class PipelineProbe : INativeRenderPipeline
	{
		private readonly INativeRenderPipeline inner;
		private string phase;
		internal readonly System.Collections.Concurrent.ConcurrentQueue<(string Phase, string Detail)> Samples = new();

		internal PipelineProbe( INativeRenderPipeline inner ) => this.inner = inner;
		internal void SetPhase( string value ) => System.Threading.Volatile.Write( ref phase, value );

		public void AddLayersToView( CRenderPipelineSetup setup )
		{
			var current = System.Threading.Volatile.Read( ref phase );
			if ( current is not null )
			{
				try
				{
					var view = setup.m_pView;
					Samples.Enqueue( (current, $"clearFlags={setup.m_nClearFlags}, clearColor={setup.m_vClearColor}, camera={view.m_ManagedCameraId}, view={view.m_ViewUniqueId}, color={setup.m_hColor}, finalColor={setup.m_hFinalColor}") );
				}
				catch ( Exception e ) { Samples.Enqueue( (current, $"probe error: {e.GetType().Name}: {e.Message}") ); }
			}
			inner.AddLayersToView( setup );
		}

		public void AddSkybox3DLayersToView( CRenderPipelineSetup setup ) => inner.AddSkybox3DLayersToView( setup );
		public void OnSceneViewSubmitted( ISceneView view ) => inner.OnSceneViewSubmitted( view );
		public ISceneLayer AddUIHook( string name, Stage stage, ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle color )
			=> inner.AddUIHook( name, stage, view, viewport, color );
		public void WarmUp() => inner.WarmUp();
		public void Shutdown() => inner.Shutdown();
	}

	private static Model ReferenceTriangle( Material material, string name, bool reverse = false )
	{
		const float unit = 1f / 0.0254f;
		var mesh = new Mesh( material );
		mesh.CreateVertexBuffer( 3, new Vertex[]
		{
			new() { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Forward, Color = Color32.White },
			new() { Position = new Vector3( 0, unit, 0 ), Normal = Vector3.Forward, Color = Color32.White },
			new() { Position = new Vector3( 0, 0, unit ), Normal = Vector3.Forward, Color = Color32.White }
		} );
		mesh.CreateIndexBuffer( 3, reverse ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 } );
		var bounds = new BBox( Vector3.Zero, new Vector3( 0, unit, unit ) );
		mesh.Bounds = bounds;
		return Model.Builder.WithName( name ).AddMesh( mesh ).WithViewBounds( bounds ).Create();
	}

	private static void RequireStockLookups()
	{
		foreach ( var name in new[]
		{
			"textures/dev/blue_noise_256.vtex", "textures/dev/ggx_integrate_brdf_lut_schlick.vtex",
			"textures/dev/ltc_amp.vtex", "textures/dev/ltc_mat.vtex"
		} )
		{
			var texture = Texture.Load( name, false );
			Assert.IsTrue( texture is { IsValid: true, IsError: false }, $"Stock lookup did not load: {name}." );
		}
	}
}
