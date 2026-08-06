using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace Editor;

public class CloudAsset
{
	private static readonly CloudAssetReferenceIndex<Asset> ReferenceIndex = new();

	internal static void ResetReferenceIndex() => ReferenceIndex.Reset();

	/// <summary>
	/// Checks if a package is installed on disk, including checking the version if it's present in the ident.
	/// </summary>
	/// <param name="ident"></param>
	/// <returns></returns>
	private static bool IsInstalled( string ident )
	{
		// this shouldn't really happen, but I guess just in case someone fudges a file
		if ( !Package.TryParseIdent( ident, out var parts ) )
			return true;

		var packageIdentNoVersion = $"{parts.org}.{parts.package}";

		// Putting this here while I look into why vmaps are referencing their own game projects 
		if ( packageIdentNoVersion == Game.Ident )
			return true;

		// Do we have this package on disk already?
		var localPackage = AssetSystem.CloudDirectory.FindPackage( packageIdentNoVersion );
		if ( localPackage is null )
			return false;

		// If it's pinned to a version, check we're on that
		if ( parts.version is not null && localPackage.Revision.VersionId != parts.version )
		{
			Log.Info( $"Package '{packageIdentNoVersion}' version mismatch, updating.. (current: {localPackage.Revision.VersionId}, required: {parts.version})" );
			return false;
		}

		return true;
	}

	/// <summary>
	/// Install a cloud asset by ident
	/// </summary>
	[ConCmd( "install", ConVarFlags.Protected )]
	public static async Task InstallSingle( string ident )
	{
		var asset = await AssetSystem.InstallAsync( ident );
		EditorUtility.InspectorObject = asset;
	}


	/// <summary>
	/// Some projects were saved with multiple references to different versions of the same
	/// package. Collapse the references down to one ident per package so we only try to
	/// install each once - an unpinned reference is upgraded by a version-pinned one, and
	/// conflicting pins resolve to the newest version. Unparseable idents are dropped.
	/// </summary>
	internal static string[] DeduplicateReferences( IEnumerable<string> packages )
	{
		Dictionary<string, int?> versions = new();
		foreach ( var ident in packages )
		{
			if ( !Package.TryParseIdent( ident, out var parts ) )
				continue;

			string fullIdent = Package.FormatIdent( parts.org, parts.package );
			var newVer = parts.version;

			if ( !versions.TryGetValue( fullIdent, out var existingVer ) || existingVer == null )
			{
				versions[fullIdent] = newVer;
				continue;
			}

			if ( newVer == null )
				continue;

			if ( existingVer != newVer )
			{
				// version conflict! choose newest
				var bestVer = Math.Max( newVer.Value, existingVer.Value );
				Log.Info( $"Found duplicate reference '{ident}' with conflicting versions (using: {bestVer})" );

				versions[fullIdent] = bestVer;
			}
		}

		return versions.Select( x => x.Value.HasValue ? $"{x.Key}#{x.Value}" : x.Key ).ToArray();
	}

	/// <summary>
	/// Install multiple packages, skipping what's already installed. Does progress window.
	/// </summary>
	public static async Task<bool> Install( string windowTitle, IEnumerable<string> packages )
	{
		if ( packages.Count() == 0 ) return true;

		if ( Backend.Package is null )
		{
			Log.Warning( $"Unable to install cloud assets, backend not available." );
			return false;
		}

		using var progress = Application.Editor.ProgressSection();
		progress.Title = windowTitle;
		var cancel = progress.GetCancel();

		var undownloaded = DeduplicateReferences( packages ).Where( x => !IsInstalled( x ) ).ToArray();

		int total = undownloaded.Count();
		int i = 0;

		await undownloaded.ForEachTaskAsync( async ident =>
		{
			Package package = await Package.FetchAsync( ident, true );
			if ( package == null )
				return;

			progress.Title = $"Installing '{package.Title}'";
			progress.TotalCount = total;
			progress.Current = ++i;

			Log.Info( $"Installing '{package.FullIdent}' (version: {package.Revision.VersionId})" );
			await AssetSystem.InstallAsync( package, false, null, cancel );

		}, token: cancel, maxRunning: 8 );


		return !cancel.IsCancellationRequested;
	}

