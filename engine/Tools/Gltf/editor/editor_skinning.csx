// Async method body for an already-running s&box editor. Isolated assets and
// preview only. Independent bind/posed silhouettes test native construction;
// a separate Model.Load result records whether glTF integration is present.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot find checkout from loaded engine: " + engineLocation );
var runId = Guid.NewGuid().ToString( "N" );
var diagnosticClear = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAMERA_CLEAR" ) == "1";
var managedCapture = Environment.GetEnvironmentVariable( "SBOX_GLTF_CAPTURE_RENDERER" ) == "managed";
var outputGroup = "editor-skinning" + (diagnosticClear ? "-camera-clear" : "") + (managedCapture ? "-managed" : "");
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", outputGroup, runId );
System.IO.Directory.CreateDirectory( output );
var path = "gltf-skinning/" + runId + "/two-bone.gltf";
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var cases = new System.Collections.Generic.List<object>();
var failures = new System.Collections.Generic.List<string>();
var cleanup = new System.Collections.Generic.List<string>();
var captures = new System.Collections.Generic.List<object>();
var models = new System.Collections.Generic.List<Model>();
var meshes = new System.Collections.Generic.List<Mesh>();
var logs = new System.Collections.Generic.List<string>();
var fixtureHashes = new System.Collections.Generic.Dictionary<string, string>();
var modelSkinMetadata = new System.Collections.Generic.List<object>();
var fixtureInverseBinds = new System.Collections.Generic.List<float[]>();
int[] fixtureJointOrder = null;
string rendererMetadataStatus = "not attempted", rendererMetadataError = null, rendererLocation = null, rendererLoadedMvid = null, rendererDiskMvid = null;
string phase = "initialize", error = null;
object managedRenderer = null;
System.Reflection.MethodInfo managedRenderToTexture = null;
bool? managedViewportEnabled = null;
object redirectObject = null;
IDisposable redirect = null;
BaseFileSystem mountedAggregate = null, mounted = null;
System.Reflection.MethodInfo unmount = null, removeRedirect = null, removeLogHandler = null;
Action<LogEvent> logHandler = null;
bool mountedOwned = false, redirectAdded = false;
Scene scene = null;
Editor.SceneRenderingWidget preview = null;
CameraComponent camera = null;
Sandbox.Rendering.CommandList clear = null;
Texture target = null;
Material material = null;
SceneModel shown = null;
Action previewCallback = null;
Exception callbackError = null;
int previewFrames = 0, modelUpdates = 0;
bool animate = false, pose = false;
bool cpuPassed = false, nativePassed = false, loadedPassed = false;
bool? loadedGeometryPassed = null;
var startFrame = Application.FrameCount;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Clean( string name, Action action ) { try { action(); } catch ( Exception ex ) { cleanup.Add( name + ": " + ex ); } }
bool Valid( Model value ) => value is { IsValid: true } && !value.IsError && !ReferenceEquals( value, Model.Error );
bool Different( Color a, Color b ) => MathF.Abs( a.r - b.r ) > .03f || MathF.Abs( a.g - b.g ) > .03f || MathF.Abs( a.b - b.b ) > .03f;
int Difference( Color[] a, Color[] b ) => a.Zip( b, ( x, y ) => Different( x, y ) ).Count( x => x );
int Nonblack( Color[] pixels ) => pixels.Count( x => Different( x, Color.Black ) );
const string fixture = """{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,mpkZvwAAAL8AAAAAmpkZPwAAAL8AAAAAAAAAAAAAAD8AAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AAEAAAABAAAAAQAA/wAAAEC/AAAA/wAAAAAAAAAAAAAAAAAA/wAAAEAAAAAAAAAAAQAAAAEAAAABAAAAAAAAAL8AAAD/AAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAAAAAAAAAAAAgD8AAAAAAAAAAM3MzL0AAAAAAACAPwAAgD8AAAAAAAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAAAAAAIA/AAAAAAAAAACamZm+AAAAAAAAgD8=","byteLength":272}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":12},{"buffer":0,"byteOffset":84,"byteLength":12},{"buffer":0,"byteOffset":96,"byteLength":12},{"buffer":0,"byteOffset":108,"byteLength":12},{"buffer":0,"byteOffset":120,"byteLength":12},{"buffer":0,"byteOffset":132,"byteLength":12},{"buffer":0,"byteOffset":144,"byteLength":128}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[-0.6,-0.5,0],"max":[0.6,0.5,0]},{"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5121,"count":3,"type":"VEC4"},{"bufferView":3,"componentType":5121,"count":3,"type":"VEC4","normalized":true},{"bufferView":4,"componentType":5121,"count":3,"type":"VEC4"},{"bufferView":5,"componentType":5121,"count":3,"type":"VEC4","normalized":true},{"bufferView":6,"componentType":5121,"count":3,"type":"VEC4"},{"bufferView":7,"componentType":5121,"count":3,"type":"VEC4","normalized":true},{"bufferView":8,"componentType":5126,"count":2,"type":"MAT4"}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"JOINTS_0":2,"WEIGHTS_0":3}}]}],"nodes":[{"translation":[0,0.1,0],"children":[1]},{"translation":[0,0.2,0]},{"mesh":0,"skin":0}],"skins":[{"inverseBindMatrices":8,"skeleton":0,"joints":[0,1]}],"scenes":[{"nodes":[0,2]}],"scene":0}""";
var source = System.Text.Encoding.UTF8.GetBytes( fixture );
var sourceHash = Hash( source );
try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run inside the existing editor outside a Graphics scope." );
	Require( sourceHash == "cd72e7f1963bf788edbf82a5ca1172c27ba6df9ec3fd39f28b57994f57da873c", "Fixture hash mismatch." );
	phase = "cpu-skinning-and-multiple-sets";
	using ( var fixtureDocument = Sandbox.Gltf.GltfReader.Read( source ) )
	{
		var skin = fixtureDocument.ReadSkin( 0 );
		fixtureJointOrder = skin.Joints.ToArray();
		foreach ( var matrix in skin.InverseBindMatrices )
			fixtureInverseBinds.Add( [matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44] );
	}
	// The inverse binds negate joint world translations (0,.1,0)/(0,.3,0).
	// Moving the child .3 along glTF X displaces vertices by exactly .3*w1.
	// The multi-set variant keeps the same two nonzero influences but puts the
	// child in WEIGHTS_1; this does not establish support for >4 influences.
	var multi = fixture.Replace( "\"JOINTS_0\":2,\"WEIGHTS_0\":3", "\"JOINTS_0\":4,\"WEIGHTS_0\":5,\"JOINTS_1\":6,\"WEIGHTS_1\":7", StringComparison.Ordinal );
	var posed = fixture.Replace( "\"translation\":[0,0.2,0]", "\"translation\":[0.3,0.2,0]", StringComparison.Ordinal );
	var multiPosed = multi.Replace( "\"translation\":[0,0.2,0]", "\"translation\":[0.3,0.2,0]", StringComparison.Ordinal );
	Require( multi != fixture && posed != fixture && multiPosed != multi, "Fixture substitutions failed." );
	var original = new[] { new System.Numerics.Vector3( -.6f, -.5f, 0 ), new System.Numerics.Vector3( .6f, -.5f, 0 ), new System.Numerics.Vector3( 0, .5f, 0 ) };
	var childWeight = new[] { 0f, 191f / 255f, 1f };
	foreach ( var entry in new[] { (Name: "single-bind", Json: fixture, Posed: false), (Name: "single-posed", Json: posed, Posed: true), (Name: "multiple-bind", Json: multi, Posed: false), (Name: "multiple-posed", Json: multiPosed, Posed: true) } )
	{
		var input = System.Text.Encoding.UTF8.GetBytes( entry.Json );
		var compiled = Sandbox.Gltf.GltfCompiler.Compile( input );
		// Retain exact self-contained fixtures so an independent official glTF
		// validator can check the test inputs and packer output later. This is
		// container packaging only, not GPU optimization.
		foreach ( var form in new[] { (Name: "source", Extension: ".gltf", Bytes: input), (Name: "compiled", Extension: ".glb", Bytes: compiled) } )
		{
			var fixtureName = entry.Name + form.Extension;
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( output, fixtureName ), form.Bytes );
			fixtureHashes.Add( fixtureName, Hash( form.Bytes ) );
			using var document = Sandbox.Gltf.GltfReader.Read( form.Bytes );
			for ( var i = 0; i < 3; i++ )
			{
				var expected = original[i] + new System.Numerics.Vector3( entry.Posed ? .3f * childWeight[i] : 0, 0, 0 );
				var actual = document.EvaluateVertexPosition( 2, 0, i );
				Require( System.Numerics.Vector3.Distance( actual, expected ) < .00001f, $"{entry.Name} {form.Name} vertex {i}: {actual}, expected {expected}." );
				cases.Add( new { entry.Name, form = form.Name, vertex = i, expected = new[] { expected.X, expected.Y, expected.Z }, actual = new[] { actual.X, actual.Y, actual.Z }, passed = true } );
			}
		}
	}
	cpuPassed = true;

	phase = "mount-and-observe-model-load";
	var redirectType = typeof( Model ).Assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
	redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
	redirect = (IDisposable)redirectObject;
	removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	var physical = System.IO.Path.Combine( output, "two-bone.gltf" );
	System.IO.File.WriteAllBytes( physical, source );
	redirectType.GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { "/" + path, physical } ); redirectAdded = true;
	var ctor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( c => { var p = c.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
	mounted = (BaseFileSystem)ctor.Invoke( new[] { redirectObject } );
	mountedAggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Managed mount aggregate unavailable." );
	unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ).Invoke( mountedAggregate, new object[] { mounted } ); mountedOwned = true;
	Require( mountedAggregate.FileExists( path ) && Hash( mountedAggregate.ReadAllBytes( path ).ToArray() ) == sourceHash, "Mounted fixture differs." );
	var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging", true );
	var logEvent = logging.GetEvent( "OnMessage", flags );
	logHandler = entry => { var message = (entry.Message ?? "") + (entry.Exception is null ? "" : " " + entry.Exception.Message); if ( message.Contains( runId, StringComparison.OrdinalIgnoreCase ) || message.Contains( "skin", StringComparison.OrdinalIgnoreCase ) ) lock ( logs ) if ( logs.Count < 30 ) logs.Add( entry.Level + ": " + message ); };
	logEvent.GetAddMethod( true ).Invoke( null, new object[] { logHandler } ); removeLogHandler = logEvent.GetRemoveMethod( true );
	Model loaded = null; string loadError = null;
	try { loaded = Model.Load( path ); } catch ( Exception ex ) { loadError = ex.ToString(); }
	await Task.Delay( 150, cancellationToken ); await MainThread.Wait();
	loadedPassed = Valid( loaded );
	if ( loadedPassed )
	{
		models.Add( loaded );
		var info = loaded.MeshInfo;
		var loadedMaterial = info?.Meshes.SelectMany( x => x.DrawCalls ).Select( x => x.Material ).FirstOrDefault( x => x is { IsValid: true } && !x.IsError );
		loadedGeometryPassed = info is not null && info.TotalTriangles == 1 && info.TotalDrawCalls > 0 && loadedMaterial is not null;
		cases.Add( new { name = "loaded model geometry and material", passed = loadedGeometryPassed, triangles = info?.TotalTriangles, draw_calls = info?.TotalDrawCalls, material_shader = loadedMaterial?.ShaderName } );
	}
	string[] loadLogs; lock ( logs ) loadLogs = logs.ToArray();
	cases.Add( new { name = "Model.Load skin integration", path, loaded = loadedPassed, load_error = loadError, diagnostics = loadLogs, status = loadedPassed ? "loaded; deformation not yet proven" : "rejected or unavailable" } );

	phase = "build-native-two-bone-and-independent-references";
	var stock = Material.Load( "materials/default/white.vmat" );
	Require( stock is { IsValid: true } && !stock.IsError, "Stock white material unavailable." );
	material = stock.CreateCopy( "gltf_skinning_" + runId );
	material.SetFeature( "F_RENDER_BACKFACES", 1 );
	var bounds = new BBox( new Vector3( -100, -100, -100 ), new Vector3( 100, 100, 100 ) );
	const float units = 1f / .0254f;
	Vertex MakeVertex( int i, bool moved ) => new Vertex
	{
		// glTF (x,y,z) -> s&box (z,x,y), independent of the evaluator.
		Position = new Vector3( 0, (original[i].X + (moved ? .3f * childWeight[i] : 0)) * units, original[i].Y * units ),
		Normal = new Vector3( 1, 0, 0 ), Tangent = new Vector4( 0, 1, 0, 1 ), Color = Color32.White
	};
	var layout = Vertex.Layout.Concat( new[] { new VertexAttribute( VertexAttributeType.BlendIndices, VertexAttributeFormat.UInt8, 4 ), new VertexAttribute( VertexAttributeType.BlendWeights, VertexAttributeFormat.UInt8, 4 ) } ).ToArray();
	Require( System.Runtime.CompilerServices.Unsafe.SizeOf<(Vertex, Color32, Color32)>() == 84, "Unexpected unmanaged skin vertex stride." );
	var skinnedVertices = new System.Collections.Generic.List<(Vertex, Color32, Color32)>
	{
		( MakeVertex( 0, false ), new Color32( 0, 1, 0, 0 ), new Color32( 255, 0, 0, 0 ) ),
		( MakeVertex( 1, false ), new Color32( 0, 1, 0, 0 ), new Color32( 64, 191, 0, 0 ) ),
		( MakeVertex( 2, false ), new Color32( 0, 1, 0, 0 ), new Color32( 0, 255, 0, 0 ) )
	};
	// ValueTuple has automatic layout. A matching total size alone does not
	// establish field offsets; verify the bytes expected by the explicit layout
	// before interpreting any failed render as a native skinning limitation.
	{
		var packed = System.Runtime.InteropServices.MemoryMarshal.AsBytes( System.Runtime.InteropServices.CollectionsMarshal.AsSpan( skinnedVertices ) );
		var weights = new[] { new byte[] { 255, 0, 0, 0 }, new byte[] { 64, 191, 0, 0 }, new byte[] { 0, 255, 0, 0 } };
		for ( var i = 0; i < skinnedVertices.Count; i++ )
		{
			var vertex = packed.Slice( i * 84, 84 );
			var expectedPosition = MakeVertex( i, false ).Position;
			Require( System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian( vertex ) == expectedPosition.x &&
				System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian( vertex[4..] ) == expectedPosition.y &&
				System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian( vertex[8..] ) == expectedPosition.z &&
				vertex.Slice( 76, 4 ).SequenceEqual( new byte[] { 0, 1, 0, 0 } ) && vertex.Slice( 80, 4 ).SequenceEqual( weights[i] ),
				"The skin tuple's field offsets do not match the explicit native vertex layout." );
		}
	}
	Mesh BuildMesh( bool skinned, bool moved )
	{
		var mesh = new Mesh( material ); meshes.Add( mesh );
		if ( skinned ) mesh.CreateVertexBuffer( 3, layout, skinnedVertices );
		else mesh.CreateVertexBuffer( 3, new[] { MakeVertex( 0, moved ), MakeVertex( 1, moved ), MakeVertex( 2, moved ) } );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } ); mesh.Bounds = bounds;
		return mesh;
	}
	Model BuildModel( Mesh mesh, string name, bool skeleton )
	{
		var builder = Model.Builder.WithName( "gltf_skin_" + name + "_" + runId ).AddMesh( mesh ).WithViewBounds( bounds );
		if ( skeleton )
		{
			builder.AddBone( "root", new Vector3( 0, 0, .1f * units ), Rotation.Identity, bounds );
			builder.AddBone( "child", new Vector3( 0, 0, .2f * units ), Rotation.Identity, bounds, "root" );
		}
		var model = builder.Create(); Require( Valid( model ), "Failed to construct " + name ); models.Add( model ); return model;
	}
	var native = BuildModel( BuildMesh( true, false ), "native", true );
	var bindReference = BuildModel( BuildMesh( false, false ), "bind_reference", false );
	var posedReference = BuildModel( BuildMesh( false, true ), "posed_reference", false );
	// Read the renderer's established metadata helper; do not infer a mapping from
	// matching counts or names. Missing metadata is an observation, not a reason
	// to suppress the independent image comparisons below.
	System.Reflection.MethodInfo readMeshSkin = null;
	try
	{
		var rendererAssembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault( assembly => !assembly.IsDynamic &&
			assembly.GetName().Name == "Sandbox.SceneRenderer" && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) == System.Runtime.Loader.AssemblyLoadContext.Default );
		if ( rendererAssembly is null ) rendererMetadataStatus = "renderer assembly not loaded";
		else
		{
			rendererLocation = rendererAssembly.Location;
			rendererLoadedMvid = rendererAssembly.ManifestModule.ModuleVersionId.ToString();
			using ( var stream = System.IO.File.OpenRead( rendererLocation ) )
			using ( var pe = new System.Reflection.PortableExecutable.PEReader( stream ) )
			{
				var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
				rendererDiskMvid = metadata.GetGuid( metadata.GetModuleDefinition().Mvid ).ToString();
			}
			if ( rendererLoadedMvid != rendererDiskMvid ) rendererMetadataStatus = "loaded renderer differs from disk";
			else
			{
				var contextType = rendererAssembly.GetType( "Sandbox.SceneRenderer.Gpu.RenderContext", false );
				readMeshSkin = contextType?.GetMethod( "ReadMeshSkin", flags, null, new[] { typeof( Model ), typeof( int ) }, null );
				rendererMetadataStatus = readMeshSkin is null ? "ReadMeshSkin helper unavailable" : "helper available";
			}
		}
	}
	catch ( Exception ex ) { rendererMetadataStatus = "renderer metadata unavailable"; rendererMetadataError = ex.ToString(); }
	void RecordSkinMetadata( Model model, string label )
	{
		var bones = new System.Collections.Generic.List<object>();
		var modelMeshes = new System.Collections.Generic.List<object>();
		string modelError = null;
		try
		{
			for ( var i = 0; i < model.BoneCount; i++ ) bones.Add( new { master_index = i, name = model.GetBoneName( i ), parent_master_index = model.GetBoneParent( i ) } );
			var info = model.MeshInfo ?? throw new InvalidOperationException( "Model mesh info unavailable." );
			for ( var m = 0; m < info.Meshes.Length; m++ )
			{
				try
				{
					if ( readMeshSkin is null ) { modelMeshes.Add( new { model_mesh = m, status = "renderer metadata helper unavailable" } ); continue; }
					var skin = readMeshSkin.Invoke( null, new object[] { model, m } );
					if ( skin is null ) { modelMeshes.Add( new { model_mesh = m, status = "native helper returned no skin metadata" } ); continue; }
					var skinType = skin.GetType();
					var masters = (int[])(skinType.GetField( "MasterBones", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "MasterBones unavailable." ));
					var matrices = (Matrix3x4[])(skinType.GetField( "InverseBindPoses", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "InverseBindPoses unavailable." ));
					var count = (int)(skinType.GetProperty( "BoneCount", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "BoneCount unavailable." ));
					var blendCount = (int)(skinType.GetField( "BlendWeightCount", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "BlendWeightCount unavailable." ));
					var vertexCount = (int)(skinType.GetField( "VertexCount", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "VertexCount unavailable." ));
					if ( masters.Length != count || matrices.Length != count ) throw new InvalidOperationException( "Native skin metadata arrays disagree with BoneCount." );
					var renderBones = new System.Collections.Generic.List<object>();
					bool FiniteRow( Vector4 row ) => float.IsFinite( row.x ) && float.IsFinite( row.y ) && float.IsFinite( row.z ) && float.IsFinite( row.w );
					for ( var r = 0; r < count; r++ )
					{
						var matrix = matrices[r];
						renderBones.Add( new { render_bone_index = r, master_bone_index = masters[r], master_index_in_range = masters[r] >= 0 && masters[r] < model.BoneCount,
							inverse_bind_finite = FiniteRow( matrix.Row0 ) && FiniteRow( matrix.Row1 ) && FiniteRow( matrix.Row2 ), inverse_bind_rows = new[] { new[] { matrix.Row0.x, matrix.Row0.y, matrix.Row0.z, matrix.Row0.w }, new[] { matrix.Row1.x, matrix.Row1.y, matrix.Row1.z, matrix.Row1.w }, new[] { matrix.Row2.x, matrix.Row2.y, matrix.Row2.z, matrix.Row2.w } } } );
					}
					modelMeshes.Add( new { model_mesh = m, status = "available", bone_count = count, blend_weight_count = blendCount, vertex_count = vertexCount, render_bones = renderBones } );
				}
				catch ( Exception ex ) { modelMeshes.Add( new { model_mesh = m, status = "unreadable", error = ex.ToString() } ); }
			}
		}
		catch ( Exception ex ) { modelError = ex.ToString(); }
		modelSkinMetadata.Add( new { model = label, status = modelError is null ? "observed" : "unreadable", error = modelError, bones, meshes = modelMeshes } );
	}
	RecordSkinMetadata( native, "runtime_built_probe" );
	if ( loadedPassed ) RecordSkinMetadata( loaded, "loaded_gltf" );
	Require( native.BoneCount == 2 && native.GetBoneName( 0 ) == "root" && native.GetBoneName( 1 ) == "child" && native.GetBoneParent( 1 ) == 0, "Unexpected native bone order or hierarchy." );
	var bindUpdate = typeof( SceneModel ).GetMethod( "UpdateToBindPose", flags, null, new[] { typeof( Action ) }, null ) ?? throw new InvalidOperationException( "Bind-pose callback unavailable." );
	var setParent = typeof( SceneModel ).GetMethod( "SetParentSpaceBone", flags ) ?? throw new InvalidOperationException( "Parent-space bone setter unavailable." );

	phase = "private-editor-preview";
	var managedType = typeof( Model ).Assembly.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
	managedViewportEnabled = (bool)managedType.GetProperty( "Enabled" ).GetValue( null );
	if ( managedCapture )
	{
		managedRenderer = managedType.GetProperty( "Renderer" ).GetValue( null ) ?? throw new InvalidOperationException( "Managed renderer unavailable." );
		managedRenderToTexture = managedRenderer.GetType().GetMethod( "RenderToTexture", new[] { typeof( CameraComponent ), typeof( Texture ) } ) ?? throw new InvalidOperationException( "Managed texture capture unavailable." );
	}
	scene = Scene.CreateEditorScene();
	using ( scene.Push() )
	{
		camera = scene.CreateObject().Components.Create<CameraComponent>(); camera.WorldPosition = new Vector3( 80, 0, 0 ); camera.WorldRotation = Rotation.FromYaw( 180 );
		camera.BackgroundColor = Color.Black; camera.ZNear = 1; camera.FieldOfView = 50; camera.EnablePostProcessing = false; camera.DebugMode = SceneCameraDebugMode.Albedo;
	}
	if ( diagnosticClear ) { clear = new Sandbox.Rendering.CommandList( "glTF skin diagnostic clear" ); clear.Clear( Color.Black, true, false, false ); camera.AddCommandList( clear, Sandbox.Rendering.Stage.AfterDepthPrepass ); }
	preview = new Editor.SceneRenderingWidget { Scene = scene, Camera = camera, WindowTitle = "glTF two-bone skinning probe", Size = new Vector2( 384, 384 ) };
	previewCallback = () =>
	{
		previewFrames++;
		if ( !animate || !shown.IsValid() || callbackError is not null ) return;
		try
		{
			Action update = () =>
			{
				var tx = new Transform( new Vector3( 0, (pose ? .3f : 0) * units, .2f * units ), Rotation.Identity );
				setParent.Invoke( shown, new object[] { 1, tx } );
			};
			bindUpdate.Invoke( shown, new object[] { update } ); modelUpdates++;
		}
		catch ( Exception ex ) { callbackError = ex; }
	};
	preview.OnPreFrame += previewCallback; preview.Show();
	target = Texture.CreateRenderTarget().WithSize( 96, 96 ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_skin_capture_" + runId );
	async Task WaitFrames()
	{
		var start = previewFrames; var timer = System.Diagnostics.Stopwatch.StartNew();
		while ( previewFrames < start + 3 )
		{
			cancellationToken.ThrowIfCancellationRequested();
			if ( callbackError is not null ) throw new InvalidOperationException( "Bone update callback failed.", callbackError );
			if ( timer.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( "Preview did not advance three actual frames." );
			await Task.Delay( 50, cancellationToken ); await MainThread.Wait();
		}
	}
	async Task<Color[]> Capture( string name )
	{
		await WaitFrames(); var frame = previewFrames;
		using ( scene.Push() )
		{
			target.Clear( Color.Black );
			var submitted = managedCapture ? (bool)managedRenderToTexture.Invoke( managedRenderer, new object[] { camera, target } ) : camera.RenderToTexture( target );
			Require( submitted, "Camera submission failed: " + name );
		}
		await WaitFrames();
		using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Readback failed: " + name );
		var png = bitmap.ToPng(); var imagePath = System.IO.Path.Combine( output, name + ".png" ); System.IO.File.WriteAllBytes( imagePath, png );
		captures.Add( new { name, path = imagePath, sha256 = Hash( png ), frame, readback_frame = previewFrames } ); return bitmap.GetPixels();
	}
	async Task<Color[]> Render( Model model, string name, bool skinned, bool moved )
	{
		pose = moved; animate = false; shown = new SceneModel( scene.SceneWorld, model, Transform.Zero ) { UseAnimGraph = false }; animate = skinned;
		var pixels = await Capture( name );
		if ( skinned )
		{
			var actual = shown.GetParentSpaceBone( 1 ).Position;
			var expected = new Vector3( 0, (moved ? .3f : 0) * units, .2f * units );
			Require( (actual - expected).Length < .01f, "Native parent-space pose mismatch: " + name + " " + actual );
		}
		animate = false; shown.Delete(); shown = null; return pixels;
	}
	var background = await Capture( "background" );
	var bindRef = await Render( bindReference, "reference_bind", false, false );
	var bindNative = await Render( native, "native_bind", true, false );
	var emptyBind = await Capture( "empty_after_bind" );
	var posedRef = await Render( posedReference, "reference_posed", false, true );
	var posedNative = await Render( native, "native_posed", true, true );
	var emptyPosed = await Capture( "empty_after_posed" );
	var coverageBind = Difference( bindRef, background ); var coveragePosed = Difference( posedRef, background );
	var controlMotion = Difference( bindRef, posedRef ); var nativeMotion = Difference( bindNative, posedNative );
	var bindDifference = Difference( bindRef, bindNative ); var poseDifference = Difference( posedRef, posedNative );
	nativePassed = Nonblack( background ) == 0 && coverageBind > 50 && coveragePosed > 50 && controlMotion > 50 && nativeMotion > 50 && bindDifference <= 5 && poseDifference <= 5 && Difference( emptyBind, background ) <= 5 && Difference( emptyPosed, background ) <= 5 && modelUpdates > 0;
	cases.Add( new { name = "native stock-material skinning", passed = nativePassed, coverage_bind = coverageBind, coverage_posed = coveragePosed, reference_motion = controlMotion, native_motion = nativeMotion, bind_difference = bindDifference, posed_difference = poseDifference, empty_bind_difference = Difference( emptyBind, background ), empty_posed_difference = Difference( emptyPosed, background ), model_updates = modelUpdates } );
	if ( !nativePassed ) failures.Add( "Native skinning image or empty controls failed; inspect case and PNGs." );
	phase = "complete";
}
catch ( OperationCanceledException ex ) { error = "Cancelled: " + ex; }
catch ( Exception ex ) { error = ex.ToString(); }
finally
{
	await MainThread.Wait(); animate = false;
	if ( preview is not null && preview.IsValid && previewCallback is not null ) Clean( "preview callback", () => preview.OnPreFrame -= previewCallback );
	if ( camera.IsValid() && clear is not null ) Clean( "camera command", () => camera.RemoveCommandList( clear ) );
	if ( preview is not null && preview.IsValid ) Clean( "preview", () => { preview.Close(); preview.Destroy(); } );
	if ( shown.IsValid() ) Clean( "scene model", () => shown.Delete() );
	if ( scene is not null ) Clean( "scene", () => scene.Destroy() );
	if ( target is not null ) Clean( "target", () => target.Dispose() );
	var destroyModel = typeof( Model ).GetMethod( "Destroy", flags );
	foreach ( var model in models ) if ( Valid( model ) ) Clean( "model", () => destroyModel.Invoke( model, null ) );
	// Models release their native handles on the main-thread queue. Release the
	// task-owned mesh handles afterwards through the engine's existing helper.
	await Task.Delay( 100 ); await MainThread.Wait();
	var loader = typeof( Model ).Assembly.GetType( "Sandbox.GltfModelLoader", true );
	var releaseMeshes = loader.GetMethod( "ReleaseMeshes", flags );
	if ( releaseMeshes is not null ) Clean( "meshes", () => releaseMeshes.Invoke( null, new object[] { meshes } ) );
	if ( material is not null && material.IsValid ) Clean( "material", () => typeof( Material ).GetMethod( "Destroy", flags ).Invoke( material, null ) );
	if ( removeLogHandler is not null && logHandler is not null ) Clean( "log handler", () => removeLogHandler.Invoke( null, new object[] { logHandler } ) );
	if ( mountedOwned ) Clean( "managed mount", () => unmount.Invoke( mountedAggregate, new object[] { mounted } ) );
	if ( redirectAdded ) Clean( "redirect", () => removeRedirect.Invoke( redirectObject, new object[] { "/" + path } ) );
	if ( redirect is not null ) Clean( "redirect filesystem", () => redirect.Dispose() );
}
var receipt = new
{
	// This remains false until native geometry succeeds AND a loaded glTF model's
	// posed rendering is covered; merely loading cannot establish skinning.
	passed = false, cpu_evaluator_passed = cpuPassed, native_construction_passed = nativePassed,
	model_load_succeeded = loadedPassed, loaded_geometry_passed = loadedGeometryPassed, model_load_deformation_tested = false,
	scope = "two-joint inverse-bind/hierarchy/normalized-weight CPU checks plus independent native stock-shader bind/posed silhouette; no proof of Model.Load skinning, >4 nonzero influences, skeletal scale, animation or full glTF conformance",
	phase, error, failures, cleanup_errors = cleanup, cases, captures, source_sha256 = sourceHash, fixture_sha256 = fixtureHashes, preview_frames = previewFrames, model_updates = modelUpdates,
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	fixture_joint_node_indices = fixtureJointOrder, fixture_inverse_bind_matrices_gltf_row_vector = fixtureInverseBinds,
	probe_builder_bone_names_in_authored_order = new[] { "root", "child" }, model_skin_metadata = modelSkinMetadata, mapping_established = false,
	renderer_metadata_status = rendererMetadataStatus, renderer_metadata_error = rendererMetadataError, renderer_location = rendererLocation, renderer_loaded_mvid = rendererLoadedMvid, renderer_disk_mvid = rendererDiskMvid,
	is_editor = Application.IsEditor, diagnostic_camera_clear = diagnosticClear, capture_renderer = managedCapture ? "managed SceneBridge.RenderToTexture" : "native CameraComponent.RenderToTexture", managed_viewport_enabled = managedViewportEnabled,
	starting_frame = startFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals } ) );
return receipt;
