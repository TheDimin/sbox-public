using Editor;
using System;
using System.Collections.Generic;

namespace Sandbox.Tests;

[TestClass]
public class StartupImportProgressTests
{
	[TestMethod]
	public void HeartbeatNamesActiveItemAndStopsAfterCompletionAndDisposal()
	{
		var elapsed = TimeSpan.Zero;
		var lines = new List<string>();
		using var progress = new StartupImportProgress( "Assets", 3, lines.Add, () => elapsed, heartbeat: false );
		progress.Begin( "models/slow.vmdl" );
		elapsed = TimeSpan.FromSeconds( 15 );
		progress.ReportWaiting();
		StringAssert.Contains( lines[^1], "15s on models/slow.vmdl" );
		StringAssert.Contains( progress.Message, "2 queued" );
		progress.Complete( "compiled" );
		var count = lines.Count;
		progress.ReportWaiting();
		Assert.AreEqual( count, lines.Count );
		progress.Begin( "textures/next.vtex" );
		progress.Dispose();
		count = lines.Count;
		progress.ReportWaiting();
		Assert.AreEqual( count, lines.Count );
		Assert.IsTrue( lines.Exists( x => x.Contains( "interrupted while importing textures/next.vtex" ) ) );
	}

	[TestMethod]
	public void FastItemsStayQuietAndSlowSummaryIsBounded()
	{
		var elapsed = TimeSpan.Zero;
		var lines = new List<string>();
		using var progress = new StartupImportProgress( "Shaders", 8, lines.Add, () => elapsed, heartbeat: false );
		progress.Begin( "cached.shader" );
		progress.Complete( "reused" );
		Assert.AreEqual( 1, lines.Count );
		for ( var i = 0; i < 7; i++ )
		{
			progress.Begin( $"slow{i}.shader" );
			elapsed += TimeSpan.FromSeconds( i + 2 );
			progress.Complete( "compiled" );
		}
		progress.Dispose();
		Assert.AreEqual( 5, lines.FindAll( x => x.Contains( "slow import" ) ).Count );
		Assert.IsTrue( lines.Exists( x => x.Contains( "1 reused" ) ) );
		Assert.IsTrue( lines.Exists( x => x.Contains( "7 compiled" ) ) );
		StringAssert.Contains( progress.Message, "8/8 completed" );
	}

	[TestMethod]
	public void FailedReturnIsReportedEvenWhenFast()
	{
		var lines = new List<string>();
		using var progress = new StartupImportProgress( "Assets", 1, lines.Add, () => TimeSpan.Zero, heartbeat: false );
		progress.Begin( "broken.vmdl" );
		progress.Complete( "compile returned false" );
		Assert.IsTrue( lines.Exists( x => x.Contains( "compile returned false broken.vmdl" ) ) );
	}
	[TestMethod]
	public void RepeatedFastFailuresAreSummarizedWithoutFloodingTheLog()
	{
		var lines = new List<string>();
		using var progress = new StartupImportProgress( "Assets", 100, lines.Add, () => TimeSpan.Zero, heartbeat: false );
		for ( var i = 0; i < 100; i++ )
		{
			progress.Begin( $"asset{i}" );
			progress.Complete( "compile returned false" );
		}
		progress.Dispose();
		Assert.AreEqual( 10, lines.FindAll( x => x.Contains( "compile returned false asset" ) ).Count );
		Assert.IsTrue( lines.Exists( x => x.Contains( "100 compile returned false" ) ) );
	}

	[TestMethod]
	public void DiagnosticSinkExceptionsCannotAbortImportsOrHeartbeat()
	{
		using var progress = new StartupImportProgress( "Assets", 1, _ => throw new InvalidOperationException( "sink failed" ), () => TimeSpan.Zero, heartbeat: false );
		progress.Begin( "asset", reportStart: true );
		progress.ReportWaiting();
		progress.Complete( "compile returned false" );
		progress.Dispose();
		StringAssert.Contains( progress.Message, "1/1 completed" );
	}

}