	/// <summary>
	/// Install multiple packages, skipping what's already installed.
	/// </summary>
	public static async Task<bool> Install( IEnumerable<string> packages, CancellationToken token )
	{
		if ( !packages.Any() ) return true;

		if ( Backend.Package is null )
		{
			Log.Warning( $"Unable to install cloud assets, backend not available." );
			return false;
		}

		var undownloaded = DeduplicateReferences( packages ).Where( x => !IsInstalled( x ) ).ToArray();

		int total = undownloaded.Count();
		if ( total == 0 ) return true;

		Log.Info( $"Installing {total:n0} missing cloud assets.." );
		var tasks = undownloaded.Select( ident => InstallPackage( ident, total, token ) ).ToList();

		while ( !tasks.All( x => x.IsCompleted ) )
		{
			await Task.Delay( 10, token );
			token.ThrowIfCancellationRequested();
		}

		return !token.IsCancellationRequested;
	}

	static SemaphoreSlim packageDownloadSemaphore = new SemaphoreSlim( 8 );
	static int installedPackages;

	static async Task InstallPackage( string ident, int totalPackageNum, CancellationToken token )
	{
		try
		{
			await packageDownloadSemaphore.WaitAsync( token );

			Package package = await Package.FetchAsync( ident, false );
			if ( package == null )
				return;

			var asset = await AssetSystem.InstallAsync( package, false, null, token );

			var i = Interlocked.Add( ref installedPackages, 1 );

			Log.Info( $"[{i}/{totalPackageNum}] '{ident}' installed." );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error installing package {ident}" );
		}
		finally
		{
			packageDownloadSemaphore.Release();
		}
	}

	internal static async Task AddNewServerPackages()
	{
		// find out what stuff we're referencing in the project that's not already a part of the published project (a NEW package)
		// add these to our ServerPackages string table so connecting clients know to fetch these

		var gamePackage = IGameInstance.Current?.Package;
		if ( gamePackage is null )
			return;

		var sw = System.Diagnostics.Stopwatch.StartNew();

		var filesInManifest = await GetPublishedManifestFiles( gamePackage );

		var packages = GetAssetReferences( true );

		int count = 0;
		foreach ( var ident in packages )
		{
			if ( !Package.TryParseIdent( ident, out var parts ) )
				continue;

			var package = AssetSystem.CloudDirectory.FindPackage( Package.FormatIdent( parts.org, parts.package ) );
			if ( package is null )
				continue;

			string filepath = package.PrimaryAsset;
			if ( string.IsNullOrEmpty( filepath ) )
				continue;

			if ( !IsPublishedAsset( filesInManifest, filepath ) )
			{
				ServerPackages.Current.AddRequirement( package );
				count++;
			}
		}

		Log.Info( $"Added new {count} cloud reference(s) to ServerPackage table.. (took {sw.Elapsed.TotalSeconds:0.000}s)" );
	}

