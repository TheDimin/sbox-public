using Sandbox.Engine;
using System.Diagnostics;
using System.Threading;

namespace Sandbox;

internal static class ResourceLoader
{
	// Native resource extensions (with _c suffix) not covered by AssetTypeAttribute.
	private static readonly HashSet<string> NativeExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".vmat_c", ".vmdl_c", ".vtex_c", ".shader_c", ".vanmgrph_c"
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
		var types = Game.TypeLibrary.GetAttributes<AssetTypeAttribute>().DistinctBy( x => x.Extension )
			.ToDictionary( x => $".{x.Extension}_c", x => x, StringComparer.OrdinalIgnoreCase );

		// Union GameResource extensions with native-only ones so PathIndex covers everything.
		var allExtensions = new HashSet<string>( types.Keys, StringComparer.OrdinalIgnoreCase );
		allExtensions.UnionWith( NativeExtensions );

		var discovery = new DiscoveryPlan();
		var pathsToRegister = new List<string>();
		foreach ( var file in fileSystem.FindFile( "/", "*", true ) )
		{
			if ( discovery.Observe( file, types, allExtensions ) )
				pathsToRegister.Add( file );
		}

		foreach ( var file in pathsToRegister )
			Game.Resources.RegisterPath( file );

		var allResources = new List<GameResource>();

