using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.Validation;

using NVector = System.Numerics.Vector3;

/// <summary>Actual pinned sample checks against independent JSON/BIN-authored reference data.</summary>
public static class AnimatedMorphCubeCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );

	public static Result[] RunAll( string sampleRoot, string referencePath )
	{
		using var reference = JsonDocument.Parse( File.ReadAllBytes( referencePath ) );
		Require( reference.RootElement.GetProperty( "pin" ).GetString() == "90d7ede14c7e280af263824604b427a1ca02cb66", "Reference corpus revision mismatch." );
		var rows = new List<Result>();
		foreach ( var form in reference.RootElement.GetProperty( "forms" ).EnumerateArray() )
		{
			var relative = form.GetProperty( "source" ).GetString();
			var path = Path.Combine( sampleRoot, relative );
			Run( "authored_morph_streams_keep_exact_raw_source_layout", () => CheckUpload( path, form ) );
			Run( "all_keys_and_between_keys_match_independent_source_morph", () => CheckPoses( path, form ) );
			Run( "sixty_hertz_schedule_preserves_authored_duration_and_independent_clocks", () => CheckClock( path, form ) );

			void Run( string name, Func<int> check )
			{
				try { rows.Add( new Result( name, relative, check(), true, null ) ); }
				catch ( Exception error ) { rows.Add( new Result( name, relative, 0, false, error.ToString() ) ); }
			}
		}
		return rows.ToArray();
	}

	private static int CheckUpload( string path, JsonElement form )
	{
		using var document = Read( path, form );
		var upload = document.MaterializeMorphTargetsUpload( 0, 0 );
		Require( upload.VertexCount == 24 && upload.Targets.Count == 2, "Authored cube/target shape changed." );
		var targetReferences = form.GetProperty( "targetStreams" );
		var assertions = 1;
		var unique = new HashSet<GltfUploadStorage>( ReferenceEqualityComparer.Instance );
		for ( var target = 0; target < upload.Targets.Count; target++ )
		{
			var expected = targetReferences[target];
			Require( upload.Targets[target].Count == expected.EnumerateObject().Count(), "Target semantic omission/extra stream." ); assertions++;
			foreach ( var property in expected.EnumerateObject() )
			{
				var stream = upload.Targets[target][property.Name]; var descriptor = property.Value;
				Require( stream.ComponentType == descriptor.GetProperty( "componentType" ).GetInt32() && stream.Components == descriptor.GetProperty( "components" ).GetInt32() && stream.Normalized == descriptor.GetProperty( "normalized" ).GetBoolean(), "Source target format was inferred or repacked." );
				Require( stream.Count == descriptor.GetProperty( "count" ).GetInt32() && stream.ByteOffset == descriptor.GetProperty( "byteOffset" ).GetInt32() && stream.ByteStride == descriptor.GetProperty( "byteStride" ).GetInt32(), "Target actual offset/stride/count changed." );
				Require( stream.Binding.AccessorIndex == descriptor.GetProperty( "accessorIndex" ).GetInt32() && stream.Binding.ViewByteOffset == descriptor.GetProperty( "viewByteOffset" ).GetInt32(), "Target provenance changed." );
				Require( Convert.ToHexStringLower( SHA256.HashData( stream.Storage.Bytes.Span ) ) == descriptor.GetProperty( "viewSha256" ).GetString(), "Owned target bytes differ from the independent source view." );
				unique.Add( stream.Storage ); assertions += 4;
			}
		}
		Require( unique.Sum( storage => (long)storage.Bytes.Length ) == upload.OwnedBytes, "Owned-byte count duplicated or omitted a source storage." );
		return assertions + 1;
	}

	private static int CheckPoses( string path, JsonElement form )
	{
		using var document = Read( path, form );
		var node = form.GetProperty( "node" ).GetInt32(); var assertions = 0;
		Require( form.GetProperty( "keyTimes" ).GetArrayLength() == 127, "Actual authored clip is not 127 keys." );
		foreach ( var sample in form.GetProperty( "samples" ).EnumerateArray() )
		{
			var pose = document.EvaluatePose( 0, sample.GetProperty( "time" ).GetSingle() );
			CheckWeights( pose, node, sample ); assertions++;
			var positions = sample.GetProperty( "positionsInches" );
			for ( var vertex = 0; vertex < 24; vertex++ )
			{
				var expected = ReadVector( positions[vertex] ) * .0254f;
				// Reverse only the independently declared engine basis to compare
				// this evaluator's glTF-world result, without calling its converter.
				expected = new NVector( expected.Y, expected.Z, expected.X );
				AssertVector( expected, document.EvaluateVertexPosition( node, 0, vertex, pose ), 2e-6f, "CPU pose numerical error" ); assertions++;
			}
		}
		return assertions + 1;
	}

	private static int CheckClock( string path, JsonElement form )
	{
		using var document = Read( path, form );
		var node = form.GetProperty( "node" ).GetInt32(); var callbacks = 0;
		var player = new GltfGpuAnimationPlayer( document, _ => callbacks++ );
		var independent = new GltfGpuAnimationPlayer( document );
		player.SelectClip( 0 ); independent.SelectClip( 0 ); independent.Seek( 1.25 );
		var independentPose = independent.CurrentPose;
		var media = form.GetProperty( "media" ); var times = media.GetProperty( "times" ); var samples = media.GetProperty( "samples" );
		Require( times.GetArrayLength() >= 250, "Coarse proof frames must not be presented as smooth animation." );
		Require( Math.Abs( media.GetProperty( "encodedDuration" ).GetDouble() - player.Duration ) < 1e-7,
			"Encoded rational frame delays changed authored clip duration." );
		player.Play(); double previous = 0; var assertions = 2;
		for ( var frame = 0; frame < times.GetArrayLength(); frame++ )
		{
			var time = times[frame].GetDouble();
			Require( player.Advance( time - previous ) == (frame != 0), "Zero delta must reuse the selected pose; nonzero movement must evaluate once." );
			Require( Math.Abs( player.CurrentTime - time ) < 1e-10, "Clock dropped elapsed source seconds." );
			CheckWeights( player.CurrentPose, node, samples[frame] );
			Require( ReferenceEquals( independent.CurrentPose, independentPose ) && independent.CurrentTime == 1.25, "Instance clocks/poses leaked." );
			previous = time; assertions += 4;
		}
		player.Advance( player.Duration - previous );
		Require( !player.IsPlaying && player.CurrentTime == player.Duration, "Endpoint was not applied before finishing." );
		CheckWeights( player.CurrentPose, node, media.GetProperty( "endpoint" ) );
		Require( callbacks == times.GetArrayLength() + 1, "Select supplies frame zero; each nonzero advance and endpoint applies one shared pose." );
		return assertions + 3;
	}

	private static GltfDocument Read( string path, JsonElement form )
	{
		var bytes = File.ReadAllBytes( path );
		Require( Convert.ToHexStringLower( SHA256.HashData( bytes ) ) == form.GetProperty( "sha256" ).GetString(), "Source differs from authored reference." );
		return GltfReader.Read( bytes, uri => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( path ), Uri.UnescapeDataString( uri ) ) ),
			supportedExtensions: new HashSet<string>( StringComparer.Ordinal ) { "KHR_mesh_quantization" } );
	}

	private static void CheckWeights( GltfPose pose, int node, JsonElement sample )
	{
		var actual = pose.GetMorphWeights( node ); var expected = sample.GetProperty( "weights" );
		Require( actual.Length == expected.GetArrayLength(), "Target weights count mismatch." );
		for ( var target = 0; target < actual.Length; target++ )
			Require( float.IsFinite( actual[target] ) && MathF.Abs( actual[target] - expected[target].GetSingle() ) <= 2e-6f, "LINEAR weight sampling differs from independently decoded source." );
	}
	private static NVector ReadVector( JsonElement value ) => new( value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle() );
	private static void AssertVector( NVector expected, NVector actual, float tolerance, string label ) =>
		Require( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && float.IsFinite( actual.Z ) &&
			MathF.Abs( actual.X - expected.X ) <= tolerance && MathF.Abs( actual.Y - expected.Y ) <= tolerance && MathF.Abs( actual.Z - expected.Z ) <= tolerance,
			$"{label}: expected{expected}, actual{actual}, tolerance{tolerance:R} meters." );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
