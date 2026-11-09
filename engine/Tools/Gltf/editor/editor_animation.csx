// Run as an async method body in the existing s&box editor. CPU pose/reference
// checks only: this does not claim Model.Load animation or native playback.
var engineLocation = typeof( Model ).Assembly.Location;
var checkout = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( checkout is not null && !System.IO.File.Exists( System.IO.Path.Combine( checkout.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) checkout = checkout.Parent;
if ( checkout is null ) throw new InvalidOperationException( "Could not locate the checkout from " + engineLocation );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( checkout.FullName, "Temp", "gltf-validation", "editor-animation", runId );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var results = new System.Collections.Generic.List<object>();
var failures = new System.Collections.Generic.List<string>();
var sourceHashes = new System.Collections.Generic.Dictionary<string, string>();
var startingFrame = Application.FrameCount;
var phase = "initialization";
string Hash( byte[] value ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( value ) );
string HashFile( string relative ) => Hash( System.IO.File.ReadAllBytes( System.IO.Path.Combine( checkout.FullName, relative ) ) );
void Save( object receipt ) => System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );

// Analytic fixture reference; independent of GltfAnimationSampler/EvaluatePose.
float[] Expected( string path, string interpolation, float time, float[][] keys, float[][] tangents )
{
	var componentCount = keys[0].Length;
	var result = new float[componentCount];
	var times = new[] { 0f, 2f, 3f };
	var segment = time < 2 ? 0 : 1;
	if ( time <= 0 ) return (float[])keys[0].Clone();
	if ( time >= 3 ) return (float[])keys[2].Clone();
	if ( interpolation == "STEP" ) return (float[])keys[segment].Clone();
	var duration = (double)times[segment + 1] - times[segment];
	var t = ((double)time - times[segment]) / duration;
	if ( interpolation == "LINEAR" && path == "rotation" )
	{
		// All fixture rotations are positive, shortest-arc rotations about Z.
		var angleA = segment * Math.PI / 4;
		var angleB = (segment + 1) * Math.PI / 4;
		var halfAngle = angleA + (angleB - angleA) * t;
		return new[] { 0f, 0f, (float)Math.Sin( halfAngle ), (float)Math.Cos( halfAngle ) };
	}
	for ( var c = 0; c < componentCount; c++ )
	{
		if ( interpolation == "LINEAR" ) result[c] = (float)(keys[segment][c] + (keys[segment + 1][c] - keys[segment][c]) * t);
		else
		{
			var t2 = t * t;
			var t3 = t2 * t;
			result[c] = (float)((2*t3 - 3*t2 + 1)*keys[segment][c] + (t3 - 2*t2 + t)*duration*tangents[segment][c]
				+ (-2*t3 + 3*t2)*keys[segment + 1][c] + (t3 - t2)*duration*tangents[segment + 1][c]);
		}
	}
	if ( path == "rotation" )
	{
		var length = Math.Sqrt( (double)result[0]*result[0] + (double)result[1]*result[1] + (double)result[2]*result[2] + (double)result[3]*result[3] );
		for ( var c = 0; c < 4; c++ ) result[c] = (float)(result[c] / length);
	}
	return result;
}

byte[] Fixture( string path, string interpolation, float[][] keys, float[][] tangents )
{
	var bytes = new System.Collections.Generic.List<byte>();
	var views = new System.Collections.Generic.List<object>();
	var accessors = new System.Collections.Generic.List<object>();
	int Floats( float[] values, string type, int count, float[] min = null, float[] max = null )
	{
		var offset = bytes.Count;
		foreach ( var value in values )
		{
			var encoded = new byte[4];
			System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( encoded, value );
			bytes.AddRange( encoded );
		}
		var view = views.Count;
		views.Add( new { buffer = 0, byteOffset = offset, byteLength = bytes.Count - offset } );
		var accessor = new System.Collections.Generic.Dictionary<string, object> { ["bufferView"] = view, ["componentType"] = 5126, ["count"] = count, ["type"] = type };
		if ( min is not null ) accessor["min"] = min;
		if ( max is not null ) accessor["max"] = max;
		accessors.Add( accessor );
		return accessors.Count - 1;
	}
	var input = Floats( new[] { 0f, 2f, 3f }, "SCALAR", 3, new[] { 0f }, new[] { 3f } );
	var values = new System.Collections.Generic.List<float>();
	for ( var i = 0; i < 3; i++ )
	{
		if ( interpolation == "CUBICSPLINE" ) values.AddRange( tangents[i] );
		values.AddRange( keys[i] );
		if ( interpolation == "CUBICSPLINE" ) values.AddRange( tangents[i] );
	}
	var animationOutput = Floats( values.ToArray(), path == "rotation" ? "VEC4" : "VEC3", interpolation == "CUBICSPLINE" ? 9 : 3 );
	var positions = Floats( new[] { 0f,0f,0f, 1f,0f,0f, 0f,1f,0f }, "VEC3", 3, new[] { 0f,0f,0f }, new[] { 1f,1f,0f } );
	var jointOffset = bytes.Count;
	bytes.AddRange( new byte[12] );
	var jointsView = views.Count;
	views.Add( new { buffer = 0, byteOffset = jointOffset, byteLength = 12 } );
	var joints = accessors.Count;
	accessors.Add( new { bufferView = jointsView, componentType = 5121, count = 3, type = "VEC4" } );
	var weights = Floats( new[] { 1f,0f,0f,0f, 1f,0f,0f,0f, 1f,0f,0f,0f }, "VEC4", 3 );
	return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( new
	{
		asset = new { version = "2.0" }, scene = 0, scenes = new[] { new { nodes = new[] { 0, 2 } } },
		nodes = new object[] { new { children = new[] { 1 } }, new { translation = new[] { 0f, 1f, 0f } }, new { mesh = 0, skin = 0 } },
		skins = new[] { new { joints = new[] { 1 }, skeleton = 0 } },
		meshes = new[] { new { primitives = new[] { new { attributes = new System.Collections.Generic.Dictionary<string, int> { ["POSITION"] = positions, ["JOINTS_0"] = joints, ["WEIGHTS_0"] = weights } } } } },
		buffers = new[] { new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes.ToArray() ), byteLength = bytes.Count } },
		bufferViews = views, accessors,
		animations = new[] { new { samplers = new[] { new { input, output = animationOutput, interpolation } }, channels = new[] { new { sampler = 0, target = new { node = 0, path } } } } }
	} );
}

