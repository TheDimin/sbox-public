// Run inside an existing editor only. These are actual Model.Load fixtures;
// model metadata, source metadata, and rendered appearance are separate evidence.
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot locate the checkout from " + engineLocation );
var runId = Guid.NewGuid().ToString( "N" );
var virtualBase = "gltf-material-acceptance/" + runId;
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-material-acceptance", runId );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var cases = new System.Collections.Generic.List<object>();
var fixtureHashes = new System.Collections.Generic.Dictionary<string, string>( StringComparer.Ordinal );
var redirects = new System.Collections.Generic.List<string>();
var ownedModels = new System.Collections.Generic.HashSet<Model>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var features = new[] { "KHR_materials_unlit", "KHR_materials_variants" }.ToHashSet( StringComparer.Ordinal );
string phase = "initialize", error = null;
object redirectObject = null;
IDisposable redirect = null;
BaseFileSystem mount = null, aggregate = null;
System.Reflection.MethodInfo unmount = null, remove = null;
bool mounted = false;
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
void Cleanup( string name, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( name + ": " + e ); } }
bool Valid( Model model ) => model is { IsValid: true } && !model.IsError && !ReferenceEquals( model, Model.Error );
bool Near( float a, float b ) => MathF.Abs( a - b ) < .0001f;
// Independent transfer oracle for the shipped stock shader's sRGB tint input.
float ExpectedStockTint( float linear ) => (float)(linear <= .0031308 ? linear * 12.92 : 1.055 * Math.Pow( linear, 1.0 / 2.4 ) - .055);

