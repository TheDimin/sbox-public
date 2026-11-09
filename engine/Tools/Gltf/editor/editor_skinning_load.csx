// Async method body for an existing s&box editor. Scoped acceptance probe:
// actual Model.Load failures stay failures, independent of CPU/native metadata.
// This does not construct native skin vertices or assume native joint indices.
var engineLocation = typeof( Model ).Assembly.Location;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Cannot locate checkout from " + engineLocation );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-skinning-load", runId );
System.IO.Directory.CreateDirectory( output );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
var fixtureFiles = new System.Collections.Generic.List<object>();
var jsonArtifacts = new System.Collections.Generic.List<object>();
var loadCases = new System.Collections.Generic.List<object>();
var cpuCases = new System.Collections.Generic.List<object>();
var fixtureByteCases = new System.Collections.Generic.List<object>();
var nativeMetadata = new System.Collections.Generic.List<object>();
var expectations = new System.Collections.Generic.List<object>();
var logs = new System.Collections.Generic.List<string>();
var failures = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var metadataErrors = new System.Collections.Generic.List<string>();
var models = new System.Collections.Generic.List<Model>();
var redirects = new System.Collections.Generic.List<string>();
var sourceHashes = new System.Collections.Generic.Dictionary<string, string>();
object redirectObject = null;
IDisposable redirect = null;
BaseFileSystem mount = null, aggregate = null;
System.Reflection.MethodInfo removeRedirect = null, unmount = null, removeLogHandler = null;
Action<LogEvent> logHandler = null;
bool mounted = false;
string phase = "initialize", error = null;
string rendererStatus = "not attempted", rendererLocation = null, rendererLoadedMvid = null, rendererDiskMvid = null;
string engineDiskMvid = null, gltfDiskMvid = null;
System.Reflection.MethodInfo readMeshSkin = null;
var startingFrame = Application.FrameCount;
int loadAttempts = 0, successfulLoads = 0, successfulFixtureByteCases = 0, successfulCpuCases = 0, completeNativeMetadata = 0;
byte[] compiled = null;
string packagingError = null;
const string clipName = "six_influence_exact_linear";
string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
void Clean( string step, Action action ) { try { action(); } catch ( Exception ex ) { cleanupErrors.Add( step + ": " + ex ); } }
bool Valid( Model value ) => value is { IsValid: true } && !value.IsError && !ReferenceEquals( value, Model.Error );
float[] Point( System.Numerics.Vector3 value ) => [value.X, value.Y, value.Z];
float[] Matrix( System.Numerics.Matrix4x4 value ) => [value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24, value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44];
void SaveJson( string name, object value )
{
	var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( value, jsonOptions ); System.IO.File.WriteAllBytes( System.IO.Path.Combine( output, name ), bytes );
	if ( name != "receipt.json" ) jsonArtifacts.Add( new { name, bytes = bytes.Length, sha256 = Hash( bytes ) } );
}
void SaveFixture( string name, byte[] bytes )
{
	System.IO.File.WriteAllBytes( System.IO.Path.Combine( output, name ), bytes );
	fixtureFiles.Add( new { name, bytes = bytes.Length, sha256 = Hash( bytes ) } );
}
string DiskMvid( string path )
{
	using var stream = System.IO.File.OpenRead( path ); using var pe = new System.Reflection.PortableExecutable.PEReader( stream );
	var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe ); return metadata.GetGuid( metadata.GetModuleDefinition().Mvid ).ToString();
}
async Task SafeMainThread()
{
	await MainThread.Wait();
	var timeout = System.Diagnostics.Stopwatch.StartNew();
	while ( Graphics.IsActive )
	{
		if ( timeout.Elapsed > TimeSpan.FromSeconds( 5 ) ) throw new TimeoutException( "Graphics remained active at a main-thread boundary." );
		await Task.Delay( 25, cancellationToken ); await MainThread.Wait();
	}
}

