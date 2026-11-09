using Sandbox.Diagnostics;

namespace EngineTests;

[TestClass]
[DoNotParallelize]
public class DevToolsFrameHistoryTests
{
	[TestMethod]
	public void BackgroundCapDoesNotInflateForegroundPercentiles()
	{
		var frames = new DevTools.FrameHistory( excludeBackgroundThrottled: true );
		for ( int i = 0; i < 20; i++ ) frames.Push( 16, 10, 2, 0 );
		for ( int i = 0; i < 10; i++ ) frames.Push( 100, 95, 2, 0, backgroundThrottled: true );

		var stats = DevTools.Measure( frames.Count, frames.FrameMs );
		Assert.AreEqual( 16f, stats.P99 );
		Assert.AreEqual( 16f, stats.Avg );
		Assert.AreEqual( 20, frames.Count );
	}

	[TestMethod]
	public void GenuineForegroundHitchesAndForegroundCapWaitStillCount()
	{
		var frames = new DevTools.FrameHistory( excludeBackgroundThrottled: true );
		for ( int i = 0; i < 20; i++ ) frames.Push( 16, 10, 2, 0 );
		frames.Push( 80, 0, 2, 0 );
		frames.Push( 100, 95, 2, 0, backgroundThrottled: true );

		var stats = DevTools.Measure( frames.Count, frames.FrameMs );
		Assert.AreEqual( 80f, stats.P99 );
		Assert.AreEqual( 80f, stats.Max );
		Assert.AreEqual( 10f, frames.IdleMs( 1 ) );
	}

	[TestMethod]
	public void RawHistoryAndItsCloneRetainBackgroundFrames()
	{
		var frames = new DevTools.FrameHistory();
		frames.Push( 100, 95, 2, 0, backgroundThrottled: true );

		var copy = frames.Clone();
		Assert.AreEqual( 1, copy.Count );
		Assert.AreEqual( 100f, copy.FrameMs( 0 ) );
		Assert.IsTrue( copy.IsBackgroundThrottled( 0 ) );
	}

	[TestMethod]
	public void FilteredCloneKeepsItsSamplingPolicy()
	{
		var frames = new DevTools.FrameHistory( excludeBackgroundThrottled: true );
		frames.Push( 16, 10, 2, 0 );
		var copy = frames.Clone();
		copy.Push( 100, 95, 2, 0, backgroundThrottled: true );

		Assert.AreEqual( 1, copy.Count );
		Assert.AreEqual( 16f, copy.FrameMs( 0 ) );
		Assert.AreEqual( 1, frames.Count );
	}
}
