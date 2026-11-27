using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;

namespace GtaV;

/// <summary>One reader per mount. Share dictionary passes across placement, material and mip requests.</summary>
internal sealed class TextureRequests( MountArchive reader ) : IDisposable
{
	// The crossing requested 19.5 MiB including repeats. Keep only recently requested source packets.
	const int Capacity = 24 * 1024 * 1024;
	const int LifetimeMs = 5000;
	static readonly Meter Meter = new( "GtaV.TextureRequests" );
	static readonly Counter<long> Members = Meter.CreateCounter<long>( "gtav.texture.member.reads" );
	readonly object gate = new();
	readonly Dictionary<(uint File, uint Member), TaskCompletionSource<ArraySegment<byte>>> pending = new();
	readonly Dictionary<uint, Queue<uint>> queues = new();
	readonly Queue<uint> files = new();
	sealed record Cached( (uint File, uint Member) Key, ArraySegment<byte> Bytes, long Until );
	readonly LinkedList<Cached> lru = new();
	readonly Dictionary<byte[], int> owners = new();
	readonly Dictionary<(uint, uint), LinkedListNode<Cached>> cached = new();
	Timer expiry;
	int workers;
	bool disposed;
	int retained, peak;
	long measuredPasses;
	long hits, joined, reads, delivered, inflated;

	internal object Statistics
	{
		get { lock ( gate ) return new { hits, joined, reads, delivered, inflated, retained, peak }; }
	}

	internal Task<BinaryReader> Read( Entry entry )
	{
		lock ( gate )
		{
			var now = Environment.TickCount64;
			Trim( now );
			int newQueues = 0;
			var result = QueueRead( entry, now, ref newQueues );
			StartWorkers( newQueues );
			return result;
		}
	}

	internal void ReadMany( Queue<AssetLoader> textures, Queue<(AssetLoader, Task<BinaryReader>)> reads, bool fullResolution = false )
	{
		if ( textures.Count == 0 ) return;
		lock ( gate )
		{
			var now = Environment.TickCount64;
			Trim( now );
			int newQueues = 0;
			try
			{
				foreach ( var texture in textures )
				{
					if ( fullResolution ? texture.TextureReady : texture.HasStreamingTexture ) continue;
					var read = texture.ReadPreviewSource() ?? QueueRead( texture.SourceEntry, now, ref newQueues );
					reads.Enqueue( (texture, ReadWithFallback( texture, read )) );
				}
			}
			finally { StartWorkers( newQueues ); }
		}
	}

	static async Task<BinaryReader> ReadWithFallback( AssetLoader asset, Task<BinaryReader> read )
	{
		try { return await read.ConfigureAwait( false ); }
		catch ( InvalidDataException ) { return await Task.Run( asset.ReadPacket ).ConfigureAwait( false ); }
	}

	void StartWorkers( int newQueues )
	{
		for ( int i = 0; i < newQueues && workers < 3; i++ )
		{
			workers++;
			_ = Task.Run( Drain );
		}
	}

	Task<BinaryReader> QueueRead( Entry entry, long now, ref int newQueues )
	{
		if ( disposed ) return Task.FromException<BinaryReader>( new ObjectDisposedException( nameof( TextureRequests ) ) );
		var key = (entry.File, entry.Member);
		if ( cached.TryGetValue( key, out var node ) )
		{
			hits++;
			lru.Remove( node );
			node.Value = node.Value with { Until = now + LifetimeMs };
			lru.AddLast( node );
			return Task.FromResult( Open( node.Value.Bytes ) );
		}
		if ( pending.TryGetValue( key, out var completion ) ) joined++;
		else
		{
			pending.Add( key, completion = new( TaskCreationOptions.RunContinuationsAsynchronously ) );
			if ( !queues.TryGetValue( entry.File, out var queue ) )
			{
				queues.Add( entry.File, queue = new() );
				files.Enqueue( entry.File );
				newQueues++;
			}
			queue.Enqueue( entry.Member );
		}
		return OpenAfterRead( completion.Task );
	}

