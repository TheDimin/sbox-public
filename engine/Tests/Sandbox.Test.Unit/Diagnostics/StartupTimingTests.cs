using System.Linq;

namespace Sandbox.Tests;

[TestClass]
public class StartupTimingTests
{
	[TestMethod]
	public void CountersAreDistinctFromMeasuredDurations()
	{
		var record = new Api.Events.EventRecord( "startup" );
		record.SetValue( "Shaders.Reused", 61 );
		record.SetDuration( "TimeToWindow", 120 );
		using ( record.ScopeTimer( "Load Project" ) ) { }
		record.StartTimer( "Time" );
		record.FinishTimer( "Time" );
		Assert.IsFalse( record.DurationKeys.Contains( "Shaders.Reused" ) );
		Assert.IsTrue( record.DurationKeys.Contains( "TimeToWindow" ) );
		Assert.IsTrue( record.DurationKeys.Contains( "Load Project" ) );
		Assert.IsTrue( record.DurationKeys.Contains( "Time" ) );
		Assert.AreEqual( 61, record.Data["Shaders.Reused"] );
	}

	[TestMethod]
	public void ReplacingDurationWithMetadataClearsItsUnit()
	{
		var record = new Api.Events.EventRecord( "startup" );
		record.SetDuration( "value", 100 );
		record.SetValue( "value", 42 );
		Assert.IsFalse( record.DurationKeys.Contains( "value" ) );
	}
}
