using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.Validation;

using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

/// <summary>
/// Model-specific tests compiled against the editor's loaded producer assembly.
/// Expected positions come from the independently transcribed two-joint sample,
/// never from EvaluatePose, GltfSkinningPalette or the GPU implementation.
/// This file is written before the direct GPU implementation and allocates no GPU resources.
/// </summary>
public static class SimpleSkinCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );

	public static Result[] RunAll( string checkoutRoot )
	{
		var results = new List<Result>();
		foreach ( var form in SimpleSkinReference.OpenForms( checkoutRoot ) )
		{
			Run( "source_derived_stream_formats_offsets_and_shared_storage", form, TestUploadDescriptors );
			Run( "bind_positions_match_independent_authored_geometry", form, TestBindPose );
			Run( "linear_rotation_keys_and_between_key_positions", form, TestAnimationPositions );
			Run( "full_affine_palette_matches_independent_engine_positions", form, TestPalette );
			Run( "evaluated_poses_are_independent_and_stable", form, TestIndependentPoses );
			Run( "upload_snapshot_outlives_document", form, TestUploadLifetime );
		}
		return results.ToArray();

		void Run( string name, SimpleSkinReference.Form form, Func<SimpleSkinReference.Form, int> test )
		{
			try { results.Add( new Result( name, form.Name, test( form ), true, null ) ); }
			catch ( Exception error ) { results.Add( new Result( name, form.Name, 0, false, error.ToString() ) ); }
		}
	}

	private static int TestUploadDescriptors( SimpleSkinReference.Form form )
	{
		using var document = form.Read();
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var position = upload.Attributes["POSITION"];
		var joints = upload.Attributes["JOINTS_0"];
		var weights = upload.Attributes["WEIGHTS_0"];
		Require( upload.Layout.VertexCount == 10 && upload.Layout.SourceMode == 4, "Unexpected geometry shape." );
		Require( upload.Attributes.Count == 3, "This sample has no authored normals, UVs, tangents or colors." );
		Require( position.ComponentType == 5126 && position.Components == 3 && position.ByteStride == 12 && position.ByteOffset == 0 && position.Count == 10 && !position.Normalized, "POSITION layout changed." );
		Require( joints.ComponentType == 5123 && joints.Components == 4 && joints.ByteStride == 16 && joints.ByteOffset == 0 && joints.Count == 10 && !joints.Normalized, "JOINTS_0 must remain U16x4 at stride 16, not packed stride 8." );
		Require( weights.ComponentType == 5126 && weights.Components == 4 && weights.ByteStride == 16 && weights.ByteOffset == 160 && weights.Count == 10 && !weights.Normalized, "WEIGHTS_0 must begin at byte 160 in the same view." );
		Require( ReferenceEquals( joints.Storage, weights.Storage ), "Shared source view was duplicated or bindings lost their relative offsets." );
		Require( joints.Storage.Bytes.Length == 320, "Shared view length changed." );
		Require( upload.Indices.ComponentType == 5123 && upload.Indices.Components == 1 && upload.Indices.Count == 24 && upload.Indices.ByteStride == 2, "Index source must remain U16." );
		for ( var vertex = 0; vertex < 10; vertex++ )
		{
			var p = SimpleSkinReference.ReadFloat3( position, vertex );
			AssertVector( SimpleSkinReference.BindPosition( vertex ), p, 0, $"source position {vertex}" );
			var observedJoints = SimpleSkinReference.ReadU16x4( joints, vertex );
			Require( observedJoints[0] == 0 && observedJoints[1] == (vertex < 2 ? 0 : 1) && observedJoints[2] == 0 && observedJoints[3] == 0, "Joint order or stride was ignored." );
			var observedWeights = SimpleSkinReference.ReadFloat4( weights, vertex );
			var w = (vertex / 2) * .25f;
			Require( observedWeights == new Vector4( 1 - w, w, 0, 0 ), "Weights were read from a packed assumed layout." );
		}
		return 8 + 10 * 3;
	}

	private static int TestBindPose( SimpleSkinReference.Form form )
	{
		using var document = form.Read();
		var skin = document.ReadSkin( 0 );
		Require( skin.Joints.SequenceEqual( new[] { 1, 2 } ), "Authored joint order changed." );
		Require( skin.InverseBindMatrices[0] == Matrix4x4.Identity, "Root inverse bind is identity." );
		Require( skin.InverseBindMatrices[1] == Matrix4x4.CreateTranslation( 0, -1, 0 ), "Hinge inverse bind has a -Y translation." );
		for ( var vertex = 0; vertex < 10; vertex++ )
			AssertVector( SimpleSkinReference.BindPosition( vertex ), document.EvaluateVertexPosition( 0, 0, vertex ), 1e-6f, $"bind {vertex}" );
		return 13;
	}

	private static int TestAnimationPositions( SimpleSkinReference.Form form )
	{
		using var document = form.Read();
		var count = 0;
		foreach ( var time in SimpleSkinReference.SampleTimes )
		{
			var pose = document.EvaluatePose( 0, time );
			for ( var vertex = 0; vertex < 10; vertex++ )
			{
				AssertVector( SimpleSkinReference.Position( vertex, time ), document.EvaluateVertexPosition( 0, 0, vertex, pose ), 2e-6f, $"time {time:R}, vertex {vertex}" );
				count++;
			}
		}
		return count;
	}

	private static int TestPalette( SimpleSkinReference.Form form )
	{
		using var document = form.Read();
		var palette = new GltfSkinningPalette( document.ReadSkin( 0 ) );
		var count = 0;
		foreach ( var time in SimpleSkinReference.SampleTimes )
		{
			var matrices = palette.Evaluate( document.EvaluatePose( 0, time ) );
			Require( matrices.Length == 2, "Skin palette count changed." );
			for ( var vertex = 0; vertex < 10; vertex++ )
			{
				var source = SimpleSkinReference.ToEngine( SimpleSkinReference.BindPosition( vertex ) );
				var w = (vertex / 2) * .25f;
				var actual = Vector3.Transform( source, matrices[0] ) * (1 - w) + Vector3.Transform( source, matrices[1] ) * w;
				AssertVector( SimpleSkinReference.ToEngine( SimpleSkinReference.Position( vertex, time ) ), actual, .0002f, $"palette {time:R}, vertex {vertex}" );
				count++;
			}
		}
		return count;
	}

	private static int TestIndependentPoses( SimpleSkinReference.Form form )
	{
		using var document = form.Read();
		var a = document.EvaluatePose( 0, .25f );
		var aMatrix = a.GetWorldTransform( 2 );
		var b = document.EvaluatePose( 0, 3.75f );
		Require( a.GetWorldTransform( 2 ) == aMatrix, "Evaluating B mutated A's joint world." );
		Require( b.GetWorldTransform( 2 ) != aMatrix, "Independent animation phases were collapsed." );
		for ( var vertex = 0; vertex < 10; vertex++ )
		{
			AssertVector( SimpleSkinReference.Position( vertex, .25f ), document.EvaluateVertexPosition( 0, 0, vertex, a ), 2e-6f, "A retained position" );
			AssertVector( SimpleSkinReference.Position( vertex, 3.75f ), document.EvaluateVertexPosition( 0, 0, vertex, b ), 2e-6f, "B position" );
		}
		return 22;
	}

	private static int TestUploadLifetime( SimpleSkinReference.Form form )
	{
		GltfPrimitiveUploadData upload;
		using ( var document = form.Read() ) upload = document.MaterializePrimitiveUpload( 0, 0 );
		for ( var vertex = 0; vertex < 10; vertex++ )
			AssertVector( SimpleSkinReference.BindPosition( vertex ), SimpleSkinReference.ReadFloat3( upload.Attributes["POSITION"], vertex ), 0, "Detached upload position" );
		Require( ReferenceEquals( upload.Attributes["JOINTS_0"].Storage, upload.Attributes["WEIGHTS_0"].Storage ), "Storage alias changed after document disposal." );
		return 11;
	}

	internal static void AssertVector( Vector3 expected, Vector3 actual, float tolerance, string label )
	{
		Require( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && float.IsFinite( actual.Z ), label + " is not finite." );
		Require( MathF.Abs( expected.X - actual.X ) <= tolerance && MathF.Abs( expected.Y - actual.Y ) <= tolerance && MathF.Abs( expected.Z - actual.Z ) <= tolerance,
			$"{label}: expected {expected}, actual {actual}, tolerance {tolerance:R}." );
	}

	internal static void Require( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
	}
}
