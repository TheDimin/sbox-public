using Sandbox.Network;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.IO;

namespace Sandbox;

internal partial class GameInstanceDll
{
	private readonly Guid _lifetimeDebugId = Guid.NewGuid();
	private int _networkFileBuildCount;

	private sealed class NetworkFileProfile
	{
		public int Build;
		public string Mode;
		public int DirtyFiles;
		public int ConfiguredPatterns;
		public int UniquePatterns;
		public int WatchersBefore;
		public int WatchersAfter;
		public int SmallEntriesBefore;
		public int LargeEntriesBefore;
		public int SmallEntriesAfter;
		public int LargeEntriesAfter;
		public int TransientFileSystems;
		public int FilesEnumerated;
		public int RejectedLegal;
		public int RejectedRules;
		public int SelectedSmall;
		public int SelectedLarge;
		public int SmallReads;
		public long SmallBytes;
		public double SmallReadMs;
		public int LargeInspected;
		public int LargeExistenceChecks;
		public int LargeSizeQueries;
		public int LargeCrcCalculations;
		public long LargeCrcBytes;
		public double LargeCrcMs;
		public int AddLarge;
		public int AddSmall;
		public int TableSets;
		public int TableRemoves = 0;
		public double GameScanMs;
		public double TransientScanMs;
		public double FilteringMs;
		public double MetadataMs;
		public double TableMs;
	}

	[Flags]
	private enum NetworkFileSource
	{
		None = 0,
		Game = 1,
		Transient = 2
	}

	private sealed record NetworkFileManifestEntry(
		NetworkFileSource Source,
		bool IsLarge,
		long Size,
		ulong Crc );

	private sealed record SmallFileWatcherRegistration(
		BaseFileSystem FileSystem,
		FileWatch Watcher );

	private readonly Dictionary<SmallNetworkFiles, SmallFileWatcherRegistration> _smallFileWatchers = new();
	private readonly Dictionary<string, NetworkFileManifestEntry> _networkFileManifest = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, NetworkFileSource> _dirtyNetworkFiles = new( StringComparer.OrdinalIgnoreCase );
	private FileWatch _gameNetworkFileWatcher;
	private FileWatch _transientNetworkFileWatcher;
	private BaseFileSystem _networkGameFileSystem;
	private LocalFileSystem _transientNetworkFileSystem;
	private Project _networkProject;
	private string _transientNetworkRoot;
	private string _networkResourceConfiguration;
	private bool _networkManifestBuilt;
	private bool _networkRefreshQueued;

	readonly StringTable CodeArchiveTable = new( "CodeArchive", true );

	internal readonly ServerPackages ServerPackages = new();

	/// <summary>
	/// The config table is used to send config (like physics config, input config) to the client.
	/// This isn't always needed, because the config is loaded from the package. But if we're operating
	/// without a package, it is needed.
	/// </summary>
	readonly StringTable ConfigTable = new( "Config", true );

	/// <summary>
	/// Hold and network any small files such as StyleSheets and compiled prefab assets.
	/// </summary>
	readonly SmallNetworkFiles NetworkedSmallFiles = new( "SmallFiles" );

	/// <summary>
	/// Hold and network any files from Project Settings (.config files.)
	/// </summary>
	readonly SmallNetworkFiles NetworkedConfigFiles = new( "ConfigFiles" );

	/// <summary>
	/// Hold and network any localization files.
	/// </summary>
	readonly SmallNetworkFiles NetworkedLangFiles = new( "LangFiles" );

	/// <summary>
	/// Hold and network any small files such as StyleSheets and compiled prefab assets.
	/// </summary>
	readonly LargeNetworkFiles NetworkedLargeFiles = new( "LargeFiles" );

	/// <summary>
	/// Hold and network any small files such as StyleSheets and compiled prefab assets.
	/// </summary>
	readonly ReplicatedConvars ReplicatedConvars = new( "ReplicatedConvars" );

	private List<FileWatch> FileWatchers { get; set; } = new();
	private bool DidMountNetworkedFiles { get; set; }

	[ConCmd( "network_manifest_hash", ConVarFlags.Protected )]
	public static void LogNetworkManifestHash()
	{
		if ( Current is null )
			return;

		var hash = NetworkFileManifestOracle.ComputeHash(
			Current.NetworkedSmallFiles.StringTable,
			Current.NetworkedLargeFiles.StringTable );

		Log.Info(
			$"NetworkManifestHash hash={hash} " +
			$"smallEntries={Current.NetworkedSmallFiles.StringTable.Entries.Count} " +
			$"largeEntries={Current.NetworkedLargeFiles.StringTable.Entries.Count}" );
	}

