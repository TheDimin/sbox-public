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

	public LargeNetworkFiles( string name )
	{
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
	public bool AddFile( string fileName, Action<long, TimeSpan> onCrc = null, Action<TimeSpan> onTableSet = null )
		=> AddFile( EngineFileSystem.Mounted, fileName, onCrc, onTableSet );

	/// <summary>
	/// Add a file from a specific filesystem to be networked.
	/// </summary>
	public bool AddFile( BaseFileSystem fs, string fileName, Action<long, TimeSpan> onCrc = null, Action<TimeSpan> onTableSet = null )
	{
		if ( !fs.FileExists( fileName ) )
			return false;

		var crcTimer = System.Diagnostics.Stopwatch.StartNew();
		var crc = fs.GetCrc( fileName );
		crcTimer.Stop();
		var size = fs.FileSize( fileName );
		onCrc?.Invoke( size, crcTimer.Elapsed );
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
		// Can we find this file somewhere, or do we need to download it?

		if ( EngineFileSystem.Mounted.FileExists( fileName ) )
		{
			var size = EngineFileSystem.Mounted.FileSize( fileName );
			if ( size == contents.Size )
			{
				var crc = EngineFileSystem.Mounted.GetCrc( fileName );
				if ( crc == contents.CRC )
				{
					if ( AssetDownloadCache.DebugNetworkFiles )
					{
						Log.Info( $"Skipping downloading {fileName} - we already have it" );
					}
					return;
				}
			}
		}

		if ( !AssetDownloadCache.IsLegalDownload( fileName ) )
			return;

		if ( AssetDownloadCache.TryMount( RedirectFileSystem, fileName, contents.CRC ) )
			return;

		if ( AssetDownloadCache.DebugNetworkFiles )
		{
			Log.Info( $"Queued Network File: {fileName} / {contents.Size} / {contents.CRC}" );
		}
		downloadQueue.Add( fileName );
	}

	public async Task RunDownloadQueue( NetworkSystem system, CancellationToken token )
	{
		if ( RedirectFileSystem is null )
			return;

		Assert.NotNull( Connection.Host );

		Log.Info( $"Downloading {downloadQueue.Count} files.." );
		var currentCount = 0;
		var sw = System.Diagnostics.Stopwatch.StartNew();

		foreach ( var file in downloadQueue )
		{
			if ( !StringTable.Entries.TryGetValue( file, out var entry ) )
				continue;

			var info = entry.Read<LargeFileInfo>();

			if ( AssetDownloadCache.DebugNetworkFiles )
			{
				Log.Info( $"Download file {file}" );
			}

			LoadingScreen.Title = $"Downloading Files ({currentCount + 1}/{downloadQueue.Count})";
			LoadingScreen.Subtitle = file;

			if ( RedirectFileSystem.FileExists( file.NormalizeFilename( true ) ) )
			{
				currentCount++;
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
				continue;
			}

			var fn = AssetDownloadCache.StoreFile( file, info.CRC, data );
			if ( fn is not null )
			{
				RedirectFileSystem.AddAbsFile( file, fn );
			}

			currentCount++;
		}

		LoadingScreen.Subtitle = null;
		Log.Info( $"Download Complete ({currentCount} files total) ({sw.Elapsed.TotalSeconds:0.00}s)" );

		downloadQueue.Clear();
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
