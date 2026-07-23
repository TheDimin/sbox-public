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

	public GameInstanceDll()
	{
		Log.Warning( $"GameInstanceDll created: {_lifetimeDebugId}" );
	}

	~GameInstanceDll()
	{
		Log.Warning( $"GameInstanceDll finalized: {_lifetimeDebugId}" );
	}

	private sealed class NetworkFileProfile
	{
		public int Build;
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

	static string[] _interestingExtensions = new[] { "_c", ".scss", ".ttf" };
	static string[] _engineAssets = new[] { "vtex_c", "vmat_c", "vsnd_c", "vmdl_c", "vpk", "vanmgrph_c", "shader_c" }; // anything the native engine loads from disk has to be a LARGE download
	List<string> _netIncludePaths = new(); // wildcard-supported paths we also want to include content of

	// Small files only live in an in-memory filesystem, which native loaders can't read - engine assets must be a real file on disk.
	internal static bool ShouldUseLargeDownload( string filename, long size )
		=> size >= 1024 * 64 || _engineAssets.Any( x => filename.EndsWith( x ) );

	bool ShouldNetworkFile( string filename )
	{
		filename = filename.NormalizeFilename();

		if ( !AssetDownloadCache.IsLegalDownload( filename ) )
			return false;

		if ( _netIncludePaths.Any( x => filename.WildcardMatch( x ) ) )
			return true;

		return _interestingExtensions.Any( x => filename.EndsWith( x ) );
	}

	void UpdateNetworkFile( BaseFileSystem fs, string filename, NetworkFileProfile profile = null )
	{
		var filtering = System.Diagnostics.Stopwatch.StartNew();

		// ignore code junk
		if ( filename.Contains( "\\code\\obj\\", System.StringComparison.OrdinalIgnoreCase ) )
		{
			filtering.Stop();
			if ( profile is not null ) profile.FilteringMs += filtering.Elapsed.TotalMilliseconds;
			return;
		}

		if ( filename.EndsWith( "vmap" ) ) filename = Path.ChangeExtension( filename, ".vpk" );
		else
		{
			var normalized = filename.NormalizeFilename();
			if ( !AssetDownloadCache.IsLegalDownload( normalized ) )
			{
				filtering.Stop();
				if ( profile is not null )
				{
					profile.RejectedLegal++;
					profile.FilteringMs += filtering.Elapsed.TotalMilliseconds;
				}
				return;
			}

			var included = _netIncludePaths.Any( x => normalized.WildcardMatch( x ) );
			var interesting = _interestingExtensions.Any( x => normalized.EndsWith( x ) );
			if ( !included && !interesting )
			{
				filtering.Stop();
				if ( profile is not null )
				{
					profile.RejectedRules++;
					profile.FilteringMs += filtering.Elapsed.TotalMilliseconds;
				}
				return;
			}
		}

		filtering.Stop();
		if ( profile is not null ) profile.FilteringMs += filtering.Elapsed.TotalMilliseconds;

		var metadata = System.Diagnostics.Stopwatch.StartNew();
		var exists = fs.FileExists( filename );
		metadata.Stop();
		if ( profile is not null ) profile.MetadataMs += metadata.Elapsed.TotalMilliseconds;

		if ( !exists )
			return;

		var fullPath = fs.GetFullPath( filename );
		metadata.Restart();
		var size = fs.FileSize( filename );
		metadata.Stop();
		if ( profile is not null ) profile.MetadataMs += metadata.Elapsed.TotalMilliseconds;

		if ( !ShouldUseLargeDownload( filename, size ) )
		{
			if ( profile is not null ) profile.SelectedSmall++;

			var read = System.Diagnostics.Stopwatch.StartNew();
			var bytes = fs.ReadAllBytes( filename );
			var contents = bytes.ToArray();
			read.Stop();
			if ( profile is not null )
			{
				profile.SmallReads++;
				profile.SmallBytes += contents.LongLength;
				profile.SmallReadMs += read.Elapsed.TotalMilliseconds;
				profile.AddSmall++;
			}

			var wasAdded = NetworkedSmallFiles.AddFile( fs, filename, contents, tableElapsed =>
			{
				if ( profile is null ) return;
				profile.TableSets++;
				profile.TableMs += tableElapsed.TotalMilliseconds;
			} );

			if ( wasAdded )
			{
				if ( AssetDownloadCache.DebugNetworkFiles )
					Log.Info( $"Adding Small File {filename} ({size.FormatBytes()})" );
			}
			else
			{
				Log.Warning( $"File '{filename}' ('{fullPath}') doesn't exist - skipping" );
			}
		}
		else
		{
			if ( profile is not null )
			{
				profile.SelectedLarge++;
				profile.LargeInspected++;
				profile.LargeExistenceChecks += 2;
				profile.LargeSizeQueries++;
				profile.AddLarge++;
			}

			var wasAdded = NetworkedLargeFiles.AddFile( filename,
				(crcBytes, crcElapsed) =>
				{
					if ( profile is null ) return;
					profile.LargeCrcCalculations++;
					profile.LargeCrcBytes += crcBytes;
					profile.LargeCrcMs += crcElapsed.TotalMilliseconds;
					profile.LargeSizeQueries++;
				},
				tableElapsed =>
				{
					if ( profile is null ) return;
					profile.TableSets++;
					profile.TableMs += tableElapsed.TotalMilliseconds;
				} );

			if ( wasAdded )
			{
				if ( AssetDownloadCache.DebugNetworkFiles )
					Log.Info( $"Adding LARGE File {filename} ({size.FormatBytes()})" );
			}
			else
			{
				Log.Warning( $"File '{filename}' ('{fullPath}') doesn't exist - skipping" );
			}
		}

	}

	/// <summary>
	/// Go through our mounted files and make them available to joining clients for download
	/// </summary>
	void BuildNetworkedFiles()
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var profile = new NetworkFileProfile
		{
			Build = ++_networkFileBuildCount,
			WatchersBefore = FileWatchers.Count,
			SmallEntriesBefore = NetworkedSmallFiles.StringTable.Entries.Count,
			LargeEntriesBefore = NetworkedLargeFiles.StringTable.Entries.Count
		};

		var gameInstance = IGameInstance.Current as GameInstance;
		if ( gameInstance is null )
		{
			Log.Warning( "Game Instance was null when building network files" );
			return;
		}

		// No network files needed for package based games
		if ( gameInstance.IsRemote ) return;

		if ( AssetDownloadCache.DebugNetworkFiles )
			Log.Info( "Building network files.." );

		// include anything on resource paths
		var project = Project.Current;
		if ( project is not null && !string.IsNullOrWhiteSpace( project.Config.Resources ) )
		{
			var resourcePaths = project.Config.Resources.Split( "\n", StringSplitOptions.RemoveEmptyEntries )
			.Select( x => x.Trim() )
			.Where( x => !x.StartsWith( "//" ) )
			.Select( x => x.NormalizeFilename( true, false ) )
			.ToArray();

			profile.ConfiguredPatterns = resourcePaths.Length;
			profile.UniquePatterns = resourcePaths.Distinct( StringComparer.OrdinalIgnoreCase ).Count();
			_netIncludePaths.AddRange( resourcePaths );
		}

		var fs = gameInstance.GameFileSystem;
		var files = fs.FindFile( "/", "*", true );

		using ( var enumerator = files.GetEnumerator() )
		{
			while ( true )
			{
				var scan = System.Diagnostics.Stopwatch.StartNew();
				var hasNext = enumerator.MoveNext();
				scan.Stop();
				profile.GameScanMs += scan.Elapsed.TotalMilliseconds;
				if ( !hasNext ) break;

				profile.FilesEnumerated++;
				UpdateNetworkFile( fs, enumerator.Current, profile );
			}
		}

		var watcher = fs.Watch();
		watcher.OnChanges += w =>
		{
			foreach ( var fileName in w.Changes )
			{
				UpdateNetworkFile( fs, fileName );
			}
		};

		FileWatchers.Add( watcher );

		NetworkTransientGeneratedFiles( project, profile );
		profile.WatchersAfter = FileWatchers.Count;
		profile.SmallEntriesAfter = NetworkedSmallFiles.StringTable.Entries.Count;
		profile.LargeEntriesAfter = NetworkedLargeFiles.StringTable.Entries.Count;
		sw.Stop();

		Log.Warning(
			$"NetworkFileProfile " +
			$"instance={_lifetimeDebugId} build={profile.Build} mode=full " +
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

		if ( AssetDownloadCache.DebugNetworkFiles )
			Log.Info( $"..done in {sw.Elapsed.TotalSeconds:0.00}s" );
	}

	/// <summary>
	/// Make runtime-generated assets in the project's .sbox/transient/ folder available to joining clients
	/// This is necessary for connected clients to see things like TextureGenerators.
	/// </summary>
	void NetworkTransientGeneratedFiles( Project project, NetworkFileProfile profile = null )
	{
		if ( project is null )
			return;

		var transientFolder = Path.Combine( project.GetRootPath(), ".sbox", "transient" );
		if ( !Directory.Exists( transientFolder ) )
			return;

		var transientFs = new LocalFileSystem( transientFolder );
		if ( profile is not null ) profile.TransientFileSystems++;

		var files = transientFs.FindFile( "/", "*", true );
		using ( var enumerator = files.GetEnumerator() )
		{
			while ( true )
			{
				var scan = System.Diagnostics.Stopwatch.StartNew();
				var hasNext = enumerator.MoveNext();
				scan.Stop();
				if ( profile is not null ) profile.TransientScanMs += scan.Elapsed.TotalMilliseconds;
				if ( !hasNext ) break;

				if ( profile is not null ) profile.FilesEnumerated++;
				UpdateNetworkFile( transientFs, enumerator.Current, profile );
			}
		}

		var watcher = transientFs.Watch();
		watcher.OnChanges += w =>
		{
			foreach ( var fileName in w.Changes )
			{
				UpdateNetworkFile( transientFs, fileName );
			}
		};

		FileWatchers.Add( watcher );
	}
}