	public GameNetworkSystem CreateGameNetworking( NetworkSystem system )
	{
		var instance = new SceneNetworkSystem( TypeLibrary, system );

		NetworkedLargeFiles.NetworkInitialize( instance );
		Platform.Chat.NetworkInitialize( instance );

		if ( Networking.IsHost )
		{
			AddFilesToNetwork( NetworkedConfigFiles, EngineFileSystem.ProjectSettings, [".config"] );
			AddFilesToNetwork( NetworkedLangFiles, Game.Language.FileSystem, [".json"] );
			BuildNetworkedFiles();
		}
		else if ( !DidMountNetworkedFiles )
		{
			EngineFileSystem.ProjectSettings.Mount( NetworkedConfigFiles.Files );
			Game.Language.FileSystem.Mount( NetworkedLangFiles.Files );
			Game.Language.Refresh();

			FileSystem.Mounted.Mount( NetworkedLargeFiles.Files );
			FileSystem.Mounted.Mount( NetworkedSmallFiles.Files );

			NetworkedSmallFiles.Refresh();
			NetworkedConfigFiles.Refresh();
			NetworkedLangFiles.Refresh();

			ResourceLoader.LoadAllGameResource( FileSystem.Mounted, reloadExisting: true );
			FontManager.Instance.LoadAll( FileSystem.Mounted );

			DidMountNetworkedFiles = true;
		}

		return instance;
	}

	public async Task<GameNetworkSystem> CreateGameNetworkingAsync( NetworkSystem system )
	{
		var instance = new SceneNetworkSystem( TypeLibrary, system );

		NetworkedLargeFiles.NetworkInitialize( instance );
		Platform.Chat.NetworkInitialize( instance );

		if ( Networking.IsHost )
		{
			AddFilesToNetwork( NetworkedConfigFiles, EngineFileSystem.ProjectSettings, [".config"] );
			AddFilesToNetwork( NetworkedLangFiles, Game.Language.FileSystem, [".json"] );
			BuildNetworkedFiles();
		}
		else if ( !DidMountNetworkedFiles )
		{
			EngineFileSystem.ProjectSettings.Mount( NetworkedConfigFiles.Files );
			Game.Language.FileSystem.Mount( NetworkedLangFiles.Files );
			Game.Language.Refresh();

			FileSystem.Mounted.Mount( NetworkedLargeFiles.Files );
			FileSystem.Mounted.Mount( NetworkedSmallFiles.Files );

			NetworkedSmallFiles.Refresh();
			NetworkedConfigFiles.Refresh();
			NetworkedLangFiles.Refresh();

			LoadingScreen.Title = "Loading Resources";
			await ResourceLoader.LoadAllGameResourceAsync( FileSystem.Mounted, reloadExisting: true );
			FontManager.Instance.LoadAll( FileSystem.Mounted );

			DidMountNetworkedFiles = true;
		}

		return instance;
	}

	void AddFilesToNetwork( SmallNetworkFiles target, BaseFileSystem fs, HashSet<string> validExtensions )
	{
		if ( _smallFileWatchers.TryGetValue( target, out var registration ) )
		{
			if ( ReferenceEquals( registration.FileSystem, fs ) )
				return;

			registration.Watcher.Dispose();
			FileWatchers.Remove( registration.Watcher );
			_smallFileWatchers.Remove( target );
		}

		var files = fs.FindFile( "/", "*", true );

		foreach ( var fileName in files )
		{
			var extension = Path.GetExtension( fileName );
			if ( !validExtensions.Contains( extension ) )
				continue;

			var text = fs.ReadAllBytes( fileName );
			target.AddFile( fs, fileName, text.ToArray() );
		}

		var watcher = fs.Watch();
		watcher.OnChanges += w =>
		{
			foreach ( var fileName in w.Changes )
			{
				var extension = Path.GetExtension( fileName );
				if ( !validExtensions.Contains( extension ) )
					continue;

				if ( fs.FileExists( fileName ) )
				{
					var text = fs.ReadAllBytes( fileName );
					target.AddFile( fs, fileName, text.ToArray() );
				}
				else
				{
					target.RemoveFile( fileName );
				}
			}
		};

		FileWatchers.Add( watcher );
		_smallFileWatchers[target] = new( fs, watcher );
	}

