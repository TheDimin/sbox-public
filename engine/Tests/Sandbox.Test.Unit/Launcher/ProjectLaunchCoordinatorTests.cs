using Sandbox.Cli;
using System;
using System.IO;
using System.Threading;

namespace Sandbox.Tests;

[TestClass]
public class ProjectLaunchCoordinatorTests
{
	sealed class Fixture : IDisposable
	{
		internal readonly string DirectoryPath = Path.Combine( Path.GetTempPath(), "sbox-cli-launch-tests", Guid.NewGuid().ToString( "N" ) );
		internal string Project => Path.Combine( DirectoryPath, "game.sbproj" );

		internal Task<IDisposable> Join( Func<CancellationToken, Task<bool>> discover, CancellationToken token = default, TimeSpan? timeout = null, string project = null )
			=> ProjectLaunchCoordinator.WaitForLaunchAsync( project ?? Project, discover, timeout ?? TimeSpan.FromSeconds( 5 ), token, DirectoryPath );

		public void Dispose()
		{
			if ( Directory.Exists( DirectoryPath ) ) Directory.Delete( DirectoryPath, true );
		}
	}

	[TestMethod]
	public async Task WaitingClientAttachesWithoutTakingTheOwnersLease()
	{
		using var fixture = new Fixture();
		var ready = false;
		Task<bool> Discover( CancellationToken token ) => Task.FromResult( Volatile.Read( ref ready ) );
		using var owner = await fixture.Join( Discover );
		Assert.IsNotNull( owner );

		var waiting = fixture.Join( Discover );
		Assert.IsFalse( waiting.IsCompleted );
		Volatile.Write( ref ready, true );
		Assert.IsNull( await waiting );

		// A client that only attaches must not release the owner's launch reservation.
		Volatile.Write( ref ready, false );
		using var cancellation = new CancellationTokenSource();
		var another = fixture.Join( Discover, cancellation.Token );
		Assert.IsFalse( another.IsCompleted );
		await cancellation.CancelAsync();
		await AssertCancelled( another );
	}

	[TestMethod]
	public async Task OwnerExitLetsAWaitingClientStartAReplacement()
	{
		using var fixture = new Fixture();
		Task<bool> Missing( CancellationToken token ) => Task.FromResult( false );
		var owner = await fixture.Join( Missing );
		try
		{
			var waiting = fixture.Join( Missing );
			Assert.IsFalse( waiting.IsCompleted );
			owner.Dispose();
			using var replacement = await waiting;
			Assert.IsNotNull( replacement );
		}
		finally
		{
			owner.Dispose();
		}
	}

	[TestMethod]
	public async Task RecheckInsideTheGateFindsAnEditorThatJustBecameReady()
	{
		using var fixture = new Fixture();
		var calls = 0;
		using var lease = await fixture.Join( token => Task.FromResult( ++calls == 2 ) );
		Assert.IsNull( lease );
		Assert.AreEqual( 2, calls );
		using var next = await fixture.Join( token => Task.FromResult( false ) );
		Assert.IsNotNull( next );
	}

	[TestMethod]
	public async Task FailedDiscoveryReleasesTheGate()
	{
		using var fixture = new Fixture();
		var calls = 0;
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( async () => await fixture.Join( token =>
			++calls == 1 ? Task.FromResult( false ) : throw new InvalidOperationException( "Discovery failed." ) ) );
		using var next = await fixture.Join( token => Task.FromResult( false ) );
		Assert.IsNotNull( next );
	}

	[TestMethod]
	public async Task WaitingTimeoutDoesNotReleaseTheOwnersGate()
	{
		using var fixture = new Fixture();
		using var owner = await fixture.Join( token => Task.FromResult( false ) );
		await Assert.ThrowsExceptionAsync<TimeoutException>( async () =>
			await fixture.Join( token => Task.FromResult( false ), timeout: TimeSpan.FromMilliseconds( 20 ) ) );
		using var cancellation = new CancellationTokenSource();
		var next = fixture.Join( token => Task.FromResult( false ), cancellation.Token );
		Assert.IsFalse( next.IsCompleted );
		await cancellation.CancelAsync();
		await AssertCancelled( next );
	}

	[TestMethod]
	public async Task DifferentProjectsDoNotBlockEachOther()
	{
		using var fixture = new Fixture();
		using var first = await fixture.Join( token => Task.FromResult( false ) );
		using var second = await fixture.Join( token => Task.FromResult( false ), project: Path.Combine( fixture.DirectoryPath, "other.sbproj" ) );
		Assert.IsNotNull( first );
		Assert.IsNotNull( second );
	}

	[TestMethod]
	public void ProjectKeysUseFullPathsAndPlatformCaseRules()
	{
		using var fixture = new Fixture();
		var key = ProjectLaunchCoordinator.ProjectKey( fixture.Project );
		Assert.AreEqual( key, ProjectLaunchCoordinator.ProjectKey( Path.Combine( fixture.DirectoryPath, "nested", "..", "game.sbproj" ) ) );
		var differentCase = ProjectLaunchCoordinator.ProjectKey( fixture.Project.ToUpperInvariant() );
		if ( OperatingSystem.IsWindows() ) Assert.AreEqual( key, differentCase );
		else Assert.AreNotEqual( key, differentCase );
	}

	static async Task AssertCancelled( Task task )
	{
		try
		{
			await task;
			Assert.Fail( "Expected cancellation." );
		}
		catch ( OperationCanceledException ) { }
	}
}
