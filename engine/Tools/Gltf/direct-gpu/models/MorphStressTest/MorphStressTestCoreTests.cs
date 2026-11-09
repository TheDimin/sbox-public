using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using static MorphStressTestReference;

/// <summary>28 CPU/data cases. No native model, GPU allocation or rendered evidence.</summary>
public static class MorphStressTestCoreTests
{
	public const int ExpectedCases = 28;
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new MorphStressTestReference( checkoutRoot ); var rows = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "all_8_targets_2_primitives_exact_source_layout", form, () =>
			{
				using var d = form.Read();
				for ( var primitive = 0; primitive < 2; primitive++ )
				{
					var upload = d.MaterializeMorphTargetsUpload( 0, primitive ); var p = reference.Primitive( primitive );
					Require( upload.Targets.Count == 8 && upload.VertexCount == reference.VertexCount( primitive ), "Target/vertex truncation." );
					var unique = new HashSet<GltfUploadStorage>( ReferenceEqualityComparer.Instance );
					for ( var target = 0; target < 8; target++ )
					{
						Require( upload.Targets[target].Keys.Order().SequenceEqual( new[] { "NORMAL", "POSITION" } ), "Authored target semantics changed." );
						foreach ( var semantic in new[] { "POSITION", "NORMAL" } )
						{
							var index = p.GetProperty( "targets" )[target].GetProperty( semantic ).GetInt32(); var expected = reference.Descriptor( form.Variant, index ); var stream = upload.Targets[target][semantic];
							Require( stream.Binding.AccessorIndex == index && stream.Binding.ViewByteOffset == expected.GetProperty( "viewByteOffset" ).GetInt32(), "Target order/provenance changed." );
							Require( stream.ByteOffset == expected.GetProperty( "byteOffset" ).GetInt32() && stream.ByteStride == expected.GetProperty( "byteStride" ).GetInt32() && stream.Count == upload.VertexCount && stream.ComponentType == 5126 && stream.Components == 3 && !stream.Normalized && !stream.IsMaterialized, "Morph raw layout inferred/repacked." );
							Require( Hash( stream.Storage.Bytes.Span ) == expected.GetProperty( "sha256" ).GetString(), "Raw target bytes differ from independent source view." ); unique.Add( stream.Storage );
						}
					}
					Require( unique.Sum( x => (long)x.Bytes.Length ) == upload.OwnedBytes, "Owned byte accounting changed." );
				}
			} );
			Run( "mesh_default_weights_and_pose_snapshots_are_independent", form, () =>
			{
				using var d = form.Read(); var a = d.EvaluatePose(); var b = d.EvaluatePose(); var weights = a.GetMorphWeights( 0 );
				Require( weights.Length == 8 && weights.All( x => x == 0 ), "Absent node weights must use authored mesh zero weights." ); weights[7] = 99;
				Require( a.GetMorphWeights( 0 )[7] == 0 && b.GetMorphWeights( 0 )[7] == 0, "Mutable weights escaped or pose instances leaked." );
			} );
			Run( "all_531_authored_keys_and_528_interiors_keep_linear_order", form, () =>
			{
				using var d = form.Read();
				for ( var clip = 0; clip < 3; clip++ )
				{
					var c = reference.Oracle.GetProperty( "clips" )[clip]; var times = Numbers( c.GetProperty( "times" ) );
					Require( c.GetProperty( "interpolation" ).GetString() == "LINEAR", "Actual source interpolation changed." );
					var samples = times.Concat( times.Zip( times.Skip( 1 ), (a, b) => (a + b) / 2 ) ).Concat( new[] { -1.0, times[^1] + 1 } );
					foreach ( var time in samples )
					{
						var sourceTime = (float)time; var expected = reference.Weights( clip, sourceTime ); var actual = d.EvaluatePose( clip, sourceTime ).GetMorphWeights( 0 );
						Require( actual.Length == 8, "Animation lost target weights." ); for ( var t = 0; t < 8; t++ ) Close( expected[t], actual[t], 2e-6, "Independent LINEAR weight" );
					}
				}
			} );
			Run( "every_target_last_target_and_interior_geometry_witnesses", form, () =>
			{
				using var d = form.Read();
				for ( var clip = 0; clip < 3; clip++ )
				{
					var times = Numbers( reference.Oracle.GetProperty( "clips" )[clip].GetProperty( "times" ) );
					foreach ( var time in new[] { times[0], times[times.Length / 2], (times[times.Length / 2] + times[times.Length / 2 + 1]) / 2, times[^1] } ) CheckPositions( reference, d, d.EvaluatePose( clip, (float)time ), reference.Weights( clip, (float)time ) );
				}
			} );
			Run( "two_uv_sets_normals_and_source_tangent_absence", form, () =>
			{
				using var d = form.Read();
				for ( var p = 0; p < 2; p++ )
				{
					var upload = d.MaterializePrimitiveUpload( 0, p ); Require( upload.Attributes.Keys.Order().SequenceEqual( new[] { "NORMAL", "POSITION", "TEXCOORD_0", "TEXCOORD_1" } ), "Missing UV set or invented authored tangent." );
					foreach ( var (name, stream) in upload.Attributes ) Require( Hash( stream.Storage.Bytes.Span ) == reference.Descriptor( form.Variant, reference.Primitive( p ).GetProperty( "attributes" ).GetProperty( name ).GetInt32() ).GetProperty( "sha256" ).GetString(), "Base source bytes changed." );
				}
			} );
		}
		foreach ( var compiled in new[] { false, true } )
		foreach ( var ninth in new[] { false, true } )
		foreach ( var nodeOverride in new[] { false, true } )
		{
			var form = reference.Fixture( ninth, nodeOverride, compiled );
			Run( "signed_unbounded_node_precedence_or_mesh_fallback_and_all_targets", form, () =>
			{
				using var d = form.Read(); var pose = d.EvaluatePose(); var expected = new double[ninth ? 9 : 8]; expected[^1] = 1.75;
				if ( nodeOverride ) expected = ninth ? reference.SignedWeights.Append( -.375 ).ToArray() : reference.SignedWeights;
				var actual = GltfGpuMorphs.ResolveWeights( pose, 0, expected.Length ); Require( actual.Length == expected.Length, "Ninth/signed weights lost." );
				for ( var t = 0; t < expected.Length; t++ ) Close( expected[t], actual[t], 0, "Signed weight/default precedence" );
				for ( var primitive = 0; primitive < 2; primitive++ ) Require( d.MaterializeMorphTargetsUpload( 0, primitive ).Targets.Count == expected.Length, "Ninth target dropped." );
				CheckPositions( reference, d, pose, expected ); actual[^1] = 99; Close( expected[^1], pose.GetMorphWeights( 0 )[^1], 0, "Weights were borrowed mutably" );
			} );
		}
		Require( rows.Count == ExpectedCases, "Frozen CPU case inventory changed." ); return rows.ToArray();
		void Run( string name, Form form, Action body ) { try { body(); rows.Add( new( name, form.Name, true, null ) ); } catch ( Exception e ) { rows.Add( new( name, form.Name, false, e.ToString() ) ); } }
	}

	private static void CheckPositions( MorphStressTestReference r, GltfDocument d, GltfPose pose, double[] weights )
	{
		for ( var primitive = 0; primitive < 2; primitive++ )
		{
			var witnesses = new HashSet<int> { 0, r.VertexCount( primitive ) - 1 };
			foreach ( var target in r.Primitive( primitive ).GetProperty( "targets" ).EnumerateArray() )
			{
				var deltas = r.AccessorValues( target.GetProperty( "POSITION" ).GetInt32() ); var first = Array.FindIndex( deltas, x => x.Any( v => v != 0 ) ); witnesses.Add( first >= 0 ? first : 0 );
			}
			foreach ( var vertex in witnesses )
			{
				var expected = r.Morph( primitive, vertex, weights, "POSITION" ); var actual = d.EvaluateVertexPosition( 0, primitive, vertex, pose );
				Close( expected[0], actual.X, 2e-6, "Morph X" ); Close( expected[1], actual.Y, 2e-6, "Morph Y" ); Close( expected[2], actual.Z, 2e-6, "Morph Z" );
			}
		}
	}
}