// These are fixture descriptors, never a native or fixed engine vertex layout.
var positions = new[] { new System.Numerics.Vector3( -.45f, -.35f, 0 ), new System.Numerics.Vector3( .55f, -.3f, 0 ), new System.Numerics.Vector3( -.1f, .6f, 0 ) };
var influenceIndices = new[] { new[] { 0, 1, 2, 3, 4, 5 }, new[] { 2, 3, 4, 5, 0, 1 }, new[] { 5, 4, 1, 0, 3, 2 } };
var weights = new[] { 51f / 255f, 51f / 255f, 51f / 255f, 51f / 255f, .1f, .1f };
var jointOrder = new[] { new[] { 4, 1, 6, 2, 5, 3 }, new[] { 2, 6, 3, 5, 1, 4 } };
var translations = new System.Numerics.Vector3[7];
var translationDeltas = new System.Numerics.Vector3[7];
var scales = new System.Numerics.Vector3[7];
var rotations = new System.Numerics.Quaternion[7];
translations[0] = new System.Numerics.Vector3( .12f, -.08f, .03f );
translationDeltas[0] = new System.Numerics.Vector3( .06f, -.03f, .02f );
scales[0] = new System.Numerics.Vector3( 1.25f, .8f, 1 );
rotations[0] = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, .17f );
for ( var node = 1; node <= 6; node++ )
{
	translations[node] = new System.Numerics.Vector3( .025f * node, .04f * (node % 3), .01f * node );
	translationDeltas[node] = new System.Numerics.Vector3( .08f * (node - 3), node % 2 == 0 ? .05f : -.05f, .015f * node );
	scales[node] = new System.Numerics.Vector3( 1, 1 + .01f * node, 1 );
	rotations[node] = System.Numerics.Quaternion.CreateFromAxisAngle( System.Numerics.Vector3.UnitZ, .09f * (node - 3) );
}
System.Numerics.Matrix4x4 Local( int node, float time ) => System.Numerics.Matrix4x4.CreateScale( scales[node] ) * System.Numerics.Matrix4x4.CreateFromQuaternion( rotations[node] ) * System.Numerics.Matrix4x4.CreateTranslation( translations[node] + translationDeltas[node] * time );
System.Numerics.Matrix4x4 World( int node, float time ) => node == 0 ? Local( 0, time ) : Local( node, time ) * Local( 0, time );
var inverseBinds = new[] { new System.Numerics.Matrix4x4[6], new System.Numerics.Matrix4x4[6] };
for ( var skin = 0; skin < 2; skin++ )
for ( var entry = 0; entry < 6; entry++ )
{
	Require( System.Numerics.Matrix4x4.Invert( World( jointOrder[skin][entry], 0 ), out var inverse ), "Fixture rest matrix is singular." );
	// Skin B deliberately has different inverse binds for the same joint nodes.
	var bindOffset = skin == 0 ? System.Numerics.Vector3.Zero : new System.Numerics.Vector3( .08f * (entry - 2), -.02f * entry, .01f * (entry % 2) );
	inverseBinds[skin][entry] = System.Numerics.Matrix4x4.CreateTranslation( bindOffset ) * inverse;
}
const float units = 1f / .0254f;
var basis = new System.Numerics.Matrix4x4( 0, units, 0, 0, 0, 0, units, 0, units, 0, 0, 0, 0, 0, 0, 1 );
Require( System.Numerics.Matrix4x4.Invert( basis, out var inverseBasis ), "Fixture basis is singular." );
var instancePlacements = new[]
{
	System.Numerics.Matrix4x4.CreateRotationX( .2f ) * System.Numerics.Matrix4x4.CreateTranslation( 3, -60, 7 ),
	System.Numerics.Matrix4x4.CreateRotationX( -.3f ) * System.Numerics.Matrix4x4.CreateTranslation( -4, 60, -5 )
};
System.Numerics.Vector3 Expected( int skin, int vertex, float time, int influenceCount = 6 )
{
	var result = System.Numerics.Vector3.Zero;
	for ( var influence = 0; influence < influenceCount; influence++ )
	{
		var entry = influenceIndices[vertex][influence];
		result += System.Numerics.Vector3.Transform( positions[vertex], inverseBinds[skin][entry] * World( jointOrder[skin][entry], time ) ) * weights[influence];
	}
	return result;
}