	internal static bool IsPublishedAsset( IReadOnlySet<string> filesInManifest, string primaryAsset )
	{
		if ( string.IsNullOrEmpty( primaryAsset ) )
			return false;

		if ( !primaryAsset.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
			primaryAsset += "_c";

		return filesInManifest.Contains( primaryAsset );
	}
	internal static async Task<HashSet<string>> GetPublishedManifestFiles( Package gamePackage )
	{
		HashSet<string> files = new( StringComparer.OrdinalIgnoreCase );
		var revision = gamePackage?.Revision;
		if ( revision is null )
			return files;

		await revision.DownloadManifestAsync();

		foreach ( var file in revision.Manifest?.Files ?? Array.Empty<ManifestSchema.File>() )
		{
			if ( !string.IsNullOrEmpty( file.Path ) )
				files.Add( file.Path );
		}

		return files;
	}

	/// <summary>
	/// Gets all cloud packages referenced from assets
	/// </summary>
	public static HashSet<string> GetAssetReferences( bool currentProjectOnly )
	{
		return new HashSet<string>( GetAssetReferenceSources( currentProjectOnly ).Keys, StringComparer.OrdinalIgnoreCase );
	}

	/// <summary>
	/// Gets all referenced cloud assets and all local assets that reference them
	/// </summary>
	public static Dictionary<string, List<Asset>> GetAssetReferenceSources( bool currentProjectOnly )
	{
		string projectPath = Project.Current.GetAssetsPath().Replace( '\\', '/' );

		HashSet<string> validAssetPaths = null;
		if ( currentProjectOnly )
		{
			validAssetPaths = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

			// Include current project
			validAssetPaths.Add( projectPath );

			// Include all libraries used by the current project
			foreach ( var library in LibrarySystem.All )
			{
				var libraryAssetsPath = library.Project.GetAssetsPath()?.Replace( '\\', '/' );
				if ( !string.IsNullOrEmpty( libraryAssetsPath ) )
				{
					validAssetPaths.Add( libraryAssetsPath );
				}
			}
		}

		ReferenceIndex.Build( AssetSystem.All, x => x.AbsolutePath, ReadReferences );
		return ReferenceIndex.Snapshot( asset => !currentProjectOnly || validAssetPaths.Any( path => asset.AbsolutePath.StartsWith( path, StringComparison.OrdinalIgnoreCase ) ) );
	}

	internal static void UpdateReferenceIndex( Asset asset, string json = null )
	{
		if ( asset is null )
		{
			ReferenceIndex.Reset();
			return;
		}

		var references = asset.AssetType.IsGameResource && json is not null
			? ReadGameResourceReferences( asset, json )
			: ReadReferences( asset );
		ReferenceIndex.Update( asset, asset.AbsolutePath, references );
	}

	internal static void ReconcileReferenceIndex()
	{
		ReferenceIndex.Reconcile( AssetSystem.All, x => x.AbsolutePath, ReadReferences );
	}

	private static IEnumerable<string> ReadReferences( Asset asset )
	{
		if ( asset.AssetType.IsGameResource )
		{
			string json = null;
			try
			{
				json = asset.ReadJson();
				return ReadGameResourceReferences( asset, json );
			}
			catch ( Exception e )
			{
				Log.Info( $"{asset.AbsolutePath} - {e.Message}" );
				return Array.Empty<string>();
			}
		}

		var config = asset.Publishing?.ProjectConfig;
		if ( config is null ) return Array.Empty<string>();
		return (config.EditorReferences?.AsEnumerable() ?? Enumerable.Empty<string>())
			.Concat( config.DistinctPackageReferences?.AsEnumerable() ?? Enumerable.Empty<string>() )
			.ToArray();
	}

	internal static string[] ReadGameResourceReferences( Asset asset, string json )
	{
		if ( string.IsNullOrWhiteSpace( json ) ) return Array.Empty<string>();
		try
		{
			if ( JsonNode.Parse( json ) is not JsonObject jso ) return Array.Empty<string>();
			if ( jso["__references"] is not JsonArray refs ) return Array.Empty<string>();
			return refs.Select( x => x?.ToString() ).Where( x => !string.IsNullOrWhiteSpace( x ) ).ToArray();
		}
		catch ( JsonException e )
		{
			Log.Info( $"{asset?.AbsolutePath} - {e.Message}" );
			Log.Info( json );
			return Array.Empty<string>();
		}
	}
}

internal sealed class CloudAssetReferenceListener : ResourceLibrary.IEventListener, AssetSystem.IEventListener
{
	void ResourceLibrary.IEventListener.OnSourceSaved( GameResource resource, string filename, string json )
	{
		CloudAsset.UpdateReferenceIndex( AssetSystem.FindByPath( filename ), json );
	}

	void AssetSystem.IEventListener.OnAssetChanged( Asset asset ) => CloudAsset.UpdateReferenceIndex( asset );
	void AssetSystem.IEventListener.OnAssetSystemChanges() => CloudAsset.ReconcileReferenceIndex();
}