	/// <summary>
	/// This is used to compile code archives that come in from the network.
	/// </summary>
	CompileGroup compileGroup;

	public void InstallNetworkTables( NetworkSystem system )
	{
		system.InstallTable( CodeArchiveTable );
		system.InstallTable( ServerPackages.StringTable );
		system.InstallTable( NetworkedSmallFiles.StringTable );
		system.InstallTable( NetworkedConfigFiles.StringTable );
		system.InstallTable( NetworkedLangFiles.StringTable );
		system.InstallTable( NetworkedLargeFiles.StringTable );
		system.InstallTable( ReplicatedConvars.StringTable );
		NetworkedLargeFiles.EnableLiveDownloads( () => NetworkedLargeFiles.RunDownloadQueue( system, default ) );

		CodeArchiveTable.OnChangeOrAdd = ( entry ) =>
		{
			var codeArchive = new CodeArchive( entry.Data );
			var compiler = compileGroup.GetOrCreateCompiler( codeArchive.CompilerName );
			compiler.UpdateFromArchive( codeArchive );
		};

		CodeArchiveTable.PostNetworkUpdate = () => FinishLoadingCodeArchives();

		//
		// Config
		//
		system.InstallTable( ConfigTable );
		ConfigTable.PostNetworkUpdate = UpdateConfigFromNetworkTable;
	}

	bool FinishLoadingCodeArchives()
	{
		if ( !compileGroup.NeedsBuild )
		{
			FinishLoadingAssemblies();
			return true;
		}

		// We need to build it syncronously because we don't want other
		// network shit coming in, that was created using the new assemblies
		// and us not being able to understand because we don't have the
		// new code compiled and loaded yet!
		SyncContext.RunBlocking( compileGroup.BuildAsync() );
		if ( !compileGroup.BuildResult.Success )
			return false;

		//
		// Get the new assemblies and update them
		//
		foreach ( var assm in compileGroup.BuildResult.Output )
		{
			using var stream = new MemoryStream( assm.AssemblyData );
			AssemblyEnroller.LoadAssemblyFromStream( assm.Compiler.AssemblyName, stream );
		}

		//
		// Do the hotload and stuff
		//
		FinishLoadingAssemblies();

		return true;
	}

	public async Task<bool> LoadNetworkTables( NetworkSystem system )
	{
		compileGroup = new CompileGroup( "server" );

		// Don't hotload while we're downloading stuff!
		using var pauseAsmLoadScope = PauseLoadingAssemblies();

		// Any assemblies come our way? 
		foreach ( var entry in CodeArchiveTable.Entries )
		{
			var codeArchive = new CodeArchive( entry.Value.Data );
			var compiler = compileGroup.GetOrCreateCompiler( codeArchive.CompilerName );
			compiler.UpdateFromArchive( codeArchive );
		}

		// We might have loaded new assemblies, here's a safe time to
		// hotload before we start downloading again.
		if ( !FinishLoadingCodeArchives() )
		{
			Disconnect( "Failed to compile code archives. Check log for details." );
			return false;
		}

		// Load configs from network tables
		UpdateConfigFromNetworkTable();

		await ServerPackages.InstallAll();

		// Prevent a blank title between the last package install and the download queue start.
		if ( string.IsNullOrWhiteSpace( LoadingScreen.Title ) )
			LoadingScreen.Title = "Loading..";

		await NetworkedLargeFiles.RunDownloadQueue( system, default );

		return true;
	}

	static readonly string[] _interestingExtensions = ["_c", ".scss", ".ttf"];
	static readonly string[] _engineAssets = ["vtex_c", "vmat_c", "vsnd_c", "vmdl_c", "vpk", "vanmgrph_c", "shader_c"]; // anything the native engine loads from disk has to be a LARGE download
	List<string> _netIncludePaths = new(); // wildcard-supported paths we also want to include content of

	// Small files only live in an in-memory filesystem, which native loaders can't read - engine assets must be a real file on disk.
	internal static bool ShouldUseLargeDownload( string filename, long size )
		=> size >= 1024 * 64 || _engineAssets.Any( x => filename.EndsWith( x ) );

