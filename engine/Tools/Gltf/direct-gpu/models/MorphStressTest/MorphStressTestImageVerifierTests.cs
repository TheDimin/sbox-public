using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using static MorphStressTestReference;

/// <summary>Four synthetic image checker controls, independent of native captures.</summary>
public static class MorphStressTestImageVerifierTests
{
	public const int ExpectedCases = 4;
	public static MorphStressTestCoreTests.Result[] RunAll()
	{
		var rows = new List<MorphStressTestCoreTests.Result>(); var empty = new Color[256]; var positive = Enumerable.Repeat( new Color( .2f, .4f, .8f ), 256 ).ToArray();
		Run( "empty_before_and_deleted_empty_are_detectable", () => { Require( MorphStressTestImageChecks.IsEmpty( empty ), "Empty not recognized." ); Require( !MorphStressTestImageChecks.IsEmpty( positive ), "Positive marked empty." ); } );
		Run( "same_nonempty_reference_and_detectable_negative_pass", () => Check( positive, positive, empty ) );
		Run( "two_empty_images_cannot_claim_parity", () => Reject( () => Check( empty, empty, empty ) ) );
		Run( "insensitive_negative_and_wrong_rgb_are_rejected", () => { Reject( () => Check( positive, positive, positive ) ); Reject( () => Check( Enumerable.Repeat( Color.Red, 256 ).ToArray(), positive, empty ) ); } );
		Require( rows.Count == ExpectedCases, "Frozen image checker inventory changed." ); return rows.ToArray();
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, "synthetic_image_checker_only", true, null ) ); } catch ( Exception e ) { rows.Add( new( name, "synthetic_image_checker_only", false, e.ToString() ) ); } }
		void Check( Color[] actual, Color[] reference, Color[] negative ) => MorphStressTestImageChecks.RequireWithin( MorphStressTestImageChecks.Compare( actual, reference, negative ), .99, .003, .015, 64 );
	}
	private static void Reject( Action body ) { try { body(); } catch ( InvalidOperationException ) { return; } throw new InvalidOperationException( "Invalid image control was accepted." ); }
}