try
{
	if ( !Application.IsEditor ) throw new InvalidOperationException( "This validation requires a running s&box editor." );
	foreach ( var path in new[] { "engine/Sandbox.Gltf/GltfAnimationSampler.cs", "engine/Sandbox.Gltf/GltfDocument.Pose.cs", "engine/Sandbox.Gltf/GltfCompiler.cs", "engine/Tools/Gltf/editor/editor_animation.csx" } ) sourceHashes[path] = HashFile( path );
	const float cpuReferenceEpsilon = 0.0001f; // CPU arithmetic guard only; no native playback tolerance is approved.
	var samples = new[] { -0.5f, 0f, 0.75f, 1f, MathF.BitDecrement( 2f ), 2f, MathF.BitIncrement( 2f ), 2.5f, 3f, 4f };
	var probes = new[] { System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY, System.Numerics.Vector3.UnitZ };
	foreach ( var path in new[] { "translation", "rotation", "scale" } )
	foreach ( var interpolation in new[] { "STEP", "LINEAR", "CUBICSPLINE" } )
	{
		cancellationToken.ThrowIfCancellationRequested();
		phase = path + "/" + interpolation;
		try
		{
		var q = MathF.Sqrt( 0.5f );
		float[][] keys = path switch
		{
			"translation" => new[] { new[] { 0f,0f,0f }, new[] { 2f,4f,6f }, new[] { 3f,6f,9f } },
			"scale" => new[] { new[] { 1f,1f,1f }, new[] { 2f,3f,4f }, new[] { 3f,4f,5f } },
			_ => new[] { new[] { 0f,0f,0f,1f }, new[] { 0f,0f,q,q }, new[] { 0f,0f,1f,0f } }
		};
		float[][] tangents = path == "rotation" ? new[] { new float[4], new float[4], new float[4] } :
			new[] { new[] { 0.25f,0.1f,-0.1f }, new[] { 0.5f,-0.2f,0.3f }, new[] { -0.1f,0.4f,0.2f } };
		var source = Fixture( path, interpolation, keys, tangents );
		var fixtureName = path + "-" + interpolation.ToLowerInvariant();
		var sourcePath = System.IO.Path.Combine( output, fixtureName + ".gltf" );
		var compiledPath = System.IO.Path.Combine( output, fixtureName + ".glb" );
		System.IO.File.WriteAllBytes( sourcePath, source );
		var compiled = Sandbox.Gltf.GltfCompiler.Compile( source );
		System.IO.File.WriteAllBytes( compiledPath, compiled );
		using var original = Sandbox.Gltf.GltfReader.Read( source );
		using var roundtrip = Sandbox.Gltf.GltfReader.Read( compiled );
		float maxError = 0, maxSourceCompiled = 0;
		var comparisons = 0;
		foreach ( var time in samples )
		{
			var expected = Expected( path, interpolation, time, keys, tangents );
			var translation = path == "translation" ? new System.Numerics.Vector3( expected[0], expected[1], expected[2] ) : System.Numerics.Vector3.Zero;
			var scale = path == "scale" ? new System.Numerics.Vector3( expected[0], expected[1], expected[2] ) : System.Numerics.Vector3.One;
			var rotation = path == "rotation" ? new System.Numerics.Quaternion( expected[0], expected[1], expected[2], expected[3] ) : System.Numerics.Quaternion.Identity;
			var parent = System.Numerics.Matrix4x4.CreateScale( scale ) * System.Numerics.Matrix4x4.CreateFromQuaternion( rotation ) * System.Numerics.Matrix4x4.CreateTranslation( translation );
			var child = System.Numerics.Matrix4x4.CreateTranslation( 0, 1, 0 ) * parent;
			var sourcePose = original.EvaluatePose( 0, time );
			var compiledPose = roundtrip.EvaluatePose( 0, time );
			foreach ( var probe in probes )
			foreach ( var node in new[] { 0, 1 } )
			{
				var reference = System.Numerics.Vector3.Transform( probe, node == 0 ? parent : child );
				var actual = System.Numerics.Vector3.Transform( probe, sourcePose.GetWorldTransform( node ) );
				var other = System.Numerics.Vector3.Transform( probe, compiledPose.GetWorldTransform( node ) );
				maxError = MathF.Max( maxError, MathF.Max( System.Numerics.Vector3.Distance( actual, reference ), System.Numerics.Vector3.Distance( other, reference ) ) );
				maxSourceCompiled = MathF.Max( maxSourceCompiled, System.Numerics.Vector3.Distance( actual, other ) );
				comparisons++;
			}
			var skinExpected = System.Numerics.Vector3.Transform( System.Numerics.Vector3.UnitX, child );
			var skinSource = original.EvaluateVertexPosition( 2, 0, 1, sourcePose );
			var skinCompiled = roundtrip.EvaluateVertexPosition( 2, 0, 1, compiledPose );
			maxError = MathF.Max( maxError, MathF.Max( System.Numerics.Vector3.Distance( skinSource, skinExpected ), System.Numerics.Vector3.Distance( skinCompiled, skinExpected ) ) );
			maxSourceCompiled = MathF.Max( maxSourceCompiled, System.Numerics.Vector3.Distance( skinSource, skinCompiled ) );
			comparisons++;
		}
		var passed = float.IsFinite( maxError ) && float.IsFinite( maxSourceCompiled ) && maxError <= cpuReferenceEpsilon && maxSourceCompiled <= cpuReferenceEpsilon;
		if ( !passed ) failures.Add( phase + ": CPU reference/source-compiled mismatch" );
		results.Add( new { path, interpolation, source_path = sourcePath, compiled_path = compiledPath, source_sha256 = Hash( source ), compiled_sha256 = Hash( compiled ), samples = samples.Length,
			comparisons, max_reference_error_gltf_units = maxError, max_source_compiled_error_gltf_units = maxSourceCompiled, passed } );
		}
		catch ( OperationCanceledException ) { throw; }
		catch ( Exception error )
		{
			failures.Add( phase + ": " + error );
			results.Add( new { path, interpolation, passed = false, error = error.ToString() } );
		}
	}
	phase = "complete";
	var receipt = new { cpu_reference_passed = failures.Count == 0 && results.Count == 9, full_animation_passed = false,
		native_model_load_animation = "not-run", native_playback = "not-run", native_rendering = "not-run", native_tolerances = "not-approved",
		scope = "synthetic glTF 2.0 CPU pose and one-joint vertex reference checks, original vs managed-compiled GLB; no native animation bridge",
		cpu_arithmetic_epsilon_gltf_units = cpuReferenceEpsilon, results, failures, source_hashes = sourceHashes,
		source_hash_qualification = "worktree source inventory at execution time; not proof that loaded modules were built from those bytes",
		engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(), gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(),
		is_editor = Application.IsEditor, starting_frame = startingFrame, final_frame = Application.FrameCount, run_id = runId, output };
	Save( receipt );
	return receipt;
}
catch ( OperationCanceledException ) { throw; }
catch ( Exception error )
{
	var receipt = new { cpu_reference_passed = false, full_animation_passed = false, native_playback = "not-run", phase, error = error.ToString(), results, failures, source_hashes = sourceHashes, run_id = runId, output };
	Save( receipt );
	return receipt;
}
