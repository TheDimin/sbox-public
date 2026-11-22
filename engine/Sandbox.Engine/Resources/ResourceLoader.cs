using Sandbox.Engine;
using System.Diagnostics;
using System.Threading;

namespace Sandbox;

internal static class ResourceLoader
{
	// Native resource extensions (with _c suffix) not covered by AssetTypeAttribute.
	private static readonly HashSet<string> NativeExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".vmat_c", ".vmdl_c", ".vtex_c", ".shader_c", ".vanmgrph_c", ".ag_c", ".anim_c", ".skel_c"
	};

	internal readonly record struct DiscoveryCandidate( string Path, AssetTypeAttribute Type );

	/// <summary>
	/// Builds the compact second-phase load plan while the mounted filesystem is enumerated.
	/// Native and unrelated files are never retained by the plan.
	/// </summary>
	internal sealed class DiscoveryPlan
	{
		private readonly List<DiscoveryCandidate> _gameResources = new();

		internal IReadOnlyList<DiscoveryCandidate> GameResources => _gameResources;
		internal int ScannedFileCount { get; private set; }
		internal int RegisteredPathCount { get; private set; }

		/// <returns>True when the path should be registered in the resource path index.</returns>
		internal bool Observe( string file, IReadOnlyDictionary<string, AssetTypeAttribute> types, IReadOnlySet<string> registeredExtensions )
		{
			ScannedFileCount++;

			var extension = System.IO.Path.GetExtension( file );
			if ( !registeredExtensions.Contains( extension ) )
				return false;

			RegisteredPathCount++;

			if ( types.TryGetValue( extension, out var type ) )
				_gameResources.Add( new DiscoveryCandidate( file, type ) );

			return true;
		}
	}

	internal static void LoadAllGameResource( BaseFileSystem fileSystem, bool reloadExisting = false, Package sourcePackage = null )
	{
		var totalTimer = Stopwatch.StartNew();
		var types = GlobalContext.Current.TypeLibrary.GetAttributes<AssetTypeAttribute>().DistinctBy( x => x.Extension )
			.ToDictionary( x => x.RuntimeExtension, x => x, StringComparer.OrdinalIgnoreCase );

		// Union GameResource extensions with native-only ones so PathIndex covers everything.
		var allExtensions = new HashSet<string>( types.Keys, StringComparer.OrdinalIgnoreCase );
		allExtensions.UnionWith( NativeExtensions );

		var discovery = new DiscoveryPlan();
		var pathsToRegister = new List<string>();
		var index = fileSystem.IndexFiles( "/" );
		foreach ( var file in index.Files )
		{
			if ( discovery.Observe( file, types, allExtensions ) )
				pathsToRegister.Add( file );
		}

		foreach ( var file in pathsToRegister )
			Game.Resources.RegisterPath( file );
		LoadUncompiledResources( fileSystem, index.Files, types, reloadExisting: reloadExisting );

		var allResources = new List<GameResource>();

		foreach ( var candidate in discovery.GameResources )
		{
			var file = candidate.Path;
			var type = candidate.Type;
			if ( type.IsUncompiled )
				continue;

			// Skip resources that are already fully loaded - this allows calling this method
			// multiple times (e.g. once per package) without redundant work.
			if ( !reloadExisting && ResourceLibrary.TryGet<GameResource>( file.Trim( '/' ), out var existing ) && !existing.IsPromise )
				continue;

			try
			{
				var se = Game.Resources.LoadGameResource( type, file, fileSystem, true, sourcePackage, index );
				if ( se != null ) allResources.Add( se );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"Exception when trying to load {file}" );
			}
		}

		//
		// When we're loading a bunch of GameResource we defer their PostLoad until everything is loaded.
		// Everyone is gonna wanna do Resource.Get<>() within their PostLoad and not care about load order.
		// This keeps things intuitive for end users.
		//
		foreach ( var resource in allResources )
		{
			resource.PostLoadInternal();
		}

		foreach ( var type in types )
		{
			AddWatcherForType( type.Value );
		}


		FileHashCache.Current.Flush();
		if ( totalTimer.Elapsed.TotalSeconds > 1 )
		{
			Log.Info( $"Resource discovery scanned {discovery.ScannedFileCount:N0} files, indexed {discovery.RegisteredPathCount:N0} paths, and loaded {allResources.Count:N0} GameResources in {totalTimer.Elapsed.TotalSeconds:0.000}s" );
		}

		// TODO: Check for edited but not saved OR recompiled assets and load in their values on server/client
		// like editing an asset while the gamemode is running would?
	}

	internal static async Task LoadAllGameResourceAsync( BaseFileSystem fileSystem, CancellationToken ct = default, bool reloadExisting = false, Package sourcePackage = null, Task<List<string>> preEnumeratedFiles = null, Func<string, bool> includeExtension = null )
	{
		// StartupTiming remains allocated after startup. Its completed Time value and the
		// first engine frame exclude later project reloads and background resource loads.
		var profile = StartupProfiler.Enabled && Bootstrap.StartupTiming is { } startupTiming
			&& !startupTiming.Data.ContainsKey( "Time" ) && EngineLoop.LoopFrames == 0;
		var profileStart = profile ? Stopwatch.GetTimestamp() : 0;
		long yieldTicks = 0, maxYieldTicks = 0;
		int yieldCount = 0;
		var totalTimer = Stopwatch.StartNew();
		var yieldTimer = Stopwatch.StartNew();
		var types = GlobalContext.Current.TypeLibrary.GetAttributes<AssetTypeAttribute>().DistinctBy( x => x.Extension )
			.ToDictionary( x => x.RuntimeExtension, x => x, StringComparer.OrdinalIgnoreCase );

		var allExtensions = new HashSet<string>( types.Keys, StringComparer.OrdinalIgnoreCase );
		allExtensions.UnionWith( NativeExtensions );

		var discovery = new DiscoveryPlan();
		var phaseStart = profile ? Stopwatch.GetTimestamp() : 0;
		var index = preEnumeratedFiles is null ? fileSystem.IndexFiles( "/" ) : null;
		var enumeratedFiles = index is null ? await preEnumeratedFiles : index.Files;
		var indexTicks = profile ? Stopwatch.GetTimestamp() - phaseStart : 0;
		phaseStart = profile ? Stopwatch.GetTimestamp() : 0;
		foreach ( var file in enumeratedFiles )
		{
			ct.ThrowIfCancellationRequested();
			if ( discovery.Observe( file, types, allExtensions ) )
				Game.Resources.RegisterPath( file );
			if ( yieldTimer.ElapsedMilliseconds > 8 )
			{
				LoadingScreen.Subtitle = System.IO.Path.GetFileName( file );
				var yieldStart = profile ? Stopwatch.GetTimestamp() : 0;
				await Task.Yield();
				if ( profile )
				{
					var elapsed = Stopwatch.GetTimestamp() - yieldStart;
					yieldTicks += elapsed;
					maxYieldTicks = Math.Max( maxYieldTicks, elapsed );
					yieldCount++;
				}
				yieldTimer.Restart();
			}
		}
		var registrationTicks = profile ? Stopwatch.GetTimestamp() - phaseStart - yieldTicks : 0;

		LoadUncompiledResources( fileSystem, enumeratedFiles, types, ct, includeExtension, reloadExisting );

		var allResources = new List<GameResource>();
		phaseStart = profile ? Stopwatch.GetTimestamp() : 0;
		var phaseYieldTicks = yieldTicks;

		for ( int i = 0; i < discovery.GameResources.Count; i++ )
		{
			ct.ThrowIfCancellationRequested();
			var candidate = discovery.GameResources[i];
			var file = candidate.Path;
			var type = candidate.Type;
			var extension = System.IO.Path.GetExtension( file );
			LoadProgress.Report( (double)i / discovery.GameResources.Count );
			if ( type.IsUncompiled ) continue;
			if ( includeExtension is not null && !includeExtension( extension ) ) continue;

			// Skip resources that are already fully loaded - this allows calling this method
			// multiple times (e.g. once per package) without redundant work.
			if ( !reloadExisting && ResourceLibrary.TryGet<GameResource>( file.Trim( '/' ), out var existing ) && !existing.IsPromise )
				continue;

			try
			{
				var se = Game.Resources.LoadGameResource( type, file, fileSystem, true, sourcePackage, index );
				if ( se != null ) allResources.Add( se );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"Exception when trying to load {file}" );
			}

			if ( yieldTimer.ElapsedMilliseconds > 8 )
			{
				LoadingScreen.Subtitle = System.IO.Path.GetFileName( file );
				var yieldStart = profile ? Stopwatch.GetTimestamp() : 0;
				await Task.Yield();
				if ( profile )
				{
					var elapsed = Stopwatch.GetTimestamp() - yieldStart;
					yieldTicks += elapsed;
					maxYieldTicks = Math.Max( maxYieldTicks, elapsed );
					yieldCount++;
				}
				yieldTimer.Restart();
			}
		}
		var loadTicks = profile ? Stopwatch.GetTimestamp() - phaseStart - (yieldTicks - phaseYieldTicks) : 0;

		phaseStart = profile ? Stopwatch.GetTimestamp() : 0;
		phaseYieldTicks = yieldTicks;
		foreach ( var resource in allResources )
		{
			ct.ThrowIfCancellationRequested();
			resource.PostLoadInternal();

			if ( yieldTimer.ElapsedMilliseconds > 8 )
			{
				LoadingScreen.Subtitle = System.IO.Path.GetFileName( resource.ResourcePath );
				var yieldStart = profile ? Stopwatch.GetTimestamp() : 0;
				await Task.Yield();
				if ( profile )
				{
					var elapsed = Stopwatch.GetTimestamp() - yieldStart;
					yieldTicks += elapsed;
					maxYieldTicks = Math.Max( maxYieldTicks, elapsed );
					yieldCount++;
				}
				yieldTimer.Restart();
			}
		}
		var postLoadTicks = profile ? Stopwatch.GetTimestamp() - phaseStart - (yieldTicks - phaseYieldTicks) : 0;

		LoadingScreen.Subtitle = null;

		foreach ( var type in types )
			AddWatcherForType( type.Value );

		phaseStart = profile ? Stopwatch.GetTimestamp() : 0;
		FileHashCache.Current.Flush();
		var flushTicks = profile ? Stopwatch.GetTimestamp() - phaseStart : 0;

		if ( profile )
		{
			static double Milliseconds( long ticks ) => ticks * 1000.0 / Stopwatch.Frequency;
			var context = GlobalContext.Current == GlobalContext.Game ? "game" : GlobalContext.Current == GlobalContext.Menu ? "menu" : "other";
			Log.Info( $"Startup profile (resource discovery): context={context}, startTicks={profileStart}, reloadExisting={reloadExisting}, preEnumerated={preEnumeratedFiles is not null}, filtered={includeExtension is not null}, " +
				$"scannedFiles={discovery.ScannedFileCount}, registeredPaths={discovery.RegisteredPathCount}, loadedResources={allResources.Count}, " +
				$"indexOrEnumerationAwaitMs={Milliseconds( indexTicks ):F3}, discoveryRegistrationWorkMs={Milliseconds( registrationTicks ):F3}, loadWorkMs={Milliseconds( loadTicks ):F3}, postLoadWorkMs={Milliseconds( postLoadTicks ):F3}, flushMs={Milliseconds( flushTicks ):F3}, " +
				$"yieldCount={yieldCount}, yieldWaitMs={Milliseconds( yieldTicks ):F3}, maxYieldWaitMs={Milliseconds( maxYieldTicks ):F3}, totalMs={totalTimer.Elapsed.TotalMilliseconds:F3}" );
		}

		if ( totalTimer.Elapsed.TotalSeconds > 1 )
		{
			Log.Info( $"Resource discovery scanned {discovery.ScannedFileCount:N0} files, indexed {discovery.RegisteredPathCount:N0} paths, and loaded {allResources.Count:N0} GameResources in {totalTimer.Elapsed.TotalSeconds:0.000}s" );
		}
	}

	/// <summary>
	/// Loads source assets before compiled resources, allowing their declarations to participate in deserialization.
	/// Scripts publish together and execute in path order before other source assets; ordinary resource loads only read their source.
	/// </summary>
	private static void LoadUncompiledResources( BaseFileSystem fileSystem, IEnumerable<string> files,
		IReadOnlyDictionary<string, AssetTypeAttribute> types, CancellationToken cancellationToken = default,
		Func<string, bool> includeExtension = null, bool reloadExisting = false )
	{
		var scripts = new Dictionary<string, string>( StringComparer.Ordinal );
		var remaining = new List<(AssetTypeAttribute Type, string Path)>();
		foreach ( var path in files.OrderBy( path => path.EndsWith( ".csx", StringComparison.OrdinalIgnoreCase ) ? 0 : 1 )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase ) )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var extension = System.IO.Path.GetExtension( path );
			if ( !types.TryGetValue( extension, out var type ) || !type.IsUncompiled )
				continue;
			if ( includeExtension is not null && !includeExtension( extension ) )
				continue;

			if ( type.TargetType == typeof( ScriptResource ) )
			{
				// The menu never loads scripts. Clients get their scripts from the host's table.
				if ( GlobalContext.Current == GlobalContext.Menu || GlobalContext.Current.ReceivesNetworkScripts )
				{
					continue;
				}

				if ( LoadUncompiledResource( type, path, fileSystem, true ) is ScriptResource script )
				{
					scripts[script.ResourcePath] = script.Source;
				}
			}
			else
			{
				remaining.Add( (type, path) );
			}
		}

		cancellationToken.ThrowIfCancellationRequested();
		LoadScriptResources( scripts );
		foreach ( var (type, path) in remaining )
		{
			cancellationToken.ThrowIfCancellationRequested();
			LoadUncompiledResource( type, path, fileSystem, reloadExisting );
		}
	}

	/// <summary>
	/// Reads uncompiled resource source, isolating I/O failures before a script batch is prepared.
	/// </summary>
	private static Resource LoadUncompiledResource( AssetTypeAttribute type, string path, BaseFileSystem fileSystem, bool reloadExisting )
	{
		try
		{
			return Game.Resources.LoadUncompiledResource( type, path, fileSystem,
				reloadExisting || type.TargetType == typeof( ScriptResource ) );
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, $"Exception when trying to load {path}" );
			return null;
		}
	}

	/// <summary>
	/// Keeps script context failures from stopping unrelated resource loading. Empty scans do not create a scripting context.
	/// </summary>
	private static void LoadScriptResources( IReadOnlyDictionary<string, string> scripts, IReadOnlyCollection<string> removed = null )
	{
		if ( scripts.Count == 0 && (removed is null || removed.Count == 0) )
		{
			return;
		}
		try
		{
			Game.Scripting.LoadResourceFiles( scripts, removed );
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, "Exception when publishing script resources" );
		}
	}

	// Watchers belong to the mounted filesystem, not just the asset type. Game and menu contexts
	// can have different mounts, and a disposed mount must not prevent a new context from watching.
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BaseFileSystem, Dictionary<(GlobalContext Context, string Type), WatcherSubscription>> Watchers = new();

	static void AddWatcherForType( AssetTypeAttribute type )
	{
		if ( string.IsNullOrEmpty( type.Extension ) )
			return;

		// Watcher already set up for this type - no need to allocate another one.
		var context = GlobalContext.Current;
		if ( context == GlobalContext.Menu && type.TargetType == typeof( ScriptResource ) )
			return;

		var fileSystem = EngineFileSystem.Mounted;
		var watchers = Watchers.GetOrCreateValue( fileSystem );
		var key = (context, type.TargetType.AssemblyQualifiedName);
		if ( watchers.ContainsKey( key ) )
			return;

		var watcher = fileSystem.Watch( $"*{type.RuntimeExtension}" );
		watcher.OnChanges += watch =>
		{
			if ( context.FileMount != fileSystem || context.ResourceSystem is null )
				return;

			// Watchers are dispatched together, outside the context in which they were created.
			using var scope = new GlobalContext.GlobalContextScope( context );
			OnAssetFilesChanged( watch, type );
		};

		var subscription = new WatcherSubscription( watcher );
		watchers[key] = subscription;
		subscription.CancellationRegistration = context.CancellationTokenSource.Token.Register( () =>
		{
			watcher.Dispose();
			watchers.Remove( key );
		} );
	}

	private static void OnAssetFilesChanged( FileWatch watch, AssetTypeAttribute type )
	{
		if ( type.TargetType == typeof( ScriptResource )
			&& (GlobalContext.Current == GlobalContext.Menu || GlobalContext.Current.ReceivesNetworkScripts) )
		{
			return;
		}

		var fs = EngineFileSystem.Mounted;
		if ( type.IsUncompiled )
		{
			var scripts = new Dictionary<string, string>( StringComparer.Ordinal );
			var removed = new List<string>();
			foreach ( var path in watch.Changes.Select( path => path.NormalizeFilename() ).Distinct( StringComparer.OrdinalIgnoreCase )
				.OrderBy( path => path, StringComparer.OrdinalIgnoreCase ) )
			{
				if ( fs.FileExists( path ) )
				{
					if ( LoadUncompiledResource( type, path, fs, reloadExisting: true ) is ScriptResource script )
					{
						scripts[script.ResourcePath] = script.Source;
					}
				}
				else if ( type.TargetType == typeof( ScriptResource ) )
				{
					removed.Add( Resource.FixPath( path ) );
				}
			}
			LoadScriptResources( scripts, removed );
			return;
		}

		Dictionary<string, (GameResource resource, ulong id)> removes = new();

		foreach ( var f in watch.Changes )
		{
			string path = Resource.FixPath( f );
			if ( !path.EndsWith( "_c" ) )
				path += "_c";

			// Added?
			if ( !ResourceLibrary.TryGet<GameResource>( path, out var resource ) || resource.IsPromise )
			{
				if ( !fs.FileExists( path ) )
					continue; // but doesn't exist? wtf

				Log.Info( $"Detected Added File {path}" );
				Game.Resources.LoadGameResource( type, path, fs );
				continue;
			}

			// Removed?
			if ( !fs.FileExists( path ) )
			{
				// but defer until later incase it's actually just been moved
				removes.TryAdd( path, (resource, resource.ResourceIdLong) );
				continue;
			}

			// Modified
			Span<byte> data = fs.ReadAllBytes( path );
			if ( data.Length <= 3 )
			{
				Log.Warning( $"Couldn't load json data from {path}" );
				continue;
			}

			// check what part of the resource was modified - compiled or source - do events
			bool isModifiedCompiled = resource.TryLoadFromData( data );
			bool isModifiedSource = false;
			if ( isModifiedCompiled && fs.FileExists( resource.ResourcePath ) )
			{
				var jsonBlob = fs.ReadAllText( resource.ResourcePath );
				if ( string.IsNullOrEmpty( jsonBlob ) ) continue;

				var sourceHash = jsonBlob.FastHash();
				if ( sourceHash != resource.LastSavedSourceHash && resource.LastSavedSourceHash != 0 )
				{
					IToolsDll.Current?.RunEvent<ResourceLibrary.IEventListener>( i => i.OnExternalChanges( resource ) );
					isModifiedSource = true;
				}
			}

			resource.PostReloadInternal();

			if ( isModifiedSource )
			{
				IToolsDll.Current?.RunEvent<ResourceLibrary.IEventListener>( i => i.OnExternalChangesPostLoad( resource ) );
			}
		}

		foreach ( (string path, (GameResource resource, ulong id)) in removes )
		{
			if ( id != resource.ResourceIdLong )
				continue; // id has been reassigned - this is a move, not a delete

			Log.Info( $"Detected Asset File Deleted {path}" );
			resource.DestroyInternal();
		}
	}

	internal static void Clear()
	{
		// Dispose of watchers too
		foreach ( var (_, watchers) in Watchers )
		{
			foreach ( var watcher in watchers.Values )
			{
				watcher.Dispose();
			}
			watchers.Clear();
		}
		Watchers.Clear();
	}

	/// <summary>
	/// Releases the cancellation callback as well as the watcher when cleared before context shutdown.
	/// </summary>
	private sealed class WatcherSubscription( FileWatch watcher ) : IDisposable
	{
		/// <summary>
		/// Context cancellation callback owned by this subscription.
		/// </summary>
		public CancellationTokenRegistration CancellationRegistration { get; set; }

		/// <inheritdoc />
		public void Dispose()
		{
			CancellationRegistration.Dispose();
			watcher.Dispose();
		}
	}
}
