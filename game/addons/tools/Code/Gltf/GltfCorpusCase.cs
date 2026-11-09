using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace Editor.Gltf;

/// <summary>
/// One corpus case: public Model.Load, recipe camera/lights/environment, deterministic seeks and native captures.
/// The scheduler drives the steps so several cases can share the same editor frames. A capture is not visual approval.
/// Port of engine/Tools/Gltf/editor/editor_corpus.csx with the same checks.
/// </summary>
internal sealed class GltfCorpusCase
{
	const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	public readonly string Id, InputPath, SourcePath, CorpusRoot, OutputDir;
	readonly JsonElement recipe;
	public readonly System.Diagnostics.Stopwatch Timer = new();
	public double TimeoutSeconds;

	readonly List<string> limitations = new();
	public readonly List<string> CleanupErrors = new();
	public readonly List<Dictionary<string, object>> AnimationFrames = new();
	readonly Dictionary<string, object> exposureReceipt = new();
	readonly Dictionary<string, object> explicitPresentationReceipt = new();
	readonly List<SceneDirectionalLight> lights = new();
	public readonly Dictionary<string, object> Producers = new();

	Scene scene;
	SceneCubemap environmentProbe;
	SceneSkyBox environmentSky;
	Material environmentSkyMaterial;
	CameraComponent camera;
	Tonemapping explicitTonemapping;
	ModelRenderer renderer;
	SkinnedModelRenderer skinned;
	// Offscreen warm-up target. Captures never depend on a visible window: a hidden,
	// minimized or occluded editor stops painting widgets, which used to stall the run.
	Texture warmTarget;
	Texture target;
	Model model;
	BaseFileSystem mount, aggregate;
	string privateMountRoot, privateCorpusLink, publicInputPath, resourcePrefix, clipName, frameDir;
	bool mounted, ownedModel, recipeComplete = true;
	public int WidgetFrames;
	int width = 512, height = 512;
	double exposure = 1, exposureCompensationEv;
	object instance;

	public string LoadStatus = "blocked", LoadError = "Not attempted", RenderStatus = "blocked", RenderError = "Not attempted", StopReason;
	object captureResult, cameraApplied, presentationApplied, environmentApplied, animationApplied, boundsObserved, drawObserved;
	public readonly List<double> Times = new();
	public readonly List<double> PresentationDurations = new();
	public bool Loop;
	public int FrameIndex;
	readonly List<Task> pendingWrites = new();

	public bool Finished => FrameIndex >= Times.Count;
	public bool Animated => Times.Count > 1;

	public GltfCorpusCase( string id, string corpusRoot, string inputPath, string outputDir, JsonElement recipe, double timeoutSeconds )
	{
		Id = id; CorpusRoot = Path.GetFullPath( corpusRoot ); InputPath = inputPath; OutputDir = Path.GetFullPath( outputDir );
		SourcePath = Path.GetFullPath( Path.Combine( CorpusRoot, inputPath ) );
		this.recipe = recipe; TimeoutSeconds = timeoutSeconds;
	}

	static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
	static JsonElement Field( JsonElement value, string name ) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty( name, out var found ) ? found : default;
	static string Text( JsonElement value ) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
	static double Number( JsonElement value, double fallback ) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble( out var number ) && double.IsFinite( number ) ? number : fallback;
	static float[] Point( Vector3 value ) => new[] { value.x, value.y, value.z };
	static bool Finite( Vector3 value ) => float.IsFinite( value.x ) && float.IsFinite( value.y ) && float.IsFinite( value.z );
	static bool Vector( JsonElement value, out Vector3 vector )
	{
		vector = Vector3.Zero;
		if ( value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3 ) return false;
		var numbers = value.EnumerateArray().Select( row => Number( row, double.NaN ) ).ToArray();
		if ( numbers.Any( number => !double.IsFinite( number ) || Math.Abs( number ) > float.MaxValue ) ) return false;
		vector = new Vector3( (float)numbers[0], (float)numbers[1], (float)numbers[2] ); return true;
	}
	static Vector3 EngineDirection( Vector3 value ) => new( value.z, value.x, value.y );
	static Vector3 EnginePosition( Vector3 value ) => EngineDirection( value ) / .0254f;
	static Color ReadColor( JsonElement value, Color fallback )
	{
		if ( value.ValueKind != JsonValueKind.Array || value.GetArrayLength() < 3 || value.GetArrayLength() > 4 ) return fallback;
		var numbers = value.EnumerateArray().Select( row => Number( row, double.NaN ) ).ToArray();
		if ( numbers.Any( number => !double.IsFinite( number ) || number < 0 || number > float.MaxValue ) ) return fallback;
		return new Color( (float)numbers[0], (float)numbers[1], (float)numbers[2], numbers.Length == 4 ? (float)numbers[3] : 1 );
	}
	/// <summary>Read the module MVID from the ECMA-335 metadata of a PE file on disk (no System.Reflection.Metadata in addon code).</summary>
	static string DiskMvid( string path )
	{
		var data = File.ReadAllBytes( path );
		int U16( int at ) => BitConverter.ToUInt16( data, at );
		int I32( int at ) => BitConverter.ToInt32( data, at );
		var pe = I32( 0x3C ); Require( I32( pe ) == 0x4550, "Not a PE file: " + path );
		var sections = U16( pe + 6 ); var optional = pe + 24; var optionalSize = U16( pe + 20 );
		var plus = U16( optional ) == 0x20B;
		var cliRva = I32( optional + (plus ? 224 : 208) );
		int Offset( int rva )
		{
			for ( var s = 0; s < sections; s++ )
			{
				var h = optional + optionalSize + s * 40; var va = I32( h + 12 ); var size = Math.Max( I32( h + 8 ), I32( h + 16 ) );
				if ( rva >= va && rva < va + size ) return rva - va + I32( h + 20 );
			}
			throw new InvalidOperationException( "RVA outside sections: " + path );
		}
		var cli = Offset( cliRva ); var meta = Offset( I32( cli + 8 ) );
		Require( I32( meta ) == 0x424A5342, "No metadata root: " + path );
		var versionLength = I32( meta + 12 ); var streamCount = U16( meta + 16 + versionLength + 2 ); var at = meta + 16 + versionLength + 4;
		int tables = -1, guids = -1;
		for ( var i = 0; i < streamCount; i++ )
		{
			var offset = I32( at ); var end = Array.IndexOf( data, (byte)0, at + 8 ); var name = System.Text.Encoding.ASCII.GetString( data, at + 8, end - at - 8 );
			if ( name is "#~" or "#-" ) tables = meta + offset; else if ( name == "#GUID" ) guids = meta + offset;
			at = at + 8 + ((name.Length + 4) & ~3);
		}
		Require( tables >= 0 && guids >= 0, "Metadata streams missing: " + path );
		var heapSizes = data[tables + 6]; var valid = BitConverter.ToUInt64( data, tables + 8 );
		Require( (valid & 1) != 0, "Module table missing: " + path );
		var rows = tables + 24 + System.Numerics.BitOperations.PopCount( valid ) * 4; // Module row starts right after the row counts.
		var stringSize = (heapSizes & 1) != 0 ? 4 : 2; var guidSize = (heapSizes & 2) != 0 ? 4 : 2;
		var mvidIndex = guidSize == 4 ? I32( rows + 2 + stringSize ) : U16( rows + 2 + stringSize );
		return new Guid( data.AsSpan( guids + (mvidIndex - 1) * 16, 16 ) ).ToString();
	}
	static void AtomicBytes( string path, byte[] bytes )
	{
		var pending = path + ".pending";
		File.WriteAllBytes( pending, bytes ); File.Move( pending, path, true );
	}
	object Diagnostic( string name ) => (instance.GetType().GetProperty( name, Flags ) ?? throw new MissingMemberException( "GltfInstance." + name )).GetValue( instance );