var bufferViews = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
var accessors = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
using var binaryStream = new System.IO.MemoryStream();
binaryStream.Write( new byte[12] ); // Nonzero first view origin is intentional.
int View( byte[] bytes, int? stride = null, int? target = null )
{
	while ( binaryStream.Position % 4 != 0 ) binaryStream.WriteByte( 0 );
	var descriptor = new System.Collections.Generic.Dictionary<string, object> { ["buffer"] = 0, ["byteOffset"] = (int)binaryStream.Position, ["byteLength"] = bytes.Length };
	if ( stride.HasValue ) descriptor["byteStride"] = stride.Value;
	if ( target.HasValue ) descriptor["target"] = target.Value;
	bufferViews.Add( descriptor ); binaryStream.Write( bytes ); return bufferViews.Count - 1;
}
int Accessor( int view, int offset, int componentType, int count, string type, bool normalized = false )
{
	var descriptor = new System.Collections.Generic.Dictionary<string, object> { ["bufferView"] = view, ["byteOffset"] = offset, ["componentType"] = componentType, ["count"] = count, ["type"] = type };
	if ( normalized ) descriptor["normalized"] = true;
	accessors.Add( descriptor ); return accessors.Count - 1;
}
void Float( byte[] bytes, int offset, float value ) => System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset, 4 ), value );
byte[] Floats( float[] values )
{
	var bytes = new byte[values.Length * 4]; for ( var i = 0; i < values.Length; i++ ) Float( bytes, i * 4, values[i] ); return bytes;
}
var geometryBytes = new byte[3 * 32];
var joints0Bytes = new byte[3 * 8]; var weights0Bytes = new byte[3 * 8];
var joints1Bytes = new byte[3 * 12]; var weights1Bytes = new byte[3 * 20];
for ( var vertex = 0; vertex < 3; vertex++ )
{
	var point = Point( positions[vertex] );
	for ( var component = 0; component < 3; component++ ) Float( geometryBytes, vertex * 32 + 4 + component * 4, point[component] );
	Float( geometryBytes, vertex * 32 + 16 + 8, 1 ); // glTF normal +Z.
	for ( var component = 0; component < 4; component++ )
	{
		joints0Bytes[vertex * 8 + 4 + component] = (byte)influenceIndices[vertex][component];
		weights0Bytes[vertex * 8 + component] = 51;
		System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( joints1Bytes.AsSpan( vertex * 12 + 4 + component * 2, 2 ), (ushort)(component < 2 ? influenceIndices[vertex][4 + component] : 0) );
		Float( weights1Bytes, vertex * 20 + 4 + component * 4, component < 2 ? .1f : 0 );
	}
}
var geometryView = View( geometryBytes, 32, 34962 );
var positionAccessor = Accessor( geometryView, 4, 5126, 3, "VEC3" );
accessors[positionAccessor]["min"] = new[] { -.45f, -.35f, 0f }; accessors[positionAccessor]["max"] = new[] { .55f, .6f, 0f };
var normalAccessor = Accessor( geometryView, 16, 5126, 3, "VEC3" );
var joints0Accessor = Accessor( View( joints0Bytes, 8, 34962 ), 4, 5121, 3, "VEC4" );
var weights0Accessor = Accessor( View( weights0Bytes, 8, 34962 ), 0, 5121, 3, "VEC4", true );
var joints1Accessor = Accessor( View( joints1Bytes, 12, 34962 ), 4, 5123, 3, "VEC4" );
var weights1Accessor = Accessor( View( weights1Bytes, 20, 34962 ), 4, 5126, 3, "VEC4" );
var inverseBindAccessors = new[] { Accessor( View( Floats( inverseBinds[0].SelectMany( Matrix ).ToArray() ) ), 0, 5126, 6, "MAT4" ), Accessor( View( Floats( inverseBinds[1].SelectMany( Matrix ).ToArray() ) ), 0, 5126, 6, "MAT4" ) };
var timeAccessor = Accessor( View( Floats( [0, 1] ) ), 0, 5126, 2, "SCALAR" );
accessors[timeAccessor]["min"] = new[] { 0f }; accessors[timeAccessor]["max"] = new[] { 1f };
var samplers = new System.Collections.Generic.List<object>(); var channels = new System.Collections.Generic.List<object>();
for ( var node = 0; node <= 6; node++ )
{
	var translationAccessor = Accessor( View( Floats( Point( translations[node] ).Concat( Point( translations[node] + translationDeltas[node] ) ).ToArray() ) ), 0, 5126, 2, "VEC3" );
	samplers.Add( new { input = timeAccessor, output = translationAccessor, interpolation = "LINEAR" } );
	channels.Add( new { sampler = node, target = new { node, path = "translation" } } );
}
var binary = binaryStream.ToArray();
float FixtureComponent( int accessorIndex, int vertex, int component )
{
	var accessor = accessors[accessorIndex]; var view = bufferViews[(int)accessor["bufferView"]];
	var componentType = (int)accessor["componentType"]; var size = componentType == 5121 ? 1 : componentType == 5123 ? 2 : componentType == 5126 ? 4 : throw new InvalidOperationException( "Unexpected fixture component type." );
	var components = (string)accessor["type"] == "VEC3" ? 3 : 4;
	Require( vertex >= 0 && vertex < (int)accessor["count"] && component >= 0 && component < components, "Fixture component index exceeds accessor." );
	var stride = view.TryGetValue( "byteStride", out var byteStride ) ? (int)byteStride : components * size;
	var relative = (int)accessor["byteOffset"] + vertex * stride + component * size;
	Require( relative >= 0 && relative + size <= (int)view["byteLength"], "Fixture component exceeds bufferView." );
	var absolute = (int)view["byteOffset"] + relative; Require( absolute >= 0 && absolute + size <= binary.Length, "Fixture component exceeds buffer." );
	var normalized = accessor.TryGetValue( "normalized", out var value ) && (bool)value;
	if ( componentType == 5121 ) return normalized ? binary[absolute] / 255f : binary[absolute];
	if ( componentType == 5123 ) { var integer = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( binary.AsSpan( absolute, 2 ) ); return normalized ? integer / 65535f : integer; }
	return System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian( binary.AsSpan( absolute, 4 ) );
}
byte[] Source( float restTime )
{
	var nodes = new System.Collections.Generic.List<object>();
	for ( var node = 0; node <= 6; node++ )
	{
		var q = rotations[node];
		var value = new System.Collections.Generic.Dictionary<string, object> { ["name"] = node == 0 ? "nonjoint_ancestor" : "joint_" + node, ["translation"] = Point( translations[node] + translationDeltas[node] * restTime ), ["rotation"] = new[] { q.X, q.Y, q.Z, q.W }, ["scale"] = Point( scales[node] ) };
		if ( node == 0 ) value["children"] = new[] { 1, 2, 3, 4, 5, 6 };
		nodes.Add( value );
	}
	// Valid but deliberately conspicuous mesh-node transforms must not be
	// multiplied into the skin equation a second time.
	nodes.Add( new { name = "shared_mesh_skin_A", mesh = 0, skin = 0, translation = new[] { 2f, -3f, 1f } } );
	nodes.Add( new { name = "shared_mesh_skin_B", mesh = 0, skin = 1, translation = new[] { -4f, 2f, -1f } } );
	return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( new
	{
		asset = new { version = "2.0", generator = "sbox editor_skinning_load acceptance fixture" },
		buffers = new[] { new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String( binary ), byteLength = binary.Length } }, bufferViews, accessors,
		meshes = new[] { new { name = "shared_six_influence_triangle", primitives = new[] { new { attributes = new System.Collections.Generic.Dictionary<string, int> { ["POSITION"] = positionAccessor, ["NORMAL"] = normalAccessor, ["JOINTS_0"] = joints0Accessor, ["WEIGHTS_0"] = weights0Accessor, ["JOINTS_1"] = joints1Accessor, ["WEIGHTS_1"] = weights1Accessor }, mode = 4 } } } },
		nodes, skins = new[] { new { name = "skin_A_reordered", inverseBindMatrices = inverseBindAccessors[0], skeleton = 0, joints = jointOrder[0] }, new { name = "skin_B_reordered_distinct_inverse_binds", inverseBindMatrices = inverseBindAccessors[1], skeleton = 0, joints = jointOrder[1] } },
		animations = new[] { new { name = clipName, samplers, channels } }, scenes = new[] { new { nodes = new[] { 0, 7, 8 } } }, scene = 0
	} );
}
var source = Source( 0 );

