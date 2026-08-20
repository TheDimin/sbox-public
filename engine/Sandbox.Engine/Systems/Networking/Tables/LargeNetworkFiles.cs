using Sandbox.Internal;
using Sandbox.Network;
using System.Threading;

namespace Sandbox;

internal class LargeNetworkFiles
{
	public BaseFileSystem Files { get; private set; }
	public StringTable StringTable { get; init; }

	internal record struct LargeFileInfo( long Size, ulong CRC );

	RedirectFileSystem RedirectFileSystem { get; set; }

	HashSet<string> downloadQueue = new();
	Dictionary<string, BaseFileSystem> fileSources = new( StringComparer.OrdinalIgnoreCase );
	Task activeDownload;
	readonly FileHashCache fileHashCache;

	internal LargeNetworkFiles( string name, FileHashCache fileHashCache = null )
	{
		this.fileHashCache = fileHashCache;
		StringTable = new( name, true );
		StringTable.OnChangeOrAdd += OnTableEntryUpdated;
		StringTable.OnRemoved += OnTableEntryRemoved;
		StringTable.OnSnapshot += OnTableSnapshot;
	}

	/// <summary>
	/// Reset the string table.
	/// </summary>
	public void Reset()
	{
		StringTable.Reset();
		fileSources.Clear();

		Files?.Dispose();
		RedirectFileSystem = AssetDownloadCache.CreateRedirectFileSystem();
		Files = new BaseFileSystem( RedirectFileSystem );
	}

	/// <summary>
	/// Add all files from the network.
	/// </summary>
	public void Refresh()
	{
		foreach ( var (_, entry) in StringTable.Entries )
		{
			AddFileToFileSystem( entry.Name, entry.Read<LargeFileInfo>() );
		}
	}

	/// <summary>
	/// Add a file to be networked.
	/// </summary>
	public bool AddFile( string fileName, Action<long, TimeSpan, bool> onCrc = null, Action<TimeSpan> onTableSet = null )
		=> AddFile( EngineFileSystem.Mounted, fileName, false, onCrc, onTableSet );

	/// <summary>
	/// Add a file from a specific filesystem to be networked.
	/// </summary>
	public bool AddFile( BaseFileSystem fs, string fileName, bool forceRefresh, Action<long, TimeSpan, bool> onCrc = null, Action<TimeSpan> onTableSet = null )
	{
		if ( !fs.FileExists( fileName ) )
			return false;

		var crcTimer = System.Diagnostics.Stopwatch.StartNew();
		var crc = (fileHashCache ?? FileHashCache.Current).GetOrComputeCrc( fs, fileName, forceRefresh, out var size, out var cacheHit );
		crcTimer.Stop();
		onCrc?.Invoke( size, crcTimer.Elapsed, cacheHit );
		var normalizedFileName = NormalizeFileName( fileName );
		fileSources[normalizedFileName] = fs;

		var value = new LargeFileInfo( size, crc );
		if ( TryGetFileInfo( normalizedFileName, out var existing ) && existing == value )
			return true;

		var tableTimer = System.Diagnostics.Stopwatch.StartNew();
		StringTable.Set( normalizedFileName, value );
		tableTimer.Stop();
		onTableSet?.Invoke( tableTimer.Elapsed );

		return true;
	}

	/// <summary>
	/// Remove a networked file.
	/// </summary>
	public bool RemoveFile( string fileName )
	{
		var normalizedFileName = NormalizeFileName( fileName );
		fileSources.Remove( normalizedFileName );
		return StringTable.Remove( normalizedFileName ) is not null;
	}

	internal bool TryGetFileInfo( string fileName, out LargeFileInfo info )
	{
		var normalizedFileName = NormalizeFileName( fileName );
		if ( StringTable.Entries.TryGetValue( normalizedFileName, out var entry ) )
		{
			info = entry.Read<LargeFileInfo>();
			return true;
		}

		info = default;
		return false;
	}

	string NormalizeFileName( string fileName )
	{
		return BaseFileSystem.NormalizeFilename( fileName ).TrimStart( '/' );
	}

	void OnTableEntryUpdated( StringTable.Entry entry )
	{
		AddFileToFileSystem( entry.Name, entry.Read<LargeFileInfo>() );
	}

	void OnTableEntryRemoved( StringTable.Entry entry )
	{
		downloadQueue.Remove( entry.Name );
		RedirectFileSystem?.RemoveAbsFile( entry.Name );
	}

	void OnTableSnapshot()
	{
		Log.Info( "Checking for network files.." );
		var sw = System.Diagnostics.Stopwatch.StartNew();

		Refresh();

		Log.Info( $"..done in {sw.Elapsed.TotalSeconds:0.00}s" );
	}

	void AddFileToFileSystem( string fileName, LargeFileInfo contents )
	{
		if ( TryReuseMatchingMountedFile(
			EngineFileSystem.Mounted,
			fileName,
			contents,
			(path, fullPath) => RedirectFileSystem?.AddAbsFile( path, fullPath ) ) )
		{
			return;
		}

		QueueFileIfNeeded(
			fileName,
			contents,
			(path, crc) => AssetDownloadCache.TryMount( RedirectFileSystem, path, crc ) );
	}