	static async Task<BinaryReader> OpenAfterRead( Task<ArraySegment<byte>> read )
		=> Open( await read.ConfigureAwait( false ) );

	static BinaryReader Open( ArraySegment<byte> data ) => new( new MemoryStream( data.Array, data.Offset, data.Count, false, true ) );

	void Drain()
	{
		while ( true )
		{
			uint file;
			uint[] members;
			lock ( gate )
			{
				if ( disposed || !files.TryDequeue( out file ) ) { workers--; return; }
				var queue = queues[file];
				members = new uint[Math.Min( 16, queue.Count )];
				for ( int i = 0; i < members.Length; i++ ) members[i] = queue.Dequeue();
				if ( queue.Count == 0 ) queues.Remove( file ); else files.Enqueue( file );
			}
			Load( file, members );
		}
	}

	void Load( uint file, uint[] members )
	{
		try
		{
			lock ( gate ) reads++;
			var batch = reader.ReadTextures( file, members, out var unpacked );
			lock ( gate ) inflated += unpacked;
			long pass = Members.Enabled ? Interlocked.Increment( ref measuredPasses ) : 0;
			foreach ( uint member in members )
			{
				// Keep slices of the original batch. Charge its backing allocation once until its last slice leaves.
				var bytes = batch[member];
				if ( pass != 0 )
				{
					var tags = new System.Diagnostics.TagList { { "file", file }, { "member", member }, { "pass", pass },
						{ "inflated", unpacked }, { "delivered", bytes.Count } };
					Members.Add( 1, tags );
				}
				lock ( gate )
				{
					if ( !pending.Remove( (file, member), out var completion ) ) continue;
					delivered += bytes.Count;
					if ( bytes.Array.Length <= Capacity )
					{
						var now = Environment.TickCount64;
						cached.Add( (file, member), lru.AddLast( new Cached( (file, member), bytes, now + LifetimeMs ) ) );
						int users = owners.GetValueOrDefault( bytes.Array );
						owners[bytes.Array] = users + 1;
						if ( users == 0 ) retained += bytes.Array.Length;
						Trim( now );
						peak = Math.Max( peak, retained );
						expiry ??= new Timer( Expire );
						expiry.Change( LifetimeMs, Timeout.Infinite );
					}
					completion.SetResult( bytes );
				}
			}
		}
		catch ( InvalidDataException ) when ( members.Length > 1 )
		{
			foreach ( uint member in members ) Load( file, [member] );
		}
		catch ( Exception ex )
		{
			lock ( gate ) foreach ( uint member in members )
				if ( pending.Remove( (file, member), out var completion ) ) completion.TrySetException( ex );
		}
	}

	void Expire( object state )
	{
		lock ( gate )
		{
			var now = Environment.TickCount64;
			Trim( now );
			if ( !disposed && lru.First is { } first ) expiry.Change( Math.Max( 1, first.Value.Until - now ), Timeout.Infinite );
		}
	}

	void Trim( long now )
	{
		while ( lru.First is { } first && (retained > Capacity || first.Value.Until <= now) ) Release( first.Value.Key );
	}

	internal void Release( (uint, uint) key )
	{
		lock ( gate )
		{
			if ( !cached.Remove( key, out var node ) ) return;
			lru.Remove( node );
			var array = node.Value.Bytes.Array;
			if ( --owners[array] == 0 )
			{
				owners.Remove( array );
				retained -= array.Length;
			}
		}
	}

	public void Dispose()
	{
		lock ( gate )
		{
			disposed = true;
			expiry?.Dispose();
			foreach ( var request in pending.Values ) request.TrySetCanceled();
			pending.Clear();
			files.Clear();
			queues.Clear();
			cached.Clear();
			owners.Clear();
			lru.Clear();
			retained = 0;
		}
	}
}
