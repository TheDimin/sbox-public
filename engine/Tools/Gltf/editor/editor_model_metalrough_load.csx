// Actual Model.Load of pinned MetalRoughSpheres, plus explicitly labeled copies
// with non-default factors. Native packed-resource readback is not visual parity.
var engine = typeof( Model ).Assembly;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engine.Location ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout." );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var runId = Guid.NewGuid().ToString( "N" ); var prefix = "gltf-model-metalrough/" + runId;
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-model-metalrough-load", runId );
System.IO.Directory.CreateDirectory( output );
var rows = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
var cleanupErrors = new System.Collections.Generic.List<string>(); var aliases = new System.Collections.Generic.List<string>();
var hashes = new System.Collections.Generic.Dictionary<string, string>(); var messages = new System.Collections.Generic.List<string>();
var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
object redirectObject = null; IDisposable redirect = null; BaseFileSystem mount = null, aggregate = null;
System.Reflection.MethodInfo unmount = null, removeRedirect = null, removeLog = null; Action<LogEvent> logHandler = null;
Model model = null; bool mounted = false, ownedModel = false;
string phase = "initialize", error = null; var initialFrame = Application.FrameCount;
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Clean( string name, Action action ) { try { action(); } catch ( Exception ex ) { cleanupErrors.Add( name + ": " + ex ); } }
async Task SafePoint()
{
	await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
	Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Run in an idle editor outside a graphics scope; defer while the user plays." );
}
byte[] DecodePinnedRgbaPng( byte[] png )
{
	// Independent fixed-format PNG decoder: the pinned fixture is RGBA8, noninterlaced.
	Require( png.Length > 33 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( 16, 4 ) ) == 1024 && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( 20, 4 ) ) == 1024 && png[24] == 8 && png[25] == 6 && png[28] == 0, "Pinned MR PNG header changed." );
	using var idat = new System.IO.MemoryStream();
	for ( var offset = 8; offset < png.Length; )
	{
		Require( offset + 12 <= png.Length, "Truncated PNG chunk." ); var size = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( offset, 4 ) ));
		Require( size >= 0 && size <= png.Length - offset - 12, "PNG chunk exceeds input." );
		if ( png.AsSpan( offset + 4, 4 ).SequenceEqual( new byte[] { 73, 68, 65, 84 } ) ) idat.Write( png, offset + 8, size ); offset += 12 + size;
	}
	idat.Position = 0; using var zlib = new System.IO.Compression.ZLibStream( idat, System.IO.Compression.CompressionMode.Decompress );
	var filtered = new byte[1024 * (4096 + 1)]; zlib.ReadExactly( filtered ); Require( zlib.ReadByte() == -1, "PNG inflated beyond its fixed dimensions." );
	var rgba = new byte[1024 * 4096];
	int Paeth( int a, int b, int c ) { var p = a + b - c; var da = Math.Abs( p - a ); var db = Math.Abs( p - b ); var dc = Math.Abs( p - c ); return da <= db && da <= dc ? a : db <= dc ? b : c; }
	for ( var y = 0; y < 1024; y++ )
	{
		var filter = filtered[y * 4097]; Require( filter <= 4, "Unsupported PNG filter." );
		for ( var x = 0; x < 4096; x++ )
		{
			var left = x < 4 ? 0 : rgba[y * 4096 + x - 4]; var up = y == 0 ? 0 : rgba[(y - 1) * 4096 + x]; var upperLeft = y == 0 || x < 4 ? 0 : rgba[(y - 1) * 4096 + x - 4];
			var predictor = filter switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, _ => Paeth( left, up, upperLeft ) };
			rgba[y * 4096 + x] = unchecked((byte)(filtered[y * 4097 + x + 1] + predictor));
		}
	}
	return rgba;
}
void Put( string relative, byte[] bytes )
{
	var path = System.IO.Path.Combine( output, "input", relative.Replace( '/', System.IO.Path.DirectorySeparatorChar ) ); System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( path ) ); System.IO.File.WriteAllBytes( path, bytes );
	var alias = "/" + prefix + "/" + relative; redirectObject.GetType().GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { alias, path } ); aliases.Add( alias ); hashes[relative] = Hash( bytes );
}
async Task<float[]> ReadFullFloat( Texture texture, string role, System.Collections.Generic.Dictionary<string, object> row )
{
	Require( texture is { IsValid: true } && !texture.IsError, "Missing native " + role + " resource." );
	Require( texture.Width == 1024 && texture.Height == 1024 && texture.Mips == 11 && texture.ImageFormat == ImageFormat.RGBA32323232F, "Native " + role + " dimensions/mips/float32 format differ." );
	var observations = new System.Collections.Generic.List<object>(); row[role + "_readiness"] = observations;
	var timer = System.Diagnostics.Stopwatch.StartNew(); var count = texture.GetPixels().Length;
	observations.Add( new { stage = "immediate", pixel_count = count, frame = Application.FrameCount, texture.IsLoaded, texture.Mips, format = texture.ImageFormat.ToString() } );
	while ( count != 1024 * 1024 && timer.Elapsed < TimeSpan.FromSeconds( 3 ) )
	{
		texture.MarkUsed( 1024 ); var frame = Application.FrameCount;
		while ( Application.FrameCount < frame + 3 && timer.Elapsed < TimeSpan.FromSeconds( 3 ) ) { await Task.Delay( 20, cancellationToken ); await SafePoint(); }
		count = texture.GetPixels().Length; observations.Add( new { stage = "after_public_usage_signal", pixel_count = count, frame = Application.FrameCount, elapsed_ms = timer.Elapsed.TotalMilliseconds } );
	}
	Require( count == 1024 * 1024, "Native " + role + " did not reach full residency within the diagnostic observation." );
	var values = new float[1024 * 1024 * 4]; texture.GetPixels( (0, 0, 1024, 1024), 0, 0, values.AsSpan(), ImageFormat.RGBA32323232F );
	row[role + "_resource_path"] = texture.ResourcePath;
	return values;
}
try
{
	await SafePoint(); phase = "verify-pinned-source";
	var corpus = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets", "Models", "MetalRoughSpheres", "glTF" );
	var expectedHashes = new System.Collections.Generic.Dictionary<string, string>
	{
		["MetalRoughSpheres.gltf"] = "a24fd6fd24b2baef80a5d56b465a55635f7ec79039414e25c341d1402cc28048",
		["MetalRoughSpheres0.bin"] = "f35a1caf37837b2987e24859d812a23e40ca37e1f741782e2a754961250e8c36",
		["Spheres_BaseColor.png"] = "7327b8de2ce91d0ca4fdefcf28fdc58cb555e885a8e5f3b3cfab867c3f349efc",
		["Spheres_MetalRough.png"] = "7b352787106705789df0e025a8ea2209074569cd87029369bc66080dfca78e5a"
	};
	var sourceFiles = new System.Collections.Generic.Dictionary<string, byte[]>();
	foreach ( var expected in expectedHashes ) { var bytes = System.IO.File.ReadAllBytes( System.IO.Path.Combine( corpus, expected.Key ) ); Require( Hash( bytes ) == expected.Value, "Pinned source changed: " + expected.Key ); sourceFiles.Add( expected.Key, bytes ); }
	var expectedRgba = DecodePinnedRgbaPng( sourceFiles["Spheres_MetalRough.png"] ); hashes["independent_decoded_mr_rgba8"] = Hash( expectedRgba );
	byte[] Resolve( string uri ) => sourceFiles.TryGetValue( uri, out var bytes ) ? bytes : throw new InvalidOperationException( "Unexpected source dependency: " + uri );
	phase = "mount-private-fixtures";
	var redirectType = engine.GetType( "Sandbox.Internal.RedirectFileSystem", true ); redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } ); redirect = (IDisposable)redirectObject;
	removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	var constructor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( ctor => { var p = ctor.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
	mount = (BaseFileSystem)constructor.Invoke( new[] { redirectObject } ); aggregate = Sandbox.FileSystem.Mounted; Require( aggregate is not null, "Managed mounts unavailable." );
	unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ).Invoke( aggregate, new object[] { mount } ); mounted = true;
	var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging", true ); var logEvent = logging.GetEvent( "OnMessage", flags );
	logHandler = entry => { var text = (entry.Message ?? "") + (entry.Exception is null ? "" : " " + entry.Exception.Message); if ( !text.Contains( prefix, StringComparison.OrdinalIgnoreCase ) ) return; lock ( messages ) if ( messages.Count < 64 ) messages.Add( entry.Level + ": " + text[..Math.Min( text.Length, 2048 )] ); };
	logEvent.GetAddMethod( true ).Invoke( null, new object[] { logHandler } ); removeLog = logEvent.GetRemoveMethod( true );
	foreach ( var factors in new[] { (name: "original", metallic: 1f, roughness: 1f), (name: "diagnostic-nondefault-factors", metallic: .25f, roughness: .7f) } ) foreach ( var form in new[] { "source", "compiled" } )
	{
		await SafePoint(); phase = factors.name + "-" + form;
		var row = new System.Collections.Generic.Dictionary<string, object> { ["name"] = factors.name, ["form"] = form, ["passed"] = false, ["expected_metallic_factor"] = factors.metallic, ["expected_roughness_factor"] = factors.roughness, ["fixture_origin"] = factors.name == "original" ? "Untouched pinned glTF source; compiled form is a packaging-only derivative." : "Explicit diagnostic copy changes material0 metallicFactor/roughnessFactor only." };
		try
		{
			var source = sourceFiles["MetalRoughSpheres.gltf"];
			if ( factors.name != "original" ) { var json = System.Text.Json.Nodes.JsonNode.Parse( source ); json["materials"][0]["pbrMetallicRoughness"]["metallicFactor"] = factors.metallic; json["materials"][0]["pbrMetallicRoughness"]["roughnessFactor"] = factors.roughness; source = System.Text.Encoding.UTF8.GetBytes( json.ToJsonString() ); }
			var directory = factors.name + "/" + form; string relative;
			if ( form == "source" ) { relative = directory + "/MetalRoughSpheres.gltf"; foreach ( var file in sourceFiles ) Put( directory + "/" + file.Key, file.Key.EndsWith( ".gltf", StringComparison.Ordinal ) ? source : file.Value ); }
			else
			{
				var compiled = Sandbox.Gltf.GltfCompiler.Compile( source, Resolve ); var bundle = Sandbox.Gltf.GltfArtifactDependencies.Prepare( compiled, Resolve );
				relative = directory + "/model.glb"; Put( relative + "_c", bundle.Artifact ); foreach ( var dependency in bundle.Dependencies ) Put( directory + "/" + dependency.Key, dependency.Value );
				Require( !aggregate.FileExists( prefix + "/" + relative ), "Compiled test has a source sibling." );
			}
			engine.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );
			var logical = prefix + "/" + relative; row["asset_path"] = logical; model = Model.Load( logical );
			var valid = model is { IsValid: true } && !model.IsError && !ReferenceEquals( model, Model.Error ); row["load"] = valid ? "passed" : "failed";
			ownedModel = valid && string.Equals( model.ResourcePath, logical, StringComparison.OrdinalIgnoreCase ); Require( valid && ownedModel, "Actual Model.Load did not return the unique valid fixture model." );
			var materials = model.MeshInfo.Meshes.SelectMany( mesh => mesh.DrawCalls ).Select( draw => draw.Material ).Where( candidate => candidate is { IsValid: true } && !candidate.IsError ).Distinct().ToArray();
			Require( materials.Length == 1, "Expected one effective native material." ); var material = materials[0];
			Require( material.GetFeature( "F_METALNESS_TEXTURE" ) == 1 && MathF.Abs( material.GetVector4( "g_flRoughnessScaleFactor" ).x - 1 ) < .000001f, "Packed texture feature/roughness exponent differs." );
			var normal = material.GetTexture( "g_tNormal" ); var metal = material.GetTexture( "g_tMetalness" );
			var roughnessPixels = await ReadFullFloat( normal, "normal_roughness", row ); var metalnessPixels = await ReadFullFloat( metal, "metalness", row );
			float maxRoughness = 0, maxMetalness = 0, maxFlatNormal = 0; double meanRoughness = 0, meanMetalness = 0;
			for ( var i = 0; i < 1024 * 1024; i++ )
			{
				var r = (float)(expectedRgba[i * 4 + 1] / 255.0 * factors.roughness); var m = (float)(expectedRgba[i * 4 + 2] / 255.0 * factors.metallic); meanRoughness += r; meanMetalness += m;
				maxRoughness = MathF.Max( maxRoughness, MathF.Abs( roughnessPixels[i * 4 + 2] - r ) ); maxMetalness = MathF.Max( maxMetalness, MathF.Abs( metalnessPixels[i * 4 + 1] - m ) );
				maxFlatNormal = MathF.Max( maxFlatNormal, MathF.Max( MathF.Abs( roughnessPixels[i * 4] - 1.00392163f * .5f ), MathF.Abs( roughnessPixels[i * 4 + 1] - 1.00392163f * .5f ) ) );
			}
			row["full_mip0_pixels_checked"] = 1024 * 1024; row["max_roughness_error"] = maxRoughness; row["max_metalness_error"] = maxMetalness; row["max_flat_normal_error"] = maxFlatNormal;
			Require( maxRoughness <= .000001f && maxMetalness <= .000001f && maxFlatNormal <= .000001f, "Native float32 packed pixels differ from independent decoded G/B channel oracle." );
			var lastR = new float[4]; var lastM = new float[4]; normal.GetPixels( (0, 0, 1, 1), 0, 10, lastR.AsSpan(), ImageFormat.RGBA32323232F ); metal.GetPixels( (0, 0, 1, 1), 0, 10, lastM.AsSpan(), ImageFormat.RGBA32323232F );
			row["last_mip_roughness"] = lastR[2]; row["last_mip_metalness"] = lastM[1]; row["expected_last_mip_roughness"] = meanRoughness / (1024 * 1024); row["expected_last_mip_metalness"] = meanMetalness / (1024 * 1024);
			Require( Math.Abs( lastR[2] - meanRoughness / (1024 * 1024) ) < .000001 && Math.Abs( lastM[1] - meanMetalness / (1024 * 1024) ) < .000001, "Last native mip differs from linear full-image average." );
			row["passed"] = true;
		}
		catch ( Exception ex ) when ( ex is not OutOfMemoryException && ex is not OperationCanceledException ) { row["error"] = ex.ToString(); }
		finally
		{
			var caseSucceeded = (bool)row["passed"]; row["passed"] = false; row["cleanup"] = "pending"; rows.Add( row );
			await MainThread.Wait(); var destroyFrame = Application.FrameCount;
			row["model_destroy_requested"] = ownedModel && model is not null;
			if ( ownedModel && model is not null ) Clean( "owned Model.Destroy", () => typeof( Model ).GetMethod( "Destroy", flags ).Invoke( model, null ) );
			model = null; ownedModel = false;
			// Only one task-owned model exists at a time. Give native queued strong
			// handle/frame-end releases real frames before compiling the next form.
			var timer = System.Diagnostics.Stopwatch.StartNew();
			while ( Application.FrameCount < destroyFrame + 3 && timer.Elapsed < TimeSpan.FromSeconds( 3 ) ) { await Task.Delay( 20, cancellationToken ); await MainThread.Wait(); }
			row["destroy_frame"] = destroyFrame; row["post_destroy_frame"] = Application.FrameCount;
			row["cleanup"] = cleanupErrors.Count == 0 && Application.FrameCount >= destroyFrame + 3 ? "passed" : "failed";
			row["passed"] = caseSucceeded && (string)row["cleanup"] == "passed";
			Require( Application.FrameCount >= destroyFrame + 3, "Native cleanup did not receive three real frames; stop before the next model." );
		}
		Require( cleanupErrors.Count == 0, "Cleanup failed; stop allocating." );
	}
	phase = "complete";
}
catch ( Exception ex ) { error = ex.ToString(); }
finally
{
	await MainThread.Wait(); if ( ownedModel && model is not null ) Clean( "remaining owned model", () => typeof( Model ).GetMethod( "Destroy", flags ).Invoke( model, null ) );
	if ( mounted ) Clean( "private unmount", () => unmount.Invoke( aggregate, new object[] { mount } ) );
	if ( redirectObject is not null ) foreach ( var alias in aliases ) Clean( "redirect", () => removeRedirect.Invoke( redirectObject, new object[] { alias } ) );
	if ( redirect is not null ) Clean( "redirect filesystem", () => redirect.Dispose() );
	if ( removeLog is not null && logHandler is not null ) Clean( "log handler", () => removeLog.Invoke( null, new object[] { logHandler } ) );
}
var receipt = new
{
	passed = error is null && phase == "complete" && cleanupErrors.Count == 0 && rows.Count == 4 && rows.All( row => (bool)row["passed"] ), phase, error, expected_cases = 4, cases = rows, cleanup_errors = cleanupErrors, fixture_sha256 = hashes, relevant_messages = messages,
	scope = "Actual Model.Load original source and derived packaging-only GLB_c; native float32 G-roughness/B-metallic factor packing and mip readback. No rendered BRDF/sampler/appearance acceptance or final compiled resource optimization claim.",
	limitations = "Two RGBA32F resources are an intermediate stock-shader mapping with substantial GPU memory and load-time conversion cost. This does not satisfy vmdl performance parity; native stock sampler overrides remain unverified/failing in the separate managed-renderer sampling proof.",
	corpus_commit = "90d7ede14c7e280af263824604b427a1ca02cb66", source_model = "MetalRoughSpheres", first_frame_readiness_verified = false, complete_material_parity_verified = false,
	engine_mvid = engine.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), source_sha256 = Hash( System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, "engine", "Tools", "Gltf", "editor", "editor_model_metalrough_load.csx" ) ) ),
	is_editor = Application.IsEditor, starting_frame = initialFrame, final_frame = Application.FrameCount, output
};
System.IO.File.WriteAllText( System.IO.Path.Combine( output, "receipt.json" ), System.Text.Json.JsonSerializer.Serialize( receipt, options ) );
return receipt;