	internal static List<string> ParseNetworkIncludePaths( string resources )
	{
		if ( string.IsNullOrWhiteSpace( resources ) )
			return [];

		return resources.Split( "\n", StringSplitOptions.RemoveEmptyEntries )
			.Select( x => x.Trim() )
			.Where( x => !string.IsNullOrWhiteSpace( x ) && !x.StartsWith( "//" ) )
			.Select( x => x.NormalizeFilename( false, true ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToList();
	}

	private static int CountConfiguredNetworkIncludePaths( string resources )
	{
		if ( string.IsNullOrWhiteSpace( resources ) )
			return 0;

		return resources.Split( "\n", StringSplitOptions.RemoveEmptyEntries )
			.Select( x => x.Trim() )
			.Count( x => !string.IsNullOrWhiteSpace( x ) && !x.StartsWith( "//" ) );
	}

	private static string NormalizeNetworkFilePath( string path )
	{
		if ( path.EndsWith( "vmap", StringComparison.OrdinalIgnoreCase ) )
			path = Path.ChangeExtension( path, ".vpk" );

		return BaseFileSystem.NormalizeFilename( path ).TrimStart( '/' );
	}

	bool ShouldNetworkFile( string filename )
	{
		filename = NormalizeNetworkFilePath( filename );

		if ( !AssetDownloadCache.IsLegalDownload( filename ) )
			return false;

		if ( _netIncludePaths.Any( x => filename.WildcardMatch( x ) ) )
			return true;

		return _interestingExtensions.Any( x => filename.EndsWith( x ) );
	}

	private void MarkNetworkFileDirty( NetworkFileSource source, string fileName )
	{
		var path = NormalizeNetworkFilePath( fileName );
		if ( path.Contains( "/code/obj/", StringComparison.OrdinalIgnoreCase ) )
			return;

		_dirtyNetworkFiles.TryGetValue( path, out var existing );
		_dirtyNetworkFiles[path] = existing | source;

		if ( !Networking.IsActive || !Networking.IsHost || _networkRefreshQueued )
			return;

		_networkRefreshQueued = true;
		MainThread.Queue( () =>
		{
			_networkRefreshQueued = false;
			if ( !Networking.IsActive || !Networking.IsHost )
				return;

			var profile = CreateNetworkFileProfile( "dirty-hotload" );
			var sw = System.Diagnostics.Stopwatch.StartNew();
			RefreshDirtyNetworkFiles( profile );
			FinishNetworkFileProfile( profile, sw );
		} );
	}

	private void DisposeNetworkFileWatcher( ref FileWatch watcher )
	{
		if ( watcher is null )
			return;

		watcher.Dispose();
		FileWatchers.Remove( watcher );
		watcher = null;
	}

	private void EnsureNetworkFileWatchers( Project project, BaseFileSystem gameFileSystem, NetworkFileProfile profile )
	{
		var transientRoot = project is null
			? null
			: Path.Combine( project.GetRootPath(), ".sbox", "transient" );
		var transientExists = transientRoot is not null && Directory.Exists( transientRoot );

		var identityChanged =
			!ReferenceEquals( _networkProject, project ) ||
			!ReferenceEquals( _networkGameFileSystem, gameFileSystem ) ||
			!string.Equals( _transientNetworkRoot, transientRoot, StringComparison.OrdinalIgnoreCase ) ||
			transientExists != (_transientNetworkFileSystem is not null);

		if ( identityChanged )
		{
			DisposeNetworkFileWatcher( ref _gameNetworkFileWatcher );
			DisposeNetworkFileWatcher( ref _transientNetworkFileWatcher );
			_transientNetworkFileSystem?.Dispose();
			_transientNetworkFileSystem = null;
			_networkProject = project;
			_networkGameFileSystem = gameFileSystem;
			_transientNetworkRoot = transientRoot;
			_networkManifestBuilt = false;
			_dirtyNetworkFiles.Clear();
		}

		if ( _gameNetworkFileWatcher is null )
		{
			_gameNetworkFileWatcher = gameFileSystem.Watch();
			_gameNetworkFileWatcher.OnChanges += watcher =>
			{
				foreach ( var fileName in watcher.Changes )
					MarkNetworkFileDirty( NetworkFileSource.Game, fileName );
			};
			FileWatchers.Add( _gameNetworkFileWatcher );
		}

		if ( transientExists && _transientNetworkFileWatcher is null )
		{
			_transientNetworkFileSystem = new LocalFileSystem( transientRoot );
			_transientNetworkFileWatcher = _transientNetworkFileSystem.Watch();
			_transientNetworkFileWatcher.OnChanges += watcher =>
			{
				foreach ( var fileName in watcher.Changes )
					MarkNetworkFileDirty( NetworkFileSource.Transient, fileName );
			};
			FileWatchers.Add( _transientNetworkFileWatcher );
			profile.TransientFileSystems++;
		}
	}

	private NetworkFileProfile CreateNetworkFileProfile( string mode )
	{
		return new()
		{
			Build = ++_networkFileBuildCount,
			Mode = mode,
			WatchersBefore = FileWatchers.Count,
			SmallEntriesBefore = NetworkedSmallFiles.StringTable.Entries.Count,
			LargeEntriesBefore = NetworkedLargeFiles.StringTable.Entries.Count
		};
	}

	private void FinishNetworkFileProfile( NetworkFileProfile profile, System.Diagnostics.Stopwatch sw )
	{
		profile.WatchersAfter = FileWatchers.Count;
		profile.SmallEntriesAfter = NetworkedSmallFiles.StringTable.Entries.Count;
		profile.LargeEntriesAfter = NetworkedLargeFiles.StringTable.Entries.Count;
		sw.Stop();

		if ( !AssetDownloadCache.DebugNetworkFiles )
			return;

		Log.Warning(
			$"NetworkFileProfile " +
			$"instance={_lifetimeDebugId} build={profile.Build} mode={profile.Mode} dirtyFiles={profile.DirtyFiles} " +
			$"configuredPatterns={profile.ConfiguredPatterns} uniquePatterns={profile.UniquePatterns} " +
			$"includePaths={_netIncludePaths.Count} watchersBefore={profile.WatchersBefore} watchersAfter={profile.WatchersAfter} " +
			$"smallEntriesBefore={profile.SmallEntriesBefore} largeEntriesBefore={profile.LargeEntriesBefore} " +
			$"smallEntriesAfter={profile.SmallEntriesAfter} largeEntriesAfter={profile.LargeEntriesAfter} " +
			$"transientFileSystems={profile.TransientFileSystems} enumerated={profile.FilesEnumerated} " +
			$"rejectedLegal={profile.RejectedLegal} rejectedRules={profile.RejectedRules} " +
			$"selectedSmall={profile.SelectedSmall} selectedLarge={profile.SelectedLarge} " +
			$"smallReads={profile.SmallReads} smallBytes={profile.SmallBytes} smallReadMs={profile.SmallReadMs:0.###} " +
			$"largeInspected={profile.LargeInspected} existenceChecks={profile.LargeExistenceChecks} " +
			$"sizeQueries={profile.LargeSizeQueries} crcFiles={profile.LargeCrcCalculations} " +
			$"crcBytes={profile.LargeCrcBytes} crcMs={profile.LargeCrcMs:0.###} " +
			$"addLarge={profile.AddLarge} addSmall={profile.AddSmall} tableSets={profile.TableSets} tableRemoves={profile.TableRemoves} " +
			$"gameScanMs={profile.GameScanMs:0.###} transientScanMs={profile.TransientScanMs:0.###} " +
			$"filteringMs={profile.FilteringMs:0.###} metadataMs={profile.MetadataMs:0.###} " +
			$"tableMs={profile.TableMs:0.###} totalMs={sw.Elapsed.TotalMilliseconds:0.###}" );
	}

	private bool ProcessNetworkFile(
		BaseFileSystem fs,
		NetworkFileSource source,
		string fileName,
		Dictionary<string, NetworkFileManifestEntry> manifest,
		NetworkFileProfile profile )
	{
		var filtering = System.Diagnostics.Stopwatch.StartNew();
		if ( fileName.Contains( "/code/obj/", StringComparison.OrdinalIgnoreCase ) ||
			 fileName.Contains( "\\code\\obj\\", StringComparison.OrdinalIgnoreCase ) )
		{
			filtering.Stop();
			profile.FilteringMs += filtering.Elapsed.TotalMilliseconds;
			return false;
		}

		var path = NormalizeNetworkFilePath( fileName );
		if ( !AssetDownloadCache.IsLegalDownload( path ) )
		{
			filtering.Stop();
			profile.RejectedLegal++;
			profile.FilteringMs += filtering.Elapsed.TotalMilliseconds;
			return false;
		}

		if ( !_netIncludePaths.Any( x => path.WildcardMatch( x ) ) &&
			 !_interestingExtensions.Any( x => path.EndsWith( x ) ) )
		{
			filtering.Stop();
			profile.RejectedRules++;
			profile.FilteringMs += filtering.Elapsed.TotalMilliseconds;
			return false;
		}

		filtering.Stop();
		profile.FilteringMs += filtering.Elapsed.TotalMilliseconds;

		var metadata = System.Diagnostics.Stopwatch.StartNew();
		var exists = fs.FileExists( path );
		metadata.Stop();
		profile.MetadataMs += metadata.Elapsed.TotalMilliseconds;
		if ( !exists )
			return false;

		metadata.Restart();
		var size = fs.FileSize( path );
		metadata.Stop();
		profile.MetadataMs += metadata.Elapsed.TotalMilliseconds;

		if ( !ShouldUseLargeDownload( path, size ) )
		{
			profile.SelectedSmall++;
			var read = System.Diagnostics.Stopwatch.StartNew();
			var contents = fs.ReadAllBytes( path ).ToArray();
			read.Stop();
			profile.SmallReads++;
			profile.SmallBytes += contents.LongLength;
			profile.SmallReadMs += read.Elapsed.TotalMilliseconds;
			profile.AddSmall++;

			if ( NetworkedLargeFiles.RemoveFile( path ) )
				profile.TableRemoves++;

			if ( !NetworkedSmallFiles.AddFile( fs, path, contents, tableElapsed =>
			{
				profile.TableSets++;
				profile.TableMs += tableElapsed.TotalMilliseconds;
			} ) )
			{
				return false;
			}

			manifest[path] = new( source, false, size, 0 );
			return true;
		}

		profile.SelectedLarge++;
		profile.LargeInspected++;
		profile.LargeExistenceChecks += 2;
		profile.LargeSizeQueries++;
		profile.AddLarge++;

		if ( NetworkedSmallFiles.RemoveFile( path ) )
			profile.TableRemoves++;

		if ( !NetworkedLargeFiles.AddFile( fs, path,
			(crcBytes, crcElapsed) =>
			{
				profile.LargeCrcCalculations++;
				profile.LargeCrcBytes += crcBytes;
				profile.LargeCrcMs += crcElapsed.TotalMilliseconds;
				profile.LargeSizeQueries++;
			},
			tableElapsed =>
			{
				profile.TableSets++;
				profile.TableMs += tableElapsed.TotalMilliseconds;
			} ) )
		{
			return false;
		}

		NetworkedLargeFiles.TryGetFileInfo( path, out var info );
		manifest[path] = new( source, true, info.Size, info.CRC );
		return true;
	}

	private void RemoveNetworkFile( string path, NetworkFileProfile profile )
	{
		var removed = NetworkedSmallFiles.RemoveFile( path );
		removed |= NetworkedLargeFiles.RemoveFile( path );
		if ( removed )
			profile.TableRemoves++;

		_networkFileManifest.Remove( path );
	}

	private void EnumerateNetworkFiles(
		BaseFileSystem fs,
		NetworkFileSource source,
		Dictionary<string, NetworkFileManifestEntry> manifest,
		NetworkFileProfile profile,
		bool transient )
	{
		var files = fs.FindFile( "/", "*", true );
		using var enumerator = files.GetEnumerator();
		while ( true )
		{
			var scan = System.Diagnostics.Stopwatch.StartNew();
			var hasNext = enumerator.MoveNext();
			scan.Stop();
			if ( transient ) profile.TransientScanMs += scan.Elapsed.TotalMilliseconds;
			else profile.GameScanMs += scan.Elapsed.TotalMilliseconds;
			if ( !hasNext ) break;

			profile.FilesEnumerated++;
			ProcessNetworkFile( fs, source, enumerator.Current, manifest, profile );
		}
	}

	private void RebuildCompleteNetworkFileManifest( NetworkFileProfile profile )
	{
		profile.Mode = "full";
		var next = new Dictionary<string, NetworkFileManifestEntry>( StringComparer.OrdinalIgnoreCase );
		EnumerateNetworkFiles( _networkGameFileSystem, NetworkFileSource.Game, next, profile, false );

		if ( _transientNetworkFileSystem is not null )
			EnumerateNetworkFiles( _transientNetworkFileSystem, NetworkFileSource.Transient, next, profile, true );

		var stale = NetworkedSmallFiles.StringTable.Entries.Keys
			.Concat( NetworkedLargeFiles.StringTable.Entries.Keys )
			.Where( path => !next.ContainsKey( path ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.ToArray();

		foreach ( var path in stale )
			RemoveNetworkFile( path, profile );

		_networkFileManifest.Clear();
		foreach ( var (path, entry) in next )
			_networkFileManifest[path] = entry;

		_dirtyNetworkFiles.Clear();
		_networkManifestBuilt = true;
	}

	private bool TryResolveNetworkFile( string path, out BaseFileSystem fs, out NetworkFileSource source )
	{
		if ( _transientNetworkFileSystem is not null && _transientNetworkFileSystem.FileExists( path ) )
		{
			fs = _transientNetworkFileSystem;
			source = NetworkFileSource.Transient;
			return true;
		}

		if ( _networkGameFileSystem is not null && _networkGameFileSystem.FileExists( path ) )
		{
			fs = _networkGameFileSystem;
			source = NetworkFileSource.Game;
			return true;
		}

		fs = null;
		source = NetworkFileSource.None;
		return false;
	}

	private void RefreshDirtyNetworkFiles( NetworkFileProfile profile )
	{
		if ( !_networkManifestBuilt || _dirtyNetworkFiles.Count == 0 )
			return;

		profile.Mode = "dirty";
		foreach ( var path in _dirtyNetworkFiles.Keys.ToArray() )
		{
			var refreshed = false;
			try
			{
				profile.DirtyFiles++;
				if ( TryResolveNetworkFile( path, out var fs, out var source ) && ShouldNetworkFile( path ) )
				{
					refreshed = ProcessNetworkFile( fs, source, path, _networkFileManifest, profile );
				}
				else
				{
					RemoveNetworkFile( path, profile );
					refreshed = true;
				}
			}
			finally
			{
				if ( refreshed )
					_dirtyNetworkFiles.Remove( path );
			}
		}
	}

	/// <summary>
	/// Go through our mounted files and make them available to joining clients for download.
	/// </summary>
	void BuildNetworkedFiles()
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var profile = CreateNetworkFileProfile( "warm" );

		var gameInstance = IGameInstance.Current as GameInstance;
		if ( gameInstance is null )
		{
			Log.Warning( "Game Instance was null when building network files" );
			return;
		}

		// No network files needed for package based games
		if ( gameInstance.IsRemote )
			return;

		var project = Project.Current;
		var includePaths = ParseNetworkIncludePaths( project?.Config.Resources );
		var resourceConfiguration = string.Join( "\n", includePaths );
		profile.ConfiguredPatterns = CountConfiguredNetworkIncludePaths( project?.Config.Resources );
		profile.UniquePatterns = includePaths.Count;

		EnsureNetworkFileWatchers( project, gameInstance.GameFileSystem, profile );

		// Retaining the manifest is an editor-session optimization. Preserve the
		// complete rebuild behavior for standalone and dedicated-server hosts.
		if ( !Application.IsEditor )
			_networkManifestBuilt = false;

		var resourcesChanged = !string.Equals(
			_networkResourceConfiguration,
			resourceConfiguration,
			StringComparison.Ordinal );

		if ( resourcesChanged )
		{
			_netIncludePaths = includePaths;
			_networkResourceConfiguration = resourceConfiguration;
			_networkManifestBuilt = false;
		}

		if ( !_networkManifestBuilt )
			RebuildCompleteNetworkFileManifest( profile );
		else
			RefreshDirtyNetworkFiles( profile );

		FinishNetworkFileProfile( profile, sw );
	}

	[ConCmd( "network_manifest_invalidate", ConVarFlags.Protected )]
	public static void InvalidateNetworkManifest()
	{
		if ( Current is null )
			return;

		Current._networkManifestBuilt = false;
		Current._dirtyNetworkFiles.Clear();
	}

	private void ResetNetworkFileManifest()
	{
		_networkManifestBuilt = false;
		_networkRefreshQueued = false;
		_networkProject = null;
		_networkGameFileSystem = null;
		_networkResourceConfiguration = null;
		_transientNetworkRoot = null;
		_gameNetworkFileWatcher = null;
		_transientNetworkFileWatcher = null;
		_transientNetworkFileSystem?.Dispose();
		_transientNetworkFileSystem = null;
		_smallFileWatchers.Clear();
		_networkFileManifest.Clear();
		_dirtyNetworkFiles.Clear();
		_netIncludePaths.Clear();
	}
}
