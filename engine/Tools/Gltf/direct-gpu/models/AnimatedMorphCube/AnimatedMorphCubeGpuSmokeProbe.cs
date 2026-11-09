using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Validation;

using NVector = System.Numerics.Vector3;

/// <summary>
/// Three authored-key GPU samples. The coordinator supplies its existing native
/// capture scene, borrowed compute/material resources, real frame waits and PNG
/// callback. This probe does not create a camera, compiler or native model/mesh.
/// </summary>
internal static class AnimatedMorphCubeGpuSmokeProbe
{
	public sealed record Bounds( float[] MinimumInches, float[] MaximumInches, float[] CenterInches,
		float[] SuggestedCameraPositionInches, float SuggestedOrthographicHeightInches );

	/// <summary>Independent source-oracle bounds over all authored/interior reference samples.</summary>
	public static Bounds ReferenceBounds( JsonElement form )
	{
		var minimum = new NVector( float.PositiveInfinity ); var maximum = new NVector( float.NegativeInfinity );
		var count = 0;
		foreach ( var sample in form.GetProperty( "samples" ).EnumerateArray() )
			foreach ( var vertex in sample.GetProperty( "positionsInches" ).EnumerateArray() )
			{
				var value = new NVector( vertex[0].GetSingle(), vertex[1].GetSingle(), vertex[2].GetSingle() );
				Require( float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z ), "Nonfinite independent source bounds." );
				minimum = NVector.Min( minimum, value ); maximum = NVector.Max( maximum, value ); count++;
			}
		Require( count > 0, "The source oracle has no positions for camera framing." );
		var center = (minimum + maximum) * .5f; var extent = maximum - minimum;
		var span = MathF.Max( extent.X, MathF.Max( extent.Y, extent.Z ) );
		Require( float.IsFinite( span ) && span > 0, "AnimatedMorphCube source bounds need finite nonzero extent." );
		// Source camera (+1,+.65,+1) maps to engine (+1,+1,+.65), without
		// changing the model basis. Twice the largest AABB span fits this view.
		var position = center + NVector.Normalize( new NVector( 1, 1, .65f ) ) * (span * 3 + 10);
		return new Bounds( Values( minimum ), Values( maximum ), Values( center ), Values( position ), span * 2 );
	}

	public static async Task<IReadOnlyList<object>> Run( GltfDocument document, JsonElement form, SceneWorld world,
		Material computeMaterial, Func<Task> waitForActualFrames,
		Func<string, GltfGpuInstance, Task> afterSample = null, Func<int?, Material> materialResolver = null )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( computeMaterial ); ArgumentNullException.ThrowIfNull( waitForActualFrames );
		var keys = form.GetProperty( "keyTimes" );
		Require( keys.GetArrayLength() == 127, "The bounded cube smoke expects this pinned clip's 127 authored keys." );
		var bounds = ReferenceBounds( form );
		var rows = new List<object>();
		using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
		using var instance = asset.CreateInstance( world );
		foreach ( var selected in new[] { (Name: "authored_start", Index: 0), (Name: "authored_middle_key", Index: keys.GetArrayLength() / 2), (Name: "authored_last_key", Index: keys.GetArrayLength() - 1) } )
		{
			var time = keys[selected.Index].GetSingle();
			var reference = form.GetProperty( "samples" ).EnumerateArray().Single( sample => sample.GetProperty( "time" ).GetSingle() == time );
			var beforeDraws = instance.DrawCalls;
			instance.SetPose( document.EvaluatePose( 0, time ) );
			var revision = instance.PoseRevision;
			await waitForActualFrames(); RequireSubmitted( instance, revision, beforeDraws );
			var snapshots = instance.Readback();
			Require( snapshots.Count == 1 && snapshots.All( snapshot => snapshot.IndexDataOrigin == "gpu-readback" ),
				"Index topology must come from completed GPU buffer readback, not a CPU plan copy." );
			var geometry = AnimatedMorphCubeGpuChecks.Measure( snapshots, form, reference, instance.Placement );
			var tangents = AnimatedMorphCubeGpuChecks.MeasureTangents( snapshots, reference, instance.Placement );
			Require( geometry.Finite && geometry.SourceTopologyMatches && tangents.Finite, "GPU source topology changed or produced nonfinite values." );
			var fingerprints = snapshots.Select( snapshot => new
			{
				snapshot.Node, snapshot.Mesh, snapshot.Primitive, snapshot.VertexCount, snapshot.VertexStride, snapshot.Fields,
				snapshot.IndexDataOrigin, indices = snapshot.Indices, source_vertices = snapshot.SourceVertices,
				vertex_bytes_sha256 = Hash( snapshot.Bytes ), index_bytes_sha256 = IndexHash( snapshot.Indices )
			} ).ToArray();
			if ( afterSample is not null )
			{
				await afterSample( selected.Name, instance ); RequireSubmitted( instance, revision, beforeDraws );
				var after = instance.Readback();
				Require( after.Count == fingerprints.Length && after.Select( (snapshot, index) =>
					Hash( snapshot.Bytes ) == fingerprints[index].vertex_bytes_sha256 && IndexHash( snapshot.Indices ) == fingerprints[index].index_bytes_sha256 ).All( same => same ),
					"Capturing one deterministic pose mutated its GPU output or topology." );
			}
			rows.Add( new
			{
				name = selected.Name, clip = 0, clip_name = "Square", authored_key_index = selected.Index, requested_time = (double)time,
				instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls, geometry, tangents, fingerprints,
				undefined_source_tangent_directions = reference.GetProperty( "undefinedTangentDirections" ).Clone(), independent_source_bounds = bounds,
				material_scope = materialResolver is null ? "source materials through the default stock s&box adapter" : "explicit borrowed material override; source-material fidelity not established",
				scope = "Three deterministic authored-key current-revision GPU readbacks. Numerical error is measured; explicit thresholds, independent images, the full key matrix, high-cadence media, wall-clock playback and Model.Load integration remain separate gates."
			} );
		}
		Require( rows.Count == 3, "Bounded cube smoke inventory changed." );
		return rows.AsReadOnly();
	}

	private static void RequireSubmitted( GltfGpuInstance instance, long revision, int beforeDraws ) =>
		Require( instance.PoseRevision == revision && instance.SubmittedRevision == revision && instance.DrawCalls > beforeDraws && instance.CallbackErrors.Length == 0,
			"Current cube pose was not successfully submitted: " + string.Join( "\n", instance.CallbackErrors ) );
	private static float[] Values( NVector value ) => new[] { value.X, value.Y, value.Z };
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static string IndexHash( uint[] values ) => Convert.ToHexStringLower( SHA256.HashData( MemoryMarshal.AsBytes( values.AsSpan() ) ) );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidDataException( message ); }
}