try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run only inside the editor outside a graphics scope." );
	phase = "prepare-fixtures";
	var corpus = Environment.GetEnvironmentVariable( "SBOX_GLTF_CORPUS" );
	if ( string.IsNullOrWhiteSpace( corpus ) ) corpus = System.IO.Path.Combine( root.FullName, "Temp", "gltf-reference", "glTF-Sample-Assets" );
	var triangleDir = System.IO.Path.Combine( corpus, "Models", "Triangle", "glTF" );
	var original = System.IO.File.ReadAllBytes( System.IO.Path.Combine( triangleDir, "Triangle.gltf" ) );
	var geometry = System.IO.File.ReadAllBytes( System.IO.Path.Combine( triangleDir, "Triangle.bin" ) );
	Require( Hash( original ) == "6f4a8a1fbefbac2d0adde0800864279fd98c3f119077380e6840810ed62b6cfc" && Hash( geometry ) == "898ed7f560dca9907a80c98be57fbbc0f3a092d2e3683b964ffcfa75c549ac62", "Pinned Khronos Triangle differs." );
	byte[] Resolve( string uri ) => uri == "Triangle.bin" ? geometry : throw new InvalidOperationException( "Unexpected fixture dependency: " + uri );
	var definitions = new (string Name, string Material)[]
	{
		("base-color", """{"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.7,0.3,0.25]}}"""),
		("metal-rough", """{"pbrMetallicRoughness":{"metallicFactor":0.25,"roughnessFactor":0.7}}"""),
		("opaque", """{"alphaMode":"OPAQUE","pbrMetallicRoughness":{"baseColorFactor":[0.2,0.7,0.3,0.25]}}"""),
		("mask", """{"alphaMode":"MASK","alphaCutoff":0.35,"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.7,0.3,0.25]}}"""),
		("blend", """{"alphaMode":"BLEND","pbrMetallicRoughness":{"baseColorFactor":[0.2,0.7,0.3,0.25]}}"""),
		("double-sided", """{"doubleSided":true}"""),
		("unlit", """{"extensions":{"KHR_materials_unlit":{}}}"""),
		("base-texture", """{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}}}"""),
		("metal-rough-texture", """{"pbrMetallicRoughness":{"metallicRoughnessTexture":{"index":0}}}"""),
		("normal-texture", """{"normalTexture":{"index":0,"scale":0.7}}"""),
		("occlusion-texture", """{"occlusionTexture":{"index":0,"strength":0.4}}"""),
		("emissive-texture", """{"emissiveTexture":{"index":0},"emissiveFactor":[0.4,0.3,0.2]}"""),
		("variants", """{"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.7,0.3,1]}}""")
	};
	// This self-generated, lossless image is only a fixture; it is not created as a runtime texture.
	using var bitmap = new Bitmap( 2, 2 );
	bitmap.SetPixels( new[] { new Color( 1, 0, 0, 1 ), new Color( 0, 1, 0, 1 ), new Color( 0, 0, 1, 1 ), new Color( 1, 1, 1, .5f ) } );
	var png = bitmap.ToPng();
	var uv = new byte[24];
	var uvValues = new[] { 0f, 0f, 1f, 0f, 0f, 1f };
	for ( var i = 0; i < uvValues.Length; i++ ) System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( uv.AsSpan( i * 4, 4 ), uvValues[i] );
	var files = new System.Collections.Generic.Dictionary<string, byte[]>( StringComparer.Ordinal );
	foreach ( var definition in definitions )
	{
		var node = System.Text.Json.Nodes.JsonNode.Parse( original );
		node["materials"] = System.Text.Json.Nodes.JsonNode.Parse( "[" + definition.Material + "]" );
		node["meshes"][0]["primitives"][0]["material"] = 0;
		if ( definition.Name.EndsWith( "-texture", StringComparison.Ordinal ) )
		{
			node["buffers"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( "{\"byteLength\":24,\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( uv ) + "\"}" ) );
			node["bufferViews"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( """{"buffer":1,"byteLength":24,"target":34962}""" ) );
			node["accessors"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( """{"bufferView":2,"componentType":5126,"count":3,"type":"VEC2"}""" ) );
			node["meshes"][0]["primitives"][0]["attributes"]["TEXCOORD_0"] = 2;
			node["images"] = System.Text.Json.Nodes.JsonNode.Parse( "[{\"uri\":\"data:image/png;base64," + Convert.ToBase64String( png ) + "\"}]" );
			node["textures"] = System.Text.Json.Nodes.JsonNode.Parse( """[{"source":0}]""" );
		}
		if ( definition.Name == "unlit" )
		{
			node["extensionsUsed"] = System.Text.Json.Nodes.JsonNode.Parse( """["KHR_materials_unlit"]""" );
			node["extensionsRequired"] = System.Text.Json.Nodes.JsonNode.Parse( """["KHR_materials_unlit"]""" );
		}
		if ( definition.Name == "variants" )
		{
			node["materials"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( """{"pbrMetallicRoughness":{"baseColorFactor":[0.8,0.1,0.4,1]}}""" ) );
			node["materials"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( """{"pbrMetallicRoughness":{"baseColorFactor":[0.1,0.2,0.9,1]}}""" ) );
			node["materials"].AsArray().Add( System.Text.Json.Nodes.JsonNode.Parse( """{"pbrMetallicRoughness":{"baseColorFactor":[0.9,0.8,0.1,1]}}""" ) );
			node["extensionsUsed"] = System.Text.Json.Nodes.JsonNode.Parse( """["KHR_materials_variants"]""" );
			node["extensionsRequired"] = System.Text.Json.Nodes.JsonNode.Parse( """["KHR_materials_variants"]""" );
			node["extensions"] = System.Text.Json.Nodes.JsonNode.Parse( """{"KHR_materials_variants":{"variants":[{"name":"Red"},{"name":"Blue"}]}}""" );
			node["meshes"][0]["primitives"][0]["extensions"] = System.Text.Json.Nodes.JsonNode.Parse( """{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]},{"material":2,"variants":[1]}]}}""" );
			var second = node["meshes"][0]["primitives"][0].DeepClone();
			second["material"] = 3;
			second["extensions"] = System.Text.Json.Nodes.JsonNode.Parse( """{"KHR_materials_variants":{"mappings":[{"material":2,"variants":[0]},{"material":1,"variants":[1]}]}}""" );
			node["meshes"][0]["primitives"].AsArray().Add( second );
		}
		var source = System.Text.Encoding.UTF8.GetBytes( node.ToJsonString() );
		var compiled = Sandbox.Gltf.GltfCompiler.Compile( source, Resolve, features );
		// Save both artifacts for independent Khronos validation and reproducibility.
		files.Add( definition.Name + ".gltf", source );
		files.Add( definition.Name + ".glb_c", compiled );
	}
	files.Add( "Triangle.bin", geometry );

	phase = "mount-fixtures";
	var redirectType = typeof( Model ).Assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
	redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
	redirect = (IDisposable)redirectObject;
	var add = redirectType.GetMethod( "AddAbsFile", flags );
	remove = redirectType.GetMethod( "RemoveAbsFile", flags );
	foreach ( var file in files )
	{
		var physical = System.IO.Path.Combine( output, file.Key );
		System.IO.File.WriteAllBytes( physical, file.Value );
		fixtureHashes.Add( file.Key, Hash( file.Value ) );
		var alias = "/" + virtualBase + "/" + file.Key;
		add.Invoke( redirectObject, new object[] { alias, physical } );
		redirects.Add( alias );
	}
	var ctor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( c => { var p = c.GetParameters(); return p.Length == 1 && p[0].ParameterType.IsInstanceOfType( redirectObject ); } );
	mount = (BaseFileSystem)ctor.Invoke( new[] { redirectObject } );
	aggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Game mount unavailable." );
	var mountMethod = typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	mountMethod.Invoke( aggregate, new object[] { mount } ); mounted = true;
	foreach ( var file in files ) Require( aggregate.FileExists( virtualBase + "/" + file.Key ) && Hash( aggregate.ReadAllBytes( virtualBase + "/" + file.Key ).ToArray() ) == fixtureHashes[file.Key], "Mounted fixture mismatch: " + file.Key );
	typeof( Model ).Assembly.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags ).Invoke( null, null );

	phase = "actual-model-load";
	foreach ( var definition in definitions )
	foreach ( var extension in new[] { ".gltf", ".glb_c" } )
	{
		cancellationToken.ThrowIfCancellationRequested();
		var name = definition.Name;
		var file = name + extension;
		var result = new System.Collections.Generic.Dictionary<string, object>
		{
			["case"] = name, ["file"] = file, ["sha256"] = fixtureHashes[file],
			["source_metadata"] = "not-tested", ["model_load"] = "not-tested", ["native_material_metadata"] = "not-tested",
			["per_instance_variant_behavior"] = "not-tested", ["rendered_appearance"] = "not-tested"
		};
		try
		{
			using var doc = Sandbox.Gltf.GltfReader.Read( files[file], Resolve, features );
			var authored = doc.ReadMaterial( 0 );
			Require( authored is not null, "Fixture material missing." );
			if ( name == "base-color" || name == "opaque" || name == "mask" || name == "blend" )
				Require( Near( authored.BaseColorFactor.X, .2f ) && Near( authored.BaseColorFactor.Y, .7f ) && Near( authored.BaseColorFactor.Z, .3f ) && Near( authored.BaseColorFactor.W, .25f ), "Authored base factor differs." );
			if ( name == "metal-rough" ) Require( Near( authored.MetallicFactor, .25f ) && Near( authored.RoughnessFactor, .7f ), "Authored metal/rough factors differ." );
			if ( name is "opaque" or "mask" or "blend" ) Require( authored.AlphaMode == name.ToUpperInvariant() && (name != "mask" || Near( authored.AlphaCutoff, .35f )), "Authored alpha differs." );
			if ( name == "double-sided" ) Require( authored.DoubleSided, "Authored double-sided flag differs." );
			if ( name == "unlit" ) Require( authored.Unlit, "Authored unlit flag differs." );
			if ( name == "base-texture" ) Require( authored.BaseColorTexture?.Index == 0, "Missing authored base texture." );
			if ( name == "metal-rough-texture" ) Require( authored.MetallicRoughnessTexture?.Index == 0, "Missing authored metal/rough texture." );
			if ( name == "normal-texture" ) Require( authored.NormalTexture?.Index == 0 && Near( authored.NormalTexture.Scale, .7f ), "Missing authored normal texture." );
			if ( name == "occlusion-texture" ) Require( authored.OcclusionTexture?.Index == 0 && Near( authored.OcclusionTexture.Strength, .4f ), "Missing authored occlusion texture." );
			if ( name == "emissive-texture" ) Require( authored.EmissiveTexture?.Index == 0 && Near( authored.EmissiveFactor.X, .4f ), "Missing authored emissive texture." );
			if ( name == "variants" ) Require( doc.GetMaterialForVariant( 0, 0, null ) == 0 && doc.GetMaterialForVariant( 0, 0, 0 ) == 1 && doc.GetMaterialForVariant( 0, 0, 1 ) == 2 && doc.GetMaterialForVariant( 0, 1, null ) == 3 && doc.GetMaterialForVariant( 0, 1, 0 ) == 2 && doc.GetMaterialForVariant( 0, 1, 1 ) == 1, "Authored two-slot default/variant mapping differs." );
			result["source_metadata"] = "passed";
		}
		catch ( OperationCanceledException ) { throw; }
		catch ( Exception e ) { result["source_metadata"] = "failed"; result["source_error"] = e.ToString(); }
		try
		{
			result["model_load"] = "failed"; // A thrown load must remain an explicit failure.
			var model = Model.Load( virtualBase + "/" + file );
			result["model_load"] = Valid( model ) ? "passed" : "failed";
			Require( Valid( model ), "Actual Model.Load returned an invalid or error model." );
			ownedModels.Add( model );
			var material = model.MeshInfo?.Meshes.SelectMany( x => x.DrawCalls ).Select( x => x.Material ).FirstOrDefault( x => x is { IsValid: true } && !x.IsError );
			Require( material is not null, "No valid native draw material." );
			result["shader"] = material.ShaderName;
			result["material_group_names"] = Enumerable.Range( 0, model.MaterialGroupCount ).Select( model.GetMaterialGroupName ).ToArray();
			if ( name is "base-color" or "opaque" )
			{
				Require( material.HasParameter( "g_vColorTint" ), "Stock tint parameter unavailable." );
				var tint = material.GetVector4( "g_vColorTint" );
				Require( Near( tint.x, ExpectedStockTint( .2f ) ) && Near( tint.y, ExpectedStockTint( .7f ) ) && Near( tint.z, ExpectedStockTint( .3f ) ) && Near( tint.w, 1f ), "Native OPAQUE tint must encode the linear glTF factor for the stock shader; alpha must be ignored." );
				result["native_material_metadata"] = "passed";
			}
			else if ( name == "variants" )
			{
				var materials = model.MeshInfo.Meshes.SelectMany( x => x.DrawCalls ).Select( x => x.Material ).Where( x => x is { IsValid: true } && !x.IsError && x.HasParameter( "g_vColorTint" ) ).ToArray();
				bool HasTint( float r, float g, float b ) => materials.Any( m => { var tint = m.GetVector4( "g_vColorTint" ); return Near( tint.x, ExpectedStockTint( r ) ) && Near( tint.y, ExpectedStockTint( g ) ) && Near( tint.z, ExpectedStockTint( b ) ) && Near( tint.w, 1 ); } );
				Require( HasTint( .2f, .7f, .3f ) && HasTint( .9f, .8f, .1f ), "Both authored default slot materials must survive loading." );
				result["native_material_metadata"] = "default slots passed; variant mapping pending";
			}
			else if ( name == "double-sided" )
			{
				Require( material.GetFeature( "F_RENDER_BACKFACES" ) == 1, "Native backface feature is disabled." );
				result["native_material_metadata"] = "passed";
			}
			else result["native_material_metadata"] = "pending: mapping needs established native shader or variant API evidence";
		}
		catch ( OperationCanceledException ) { throw; }
		catch ( Exception e )
		{
			if ( result["model_load"].Equals( "passed" ) ) result["native_material_metadata"] = "failed";
			result["error"] = e.ToString();
		}
		cases.Add( result );
	}
	phase = "complete";
}
catch ( OperationCanceledException e ) { error = "Cancelled: " + e; }
catch ( Exception e ) { error = e.ToString(); }
finally
{
	await MainThread.Wait();
	var destroy = typeof( Model ).GetMethod( "Destroy", flags );
	foreach ( var model in ownedModels ) if ( Valid( model ) && !ReferenceEquals( model, Model.Sphere ) ) Cleanup( "loaded model", () => destroy.Invoke( model, null ) );
	if ( mounted ) Cleanup( "managed mount", () => unmount.Invoke( aggregate, new object[] { mount } ) );
	if ( remove is not null ) foreach ( var path in redirects ) Cleanup( "redirect " + path, () => remove.Invoke( redirectObject, new object[] { path } ) );
	if ( redirect is not null ) Cleanup( "redirect filesystem", () => redirect.Dispose() );
}
var receipt = new
{
	phase, error, cases, cleanup_errors = cleanupErrors, fixture_sha256 = fixtureHashes,
	fixture_count = 26, recorded_cases = cases.Count,
	all_loads_passed = phase == "complete" && error is null && cleanupErrors.Count == 0 && cases.Count == 26 && cases.All( c => ((System.Collections.Generic.Dictionary<string, object>)c)["model_load"].Equals( "passed" ) ),
	full_material_acceptance = "pending: rendered appearance and per-instance variant behavior not tested",
	scope = "Mounted source and packaging-only GLB Model.Load, authored metadata and selected native material metadata; no renderer, per-instance variant, performance, optimizer or full material-fidelity proof.",
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), output
};
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
