using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using System;
using System.Threading;

namespace NetworkTests;

[TestClass, DoNotParallelize]
public class LobbyStartupTests
{
	private NLog.Config.LoggingConfiguration _previousLogging;
	private NLog.Targets.MemoryTarget _logs;

	[TestInitialize]
	public void CaptureLogs()
	{
		Logging.GetLogger( "LobbyStartupTests" );
		_previousLogging = NLog.LogManager.Configuration;
		_logs = new NLog.Targets.MemoryTarget { Layout = "${level}|${message}|${exception:format=ToString}" };
		var config = new NLog.Config.LoggingConfiguration();
		config.AddRuleForAllLevels( _logs );
		NLog.LogManager.Configuration = config;
	}

	[TestCleanup]
	public void RestoreLogs()
	{
		NLog.LogManager.Configuration = _previousLogging;
		_logs.Dispose();
	}

	[TestMethod]
	public void MissingSettingsSourceHasNoNetworkFiles()
	{
		var instance = new GameInstanceDll();
		var files = new SmallNetworkFiles( "missing-settings" );
		instance.AddFilesToNetwork( files, null, [".config"] );
		Assert.AreEqual( 0, files.StringTable.Entries.Count );
	}

	[TestMethod]
	public void SettingsSourceNetworksOnlyConfigFilesAndCanBeRemoved()
	{
		var source = new MemoryFileSystem();
		var instance = new GameInstanceDll();
		var files = new SmallNetworkFiles( "settings" );
		files.Reset();
		try
		{
			source.WriteAllBytes( "Input.config", [1, 2, 3] );
			source.WriteAllBytes( "ignore.txt", [4] );
			instance.AddFilesToNetwork( files, source, [".config"] );
			Assert.AreEqual( 1, files.StringTable.Entries.Count );
			CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, files.StringTable.Entries["input.config"].Data );
			instance.AddFilesToNetwork( files, null, [".config"] );
			Assert.AreEqual( 0, files.StringTable.Entries.Count );
		}
		finally
		{
			files.Files.Dispose();
			source.Dispose();
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task GameNetworkingSupportsASceneWithoutAPackage( bool asynchronous )
	{
		var previousSettings = EngineFileSystem.ProjectSettings;
		var previousLanguage = Game.Language;
		var previousInstance = IGameInstance.Current;
		var previousSystem = Networking.System;
		var previousSceneSystem = SceneNetworkSystem.Instance;
		EngineFileSystem.ProjectSettings = null;
		Game.Language = null;
		IGameInstance.Current = null;
		Networking.System = null;
		GameNetworkSystem result = null;
		try
		{
			var instance = new GameInstanceDll();
			var system = new NetworkSystem( "scene-only-lobby-test", new TypeLibrary() );
			result = asynchronous
				? await instance.CreateGameNetworkingAsync( system )
				: instance.CreateGameNetworking( system );
			Assert.IsInstanceOfType<SceneNetworkSystem>( result );
		}
		finally
		{
			result?.Dispose();
			SceneNetworkSystem.Instance = previousSceneSystem;
			Networking.System = previousSystem;
			IGameInstance.Current = previousInstance;
			Game.Language = previousLanguage;
			EngineFileSystem.ProjectSettings = previousSettings;
		}
	}
	[TestMethod]
	public void PublicCreationReportsFailureBeforeANetworkSystemExists()
	{
		var previousInstance = IGameInstanceDll.Current;
		var previousSystem = Networking.System;
		IGameInstanceDll.Current = null;
		Networking.System = null;
		try
		{
			Networking.CreateLobby( default( LobbyConfig ) );
			Assert.IsFalse( Networking.IsActive );
			Assert.IsTrue( _logs.Logs.Any( x => x.Contains( "Failed to create lobby." ) && x.Contains( nameof( NullReferenceException ) ) ) );
		}
		finally
		{
			Networking.Disconnect( false );
			IGameInstanceDll.Current = previousInstance;
			Networking.System = previousSystem;
		}
	}
	[TestMethod]
	public async Task StartupExceptionIsObservedAndFailedSessionIsRemoved()
	{
		var previous = Networking.System;
		var system = new NetworkSystem( "failed-lobby-test", new TypeLibrary() );
		Networking.System = system;
		try
		{
			await Networking.ObserveLobbyCreationAsync( Task.FromException<bool>( new InvalidOperationException( "Lobby startup test failure" ) ), system, CancellationToken.None );
			Assert.IsFalse( Networking.IsActive );
			Assert.IsTrue( system.IsDisconnected );
			Assert.IsTrue( _logs.Logs.Any( x => x.Contains( "Failed to create lobby." ) && x.Contains( "Lobby startup test failure" ) ) );
		}
		finally
		{
			Networking.System = previous;
		}
	}

	[TestMethod]
	public async Task CancelledStartupDoesNotLeaveAnActiveSession()
	{
		var previous = Networking.System;
		var system = new NetworkSystem( "cancelled-lobby-test", new TypeLibrary() );
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		Networking.System = system;
		try
		{
			await Networking.ObserveLobbyCreationAsync( Task.FromCanceled<bool>( cancellation.Token ), system, cancellation.Token );
			Assert.IsFalse( Networking.IsActive );
			Assert.IsTrue( system.IsDisconnected );
			Assert.IsFalse( _logs.Logs.Any( x => x.StartsWith( "Error|" ) ) );
		}
		finally
		{
			Networking.System = previous;
		}
	}

	[TestMethod]
	public async Task LateFailureDoesNotDisconnectANewerSession()
	{
		var previous = Networking.System;
		var oldSystem = new NetworkSystem( "old-lobby-test", new TypeLibrary() );
		var newSystem = new NetworkSystem( "new-lobby-test", new TypeLibrary() );
		var completion = new TaskCompletionSource<bool>();
		Networking.System = oldSystem;
		try
		{
			var observer = Networking.ObserveLobbyCreationAsync( completion.Task, oldSystem, CancellationToken.None );
			Networking.System = newSystem;
			completion.SetException( new InvalidOperationException( "Late lobby startup test failure" ) );
			await observer;
			Assert.AreSame( newSystem, Networking.System );
			Assert.IsFalse( newSystem.IsDisconnected );
		}
		finally
		{
			Networking.System = previous;
		}
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, true )]
	public async Task UnsuccessfulOrCancelledResultReleasesTheSession( bool created, bool cancelled )
	{
		var previous = Networking.System;
		var system = new NetworkSystem( "incomplete-lobby-test", new TypeLibrary() );
		using var cancellation = new CancellationTokenSource();
		if ( cancelled ) cancellation.Cancel();
		Networking.System = system;
		try
		{
			await Networking.ObserveLobbyCreationAsync( Task.FromResult( created ), system, cancellation.Token );
			Assert.IsFalse( Networking.IsActive );
			Assert.IsTrue( system.IsDisconnected );
		}
		finally
		{
			Networking.System = previous;
		}
	}

	[TestMethod]
	public async Task SuccessfulStartupKeepsItsSession()
	{
		var previous = Networking.System;
		var system = new NetworkSystem( "successful-lobby-test", new TypeLibrary() );
		Networking.System = system;
		try
		{
			await Networking.ObserveLobbyCreationAsync( Task.FromResult( true ), system, CancellationToken.None );
			Assert.AreSame( system, Networking.System );
			Assert.IsFalse( system.IsDisconnected );
		}
		finally
		{
			Networking.System = previous;
		}
	}
}