		foreach ( var candidate in discovery.GameResources )
		{
			var file = candidate.Path;
			var type = candidate.Type;

			// Skip resources that are already fully loaded - this allows calling this method
			// multiple times (e.g. once per package) without redundant work.
			if ( !reloadExisting && ResourceLibrary.TryGet<GameResource>( file.Trim( '/' ), out var existing ) && !existing.IsPromise )
				continue;

			try
			{
				var se = Game.Resources.LoadGameResource( type, file, fileSystem, true, sourcePackage );
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

	internal static async Task LoadAllGameResourceAsync( BaseFileSystem fileSystem, CancellationToken ct = default, bool reloadExisting = false, Package sourcePackage = null )
	{
		var totalTimer = Stopwatch.StartNew();
		var yieldTimer = Stopwatch.StartNew();
		var types = Game.TypeLibrary.GetAttributes<AssetTypeAttribute>().DistinctBy( x => x.Extension )
			.ToDictionary( x => $".{x.Extension}_c", x => x, StringComparer.OrdinalIgnoreCase );

		var allExtensions = new HashSet<string>( types.Keys, StringComparer.OrdinalIgnoreCase );
		allExtensions.UnionWith( NativeExtensions );

		var discovery = new DiscoveryPlan();
		foreach ( var file in fileSystem.FindFile( "/", "*", true ) )
		{
			ct.ThrowIfCancellationRequested();
			if ( discovery.Observe( file, types, allExtensions ) )
				Game.Resources.RegisterPath( file );
			if ( yieldTimer.ElapsedMilliseconds > 8 ) { LoadingScreen.Subtitle = System.IO.Path.GetFileName( file ); await Task.Yield(); yieldTimer.Restart(); }
		}

		var allResources = new List<GameResource>();

		foreach ( var candidate in discovery.GameResources )
		{
			ct.ThrowIfCancellationRequested();
			var file = candidate.Path;
			var type = candidate.Type;

			// Skip resources that are already fully loaded - this allows calling this method
			// multiple times (e.g. once per package) without redundant work.
			if ( !reloadExisting && ResourceLibrary.TryGet<GameResource>( file.Trim( '/' ), out var existing ) && !existing.IsPromise )
				continue;

			try
			{
				var se = Game.Resources.LoadGameResource( type, file, fileSystem, true, sourcePackage );
				if ( se != null ) allResources.Add( se );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"Exception when trying to load {file}" );
			}

			if ( yieldTimer.ElapsedMilliseconds > 8 ) { LoadingScreen.Subtitle = System.IO.Path.GetFileName( file ); await Task.Yield(); yieldTimer.Restart(); }
		}

		foreach ( var resource in allResources )
		{
			ct.ThrowIfCancellationRequested();
			resource.PostLoadInternal();

			if ( yieldTimer.ElapsedMilliseconds > 8 ) { LoadingScreen.Subtitle = System.IO.Path.GetFileName( resource.ResourcePath ); await Task.Yield(); yieldTimer.Restart(); }
		}

		LoadingScreen.Subtitle = null;

		foreach ( var type in types )
			AddWatcherForType( type.Value );

		FileHashCache.Current.Flush();

		if ( totalTimer.Elapsed.TotalSeconds > 1 )
		{
			Log.Info( $"Resource discovery scanned {discovery.ScannedFileCount:N0} files, indexed {discovery.RegisteredPathCount:N0} paths, and loaded {allResources.Count:N0} GameResources in {totalTimer.Elapsed.TotalSeconds:0.000}s" );
		}
	}


	static Dictionary<string, FileWatch> Watchers = new();

	static void AddWatcherForType( AssetTypeAttribute type )
	{
		if ( string.IsNullOrEmpty( type.Extension ) )
			return;

		// Watcher already set up for this type - no need to allocate another one.
		if ( Watchers.ContainsKey( type.TargetType.AssemblyQualifiedName ) )
			return;

		var watcher = EngineFileSystem.Mounted.Watch( $"*.{type.Extension}_c" );
		watcher.OnChanges += ( w ) => OnAssetFilesChanged( w, type );

		Watchers[type.TargetType.AssemblyQualifiedName] = watcher;
	}

	private static void OnAssetFilesChanged( FileWatch watch, AssetTypeAttribute type )
	{
		foreach ( var change in watch.Changes )
		{
			OnAssetFileChanged( change, type );
		}
	}

	static void OnAssetFileChanged( string file, AssetTypeAttribute type )
	{
		var fs = EngineFileSystem.Mounted;

		if ( !file.EndsWith( "_c" ) )
			file += "_c";

		//
		// Asset doesn't exist, maybe just added?
		//
		if ( !ResourceLibrary.TryGet<GameResource>( file.Trim( '/' ), out var asset ) || asset.IsPromise )
		{
			// file wasn't found, so I don't know what was happening.
			if ( !fs.FileExists( file ) )
				return;

			Log.Info( $"Detected Added File {file}" );
			Game.Resources.LoadGameResource( type, file, fs );
			return;
		}

		//
		// File was removed, tell the asset system it died
		//
		if ( !fs.FileExists( file ) )
		{
			Log.Info( $"Detected Asset File Deleted {file}" );

			// Removes from ResourceLibrary
			asset.DestroyInternal();
			return;
		}

		Span<byte> data = fs.ReadAllBytes( file );

		if ( data.Length <= 3 )
		{
			Log.Warning( $"Couldn't load json data from {file}" );
			return;
		}

		bool hasCompiledChanges = asset.TryLoadFromData( data );
		bool externalChanges = false;
		if ( hasCompiledChanges )
		{
			// check for source file changes
			if ( fs.FileExists( asset.ResourcePath ) )
			{
				var jsonBlob = fs.ReadAllText( asset.ResourcePath );
				if ( string.IsNullOrEmpty( jsonBlob ) ) return;

				var sourceHash = jsonBlob.FastHash();
				if ( sourceHash != asset.LastSavedSourceHash && asset.LastSavedSourceHash != 0 )
				{
					IToolsDll.Current?.RunEvent<ResourceLibrary.IEventListener>( i => i.OnExternalChanges( asset ) );
					externalChanges = true;
				}
			}
		}

		asset.PostReloadInternal();

		if ( externalChanges )
		{
			IToolsDll.Current?.RunEvent<ResourceLibrary.IEventListener>( i => i.OnExternalChangesPostLoad( asset ) );
		}
	}

	internal static void Clear()
	{
		// Dispose of watchers too
		foreach ( var watcher in Watchers ) watcher.Value.Dispose();
		Watchers.Clear();
	}
}