	public bool Expired => Timer.Elapsed.TotalSeconds > TimeoutSeconds;

	public void ThrowIfExpired()
	{
		if ( Expired ) throw new TimeoutException( "Per-case capture deadline exceeded." );
	}

	public void ClearCaptureFiles()
	{
		captureResult = null; AnimationFrames.Clear();
		foreach ( var name in new[] { "capture.png", "capture.png.pending", "capture.apng", "capture.apng.pending", "poster.png" } )
		{
			var path = Path.Combine( OutputDir, name ); if ( File.Exists( path ) ) File.Delete( path );
		}
		var frames = Path.Combine( OutputDir, "frames" );
		if ( Directory.Exists( frames ) ) Directory.Delete( frames, true );
	}

	/// <summary>Synchronous main-thread setup. Model.Load cannot be interrupted; the external watchdog covers a hang.</summary>
	public void Prepare()
	{
		Timer.Start();
		Directory.CreateDirectory( OutputDir );
		ClearCaptureFiles();
		Require( SourcePath.StartsWith( CorpusRoot.TrimEnd( Path.DirectorySeparatorChar ) + Path.DirectorySeparatorChar, StringComparison.Ordinal ), "sourcePath must be inside corpusRoot." );
		Require( !Path.IsPathRooted( InputPath ) && !InputPath.Contains( '\\' ), "inputPath must be corpus-relative." );

		var requestedCamera = Field( recipe, "camera" ); var presentation = Field( recipe, "presentation" );
		var animation = Field( recipe, "animation" );
		var resolution = Field( presentation, "resolution" );
		width = (int)Number( Field( resolution, "width" ), 512 ); height = (int)Number( Field( resolution, "height" ), 512 );
		Require( width >= 64 && width <= 512 && height >= 64 && height <= 512, "Capture dimensions must be in [64,512]." );

		var engine = typeof( Model ).Assembly;
		var gltf = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( assembly => assembly.GetName().Name == "Sandbox.Gltf" );
		var rendering = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault( assembly => assembly.GetName().Name == "Sandbox.SceneRenderer" );
		foreach ( var pair in new[] { (Name: "engine", Assembly: engine), (Name: "gltf", Assembly: gltf), (Name: "renderer", Assembly: rendering) } )
		{
			Require( pair.Assembly is not null, "Required loaded producer is absent: " + pair.Name );
			var loadedMvid = pair.Assembly.ManifestModule.ModuleVersionId.ToString(); var diskMvid = DiskMvid( pair.Assembly.Location );
			Producers[pair.Name] = new { mvid = loadedMvid, diskMvid, path = pair.Assembly.Location, diskSha256 = GltfCorpusProof.HashFile( pair.Assembly.Location ) };
			Require( loadedMvid == diskMvid, "Loaded/disk producer MVID mismatch: " + pair.Name );
		}

		aggregate = FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
		// A private resource prefix prevents collisions with any pre-existing cached model.
		resourcePrefix = "gltf_run_" + Guid.NewGuid().ToString( "N" );
		privateMountRoot = Path.Combine( Path.GetTempPath(), resourcePrefix );
		Directory.CreateDirectory( privateMountRoot );
		privateCorpusLink = Path.Combine( privateMountRoot, resourcePrefix );
		var corpusLink = Directory.CreateSymbolicLink( privateCorpusLink, CorpusRoot );
		Require( corpusLink.ResolveLinkTarget( true )?.FullName == CorpusRoot, "Private corpus link did not resolve to the verified corpusRoot." );
		publicInputPath = resourcePrefix + "/" + InputPath;
		var localType = typeof( BaseFileSystem ).Assembly.GetType( "Sandbox.LocalFileSystem", true );
		var constructor = localType.GetConstructor( Flags, null, new[] { typeof( string ), typeof( bool ) }, null ) ?? throw new MissingMethodException( "LocalFileSystem constructor" );
		mount = (BaseFileSystem)constructor.Invoke( new object[] { privateMountRoot, true } );
		mounted = true;
		(typeof( BaseFileSystem ).GetMethod( "Mount", Flags, null, new[] { typeof( BaseFileSystem ) }, null ) ?? throw new MissingMethodException( "BaseFileSystem.Mount" )).Invoke( aggregate, new object[] { mount } );

		LoadError = null; LoadStatus = "loading";
		model = Model.Load( publicInputPath );
		ownedModel = model is not null && model.ResourcePath?.StartsWith( resourcePrefix + "/", StringComparison.Ordinal ) == true && !ReferenceEquals( model, Model.Error );
		ThrowIfExpired();
		Require( model is { IsValid: true, IsError: false } && ownedModel, "Model.Load returned null/invalid/error or non-owned resource." );
		var native = (typeof( Model ).GetField( "native", Flags ) ?? throw new MissingFieldException( "Model.native" )).GetValue( model );
		var nullProperty = native.GetType().GetProperty( "IsNull", Flags ) ?? throw new MissingMemberException( "IModel.IsNull" );
		Require( nullProperty.GetValue( native ) is true, "Public glTF loading returned a native model placeholder." );
		LoadStatus = "passed"; LoadError = null;
		Require( model.MeshCount > 0, "Source contains zero drawable meshes; no substitute geometry is allowed." );
		var bounds = model.Bounds;
		Require( Finite( bounds.Mins ) && Finite( bounds.Maxs ), "Model bounds are nonfinite." );
		boundsObserved = new { minimumInches = Point( bounds.Mins ), maximumInches = Point( bounds.Maxs ), model.MeshCount, model.BoneCount, model.AnimationCount };

		scene = Scene.CreateEditorScene();
		using ( scene.Push() ) camera = scene.CreateObject().Components.Create<CameraComponent>();
		ApplyCamera( requestedCamera, bounds );
		ApplyPresentation( presentation, bounds );

		using ( scene.Push() )
		{
			if ( model.BoneCount > 0 || model.AnimationCount > 0 )
			{
				skinned = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false ); renderer = skinned;
				skinned.UseAnimGraph = false; skinned.PlayAnimationsInEditorScene = true; skinned.PlaybackRate = 0;
			}
			else renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
			renderer.WorldTransform = Transform.Zero; renderer.Model = model; renderer.Enabled = true; scene.EditorTick( 0, 0 );
		}
		Require( renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null, "Public renderer must own a custom object without a native model." );
		instance = (typeof( ModelRenderer ).GetProperty( "GltfInstance", Flags ) ?? throw new MissingMemberException( "ModelRenderer.GltfInstance" )).GetValue( renderer );
		Require( instance is not null, "Public renderer has no managed draw instance." );

