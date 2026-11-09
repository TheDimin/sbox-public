// Isolated editor preview. This leaves the active project scene and assets untouched.
// The native texture and render target are task-owned; the shared stock template is not.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Could not locate the s&box checkout from loaded engine: " + engineLocation );
var corpusRoot = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
if ( string.IsNullOrWhiteSpace( corpusRoot ) ) corpusRoot = System.IO.Path.Combine( root.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
var sourceDirectory = System.IO.Path.Combine( corpusRoot, "Models", "BoxTextured", "glTF" );
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-texture" );
var artifactPath = System.IO.Path.Combine( output, "package", "model.glb_c" );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var suffix = Guid.NewGuid().ToString( "N" );
var virtualBase = "gltf-editor-texture/" + suffix;
var redirectName = "/" + virtualBase + ".vtex_c";
var generatedPath = System.IO.Path.Combine( output, "temporary-" + suffix + ".vtex_c" );
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var assembly = typeof( Model ).Assembly;
var completed = new System.Collections.Generic.List<string>();
var captures = new System.Collections.Generic.List<object>();
var sourceDependencies = new System.Collections.Generic.Dictionary<string, string>();
var packagedDependencies = new System.Collections.Generic.Dictionary<string, string>();
var startFrame = Application.FrameCount;
var phase = "read-assets";
Scene scene = null;
Editor.SceneRenderingWidget preview = null;
Action onPreviewFrame = null;
var previewFrameCount = 0;
SceneModel modelObject = null;
Texture target = null;
Texture texture = null;
Model sourceModel = null;
Model compiledModel = null;
Model controlModel = null;
IDisposable redirect = null;
IDisposable sourceLease = null;
IDisposable secondLease = null;
IDisposable controlLease = null;
object redirectObject = null;
bool redirectAdded = false;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void WriteReceipt( object value ) => System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
try
{
	if ( !Application.IsEditor ) throw new InvalidOperationException( "This proof must run inside the s&box editor." );
	completed.Add( "verify-editor" );
	var source = System.IO.File.ReadAllBytes( System.IO.Path.Combine( sourceDirectory, "BoxTextured.gltf" ) );
	var sourceHash = Hash( source );
	if ( sourceHash != "1e9003a4a2a8822ff60da529357bd8e4dec4a59b1a479017993e7e2ad5fcebef" )
		throw new InvalidOperationException( "Unexpected Khronos BoxTextured source hash: " + sourceHash );
	byte[] ResolveSource( string uri )
	{
		var bytes = System.IO.File.ReadAllBytes( System.IO.Path.Combine( sourceDirectory, uri ) );
		sourceDependencies[uri] = Hash( bytes );
		return bytes;
	}
	byte[] ResolveArtifact( string uri ) => Sandbox.Gltf.GltfArtifactDependencies.ResolvePackaged( artifactPath, uri );
	var container = Sandbox.Gltf.GltfCompiler.Compile( source, ResolveSource );
	var bundle = Sandbox.Gltf.GltfArtifactDependencies.Prepare( container, ResolveSource );
	Sandbox.Gltf.GltfArtifactDependencies.WritePackage( artifactPath, bundle.Artifact, bundle.Dependencies );
	foreach ( var dependency in bundle.Dependencies )
	{
		var packagedBytes = ResolveArtifact( dependency.Key );
		if ( !packagedBytes.AsSpan().SequenceEqual( dependency.Value ) ) throw new InvalidOperationException( "Packaged dependency differs from compiled dependency: " + dependency.Key );
		packagedDependencies[dependency.Key] = Hash( packagedBytes );
	}
	var compiled = System.IO.File.ReadAllBytes( artifactPath );
	var artifactHash = Hash( compiled );
	if ( !compiled.AsSpan().SequenceEqual( bundle.Artifact ) ) throw new InvalidOperationException( "Written GLB differs from compiled package." );
	using var sourceDoc = Sandbox.Gltf.GltfReader.Read( source, ResolveSource );
	using var compiledDoc = Sandbox.Gltf.GltfReader.Read( compiled, ResolveArtifact );
	var sourceImage = sourceDoc.ReadImage( 0 );
	var compiledImage = compiledDoc.ReadImage( 0 );
	var imageHash = Hash( sourceImage.Bytes );
	if ( imageHash != "9c22b05c5b136d03c5621a8765e50a8322be6c35b9de53e9fe22685840d7f469" ||
		!sourceImage.Bytes.AsSpan().SequenceEqual( compiledImage.Bytes ) )
		throw new InvalidOperationException( "Source and packaged BoxTextured PNG differ from the pinned image." );
	var sourceData = sourceDoc.CreateDrawData( 0, 0 );
	var compiledData = compiledDoc.CreateDrawData( 0, 0 );
	var sourceMatrix = Sandbox.Gltf.GltfGeometry.ConvertTransform( sourceDoc.GetWorldTransform( 1 ) );
	var compiledMatrix = Sandbox.Gltf.GltfGeometry.ConvertTransform( compiledDoc.GetWorldTransform( 1 ) );
	if ( sourceMatrix != compiledMatrix ) throw new InvalidOperationException( "Source and compiled node transforms differ." );
	if ( !System.Numerics.Matrix4x4.Decompose( sourceMatrix, out var scale, out var rotation, out var translation ) )
		throw new InvalidOperationException( "BoxTextured node transform cannot be decomposed." );
	var recomposed = System.Numerics.Matrix4x4.CreateScale( scale ) * System.Numerics.Matrix4x4.CreateFromQuaternion( rotation ) * System.Numerics.Matrix4x4.CreateTranslation( translation );
	float[] MatrixValues( System.Numerics.Matrix4x4 m ) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24, m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
	var matrixError = MatrixValues( sourceMatrix ).Zip( MatrixValues( recomposed ), ( a, b ) => MathF.Abs( a - b ) ).Max();
	if ( matrixError > 0.00001f ) throw new InvalidOperationException( "BoxTextured node transform lost precision." );
	var modelTransform = new Transform { Position = translation, Rotation = rotation, Scale = scale };
	completed.Add( "read-assets-and-transform" );

	phase = "compile-and-load-texture";
	var compilerType = assembly.GetType( "Sandbox.GltfTextureCompiler", true );
	var compile = compilerType.GetMethod( "Compile", flags ) ?? throw new InvalidOperationException( "The live engine lacks the texture compiler." );
	var nativeBytes = (byte[])compile.Invoke( null, new object[] { sourceImage, true, 256L * 1024 * 1024 } );
	System.IO.File.WriteAllBytes( generatedPath, nativeBytes );
	var redirectType = assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
	redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
	redirect = (IDisposable)redirectObject;
	redirectType.GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { redirectName, generatedPath } );
	redirectAdded = true;
	var resourceType = assembly.GetType( "NativeEngine.g_pResourceSystem", true );
	resourceType.GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );
	texture = Texture.Load( virtualBase + ".vtex", false );
	if ( texture is null || !texture.IsValid || texture.IsError ) throw new InvalidOperationException( "Native texture resource failed to load." );
	using var expectedBitmap = Sandbox.Bitmap.CreateFromBytes( sourceImage.Bytes );
	if ( expectedBitmap is null || expectedBitmap.Width != texture.Width || expectedBitmap.Height != texture.Height )
		throw new InvalidOperationException( "Native texture dimensions disagree with the decoded image." );
	var expectedTexturePixels = expectedBitmap.GetPixels32();
	var nativeTexturePixels = texture.GetPixels();
	var texturePixelDifferences = expectedTexturePixels.Zip( nativeTexturePixels, ( a, b ) => a.Equals( b ) ).Count( equal => !equal );
	if ( expectedTexturePixels.Length != nativeTexturePixels.Length || texturePixelDifferences != 0 )
		throw new InvalidOperationException( "Native texture readback differs from decoded source pixels: " + texturePixelDifferences );
	completed.Add( "compile-load-and-readback-texture" );

	phase = "acquire-material-and-build-models";
	var stock = Material.Load( "materials/default/white.vmat" );
	if ( stock is null || !stock.IsValid || stock.IsError ) throw new InvalidOperationException( "Stock white material is unavailable." );
	if ( !stock.ShaderName.Contains( "complex", StringComparison.OrdinalIgnoreCase ) ) throw new InvalidOperationException( "Expected the stock complex shader; got " + stock.ShaderName );
	var bindingType = assembly.GetType( "Sandbox.GltfMaterialTextureBinding", true );
	var keyType = assembly.GetType( "Sandbox.GltfMaterialKey", true );
	var cacheType = assembly.GetType( "Sandbox.GltfMaterialCache", true );
	var textureRevision = (int)typeof( Texture ).GetField( "DirtyVersion", flags ).GetValue( texture );
	var sampler = new Sandbox.Rendering.SamplerState();
	var binding = Activator.CreateInstance( bindingType, flags, null, new object[] { "g_tColor", texture, textureRevision, sampler, null }, null );
	var textures = Array.CreateInstance( bindingType, 1 );
	textures.SetValue( binding, 0 );
	var emptyTextures = Array.CreateInstance( bindingType, 0 );
	var features = new System.Collections.Generic.KeyValuePair<string, int>[] { new( "F_RENDER_BACKFACES", 1 ) };
	var parameters = Array.Empty<System.Collections.Generic.KeyValuePair<string, Vector4>>();
	var scalars = Array.Empty<System.Collections.Generic.KeyValuePair<string, float>>();
	object Key( Array textureBindings ) => Activator.CreateInstance( keyType, flags, null, new object[] { stock, 0, features, parameters, textureBindings, scalars }, null );
	var cache = cacheType.GetProperty( "Shared", flags ).GetValue( null );
	var acquire = cacheType.GetMethod( "Acquire", flags );
	var firstObject = acquire.Invoke( cache, new[] { Key( textures ) } );
	sourceLease = (IDisposable)firstObject;
	var secondObject = acquire.Invoke( cache, new[] { Key( textures ) } );
	secondLease = (IDisposable)secondObject;
	var valueProperty = firstObject.GetType().GetProperty( "Value", flags );
	var material = (Material)valueProperty.GetValue( firstObject );
	if ( !ReferenceEquals( material, valueProperty.GetValue( secondObject ) ) ) throw new InvalidOperationException( "Equal material keys did not share the cache entry." );
	var controlObject = acquire.Invoke( cache, new[] { Key( emptyTextures ) } );
	controlLease = (IDisposable)controlObject;
	var controlMaterial = (Material)valueProperty.GetValue( controlObject );
	if ( material is null || !material.IsValid || material.GetFeature( "F_RENDER_BACKFACES" ) != 1 || material.ShaderName != stock.ShaderName )
		throw new InvalidOperationException( "Cached stock material or shader is invalid." );
	var boundTexture = material.GetTexture( "g_tColor" );
	if ( boundTexture is null || !boundTexture.IsValid || !ReferenceEquals( texture, boundTexture ) )
		throw new InvalidOperationException( "Cached material does not retain the expected native color texture." );
	var build = assembly.GetType( "Sandbox.GltfModelPrimitiveBuilder", true ).GetMethod( "Build", flags );
	sourceModel = (Model)build.Invoke( null, new object[] { sourceData, material, "gltf_textured_source_" + suffix } );
	compiledModel = (Model)build.Invoke( null, new object[] { compiledData, material, "gltf_textured_compiled_" + suffix } );
	controlModel = (Model)build.Invoke( null, new object[] { sourceData, controlMaterial, "gltf_untextured_control_" + suffix } );
	if ( !sourceModel.IsValid || sourceModel.IsError || !compiledModel.IsValid || compiledModel.IsError || !controlModel.IsValid || controlModel.IsError )
		throw new InvalidOperationException( "A preview model failed to build." );
	completed.Add( "share-material-and-build-models" );

	phase = "create-preview";
	scene = Scene.CreateEditorScene();
	CameraComponent camera;
	using ( scene.Push() )
	{
		camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.WorldPosition = new Vector3( 80, 0, 0 );
		camera.WorldRotation = Rotation.FromYaw( 180 );
		camera.BackgroundColor = Color.Black;
		camera.ZNear = 1;
		camera.FieldOfView = 50;
		camera.EnablePostProcessing = false;
		camera.DebugMode = SceneCameraDebugMode.Albedo;
	}
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF textured isolated preview", Size = new Vector2( 384, 384 ) };
	onPreviewFrame = () => previewFrameCount++;
	preview.OnPreFrame += onPreviewFrame;
	preview.Show();
	target = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_textured_capture_" + suffix );
	completed.Add( "create-preview" );

	async Task WaitForPreviewFrames()
	{
		var startingFrame = previewFrameCount;
		var timer = System.Diagnostics.Stopwatch.StartNew();
		while ( previewFrameCount < startingFrame + 3 )
		{
			cancellationToken.ThrowIfCancellationRequested();
			if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( $"Preview did not receive three OnPreFrame callbacks (started {startingFrame}, now {previewFrameCount}, visible {preview.Visible}, size {preview.Size})." );
			await Task.Delay( 50, cancellationToken );
			await MainThread.Wait();
		}
	}
	async Task<Color[]> Capture( string name )
	{
		phase = name;
		await WaitForPreviewFrames();
		var beforeSubmit = previewFrameCount;
		using ( scene.Push() )
		{
			target.Clear( Color.Black );
			if ( !camera.RenderToTexture( target ) ) throw new InvalidOperationException( "Camera failed to submit " + name );
		}
		// Let preview widget frames advance after submission while the scene is unchanged.
		// RenderToTexture may have queued work for the editor's current frame.
		await WaitForPreviewFrames();
		cancellationToken.ThrowIfCancellationRequested();
		using var bitmap = target.GetBitmap();
		if ( bitmap is null ) throw new InvalidOperationException( "Camera readback failed for " + name );
		var png = bitmap.ToPng();
		var path = System.IO.Path.Combine( output, name + ".png" );
		System.IO.File.WriteAllBytes( path, png );
		var sceneCamera = typeof( CameraComponent ).GetProperty( "SceneCamera", flags )?.GetValue( camera );
		var cameraSize = sceneCamera?.GetType().GetProperty( "Size", flags )?.GetValue( sceneCamera )?.ToString();
		captures.Add( new { name, path, sha256 = Hash( png ), frame = Application.FrameCount, preview_frames_at_submit = beforeSubmit, preview_frames_at_readback = previewFrameCount, preview_visible = preview.Visible, preview_size = preview.Size.ToString(), camera_size = cameraSize } );
		completed.Add( name );
		return bitmap.GetPixels();
	}
	bool Different( Color a, Color b ) => MathF.Abs( a.r - b.r ) > 0.03f || MathF.Abs( a.g - b.g ) > 0.03f || MathF.Abs( a.b - b.b ) > 0.03f;
	int Difference( Color[] a, Color[] b ) => a.Zip( b, ( x, y ) => Different( x, y ) ).Count( x => x );
	int Nonblack( Color[] pixels ) => pixels.Count( x => Different( x, Color.Black ) );
	var initialBackground = await Capture( "background-initial" );
	var background = await Capture( "background" );
	modelObject = new SceneModel( scene.SceneWorld, sourceModel, modelTransform );
	var sourcePixels = await Capture( "source" );
	modelObject.Model = compiledModel;
	var compiledPixels = await Capture( "compiled" );
	modelObject.Model = controlModel;
	var controlPixels = await Capture( "untextured-control" );
	modelObject.RenderingEnabled = false;
	var disabledPixels = await Capture( "disabled" );
	var coverage = Difference( sourcePixels, background );
	var sourceCompiledDifference = Difference( sourcePixels, compiledPixels );
	var textureControlDifference = Difference( sourcePixels, controlPixels );
	var disabledDifference = Difference( background, disabledPixels );
	var initialBackgroundNonblack = Nonblack( initialBackground );
	var backgroundNonblack = Nonblack( background );
	var disabledNonblack = Nonblack( disabledPixels );
	var bluePixels = sourcePixels.Zip( background, ( c, b ) => Different( c, b ) && c.b > c.r + 0.05f && c.b > c.g + 0.02f ).Count( x => x );
	var greenPixels = sourcePixels.Zip( background, ( c, b ) => Different( c, b ) && c.g > c.r + 0.05f && c.g > c.b + 0.02f ).Count( x => x );
	var passed = coverage > 50 && sourceCompiledDifference <= 5 && textureControlDifference > 50 && disabledDifference <= 5 && bluePixels > 5 && greenPixels > 5 && initialBackgroundNonblack == 0 && backgroundNonblack == 0 && disabledNonblack == 0;
	var receipt = new
	{
		passed,
		scope = "isolated editor stock albedo preview: static opaque BoxTextured base color only; no independent reference renderer, PBR, sampler or mip conformance",
		source_sha256 = sourceHash, compiled_sha256 = artifactHash, image_sha256 = imageHash,
		texture_resource_sha256 = Hash( nativeBytes ), texture_width = texture.Width, texture_height = texture.Height,
		texture_pixel_differences = texturePixelDifferences, texture_pixel_count = nativeTexturePixels.Length,
		shared_material = ReferenceEquals( material, valueProperty.GetValue( secondObject ) ), stock_shader = material.ShaderName, node_transform_max_checked_error = matrixError,
		coverage, source_compiled_difference = sourceCompiledDifference, texture_control_difference = textureControlDifference,
		disabled_difference = disabledDifference, blue_pixels = bluePixels, green_pixels = greenPixels,
		initial_background_nonblack = initialBackgroundNonblack, background_nonblack = backgroundNonblack, disabled_nonblack = disabledNonblack,
		capture_preview_preframes_after_submission = 3, preview_frame_count = previewFrameCount, is_editor = Application.IsEditor,
		engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
		gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
		completed, captures, source_dependencies = sourceDependencies, packaged_dependencies = packagedDependencies,
		engine_location = engineLocation, corpus_root = corpusRoot, artifact_path = artifactPath,
		starting_frame = startFrame, frame = Application.FrameCount, output
	};
	WriteReceipt( receipt );
	return receipt;
}
catch ( OperationCanceledException ) { throw; }
catch ( Exception error )
{
	var receipt = new { passed = false, phase, completed, error = error.ToString(), output };
	WriteReceipt( receipt );
	return receipt;
}
finally
{
	await MainThread.Wait();
	if ( preview is not null && preview.IsValid && onPreviewFrame is not null ) preview.OnPreFrame -= onPreviewFrame;
	if ( preview is not null && preview.IsValid ) { preview.Close(); preview.Destroy(); }
	if ( modelObject.IsValid() ) modelObject.Delete();
	scene?.Destroy();
	target?.Dispose();
	var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
	if ( sourceModel is not null ) destroyModel.Invoke( sourceModel, null );
	if ( compiledModel is not null ) destroyModel.Invoke( compiledModel, null );
	if ( controlModel is not null ) destroyModel.Invoke( controlModel, null );
	controlLease?.Dispose();
	secondLease?.Dispose();
	sourceLease?.Dispose();
	texture?.Dispose();
	if ( redirectAdded ) redirectObject.GetType().GetMethod( "RemoveAbsFile", flags ).Invoke( redirectObject, new object[] { redirectName } );
	redirect?.Dispose();
	if ( System.IO.File.Exists( generatedPath ) ) System.IO.File.Delete( generatedPath );
}
