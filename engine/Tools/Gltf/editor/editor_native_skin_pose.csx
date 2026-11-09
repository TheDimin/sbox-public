// Isolated real-editor GPU oracle. Native declaration metadata is not a pass:
// the posed raster silhouette must match independently transformed geometry.
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
const string captureRenderer = "managed SceneBridge.RenderToTexture";
var rendererAssembly = System.Reflection.Assembly.Load( "Sandbox.SceneRenderer" );
var managedRenderer = typeof( Model ).Assembly.GetType( "Sandbox.Rendering.ManagedSceneRendering", true ).GetProperty( "Renderer", flags ).GetValue( null );
if ( managedRenderer is null || managedRenderer.GetType().Assembly != rendererAssembly ) throw new InvalidOperationException( "Verified managed renderer unavailable." );
var render = managedRenderer.GetType().GetMethod( "RenderToTexture", flags, null, new[] { typeof( CameraComponent ), typeof( Texture ) }, null );
var engineDirectory = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( typeof( Model ).Assembly.Location ) );
while ( engineDirectory is not null && !System.IO.File.Exists( System.IO.Path.Combine( engineDirectory.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) engineDirectory = engineDirectory.Parent;
if ( engineDirectory is null ) throw new InvalidOperationException( "Cannot locate engine checkout." );
var output = System.IO.Path.Combine( engineDirectory.FullName, "Temp", "gltf-validation", "native-skin-pose", Guid.NewGuid().ToString( "N" ) );
System.IO.Directory.CreateDirectory( output );
var results = new System.Collections.Generic.List<object>();
var captures = new System.Collections.Generic.List<object>();
var declarations = new System.Collections.Generic.List<object>();
var ownedModels = new System.Collections.Generic.List<Model>();
var sourcePoints = new[] { new Vector3( 0, -15, -15 ), new Vector3( 0, 15, -15 ), new Vector3( 0, -15, 15 ) };
var material = Material.Load( "materials/default/white.vmat" ).CreateCopy();
Scene scene = null;
Editor.SceneRenderingWidget preview = null;
SceneModel shown = null;
Action frameCallback = null;
var frames = 0;
string phase = "initialize", failure = null;
// Exercise the installed raw byte upload used by the production loader. The
// legacy public VertexAttribute mapper rejects UInt16 before native dispatch.
// This diagnostic wrapper stages independent fixture bytes; it does not load or
// substitute glTF producer source and is not a Model.Load acceptance result.
object UploadRawFixture( Mesh mesh, int stride, byte[] bytes, VertexAttribute[] layout )
{
	var engine = typeof( Model ).Assembly;
	var preparedType = engine.GetType( "Sandbox.GltfModelPrimitiveBuilder+PreparedPrimitive", true );
	var fieldType = engine.GetType( "Sandbox.RawVertexField", true );
	var formatType = engine.GetType( "NativeEngine.ColorFormat", true );
	var constructor = fieldType.GetConstructors( flags ).Single( value => value.GetParameters().Length == 4 );
	var fields = Array.CreateInstance( fieldType, layout.Length ); var offset = 0;
	var semantics = new[] { "position", "normal", "tangent", "texcoord", "color", "blendindices", "blendweight" };
	var sizes = new[] { 4, 2, 4, 4, 2, 2, 1, 1 };
	for ( var i = 0; i < layout.Length; i++ )
	{
		var attribute = layout[i];
		var format = attribute.Format == VertexAttributeFormat.UInt16 && attribute.Type is VertexAttributeType.BlendIndices or VertexAttributeType.BlendWeights
			? Enum.Parse( formatType, attribute.Type == VertexAttributeType.BlendIndices ? "COLOR_FORMAT_R16G16B16A16_UINT" : "COLOR_FORMAT_R16G16B16A16_UNORM" )
			: typeof( VertexAttribute ).GetMethod( "GetColorFormat", flags ).Invoke( attribute, null );
		fields.SetValue( constructor.Invoke( new object[] { semantics[(int)attribute.Type], attribute.SemanticIndex, offset, format } ), i );
		offset += attribute.Components * sizes[(int)attribute.Format];
	}
	if ( offset != stride ) throw new InvalidOperationException( "Diagnostic declaration stride differs from fixture storage." );
	var prepared = Activator.CreateInstance( preparedType, true );
	preparedType.GetProperty( "VertexStride", flags ).SetValue( prepared, stride );
	preparedType.GetProperty( "VertexCount", flags ).SetValue( prepared, 3 );
	preparedType.GetProperty( "VertexBytes", flags ).SetValue( prepared, bytes );
	preparedType.GetProperty( "VertexFields", flags ).SetValue( prepared, fields );
	preparedType.GetMethod( "CreateVertexBuffer", flags ).Invoke( prepared, new object[] { mesh } );
	return prepared;
}
object RegisterDeclaration( Model model, object prepared, bool register )
{
	var engine = typeof( Model ).Assembly; var registry = engine.GetType( "Sandbox.GltfSkinningMeshFormats", false );
	if ( registry is null ) return new { registered = false, reason = "Installed producer predates layout registration." };
	if ( !register ) return new { registered = false, reason = "Six influences remain an explicit unsupported positive acceptance case." };
	var preparedType = prepared.GetType(); var layoutType = engine.GetType( "Sandbox.GltfSkinningVertexLayout", true ); var fields = preparedType.GetProperty( "VertexFields", flags ).GetValue( prepared );
	var stride = (int)preparedType.GetProperty( "VertexStride", flags ).GetValue( prepared ); var count = (int)preparedType.GetProperty( "VertexCount", flags ).GetValue( prepared ); var bytes = (byte[])preparedType.GetProperty( "VertexBytes", flags ).GetValue( prepared );
	var descriptor = layoutType.GetConstructors( flags ).Single( value => value.GetParameters().Length == 4 ).Invoke( new object[] { stride, count, bytes.Length, fields } );
	var dictionary = (System.Collections.IDictionary)Activator.CreateInstance( typeof( System.Collections.Generic.Dictionary<,> ).MakeGenericType( typeof( string ), layoutType ) );
	var meshName = model.MeshInfo.Meshes.Single().Name; dictionary.Add( meshName, descriptor ); registry.GetMethod( "Register", flags ).Invoke( null, new object[] { model, dictionary } );
	var format = registry.GetMethod( "ForModel", flags ).Invoke( null, new object[] { model, 0 } );
	return new { registered = true, native_mesh_name = meshName, stride, vertex_count = count, byte_count = bytes.Length, format = format.ToString(), format_value = Convert.ToInt32( format ) };
}
object ShaderArtifact()
{
	var sourcePath = System.IO.Path.Combine( engineDirectory.FullName, "game/core/shaders/skinning_cs.shader" ); var compiledPath = Editor.AssetSystem.FindByPath( "shaders/skinning_cs.shader" )?.GetCompiledFile( true );
	string Digest( string path ) => path is not null && System.IO.File.Exists( path ) ? Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( System.IO.File.ReadAllBytes( path ) ) ) : null;
	return new { source_path = sourcePath, source_sha256 = Digest( sourcePath ), compiled_path = compiledPath, compiled_sha256 = Digest( compiledPath ) };
}
await MainThread.Wait();
if ( Graphics.IsActive || Game.IsPlaying ) throw new InvalidOperationException( "Run in the idle editor outside rendering." );
try
{
	material.SetFeature( "F_RENDER_BACKFACES", 1 );
	scene = Scene.CreateEditorScene();
	CameraComponent camera;
	using ( scene.Push() )
	{
		camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.WorldPosition = new Vector3( 120, 8, 0 );
		camera.WorldRotation = Rotation.FromYaw( 180 );
		camera.BackgroundColor = Color.Black; camera.ZNear = 1; camera.FieldOfView = 50;
		camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo;
	}
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF native skin exact-weight oracle", Size = new Vector2( 384, 384 ) };
	frameCallback = () => frames++;
	preview.OnPreFrame += frameCallback;
	preview.Show();
	async Task WaitFrames()
	{
		var start = frames; var timer = System.Diagnostics.Stopwatch.StartNew();
		while ( frames < start + 3 )
		{
			if ( timer.Elapsed > TimeSpan.FromSeconds( 8 ) ) throw new TimeoutException( "Diagnostic preview stopped receiving frames." );
			await Task.Delay( 30, cancellationToken ); await MainThread.Wait();
			if ( Game.IsPlaying ) throw new InvalidOperationException( "User entered play mode; stop the private diagnostic." );
		}
	}
	async Task<Color[]> Capture( string name )
	{
		phase = name; await WaitFrames();
		if ( Graphics.IsActive || Game.IsPlaying ) throw new InvalidOperationException( "No safe idle rendering boundary." );
		using var target = Texture.CreateRenderTarget().WithSize( 192, 192 ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "skin_pose_" + Guid.NewGuid().ToString( "N" ) );
		using ( scene.Push() ) { target.Clear( Color.Magenta ); if ( !(bool)render.Invoke( managedRenderer, new object[] { camera, target } ) ) throw new InvalidOperationException( "Capture submission failed." ); }
		await WaitFrames();
		using var bitmap = target.GetBitmap();
		if ( bitmap is null ) throw new InvalidOperationException( "Capture readback failed." );
		var path = System.IO.Path.Combine( output, name + ".png" );
		var png = bitmap.ToPng(); System.IO.File.WriteAllBytes( path, png );
		captures.Add( new { name, path, sha256 = Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( png ) ), frames, engineFrame = Application.FrameCount } );
		return bitmap.GetPixels();
	}
	bool Visible( Color p ) => p.r > .03f || p.g > .03f || p.b > .03f;
	var background = await Capture( "empty_initial" );
	if ( background.Any( Visible ) ) throw new InvalidOperationException( "Fresh-target empty control is not black." );
	foreach ( var definition in new[] { (Name: "unorm8_four", Floating: false, Sets: 1, Index16: false, Weight16: false), (Name: "float_four", Floating: true, Sets: 1, Index16: false, Weight16: false), (Name: "u16_unorm8_four", Floating: false, Sets: 1, Index16: true, Weight16: false), (Name: "u16_float_four", Floating: true, Sets: 1, Index16: true, Weight16: false), (Name: "unorm16_four", Floating: false, Sets: 1, Index16: false, Weight16: true), (Name: "u16_unorm16_four", Floating: false, Sets: 1, Index16: true, Weight16: true), (Name: "float_six", Floating: true, Sets: 2, Index16: false, Weight16: false) } )
	{
		// The diagnostic POD is storage only. Its size is checked against the explicit
		// declaration; it is not a representation used by the glTF loader.
		var storage = new (Matrix First, Matrix Second)[3];
		var stride = System.Runtime.InteropServices.MemoryMarshal.AsBytes( storage.AsSpan() ).Length / storage.Length;
		var layout = new System.Collections.Generic.List<VertexAttribute> { new( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ), new( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ), new( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 4 ) };
		var used = 40;
		for ( var set = 0; set < definition.Sets; set++ )
		{
			layout.Add( new( VertexAttributeType.BlendIndices, definition.Index16 ? VertexAttributeFormat.UInt16 : VertexAttributeFormat.UInt8, 4, set ) );
			layout.Add( new( VertexAttributeType.BlendWeights, definition.Weight16 ? VertexAttributeFormat.UInt16 : definition.Floating ? VertexAttributeFormat.Float32 : VertexAttributeFormat.UInt8, 4, set ) );
			used += (definition.Index16 ? 8 : 4) + (definition.Weight16 ? 8 : definition.Floating ? 16 : 4);
		}
		for ( var set = 0; used < stride; set++ ) { var components = Math.Min( 4, (stride - used) / 4 ); if ( components == 0 ) throw new InvalidOperationException( "Unexpected POD stride." ); layout.Add( new( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, components, set ) ); used += components * 4; }
		var weights = definition.Sets == 2 ? new[] { .1f, .2f, .1f, .2f, .1f, .3f, 0f, 0f } : definition.Weight16 ? new[] { 19660f / 65535, 45875f / 65535, 0f, 0f } : definition.Floating ? new[] { .3f, .7f, 0f, 0f } : new[] { 85f / 255, 170f / 255, 0f, 0f };
		{
			var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes( storage.AsSpan() );
			void Float( int at, float value ) => System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( System.Runtime.InteropServices.MemoryMarshal.AsBytes( storage.AsSpan() ).Slice( at, 4 ), value );
			for ( var vertex = 0; vertex < 3; vertex++ )
			{
				var at = vertex * stride;
				Float( at, sourcePoints[vertex].x ); Float( at + 4, sourcePoints[vertex].y ); Float( at + 8, sourcePoints[vertex].z );
				Float( at + 12, 1 ); Float( at + 28, 1 ); Float( at + 36, -1 );
				var attribute = at + 40;
				for ( var set = 0; set < definition.Sets; set++ )
				{
					var indexBytes = definition.Index16 ? 8 : 4;
					for ( var component = 0; component < 4; component++ )
					{
						var joint = (ushort)Math.Min( set * 4 + component, definition.Sets == 2 ? 5 : 1 );
						if ( definition.Index16 ) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.Slice( attribute + component * 2, 2 ), joint ); else bytes[attribute + component] = (byte)joint;
					}
					for ( var component = 0; component < 4; component++ )
						if ( definition.Floating ) Float( attribute + indexBytes + component * 4, weights[set * 4 + component] );
						else if ( definition.Weight16 ) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.Slice( attribute + indexBytes + component * 2, 2 ), component == 0 ? (ushort)19660 : component == 1 ? (ushort)45875 : (ushort)0 );
						else bytes[attribute + indexBytes + component] = component == 0 ? (byte)85 : component == 1 ? (byte)170 : (byte)0;
					attribute += indexBytes + (definition.Weight16 ? 8 : definition.Floating ? 16 : 4);
				}
			}
		}
		var nativeMesh = new Mesh( definition.Name, material );
		var prepared = UploadRawFixture( nativeMesh, stride, System.Runtime.InteropServices.MemoryMarshal.AsBytes( storage.AsSpan() ).ToArray(), layout.ToArray() );
		nativeMesh.CreateIndexBuffer( 3, new int[] { 0, 1, 2 }.AsSpan() );
		var diagnosticBounds = new BBox( new Vector3( -100 ), new Vector3( 100 ) );
		nativeMesh.Bounds = diagnosticBounds;
		var builder = Model.Builder.WithName( "skin_gpu_oracle_" + Guid.NewGuid().ToString( "N" ) ).AddMesh( nativeMesh );
		var boneCount = definition.Sets == 2 ? 6 : 2;
		for ( var bone = 0; bone < boneCount; bone++ ) builder.AddBone( "bone_" + bone, Vector3.Zero, Rotation.Identity, diagnosticBounds );
		var model = builder.Create(); ownedModels.Add( model );
		if ( model is null || !model.IsValid || model.IsError ) throw new InvalidOperationException( "Native diagnostic model failed: " + definition.Name );
		var registered = RegisterDeclaration( model, prepared, definition.Sets == 1 ); declarations.Add( new { definition.Name, registration = registered } );
		foreach ( var posed in new[] { false, true } )
		{
			var label = definition.Name + (posed ? "_posed" : "_identity");
			var transforms = new Transform[boneCount];
			for ( var bone = 0; bone < boneCount; bone++ ) transforms[bone] = posed ? new Transform( new Vector3( 0, 8 * bone, 3 * bone ), Rotation.FromAxis( Vector3.Forward, 17 * bone ) ) : Transform.Zero;
			shown = new SceneModel( scene.SceneWorld, model, Transform.Zero );
			shown.UseAnimGraph = false; shown.UpdateToBindPose();
			for ( var bone = 0; bone < boneCount; bone++ ) shown.SetBoneWorldTransform( bone, transforms[bone] );
			shown.Bounds = diagnosticBounds;
			var actual = await Capture( label + "_actual" );
			object dispatchMaterial = null;
			var registry = typeof( Model ).Assembly.GetType( "Sandbox.GltfSkinningMeshFormats", false );
			if ( registry is not null && definition.Sets == 1 )
			{
				var selectedFormat = registry.GetMethod( "ForModel", flags ).Invoke( null, new object[] { model, 0 } ); var selectedValue = Convert.ToInt32( selectedFormat );
				var states = (System.Collections.IDictionary)managedRenderer.GetType().GetField( "cameras", flags ).GetValue( managedRenderer ); var state = states[camera];
				if ( state is null ) throw new InvalidOperationException( "Actual camera render state missing." );
				var system = state.GetType().GetField( "System", flags ).GetValue( state ); var feature = system.GetType().GetField( "meshes", flags ).GetValue( system ); Material compute;
				if ( selectedValue == 0 ) compute = (Material)feature.GetType().GetField( "skinningShader", flags ).GetValue( feature );
				else { var variants = (System.Collections.IDictionary)feature.GetType().GetField( "skinningVariants", flags ).GetValue( feature ); compute = (Material)variants[selectedFormat]; }
				if ( compute is not { IsValid: true, IsError: false } || compute.GetFeature( "F_SKIN_WEIGHT_FORMAT" ) != selectedValue ) throw new InvalidOperationException( "Actual dispatched compute material variant does not match the uploaded weight format." );
				dispatchMaterial = new { expected_format = selectedFormat.ToString(), feature_value = compute.GetFeature( "F_SKIN_WEIGHT_FORMAT" ), resource = compute.ResourcePath, shader = compute.ShaderName, shader_artifact = ShaderArtifact() };
			}
			shown.Delete(); shown = null;
			var expectedVertices = new Vertex[3];
			for ( var vertex = 0; vertex < 3; vertex++ )
			{
				var expected = Vector3.Zero;
				for ( var influence = 0; influence < weights.Length; influence++ ) if ( weights[influence] != 0 ) expected += transforms[Math.Min( influence, boneCount - 1 )].PointToWorld( sourcePoints[vertex] ) * weights[influence];
				expectedVertices[vertex] = new Vertex( expected ) { Normal = Vector3.Forward, Tangent = new Vector4( 0, 1, 0, -1 ) };
			}
			var controlMesh = new Mesh( "independent_weighted_reference", material ); controlMesh.CreateVertexBuffer( 3, expectedVertices.AsSpan() ); controlMesh.CreateIndexBuffer( 3, new int[] { 0, 1, 2 }.AsSpan() ); controlMesh.Bounds = diagnosticBounds;
			var control = Model.Builder.WithName( "skin_gpu_reference_" + Guid.NewGuid().ToString( "N" ) ).AddMesh( controlMesh ).Create(); ownedModels.Add( control );
			shown = new SceneModel( scene.SceneWorld, control, Transform.Zero );
			var expectedPixels = await Capture( label + "_reference" ); shown.Delete(); shown = null;
			var empty = await Capture( label + "_empty_after" );
			var intersection = 0; var union = 0; var actualCoverage = 0; var referenceCoverage = 0;
			for ( var pixel = 0; pixel < actual.Length; pixel++ ) { var a = Visible( actual[pixel] ); var b = Visible( expectedPixels[pixel] ); if ( a ) actualCoverage++; if ( b ) referenceCoverage++; if ( a && b ) intersection++; if ( a || b ) union++; }
			var iou = union > 0 ? (double)intersection / union : 0;
			results.Add( new { label, actualCoverage, referenceCoverage, iou, emptyPassed = !empty.Any( Visible ), passed = actualCoverage > 40 && referenceCoverage > 40 && iou > .97 && !empty.Any( Visible ), weights, dispatch_material = dispatchMaterial } );
		}
	}
}
catch ( Exception e ) { failure = e.ToString(); }
finally
{
	await MainThread.Wait();
	if ( preview is not null && preview.IsValid && frameCallback is not null ) preview.OnPreFrame -= frameCallback;
	if ( preview is not null && preview.IsValid ) { preview.Close(); preview.Destroy(); }
	if ( shown.IsValid() ) shown.Delete(); scene?.Destroy();
	var destroy = typeof( Model ).GetMethod( "Destroy", flags );
	foreach ( var model in ownedModels ) if ( model is not null ) destroy.Invoke( model, null );
	if ( material is not null && material.IsValid ) typeof( Material ).GetMethod( "Destroy", flags ).Invoke( material, null );
}
var receipt = new { purpose = "Native procedural model posed raster oracle through installed raw byte upload, not glTF acceptance", uploadRoute = "Installed PreparedPrimitive.CreateVertexBuffer -> Mesh.CreateRawVertexBuffer with independent diagnostic bytes", captureRenderer, rendererMvid = rendererAssembly.ManifestModule.ModuleVersionId, phase, failure, output, engineMvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId, declarations, shader_artifact = ShaderArtifact(), results, captures };
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