		ApplyAnimation( animation );

		// Allocate then configure: references exist before any subsequent operation can throw.
		warmTarget = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RGBA8888 ).Create( "gltf_run_warm_" + Guid.NewGuid().ToString( "N" ) );
		target = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Magenta ).Create( "gltf_run_" + Guid.NewGuid().ToString( "N" ) );
		RenderStatus = "capturing"; RenderError = null;
		frameDir = Path.Combine( OutputDir, "frames" ); if ( Animated ) Directory.CreateDirectory( frameDir );
	}

	void ApplyCamera( JsonElement requestedCamera, BBox bounds )
	{
		var position = Vector3.Zero; var targetPoint = Vector3.Zero; var up = Vector3.Up;
		var projection = Text( Field( requestedCamera, "projection" ) );
		var recipeCameraValid = Text( Field( requestedCamera, "space" ) ) == "gltf-metres"
			&& Vector( Field( requestedCamera, "position" ), out position ) && Vector( Field( requestedCamera, "target" ), out targetPoint )
			&& Vector( Field( requestedCamera, "up" ), out up ) && (targetPoint - position).Length > .000001f && up.Length > .000001f
			&& Vector3.Cross( targetPoint - position, up ).Length > .000001f && projection is "perspective" or "orthographic";
		var verticalFov = Number( Field( requestedCamera, "verticalFov" ), double.NaN );
		var orthoHeight = Number( Field( requestedCamera, "orthographicHeight" ), double.NaN );
		recipeCameraValid &= projection == "perspective" ? verticalFov > 0 && verticalFov < 179 : orthoHeight > 0 && double.IsFinite( orthoHeight );
		if ( recipeCameraValid )
		{
			camera.WorldPosition = EnginePosition( position ); targetPoint = EnginePosition( targetPoint ); up = EngineDirection( up ).Normal;
			camera.Orthographic = projection == "orthographic";
			if ( camera.Orthographic ) camera.OrthographicHeight = (float)(orthoHeight / .0254);
			else { camera.FovAxis = CameraComponent.Axis.Vertical; camera.FieldOfView = (float)verticalFov; }
		}
		else
		{
			targetPoint = (bounds.Mins + bounds.Maxs) * .5f; var diagonal = Math.Max( (bounds.Maxs - bounds.Mins).Length, 1 );
			camera.WorldPosition = targetPoint + new Vector3( 1, 1, .65f ).Normal * diagonal * 3; up = Vector3.Up;
			camera.Orthographic = true; camera.OrthographicHeight = diagonal * 1.25f;
			recipeComplete = false; limitations.Add( "Missing/invalid recipe camera: fitted public model.Bounds; not a Khronos matched view." );
		}
		camera.WorldRotation = Rotation.LookAt( targetPoint - camera.WorldPosition, up );
		camera.ZNear = .01f; camera.ZFar = Math.Max( 1000, (camera.WorldPosition - targetPoint).Length + (bounds.Maxs - bounds.Mins).Length * 4 );
		camera.CustomSize = new Vector2( width, height );
		cameraApplied = new { source = recipeCameraValid ? "recipe" : "bounds-fit", positionInches = Point( camera.WorldPosition ), targetInches = Point( targetPoint ), up = Point( up ), camera.Orthographic, camera.OrthographicHeight, camera.FieldOfView, fovAxis = camera.FovAxis.ToString(), camera.ZNear, camera.ZFar, width, height };
	}

	void ApplyPresentation( JsonElement presentation, BBox bounds )
	{
		camera.BackgroundColor = ReadColor( Field( Field( presentation, "background" ), "color" ), Color.Black );
		camera.ClearFlags = ClearFlags.All; camera.DebugMode = SceneCameraDebugMode.Normal; camera.EnablePostProcessing = true;
		exposure = Number( Field( presentation, "exposure" ), 1 ); Require( exposure > 0 && exposure <= 100, "Exposure must be finite in (0,100]." );
		camera.AutoExposure.Enabled = true; camera.AutoExposure.MinimumExposure = (float)exposure; camera.AutoExposure.MaximumExposure = (float)exposure; camera.AutoExposure.Compensation = 0;
		var tonemapping = Field( presentation, "tonemapping" );
		if ( tonemapping.ValueKind != JsonValueKind.Undefined )
		{
			Require( tonemapping.ValueKind == JsonValueKind.Object, "tonemapping must be an explicit object." );
			var mode = Text( Field( tonemapping, "mode" ) );
			Require( mode is "ACES" or "HableFilmic" or "ReinhardJodie" or "Linear" or "AgX" or "KhronosPbrNeutral", "Unsupported native tonemapping mode." );
			exposureCompensationEv = Number( Field( tonemapping, "exposureCompensationEv" ), double.NaN );
			Require( double.IsFinite( exposureCompensationEv ) && Math.Abs( exposureCompensationEv ) <= 5, "Explicit exposureCompensationEv must be in [-5,5]." );
			// Editor scene ticks reset camera-only exposure to [1,2]. A colocated
			// stock effect restores the requested settings during effect collection.
			using ( scene.Push() ) explicitTonemapping = camera.GameObject.Components.Create<Tonemapping>();
			explicitTonemapping.Mode = Enum.Parse<Tonemapping.TonemappingMode>( mode );
			explicitTonemapping.AutoExposureEnabled = true;
			explicitTonemapping.MinimumExposure = (float)exposure;
			explicitTonemapping.MaximumExposure = (float)exposure;
			explicitTonemapping.ExposureCompensation = (float)exposureCompensationEv;
			explicitTonemapping.Rate = 20;
		}
		var appliedLights = new List<object>(); var requestedLights = Field( presentation, "lights" );
		if ( requestedLights.ValueKind == JsonValueKind.Array )
		{
			Require( requestedLights.GetArrayLength() <= 8, "At most eight recipe lights per bounded case." );
			foreach ( var light in requestedLights.EnumerateArray() )
			{
				if ( Text( Field( light, "type" ) ) != "directional" || !Vector( Field( light, "direction" ), out var direction ) || direction.Length < .000001f ) { recipeComplete = false; limitations.Add( "Unsupported/invalid recipe light omitted; no default light substituted." ); continue; }
				var lightDirection = EngineDirection( direction ).Normal; var color = ReadColor( Field( light, "color" ), Color.White );
				var intensity = Number( Field( light, "intensity" ), 1 ); Require( intensity >= 0 && intensity <= 100000, "Invalid directional light intensity." );
				var rotation = Rotation.LookAt( lightDirection, Math.Abs( lightDirection.z ) > .99f ? Vector3.Left : Vector3.Up );
				var created = new SceneDirectionalLight( scene.SceneWorld, rotation, color * (float)intensity ); lights.Add( created );
				appliedLights.Add( new { type = "directional", direction = Point( lightDirection ), color = new[] { color.r, color.g, color.b, color.a }, intensity } );
			}
		}
		// Opt-in only: legacy environment descriptions do not create an implicit probe.
		var environment = Field( presentation, "environment" );
		if ( Text( Field( environment, "type" ) ) == "cubemap" ) ApplyEnvironment( environment, bounds );
		if ( appliedLights.Count == 0 && environmentProbe is null ) limitations.Add( "No valid recipe lights; no implicit light/sky/environment substituted." );
		exposureReceipt["requested"] = new { enabled = true, minimum = exposure, maximum = exposure, compensation = exposureCompensationEv };
		exposureReceipt["lockStatus"] = "unverified";
		exposureReceipt["locked"] = false;
		presentationApplied = new { background = new[] { camera.BackgroundColor.r, camera.BackgroundColor.g, camera.BackgroundColor.b, camera.BackgroundColor.a }, lights = appliedLights, environment = environmentApplied, exposure, exposureLocked = false, exposureLockStatus = "unverified", exposureDetails = exposureReceipt, nativeDebugMode = "Normal", postProcessing = true };
		if ( explicitTonemapping is null ) limitations.Add( "Requested exposure is not a verified lock: editor postprocessing can change camera exposure settings." );
		else
		{
			explicitPresentationReceipt["background"] = new[] { camera.BackgroundColor.r, camera.BackgroundColor.g, camera.BackgroundColor.b, camera.BackgroundColor.a };
			explicitPresentationReceipt["lights"] = appliedLights; explicitPresentationReceipt["environment"] = environmentApplied;
			explicitPresentationReceipt["exposure"] = exposure; explicitPresentationReceipt["exposureLocked"] = false;
			explicitPresentationReceipt["exposureLockStatus"] = "awaiting-render"; explicitPresentationReceipt["exposureDetails"] = exposureReceipt;
			explicitPresentationReceipt["nativeDebugMode"] = "Normal"; explicitPresentationReceipt["postProcessing"] = true;
			explicitPresentationReceipt["tonemapping"] = new { mode = explicitTonemapping.Mode.ToString(), exposureCompensationEv, source = "camera-colocated stock Tonemapping component", scope = "Component selection and post-render camera settings; no independent shader pixel oracle." };
			presentationApplied = explicitPresentationReceipt;
		}
	}

	void ApplyEnvironment( JsonElement environment, BBox bounds )
	{
		var texturePath = Text( Field( environment, "texturePath" ) );
		var environmentSourcePath = Text( Field( environment, "sourcePath" ) );
		var compiledPath = Text( Field( environment, "compiledPath" ) );
		var sourceExpected = Text( Field( environment, "sourceSha256" ) );
		var compiledExpected = Text( Field( environment, "compiledSha256" ) );
		Require( !string.IsNullOrWhiteSpace( texturePath ) && !Path.IsPathRooted( texturePath ) && !texturePath.Contains( ".." ) && !texturePath.Contains( '\\' ), "Environment texturePath must be a mounted resource-relative path." );
		Require( Path.IsPathFullyQualified( environmentSourcePath ?? "" ) && File.Exists( environmentSourcePath ), "Environment source file is unavailable." );
		Require( Path.IsPathFullyQualified( compiledPath ?? "" ) && File.Exists( compiledPath ), "Compiled environment file is unavailable." );
		var sourceActual = GltfCorpusProof.HashFile( environmentSourcePath ); var compiledActual = GltfCorpusProof.HashFile( compiledPath );
		Require( sourceActual == sourceExpected && compiledActual == compiledExpected, "Environment source or compiled hash differs from the request." );
		var texture = Texture.Load( texturePath );
		Require( texture is { IsValid: true, IsError: false } && texture.Depth == 6 && texture.Width == texture.Height && texture.Mips >= 2, "Environment resource is not a valid square cubemap with mips." );
		var size = Number( Field( environment, "sizeInches" ), double.NaN );
		var yaw = Number( Field( environment, "yawDegrees" ), double.NaN );
		var tint = ReadColor( Field( environment, "tint" ), Color.White );
		Require( double.IsFinite( size ) && size > 0 && size <= 100000000 && double.IsFinite( yaw ) && Math.Abs( yaw ) <= 3600, "Environment size or yaw is invalid." );
		var center = (bounds.Mins + bounds.Maxs) * .5f;
		var extent = bounds.Maxs - bounds.Mins;
		Require( size > Math.Max( extent.x, Math.Max( extent.y, extent.z ) ) * 2, "Environment probe bounds must cover the model with margin." );
		environmentProbe = new SceneCubemap( scene.SceneWorld, texture, BBox.FromPositionAndSize( Vector3.Zero, (float)size ) );
		environmentProbe.Projection = SceneCubemap.ProjectionMode.Sphere;
		environmentProbe.Transform = Transform.Zero.WithPosition( center ).WithRotation( Rotation.FromYaw( (float)yaw ) );
		environmentProbe.TintColor = tint; environmentProbe.Feathering = .01f; environmentProbe.RenderDirty();
		Require( environmentProbe.IsValid() && environmentProbe.Texture is { IsValid: true, IsError: false }, "Environment probe did not retain the cubemap." );
		var visibleSky = Field( environment, "visibleSky" );
		Require( visibleSky.ValueKind is JsonValueKind.Undefined or JsonValueKind.True or JsonValueKind.False, "visibleSky must be an explicit boolean when supplied." );
		object skyApplied = null;
		if ( visibleSky.ValueKind == JsonValueKind.True )
		{
			// Blurred background like the Sample Viewer (textureLod at 0.6 of the mip chain, intensity 1).
			const string skyShaderPath = "shaders/gltf_reference_sky.shader";
			environmentSkyMaterial = Material.Create( "gltf_reference_sky_" + Guid.NewGuid().ToString( "N" ), skyShaderPath );
			Require( environmentSkyMaterial is { IsValid: true } && environmentSkyMaterial.Set( "g_tSkyTexture", texture ), "Reference sky material did not accept the requested cubemap." );
			Require( environmentSkyMaterial.GetTexture( "g_tSkyTexture" ) == texture, "Reference sky material retained another texture." );
			environmentSkyMaterial.Set( "g_flGltfSkyYaw", (float)yaw ); environmentSkyMaterial.Set( "g_flGltfSkyBlur", float.Parse( System.Environment.GetEnvironmentVariable( "GLTF_SKY_BLUR" ) ?? "0.6" ) );
			environmentSky = new SceneSkyBox( scene.SceneWorld, environmentSkyMaterial );
			Require( environmentSky.IsValid(), "Native sky object is invalid." );
			skyApplied = new { visible = true, materialTemplate = skyShaderPath, blurMipFraction = 0.6, gain = 1.0, shader = environmentSkyMaterial.ShaderName, textureResourcePath = texture.ResourcePath, yawDegrees = yaw, scope = "Same native cubemap as the probe; sky visibility does not establish IBL correctness." };
		}
		environmentApplied = new { type = "cubemap", texturePath, textureResourcePath = texture.ResourcePath, sourcePath = environmentSourcePath, sourceSha256 = sourceActual, compiledPath, compiledSha256 = compiledActual, texture.Width, texture.Height, texture.Depth, texture.Mips, format = texture.ImageFormat.ToString(), centerInches = Point( center ), sizeInches = size, yawDegrees = yaw, tint = new[] { tint.r, tint.g, tint.b, tint.a }, projection = environmentProbe.Projection.ToString(), sky = skyApplied, limitations = "Loaded resource-to-compiled-file identity, orientation and actual IBL pixels require native validation." };
	}

	void ApplyAnimation( JsonElement animation )
	{
		clipName = Text( Field( animation, "name" ) ); var clip = Field( animation, "index" ); if ( clip.ValueKind == JsonValueKind.Undefined ) clip = Field( animation, "clip" );
		var requestedIndex = Number( clip, double.NaN );
		if ( string.IsNullOrEmpty( clipName ) && double.IsFinite( requestedIndex ) )
		{
			Require( requestedIndex == Math.Truncate( requestedIndex ) && requestedIndex >= 0 && requestedIndex < model.AnimationCount, "Requested animation index is out of range." ); clipName = model.AnimationNames[(int)requestedIndex];
		}
		if ( clipName is not null ) { Require( skinned is not null && model.AnimationNames.Contains( clipName ), "Requested animation is unavailable." ); skinned.Sequence.Name = clipName; }
		else if ( model.AnimationCount > 0 ) { clipName = model.AnimationNames[0]; skinned.Sequence.Name = clipName; limitations.Add( "No requested clip: captured first public animation sequence." ); }
		Loop = Field( animation, "loop" ).ValueKind == JsonValueKind.True;
		if ( skinned is not null && clipName is not null ) skinned.Sequence.Looping = Loop;
		var simultaneous = Field( animation, "simultaneousClips" );
		var requestedClips = simultaneous.ValueKind == JsonValueKind.Array ? simultaneous.EnumerateArray().Select( value => value.Clone() ).ToArray() : Array.Empty<JsonElement>();
		var requirements = Field( animation, "requires" );
		var requestedRequirements = requirements.ValueKind == JsonValueKind.Array ? requirements.EnumerateArray().Select( value => value.ToString() ).ToArray() : Array.Empty<string>();
		var appliedClipCount = clipName is null ? 0 : 1;
		if ( requestedClips.Length > 1 || requestedRequirements.Length > 0 )
		{
			recipeComplete = false;
			limitations.Add( $"Recipe requests {requestedClips.Length} simultaneous clips; this capture applies {appliedClipCount} public Sequence only. Requirements not fulfilled: " + string.Join( "; ", requestedRequirements ) );
		}
		// The Run driver replaced captureTimes with the recording sample times for APNG recipes.
		var recording = Field( animation, "recording" );
		var planned = Text( Field( recording, "kind" ) ) == "apng" ? Field( recording, "sampleTimes" ) : Field( animation, "captureTimes" );
		if ( planned.ValueKind == JsonValueKind.Array )
		{
			Require( planned.GetArrayLength() > 0 && planned.GetArrayLength() <= 2048, "captureTimes requires 1..2048 frames." );
			foreach ( var time in planned.EnumerateArray() ) { var value = Number( time, double.NaN ); Require( double.IsFinite( value ) && value >= 0 && value <= float.MaxValue, "Invalid capture time." ); Times.Add( value ); }
			// Source time may wrap at a looping clip boundary. Presentation time is
			// separately described by positive recording delays, never source deltas.
			Require( Loop || Times.Zip( Times.Skip( 1 ), ( a, b ) => b > a ).All( value => value ), "Non-looping capture times must increase strictly." );
		}
		else Times.Add( Number( Field( animation, "time" ), 0 ) );
		Require( Times.Count == 1 || clipName is not null, "Multiple capture times require an authored animation clip." );
		if ( Loop && clipName is not null ) Require( Times.All( time => time <= skinned.Sequence.Duration ), "Looped seek time exceeds the public clip duration." );
		if ( Text( Field( recording, "kind" ) ) == "apng" )
		{
			var durations = Field( recording, "frameDurationsSeconds" );
			Require( durations.ValueKind == JsonValueKind.Array && durations.GetArrayLength() == Times.Count && Number( Field( recording, "frameCount" ), -1 ) == Times.Count, "Recording seek/duration inventory differs." );
			foreach ( var delay in durations.EnumerateArray() ) { var value = Number( delay, double.NaN ); Require( double.IsFinite( value ) && value > 0, "Presentation frame durations must be strictly positive." ); PresentationDurations.Add( value ); }
			var declaredDuration = Number( Field( recording, "duration" ), double.NaN );
			Require( double.IsFinite( declaredDuration ) && declaredDuration > 0 && Math.Abs( PresentationDurations.Sum() - declaredDuration ) <= Math.Max( 1e-7, declaredDuration * 1e-7 ), "Recording presentation duration differs from positive frame delays." );
		}
		else Require( Times.Count <= 1 || Times.Zip( Times.Skip( 1 ), ( a, b ) => b > a ).All( value => value ), "Wrapped multi-frame seeks require explicit positive recording delays." );
		var rate = Number( Field( animation, "rate" ), 1 ); var duration = Number( Field( animation, "duration" ), skinned is not null && clipName is not null ? skinned.Sequence.Duration : 0 );
		animationApplied = new { clip = clipName, captureTimes = Times, loop = Loop, frameDurationsSeconds = PresentationDurations, requestedClips, requestedClipCount = requestedClips.Length > 0 ? requestedClips.Length : appliedClipCount, appliedClipCount, requires = requestedRequirements, requestedRate = rate, duration, playbackRate = 0, mode = "public Sequence.Time deterministic seeks; not real-time playback" };
	}

	/// <summary>Step 1 of a frame: seek the public sequence and tick the editor scene.</summary>
	public void Seek()
	{
		if ( skinned is not null && clipName is not null ) skinned.Sequence.Time = (float)Times[FrameIndex];
		using ( scene.Push() ) scene.EditorTick( 0, 0 );
	}

	/// <summary>One offscreen render of the scene, so uploads, deformation and exposure settle before the capture.</summary>
	public void WarmRender()
	{
		bool submitted; using ( scene.Push() ) submitted = camera.RenderToTexture( warmTarget );
		Require( submitted, "Native camera rejected warm-up render." );
		WidgetFrames++;
	}

	/// <summary>Step 2: after real editor frames, submit the native camera render into a magenta-cleared target.</summary>
	public void Submit()
	{
		bool submitted; using ( scene.Push() ) { target.Clear( Color.Magenta ); submitted = camera.RenderToTexture( target ); }
		Require( submitted, "Native camera rejected capture submission." );
		exposureReceipt["observedAfterRender"] = new { frameIndex = FrameIndex, enabled = camera.AutoExposure.Enabled, minimum = camera.AutoExposure.MinimumExposure, maximum = camera.AutoExposure.MaximumExposure, compensation = camera.AutoExposure.Compensation, rate = camera.AutoExposure.Rate, sceneMainCamera = ReferenceEquals( scene.Camera, camera ), engineFrame = Sandbox.Application.FrameCount };
		if ( explicitTonemapping is not null )
		{
			var retained = camera.AutoExposure.Enabled
				&& Math.Abs( camera.AutoExposure.MinimumExposure - exposure ) < 1e-5
				&& Math.Abs( camera.AutoExposure.MaximumExposure - exposure ) < 1e-5
				&& Math.Abs( camera.AutoExposure.Compensation - exposureCompensationEv ) < 1e-5;
			exposureReceipt["locked"] = retained; exposureReceipt["lockStatus"] = retained ? "component-settings-retained-after-render" : "failed";
			explicitPresentationReceipt["exposureLocked"] = retained; explicitPresentationReceipt["exposureLockStatus"] = exposureReceipt["lockStatus"];
			Require( retained, "Native render did not retain the explicitly requested exposure settings." );
		}
	}

	/// <summary>Step 3: read back after real editor frames, verify the draw, then encode/hash/write the PNG off the main thread.</summary>
	public void Read()
	{
		var bitmap = target.GetBitmap(); Require( bitmap is not null && bitmap.Width == width && bitmap.Height == height, "Native target returned no correctly sized bitmap." );
		var pixels = bitmap.GetPixels(); Require( pixels.Length == width * height, "Native capture pixel count differs." );
		var callbackErrors = (string[])Diagnostic( "CallbackErrors" );
		var drawCalls = Convert.ToInt32( Diagnostic( "DrawCalls" ) );
		var requestedRevision = Convert.ToInt64( Diagnostic( "PoseRevision" ) ); var submittedRevision = Convert.ToInt64( Diagnostic( "SubmittedRevision" ) );
		drawObserved = new { renderer = renderer.GetType().Name, resourcePath = model.ResourcePath, callbackErrors, drawCalls, requestedRevision, submittedRevision, nativeCaptureSubmitted = true, widgetFrames = WidgetFrames, bounds = boundsObserved };
		Require( callbackErrors.Length == 0, "Managed draw callback failed: " + string.Join( "\n", callbackErrors ) );
		Require( drawCalls > 0 && submittedRevision == requestedRevision, "Native capture completed without a successful current managed draw submission." );
		Require( !pixels.All( pixel => pixel.r > .99f && pixel.g < .01f && pixel.b > .99f ), "Native target retained its magenta initialization." );

		var index = FrameIndex; var count = Times.Count;
		var path = count == 1 ? Path.Combine( OutputDir, "capture.png" ) : Path.Combine( frameDir, "frame" + index.ToString( "D4" ) + ".png" );
		var actualTime = skinned is not null && clipName is not null ? skinned.Sequence.Time : (float?)null;
		var frame = new Dictionary<string, object> { ["index"] = index, ["requestedTime"] = Times[index], ["publicSeekTime"] = (float)Times[index], ["actualTime"] = actualTime, ["path"] = path, ["engineFrame"] = Sandbox.Application.FrameCount, ["wallSeconds"] = Timer.Elapsed.TotalSeconds, ["width"] = width, ["height"] = height };
		AnimationFrames.Add( frame );
		var poster = Path.Combine( OutputDir, "capture.png" );
		pendingWrites.Add( Task.Run( () =>
		{
			byte[] png; try { png = bitmap.ToPng(); } finally { bitmap.Dispose(); }
			var hash = GltfCorpusProof.Hash( png );
			AtomicBytes( path, png );
			if ( index == 0 && count > 1 ) AtomicBytes( poster, png );
			lock ( frame ) frame["sha256"] = hash;
			if ( index == 0 ) captureResult = new { path = poster, sha256 = hash, kind = "image" };
		} ) );
		FrameIndex++;
	}

	/// <summary>Wait for off-thread PNG writes; any failure fails the case.</summary>
	public async Task FlushWrites()
	{
		await Task.WhenAll( pendingWrites );
		pendingWrites.Clear();
	}

	public void Fail( Exception error )
	{
		StopReason ??= error is OperationCanceledException ? "cancelled" : error is TimeoutException ? "timeout" : Game.IsPlaying ? "human_play_active" : null;
		if ( LoadStatus != "passed" ) { LoadStatus = StopReason is null ? "failed" : "blocked"; LoadError = error.ToString(); }
		RenderStatus = StopReason is null ? "failed" : "blocked"; RenderError = error.ToString();
	}

	public void Pass()
	{
		limitations.Add( "Captured default source materials through public components; visual approval, independent geometry/material parity and performance remain pending." );
		RenderStatus = "passed"; RenderError = null;
	}

	void Clean( string label, Action action )
	{
		try { action(); } catch ( Exception error ) { CleanupErrors.Add( label + ": " + error ); }
	}

	/// <summary>Owned cleanup; each step is independent. Main thread only.</summary>
	public void Cleanup()
	{
		try { Task.WhenAll( pendingWrites ).Wait( TimeSpan.FromSeconds( 30 ) ); } catch { }
		Clean( "renderer", () => { if ( renderer.IsValid() ) { using var scope = scene.Push(); renderer.GameObject.Destroy(); scene.ProcessDeletes(); } } );
		Clean( "warm target", () => { warmTarget?.Dispose(); warmTarget = null; } );
		Clean( "render target", () => target?.Dispose() );
		foreach ( var light in lights ) Clean( "light", () => { if ( light.IsValid() ) light.Delete(); } );
		Clean( "environment sky", () => { if ( environmentSky.IsValid() ) environmentSky.Delete(); } );
		Clean( "owned sky material", () => { if ( environmentSkyMaterial is { IsValid: true } ) (typeof( Material ).GetMethod( "Destroy", Flags, null, Type.EmptyTypes, null ) ?? throw new MissingMethodException( "Material.Destroy" )).Invoke( environmentSkyMaterial, null ); } );
		Clean( "environment probe", () => { if ( environmentProbe.IsValid() ) environmentProbe.Delete(); } );
		Clean( "scene", () => { if ( scene.IsValid() ) scene.Destroy(); } );
		Clean( "model", () => { if ( ownedModel ) (typeof( Model ).GetMethod( "Destroy", Flags, null, Type.EmptyTypes, null ) ?? throw new MissingMethodException( "Model.Destroy" )).Invoke( model, null ); } );
		Clean( "unmount", () => { if ( mounted ) (typeof( BaseFileSystem ).GetMethod( "UnMount", Flags, null, new[] { typeof( BaseFileSystem ) }, null ) ?? throw new MissingMethodException( "BaseFileSystem.UnMount" )).Invoke( aggregate, new object[] { mount } ); } );
		Clean( "filesystem", () => { if ( mount is not null ) (mount.GetType().GetMethod( "Dispose", Flags, null, Type.EmptyTypes, null ) ?? throw new MissingMethodException( "LocalFileSystem.Dispose" )).Invoke( mount, null ); } );
		Clean( "private corpus link", () => { if ( privateCorpusLink is not null && Directory.Exists( privateCorpusLink ) ) Directory.Delete( privateCorpusLink, false ); } );
		Clean( "private mount directory", () => { if ( privateMountRoot is not null && Directory.Exists( privateMountRoot ) ) Directory.Delete( privateMountRoot, false ); } );
		if ( CleanupErrors.Count > 0 ) { RenderStatus = "failed"; RenderError = (RenderError ?? "") + "\nOwned cleanup failed; stop Run."; StopReason = "cleanup_failure"; }
		if ( RenderStatus != "passed" ) Clean( "failed capture invalidation", ClearCaptureFiles );
		if ( CleanupErrors.Count > 0 ) captureResult = null;
		Timer.Stop();
	}

	public Dictionary<string, object> Result() => new()
	{
		["id"] = Id,
		["load"] = new { status = LoadStatus, error = LoadError },
		["render"] = new { status = RenderStatus, error = RenderError, observation = drawObserved, visualApproval = "pending" },
		["capture"] = captureResult,
		["recipeApplied"] = new { complete = recipeComplete && RenderStatus == "passed", camera = cameraApplied, presentation = presentationApplied, animation = animationApplied, limitations },
		["producer"] = Producers, ["cleanup_errors"] = CleanupErrors, ["cleanupConfirmed"] = CleanupErrors.Count == 0, ["stopReason"] = StopReason, ["elapsedSeconds"] = Timer.Elapsed.TotalSeconds
	};
}