void ObserveNativeSkin( Model model, string label )
{
	var bones = new System.Collections.Generic.List<object>(); var meshRecords = new System.Collections.Generic.List<object>();
	var meshCount = 0; var readableSkinCount = 0;
	string observationError = null;
	try
	{
		for ( var master = 0; master < model.BoneCount; master++ ) bones.Add( new { master_index = master, name = model.GetBoneName( master ), parent_master_index = model.GetBoneParent( master ) } );
		var info = model.MeshInfo ?? throw new InvalidOperationException( "Model.MeshInfo unavailable." ); meshCount = info.Meshes.Length;
		for ( var mesh = 0; mesh < meshCount; mesh++ )
		{
			try
			{
				if ( readMeshSkin is null ) throw new InvalidOperationException( rendererStatus );
				var skin = readMeshSkin.Invoke( null, new object[] { model, mesh } );
				if ( skin is null ) throw new InvalidOperationException( "Native helper returned no skin metadata." );
				var type = skin.GetType();
				var masters = (int[])(type.GetField( "MasterBones", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "MasterBones unavailable." ));
				var inverse = (Matrix3x4[])(type.GetField( "InverseBindPoses", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "InverseBindPoses unavailable." ));
				var count = (int)(type.GetProperty( "BoneCount", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "BoneCount unavailable." ));
				var blendCount = (int)(type.GetField( "BlendWeightCount", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "BlendWeightCount unavailable." ));
				var vertexCount = (int)(type.GetField( "VertexCount", flags )?.GetValue( skin ) ?? throw new InvalidOperationException( "VertexCount unavailable." ));
				Require( count > 0 && masters.Length == count && inverse.Length == count, "Native skin arrays disagree." );
				var renderBones = new System.Collections.Generic.List<object>();
				for ( var renderBone = 0; renderBone < count; renderBone++ )
				{
					var value = inverse[renderBone]; var master = masters[renderBone];
					Require( master >= 0 && master < model.BoneCount, "Native master bone index is out of range." );
					var rows = new[] { new[] { value.Row0.x, value.Row0.y, value.Row0.z, value.Row0.w }, new[] { value.Row1.x, value.Row1.y, value.Row1.z, value.Row1.w }, new[] { value.Row2.x, value.Row2.y, value.Row2.z, value.Row2.w } };
					Require( rows.SelectMany( row => row ).All( float.IsFinite ), "Native inverse bind contains nonfinite values." );
					renderBones.Add( new { render_bone_index = renderBone, master_bone_index = master, master_bone_name = model.GetBoneName( master ), native_inverse_bind_rows = rows } );
				}
				readableSkinCount++;
				meshRecords.Add( new { model_mesh = mesh, status = "observed", bone_count = count, blend_weight_count = blendCount, vertex_count = vertexCount, render_bones = renderBones } );
			}
			catch ( Exception ex ) { meshRecords.Add( new { model_mesh = mesh, status = "unavailable", error = ex.ToString() } ); }
		}
		if ( model.BoneCount > 0 && meshCount > 0 && readableSkinCount == meshCount ) completeNativeMetadata++;
	}
	catch ( Exception ex ) { observationError = ex.ToString(); }
	nativeMetadata.Add( new { model = label, master_bone_count = model.BoneCount, model_mesh_count = meshCount, readable_skin_count = readableSkinCount, error = observationError, bones, meshes = meshRecords, mapping_established = false,
		mapping_note = "Native master/render indices and inverse binds are observations. Counts/names alone do not identify the source skin, source joint entry or native vertex-index interpretation." } );
}