	internal static bool TryReuseMatchingMountedFile(
		BaseFileSystem mountedFiles,
		string fileName,
		LargeFileInfo contents,
		Action<string, string> addRedirect )
	{
		if ( mountedFiles is null )
			return false;

		foreach ( var fullPath in mountedFiles.GetPhysicalPaths( fileName ) )
		{
			if ( !PhysicalPathMatchesLogicalPath( fullPath, fileName ) )
				continue;

			var fileInfo = new System.IO.FileInfo( fullPath );
			if ( fileInfo.Length != contents.Size )
				continue;

			using var stream = System.IO.File.OpenRead( fullPath );
			if ( Sandbox.Utility.Crc64.FromStream( stream ) != contents.CRC )
				continue;

			addRedirect?.Invoke( fileName.NormalizeFilename( true ), fullPath );
			return true;
		}

		return false;
	}

	internal static bool PhysicalPathMatchesLogicalPath( string fullPath, string fileName )
	{
		if ( string.IsNullOrWhiteSpace( fullPath ) || string.IsNullOrWhiteSpace( fileName ) )
			return false;

		var relativePath = BaseFileSystem.NormalizeFilename( fileName )
			.TrimStart( '/' )
			.Replace( '/', System.IO.Path.DirectorySeparatorChar );
		return fullPath.EndsWith( relativePath, StringComparison.OrdinalIgnoreCase );
	}

	internal void QueueFileIfNeeded( string fileName, LargeFileInfo contents, Func<string, ulong, bool> tryMount )
	{
		if ( !AssetDownloadCache.IsLegalDownload( fileName ) )
			return;

		if ( tryMount?.Invoke( fileName, contents.CRC ) == true )
			return;

		if ( AssetDownloadCache.DebugNetworkFiles )
		{
			Log.Info( $"Queued Network File: {fileName} / {contents.Size} / {contents.CRC}" );
		}
		downloadQueue.Add( fileName );
	}

	internal int PendingDownloadCount => downloadQueue.Count;

	internal void EnableLiveDownloads( Func<Task> runDownloads )
	{
		StringTable.PostNetworkUpdate = async () =>
		{
			try
			{
				await runDownloads();
			}
			catch ( OperationCanceledException )
			{
				// The connection or environment was reset while downloading.
			}
			catch ( Exception e )
			{
				Log.Warning( e, "Failed to download updated network files" );
			}
		};
	}

	public Task RunDownloadQueue( NetworkSystem system, CancellationToken token )
	{
		if ( activeDownload is { IsCompleted: false } )
			return activeDownload;

		activeDownload = DrainDownloadQueue( system, token );
		return activeDownload;
	}

	private async Task DrainDownloadQueue( NetworkSystem system, CancellationToken token )
	{
		if ( RedirectFileSystem is null )
			return;

		Assert.NotNull( Connection.Host );

		Log.Info( $"Downloading {downloadQueue.Count} files.." );
		var currentCount = 0;
		var sw = System.Diagnostics.Stopwatch.StartNew();

		while ( downloadQueue.Count > 0 )
		{
			foreach ( var file in downloadQueue.ToArray() )
			{
				if ( !StringTable.Entries.TryGetValue( file, out var entry ) )
				{
					downloadQueue.Remove( file );
					continue;
				}

				var info = entry.Read<LargeFileInfo>();

				if ( AssetDownloadCache.DebugNetworkFiles )
				{
					Log.Info( $"Download file {file}" );
				}

				LoadingScreen.Title = $"Downloading Files ({currentCount + 1})";
				LoadingScreen.Subtitle = file;

				if ( RedirectFileSystem.FileExists( file.NormalizeFilename( true ) ) )
				{
					currentCount++;
					downloadQueue.Remove( file );
					continue;
				}

				token.ThrowIfCancellationRequested();

				if ( Connection.Host is null )
				{
					throw new TaskCanceledException( "Connection became null" );
				}

				// download the file
				var response = await Connection.Host.SendRequest( new RequestFile { filename = file } );

				token.ThrowIfCancellationRequested();

				if ( response is not byte[] data || data.Length == 0 )
				{
					Log.Warning( $"Failed to download file {file}! (response: {response})" );
					currentCount++;
					downloadQueue.Remove( file );
					continue;
				}

				var fn = AssetDownloadCache.StoreFile( file, info.CRC, data );
				if ( fn is not null )
				{
					RedirectFileSystem.AddAbsFile( file, fn );
				}

				currentCount++;
				downloadQueue.Remove( file );
			}
		}

		LoadingScreen.Subtitle = null;
		Log.Info( $"Download Complete ({currentCount} files total) ({sw.Elapsed.TotalSeconds:0.00}s)" );
	}

	internal void NetworkInitialize( GameNetworkSystem instance )
	{
		instance.AddHandler<RequestFile>( OnRequestNetworkFile );
	}

	async Task OnRequestNetworkFile( RequestFile file, Connection connection, Guid msgGuid )
	{
		var normalizedFileName = NormalizeFileName( file.filename );
		var fs = fileSources.TryGetValue( normalizedFileName, out var source )
			? source
			: EngineFileSystem.Mounted;

		if ( !fs.FileExists( normalizedFileName ) )
		{
			Log.Warning( $"Client ({connection.Name}) requested missing file: {file.filename}" );
			connection.SendResponse( msgGuid, Array.Empty<byte>() );
			return;
		}

		var contents = await fs.ReadAllBytesAsync( normalizedFileName );

		connection.SendResponse( msgGuid, contents );
	}

	[Expose]
	public record struct RequestFile( string filename );

}
