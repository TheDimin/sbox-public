using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.BoxTexturedValidation;

/// <summary>Pixel-checker sensitivity tests, authored before public capture execution.</summary>
public static class BoxTexturedImageControlTests
{
	public static BoxTexturedCoreTests.Result[] RunAll()
	{
		var rows = new List<BoxTexturedCoreTests.Result>();
		var empty = Enumerable.Repeat( Color.Black, 256 ).ToArray();
		var positive = Enumerable.Range( 0, 256 ).Select( i => i < 128 ? Color.Blue : Color.Green ).ToArray();
		var flipped = positive.Reverse().ToArray();
		var equal = BoxTexturedImageChecks.Compare( positive, positive, 16, 16 );
		var negative = BoxTexturedImageChecks.Compare( positive, flipped, 16, 16 );
		Run( "exact_positive_and_asymmetric_negative_pass", () => BoxTexturedImageChecks.RequireBindingComparison( equal, negative, 1, 0, 0 ) );
		Run( "wrong_v_actual_rejected", () => Reject( () => BoxTexturedImageChecks.RequireBindingComparison( negative, negative, 1, 0, 0 ) ) );
		Run( "missing_negative_rejected", () => Reject( () => BoxTexturedImageChecks.RequireBindingComparison( equal, equal, 1, 0, 0 ) ) );
		Run( "empty_reference_actual_rejected", () => Reject( () => BoxTexturedImageChecks.RequireBindingComparison( BoxTexturedImageChecks.Compare( empty, empty, 16, 16 ), negative, 1, 0, 0 ) ) );
		Run( "empty_positive_rejected", () => Reject( () => BoxTexturedImageChecks.RequirePositive( empty, 16, 16, "control" ) ) );
		Run( "magenta_empty_rejected", () => Reject( () => BoxTexturedImageChecks.RequireEmpty( Enumerable.Repeat( Color.Magenta, 256 ).ToArray(), 16, 16, "sentinel" ) ) );
		Run( "wrong_dimensions_rejected", () => Reject( () => BoxTexturedImageChecks.Compare( positive, positive, 15, 16 ) ) );
		Run( "nonfinite_rgb_rejected", () => Reject( () => BoxTexturedImageChecks.Compare( positive, Enumerable.Repeat( new Color( float.NaN, 0, 0 ), 256 ).ToArray(), 16, 16 ) ) );
		return rows.ToArray();

		void Run( string name, Action body )
		{
			try { body(); rows.Add( new( name, "pixel_checker_control", 1, true, null ) ); }
			catch ( Exception error ) { rows.Add( new( name, "pixel_checker_control", 0, false, error.ToString() ) ); }
		}
	}
	private static void Reject( Action body )
	{
		try { body(); } catch ( InvalidOperationException ) { return; }
		throw new InvalidOperationException( "Corrupted image control was accepted." );
	}
}