try
{
	await SafeMainThread(); Require( Application.IsEditor, "Run in the existing editor." );
	try { engineDiskMvid = DiskMvid( engineLocation ); } catch ( Exception ex ) { metadataErrors.Add( "Engine disk identity: " + ex ); }
	try { gltfDiskMvid = DiskMvid( typeof( Sandbox.Gltf.GltfReader ).Assembly.Location ); } catch ( Exception ex ) { metadataErrors.Add( "glTF disk identity: " + ex ); }
	foreach ( var file in new[] { "engine/Tools/Gltf/editor/editor_skinning_load.csx", "engine/Sandbox.Engine/Resources/Model/Gltf/GltfModelLoader.cs", "engine/Sandbox.Engine/Resources/Model/Builder/ModelBuilder.Skeleton.cs", "engine/Sandbox.SceneRenderer/Gpu/RenderContext.cs", "engine/Sandbox.Gltf/GltfDocument.Scene.cs", "engine/Sandbox.Gltf/GltfDocument.Primitives.cs", "engine/Sandbox.Gltf/GltfDocument.Deformation.cs" } )
		try { sourceHashes[file] = Hash( System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, file ) ) ); } catch ( Exception ex ) { metadataErrors.Add( "Source hash " + file + ": " + ex ); }
	Require( weights.All( value => value > 0 ) && MathF.Abs( weights.Sum() - 1 ) < .000001f, "Fixture must have six nonzero influences summing to one across both sets." );
	SaveFixture( "six-influence.gltf", source ); SaveFixture( "six-influence.bin", binary );
	SaveJson( "fixture-descriptors.json", new { bufferViews, accessors, primitive_attributes = new { POSITION = positionAccessor, NORMAL = normalAccessor, JOINTS_0 = joints0Accessor, WEIGHTS_0 = weights0Accessor, JOINTS_1 = joints1Accessor, WEIGHTS_1 = weights1Accessor }, influence_indices = influenceIndices, decoded_weights = weights, combined_weight_sum = weights.Sum(), six_nonzero_influences_per_vertex = true, gltf_joint_orders = jointOrder, source_mesh = 0, mesh_nodes = new[] { 7, 8 }, skin_indices = new[] { 0, 1 }, nonjoint_ancestor = 0 } );
	foreach ( var time in new[] { 0f, .25f, .75f, 1f } )
	{
		var poseLabel = time.ToString( "0.00", System.Globalization.CultureInfo.InvariantCulture );
		SaveFixture( "rest-at-" + poseLabel + ".gltf", Source( time ) );
		for ( var skin = 0; skin < 2; skin++ )
		{
			var expected = positions.Select( ( _, vertex ) => Expected( skin, vertex, time ) ).ToArray();
			var omitted = positions.Select( ( _, vertex ) => Expected( skin, vertex, time, 4 ) ).ToArray();
			Require( expected.Zip( omitted, ( a, b ) => System.Numerics.Vector3.Distance( a, b ) ).Any( distance => distance > .001f ), "Fixture cannot detect omitted influence set." );
			if ( time > 0 ) Require( expected.Zip( omitted, ( a, b ) => System.Numerics.Vector3.Distance( a, b / .8f ) ).Any( distance => distance > .001f ), "Posed fixture cannot detect renormalized first-set truncation." );
			var palette = Enumerable.Range( 0, 6 ).Select( entry => inverseBinds[skin][entry] * World( jointOrder[skin][entry], time ) ).ToArray();
			expectations.Add( new { time, skin, mesh_node = 7 + skin, nonjoint_ancestor_world_matrix = Matrix( World( 0, time ) ), joint_node_order = jointOrder[skin], joint_local_matrices = jointOrder[skin].Select( node => Matrix( Local( node, time ) ) ).ToArray(), inverse_bind_matrices = inverseBinds[skin].Select( Matrix ).ToArray(), joint_world_matrices = jointOrder[skin].Select( node => Matrix( World( node, time ) ) ).ToArray(), final_skin_matrices = palette.Select( Matrix ).ToArray(), expected_gltf_world_points = expected.Select( Point ).ToArray(), omitted_second_set_points = omitted.Select( Point ).ToArray(), renormalized_first_set_points = omitted.Select( value => Point( value / .8f ) ).ToArray(), expected_engine_points_before_instance_placement = expected.Select( value => Point( System.Numerics.Vector3.Transform( value, basis ) ) ).ToArray(),
				instance_expectations = instancePlacements.Select( ( placement, instance ) => new { instance, placement = Matrix( placement ), final_engine_palette = palette.Select( value => Matrix( inverseBasis * value * basis * placement ) ).ToArray(), expected_engine_world_points = expected.Select( value => Point( System.Numerics.Vector3.Transform( System.Numerics.Vector3.Transform( value, basis ), placement ) ) ).ToArray() } ).ToArray() } );
		}
	}
	SaveJson( "expected-matrices-and-points.json", new { matrix_order = "System.Numerics row vectors; each 16-value row-major array is the equivalent glTF column-major matrix data", coordinate_basis = "glTF metres (x,y,z) -> engine inches (z,x,y)", basis = Matrix( basis ), clip = clipName, required_independent_instance_times = new[] { .25f, .75f }, source = "fixture constants and BCL matrix operations; no glTF evaluator or native bone data used", expectations } );

	phase = "mount-and-actual-model-load";
	var redirectType = typeof( Model ).Assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
	redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } ); redirect = (IDisposable)redirectObject;
	removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
	var constructor = typeof( BaseFileSystem ).GetConstructors( flags ).Single( value => { var parameters = value.GetParameters(); return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType( redirectObject ); } );
	mount = (BaseFileSystem)constructor.Invoke( new[] { redirectObject } ); aggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
	unmount = typeof( BaseFileSystem ).GetMethod( "UnMount", flags, null, new[] { typeof( BaseFileSystem ) }, null );
	typeof( BaseFileSystem ).GetMethod( "Mount", flags, null, new[] { typeof( BaseFileSystem ) }, null ).Invoke( aggregate, new object[] { mount } ); mounted = true;
	try
	{
		var logging = typeof( LogEvent ).Assembly.GetType( "Sandbox.Diagnostics.Logging", true ); var messageEvent = logging.GetEvent( "OnMessage", flags );
		logHandler = entry => { var message = (entry.Message ?? "") + (entry.Exception is null ? "" : " " + entry.Exception.Message); if ( message.Contains( runId, StringComparison.OrdinalIgnoreCase ) || message.Contains( "gltf", StringComparison.OrdinalIgnoreCase ) || message.Contains( "skin", StringComparison.OrdinalIgnoreCase ) ) lock ( logs ) if ( logs.Count < 90 ) logs.Add( entry.Level + ": " + message ); };
		messageEvent.GetAddMethod( true ).Invoke( null, new object[] { logHandler } ); removeLogHandler = messageEvent.GetRemoveMethod( true );
	}
	catch ( Exception ex ) { metadataErrors.Add( "Log capture unavailable: " + ex ); }
	foreach ( var form in new[] { (Name: "source", File: "six-influence.gltf"), (Name: "binary", File: "six-influence.glb"), (Name: "packaged", File: "six-influence.glb_c") } )
	{
		var bytes = source;
		if ( form.Name != "source" )
		{
			// Attempt source Model.Load before even packaging. A compiler failure
			// cannot replace or suppress the real source-loader observation.
			if ( compiled is null && packagingError is null )
			{
				try
				{
					var packaged = Sandbox.Gltf.GltfCompiler.Compile( source );
					// Portable GLB packaging only; no GPU optimization claim.
					SaveFixture( "six-influence.glb", packaged ); SaveFixture( "six-influence.glb_c", packaged ); compiled = packaged;
				}
				catch ( Exception ex ) { packagingError = ex.ToString(); failures.Add( "Fixture GLB packaging failed: " + ex.Message ); }
			}
			if ( compiled is null )
			{
				loadCases.Add( new { name = "actual Model.Load " + form.Name, passed = false, attempted = false, status = "blocked by fixture packaging failure", error = packagingError } );
				continue;
			}
			bytes = compiled;
		}
		var path = "gltf-skinning-load/" + runId + "/" + form.File;
		redirectType.GetMethod( "AddAbsFile", flags ).Invoke( redirectObject, new object[] { "/" + path, System.IO.Path.Combine( output, form.File ) } ); redirects.Add( "/" + path );
		Require( aggregate.FileExists( path ) && Hash( aggregate.ReadAllBytes( path ).ToArray() ) == Hash( bytes ), "Mounted fixture differs: " + path );
		Model loaded = null; string loadError = null; int logStart; lock ( logs ) logStart = logs.Count;
		loadAttempts++;
		try { loaded = Model.Load( path ); } catch ( Exception ex ) { loadError = ex.ToString(); }
		var valid = Valid( loaded );
		if ( valid ) { successfulLoads++; if ( !models.Any( item => ReferenceEquals( item, loaded ) ) ) models.Add( loaded ); }
		int? boneCount = null; string[] animationNames = Array.Empty<string>(); string loadMetadataError = null;
		if ( valid ) try { boneCount = loaded.BoneCount; animationNames = loaded.AnimationNames.ToArray(); } catch ( Exception ex ) { loadMetadataError = ex.ToString(); }
		string[] loadLogs; lock ( logs ) loadLogs = logs.Skip( logStart ).ToArray();
		loadCases.Add( new { name = "actual Model.Load " + form.Name, passed = valid, attempted = true, path, input_sha256 = Hash( bytes ), valid_model = valid, load_error = loadError, diagnostics = loadLogs,
			master_bone_count = boneCount, animation_names = animationNames, metadata_error = loadMetadataError, deformation_proven = false } );
		if ( !valid ) failures.Add( "Actual Model.Load rejected " + form.Name + " skin fixture; see load_error and diagnostics." );
	}

	// Optional metadata is discovered after every load outcome was retained.
	phase = "native-master-and-render-bone-observations";
	try
	{
		var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault( value => !value.IsDynamic && value.GetName().Name == "Sandbox.SceneRenderer" && System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( value ) == System.Runtime.Loader.AssemblyLoadContext.Default );
		if ( assembly is null ) rendererStatus = "renderer assembly not loaded";
		else
		{
			rendererLocation = assembly.Location; rendererLoadedMvid = assembly.ManifestModule.ModuleVersionId.ToString();
			rendererDiskMvid = DiskMvid( rendererLocation );
			if ( rendererLoadedMvid != rendererDiskMvid ) rendererStatus = "loaded renderer differs from disk";
			else { readMeshSkin = assembly.GetType( "Sandbox.SceneRenderer.Gpu.RenderContext", false )?.GetMethod( "ReadMeshSkin", flags, null, new[] { typeof( Model ), typeof( int ) }, null ); rendererStatus = readMeshSkin is null ? "ReadMeshSkin unavailable" : "helper available; mapping unproven"; }
		}
	}
	catch ( Exception ex ) { rendererStatus = "metadata discovery failed: " + ex; }
	foreach ( var loaded in models ) ObserveNativeSkin( loaded, loaded.Name );

	phase = "descriptor-driven-fixture-byte-audit";
	for ( var vertex = 0; vertex < 3; vertex++ )
	{
		try
		{
			var decodedWeights = new float[8]; var decodedJoints = new int[8];
			for ( var component = 0; component < 4; component++ )
			{
				decodedWeights[component] = FixtureComponent( weights0Accessor, vertex, component ); decodedWeights[4 + component] = FixtureComponent( weights1Accessor, vertex, component );
				decodedJoints[component] = (int)FixtureComponent( joints0Accessor, vertex, component ); decodedJoints[4 + component] = (int)FixtureComponent( joints1Accessor, vertex, component );
			}
			for ( var influence = 0; influence < 6; influence++ ) Require( decodedJoints[influence] == influenceIndices[vertex][influence] && decodedWeights[influence] == weights[influence], "Descriptor addressing changed a nonzero influence." );
			Require( decodedWeights[6] == 0 && decodedWeights[7] == 0 && decodedWeights.Count( value => value > 0 ) == 6 && MathF.Abs( decodedWeights.Sum() - 1 ) < .000001f, "Packed weights must have six nonzero entries summing to one across both sets." );
			var expectedPoint = Point( positions[vertex] );
			for ( var component = 0; component < 3; component++ ) Require( FixtureComponent( positionAccessor, vertex, component ) == expectedPoint[component] && FixtureComponent( normalAccessor, vertex, component ) == (component == 2 ? 1 : 0), "Interleaved position/normal descriptor addressing differs." );
			successfulFixtureByteCases++;
			fixtureByteCases.Add( new { passed = true, vertex, decoded_joint_entries = decodedJoints, decoded_weights = decodedWeights, combined_weight_sum = decodedWeights.Sum() } );
		}
		catch ( Exception ex ) { fixtureByteCases.Add( new { passed = false, vertex, error = ex.ToString() } ); failures.Add( "Fixture byte audit failed for vertex " + vertex + ": " + ex.Message ); }
	}

	phase = "independent-cpu-reference-comparisons";
	// No UploadLayout/UploadData producer types are referenced by this probe.
	// Posed snapshots avoid requiring a newly added pose API to compile the body.
	foreach ( var time in new[] { 0f, .25f, .75f, 1f } )
	foreach ( var form in new[] { "source", "compiled" } )
	{
		try
		{
			var bytes = Source( time ); if ( form == "compiled" ) bytes = Sandbox.Gltf.GltfCompiler.Compile( bytes );
			using var document = Sandbox.Gltf.GltfReader.Read( bytes );
			for ( var skin = 0; skin < 2; skin++ )
			{
				var decoded = document.ReadSkin( skin ); Require( decoded.Joints.SequenceEqual( jointOrder[skin] ), "Reader changed skin joint order." );
				for ( var vertex = 0; vertex < 3; vertex++ )
				{
					var expected = Expected( skin, vertex, time ); var actual = document.EvaluateVertexPosition( 7 + skin, 0, vertex ); var distance = System.Numerics.Vector3.Distance( expected, actual );
					Require( float.IsFinite( distance ) && distance < .00001f, $"{form} skin {skin}, vertex {vertex}, time {time}: {actual}; expected {expected}." );
					successfulCpuCases++; cpuCases.Add( new { passed = true, form, time, skin, vertex, expected = Point( expected ), actual = Point( actual ), error_metres = distance } );
				}
			}
		}
		catch ( Exception ex ) { cpuCases.Add( new { passed = false, form, time, error = ex.ToString() } ); failures.Add( "CPU comparison failed for " + form + " at time " + time + ": " + ex.Message ); }
	}
	phase = "complete-scoped-observations";
}
catch ( OperationCanceledException ex ) { error = "Cancelled: " + ex; }
catch ( Exception ex ) { error = ex.ToString(); }
finally
{
	var safeToClean = false;
	try { await SafeMainThread(); safeToClean = true; }
	catch ( Exception ex ) { cleanupErrors.Add( "main-thread boundary: " + ex ); }
	if ( safeToClean )
	{
		var destroy = typeof( Model ).GetMethod( "Destroy", flags );
		foreach ( var loaded in models ) Clean( "task-owned loaded model", () => (destroy ?? throw new InvalidOperationException( "Model.Destroy unavailable." )).Invoke( loaded, null ) );
		if ( removeLogHandler is not null && logHandler is not null ) Clean( "log handler", () => removeLogHandler.Invoke( null, new object[] { logHandler } ) );
		if ( mounted ) Clean( "managed mount", () => unmount.Invoke( aggregate, new object[] { mount } ) );
		foreach ( var path in redirects ) Clean( "redirect " + path, () => removeRedirect.Invoke( redirectObject, new object[] { path } ) );
		if ( redirect is not null ) Clean( "redirect filesystem", () => redirect.Dispose() );
	}
	else cleanupErrors.Add( "No safe main-thread boundary; task-owned native handles/mount retained rather than destroyed during rendering or on a worker thread." );
}
var receipt = new
{
	// Full acceptance requires independent runtime and rendered proofs below.
	passed = false, scope = "Six-influence source/GLB/packaged actual Model.Load, raw native master/render-bone observations and independent CPU fixture checks; no native construction substitute.",
	phase, error, failures, metadata_errors = metadataErrors, cleanup_errors = cleanupErrors, output, run_id = runId, is_editor = Application.IsEditor, starting_frame = startingFrame, final_frame = Application.FrameCount,
	engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), engine_disk_mvid = engineDiskMvid, gltf_location = typeof( Sandbox.Gltf.GltfReader ).Assembly.Location, gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_disk_mvid = gltfDiskMvid,
	renderer_status = rendererStatus, renderer_location = rendererLocation, renderer_loaded_mvid = rendererLoadedMvid, renderer_disk_mvid = rendererDiskMvid, source_sha256 = sourceHashes, fixture_files = fixtureFiles, json_artifacts = jsonArtifacts,
	load_attempts = loadAttempts, successful_loads = successfulLoads, fixture_packaging_error = packagingError, load_cases = loadCases, fixture_byte_cases = fixtureByteCases, cpu_cases = cpuCases, native_model_metadata = nativeMetadata,
	gates = new { engine_and_gltf_disk_identity_matches = engineDiskMvid == typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString() && gltfDiskMvid == typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), actual_model_load = loadAttempts == 3 && successfulLoads == 3, fixture_descriptors_match_bytes = successfulFixtureByteCases == 3, independent_cpu_points = successfulCpuCases == 48, all_loaded_model_native_metadata_observed = models.Count > 0 && completeNativeMetadata == models.Count, native_render_joint_mapping_established = false, ordinary_component_two_independent_pose_times = false, six_influence_gpu_deformation = false, source_compiled_render_parity = false, posed_bounds_visibility_and_shadows = false, legacy_vmdl_regression = false, independent_khronos_fixture_validation = false },
	required_next_proofs = new[]
	{
		"Validate the retained source, GLB and posed snapshot bytes independently with Khronos tooling; preserve validator identity and reports.",
		"Map each native mesh/render bone through its master bone to the correct source mesh-node, skin and original skin-joint entry; prove vertex JOINTS interpretation without index equivalence assumptions.",
		"Use two ordinary SkinnedModelRenderer components sharing the loaded model at exact clip times .25/.75 with the retained independent placements. Change one instance, prove the other unchanged, then hide/show and replace its model.",
		"Render each skin and each posed instance against retained independent six-influence point geometry, including omitted-set and renormalized-first-set negative controls; test bounds, shadows, source/compiled parity and empty frames in each renderer separately."
	}
};
SaveJson( "receipt.json", receipt );
return receipt;
