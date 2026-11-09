using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

/// <summary>
/// Controlled CPU overlap at the production submission boundary. This checks
/// shared attribute/retirement exclusion; native rendering remains a separate
/// required replay. The ungated control must demonstrate the overwrite.
/// </summary>
internal static class SimpleInstancingRenderConcurrencyTests
{
	public sealed record Result( string Name, bool Passed, string Error );
	private sealed record Observation( bool Overlap, bool FirstStatePreserved, bool BothCompleted );
	public static Result[] RunAll()
	{
		var results = new List<Result>();
		Run( "ungated_control_exposes_cross_view_overwrite_of_all125_rows", () =>
		{
			var observation = OverlappingViews( body => body() );
			Require( observation.Overlap && !observation.FirstStatePreserved && observation.BothCompleted,
				"Negative control did not exercise two overlapping callback bodies and observe the shared-row overwrite." );
		} );
		Run( "production_gate_preserves125_shared_rows_until_first_view_finishes", () =>
		{
			var gate = new GltfGpuSubmissionGate(); var otherInstance = new GltfGpuSubmissionGate();
			var observation = OverlappingViews( gate.Run, independentSubmit: otherInstance.Run );
			Require( !observation.Overlap && observation.FirstStatePreserved && observation.BothCompleted,
				"A second view mutated the first view's125 attribute rows before its submission completed." );
		} );
		Run( "retirement_waits_for_active_submission", () =>
		{
			var gate = new GltfGpuSubmissionGate();
			var observation = OverlappingViews( gate.Run, retirement: true );
			Require( !observation.Overlap && observation.FirstStatePreserved && observation.BothCompleted,
				"Retirement invalidated resources while the first native submission still held them." );
		} );
		Run( "throwing_submission_releases_gate_for_the_next_view", () =>
		{
			var gate = new GltfGpuSubmissionGate(); var expected = new InvalidOperationException( "submission failure" );
			try { gate.Run( () => throw expected ); throw new Exception( "The original callback error disappeared." ); }
			catch ( InvalidOperationException error ) { Require( ReferenceEquals( error, expected ), "Callback error identity changed." ); }
			var completed = false; Exception failure = null;
			var next = new Thread( () => { try { gate.Run( () => completed = true ); } catch ( Exception error ) { failure = error; } } );
			next.Start(); Require( next.Join( TimeSpan.FromSeconds( 3 ) ), "A throwing callback left the submission gate locked." );
			Require( completed && failure is null, "The next view could not enter after a callback failure: " + failure );
		} );
		return results.ToArray();

		void Run( string name, Action body )
		{
			try { body(); results.Add( new( name, true, null ) ); }
			catch ( Exception error ) { results.Add( new( name, false, error.ToString() ) ); }
		}
	}

	private static Observation OverlappingViews( Action<Action> submit, bool retirement = false, Action<Action> independentSubmit = null )
	{
		using var firstEntered = new ManualResetEventSlim(); using var releaseFirst = new ManualResetEventSlim();
		using var secondAttempting = new ManualResetEventSlim(); using var secondEntered = new ManualResetEventSlim();
		using var independentEntered = new ManualResetEventSlim();
		var attributes = new int[125]; var firstPreserved = false; var firstDone = false; var secondDone = false;
		Exception firstError = null, secondError = null, independentError = null;
		var first = new Thread( () =>
		{
			try
			{
				submit( () =>
				{
					Array.Fill( attributes, 1 ); firstEntered.Set();
					// Only the coordinator's finally opens this window. An inner
					// timeout must not release it before the bounded witness waits.
					releaseFirst.Wait();
					firstPreserved = attributes.All( value => value == 1 );
				} );
				firstDone = true;
			}
			catch ( Exception error ) { firstError = error; firstEntered.Set(); }
		} );
		var second = new Thread( () =>
		{
			try
			{
				secondAttempting.Set();
				submit( () => { Array.Fill( attributes, retirement ? -1 : 2 ); secondEntered.Set(); } );
				secondDone = true;
			}
			catch ( Exception error ) { secondError = error; secondEntered.Set(); }
		} );
		Thread independent = null;
		first.Start(); var secondStarted = false; var overlap = false;
		try
		{
			Require( firstEntered.Wait( TimeSpan.FromSeconds( 3 ) ) && firstError is null, "First view failed before the overlap window: " + firstError );
			if ( independentSubmit is not null )
			{
				independent = new Thread( () =>
				{
					try { independentSubmit( independentEntered.Set ); }
					catch ( Exception error ) { independentError = error; independentEntered.Set(); }
				} );
				independent.Start();
				Require( independentEntered.Wait( TimeSpan.FromSeconds( 1 ) ) && independentError is null,
					"Another instance was serialized behind the held instance; submission gates must not be global." );
			}
			second.Start(); secondStarted = true;
			Require( secondAttempting.Wait( TimeSpan.FromSeconds( 3 ) ), "Second dedicated worker did not attempt submission." );
			// After announcing its attempt the dedicated second worker performs
			// no waits except the production gate. Observe either actual body
			// entry or a blocked monitor acquisition; descheduling alone is not
			// evidence of exclusion. A timeout is a failed/incomplete rendezvous.
			Require( SpinWait.SpinUntil( () => secondEntered.IsSet ||
				(second.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds( 3 ) ),
				"Second worker neither entered its body nor reached a blocked acquisition; exclusion was not observed." );
			overlap = secondEntered.IsSet;
		}
		finally
		{
			releaseFirst.Set();
			Require( first.Join( TimeSpan.FromSeconds( 3 ) ), "First controlled worker did not finish." );
			if ( secondStarted ) Require( second.Join( TimeSpan.FromSeconds( 3 ) ), "Second controlled worker did not finish." );
			if ( independent is not null ) Require( independent.Join( TimeSpan.FromSeconds( 3 ) ), "Independent instance worker did not finish." );
		}
		Require( firstError is null && secondError is null, "Controlled worker failed: " + firstError + "\n" + secondError );
		return new( overlap, firstPreserved, firstDone && secondDone );
	}

	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
