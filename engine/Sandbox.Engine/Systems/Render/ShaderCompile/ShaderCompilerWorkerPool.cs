using System.Threading;

namespace Sandbox.Engine.Shaders;

/// <summary>
/// Runs synchronous compiler calls on a fixed set of OS threads. Native Slang sessions are cached
/// by thread ID, so these threads must not be replaced between shaders or game sessions.
/// </summary>
internal sealed class ShaderCompilerWorkerPool : IDisposable
{
	readonly object gate = new();
	readonly List<Batch> batches = new();
	readonly Thread[] workers;
	bool stopped;

	sealed class Batch
	{
		internal Action<int> Compile;
		internal Action Cleanup;
		internal CancellationToken Token;
		internal int Count, Limit, Next, Active;
		internal Exception Error;
		internal readonly TaskCompletionSource Completion = new( TaskCreationOptions.RunContinuationsAsynchronously );
	}

	internal int WorkerCount => workers.Length;
	internal bool IsWorker => workers.Contains( Thread.CurrentThread );

	internal ShaderCompilerWorkerPool( int workerCount )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( workerCount, 1 );
		workers = new Thread[workerCount];
		try
		{
			for ( int i = 0; i < workers.Length; i++ )
			{
				workers[i] = new Thread( Work ) { IsBackground = true, Name = $"Shader compiler {i + 1}" };
				// Do not retain the first caller's scene or other AsyncLocal state for engine lifetime.
				workers[i].UnsafeStart();
			}
		}
		catch
		{
			lock ( gate )
			{
				stopped = true;
				Monitor.PulseAll( gate );
			}
			foreach ( var worker in workers )
				if ( worker?.IsAlive == true ) worker.Join();
			throw;
		}
	}

	/// <summary>
	/// Completion includes all in-flight calls and cleanup, even on cancellation or failure.
	/// Cleanup releases the native context before shutdown can return. It must not call this pool.
	/// </summary>
	internal async Task RunAsync( int count, int maxConcurrency, Action<int> compile, CancellationToken token = default, Action cleanup = null )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( count );
		ArgumentOutOfRangeException.ThrowIfLessThan( maxConcurrency, 1 );
		ArgumentNullException.ThrowIfNull( compile );
		if ( IsWorker ) throw new InvalidOperationException( "Compiler workers cannot submit nested batches." );

		var batch = new Batch { Count = count, Limit = Math.Min( maxConcurrency, WorkerCount ), Compile = compile, Cleanup = cleanup, Token = token };
		lock ( gate )
		{
			if ( stopped ) batch.Error = new ObjectDisposedException( nameof( ShaderCompilerWorkerPool ) );
			batches.Add( batch );
			Finish( batch );
			Monitor.PulseAll( gate );
		}

		using var registration = token.Register( () =>
		{
			lock ( gate )
			{
				Finish( batch );
				Monitor.PulseAll( gate );
			}
		} );
		await batch.Completion.Task.ConfigureAwait( false );
	}

	// gate is held. Never complete a task while a delegate still uses its context.
	void Finish( Batch batch )
	{
		if ( batch.Active != 0 || (batch.Next < batch.Count && batch.Error is null && !batch.Token.IsCancellationRequested) ) return;
		if ( !batches.Remove( batch ) ) return;
		try { batch.Cleanup?.Invoke(); }
		catch ( Exception e ) { batch.Error ??= e; }
		batch.Compile = null;
		batch.Cleanup = null;
		if ( batch.Error is not null ) batch.Completion.SetException( batch.Error );
		else if ( batch.Token.IsCancellationRequested ) batch.Completion.SetCanceled( batch.Token );
		else batch.Completion.SetResult();
	}

	void Work()
	{
		while ( true )
		{
			Batch batch;
			int index;
			lock ( gate )
			{
				while ( true )
				{
					if ( stopped ) return;
					batch = batches.FirstOrDefault( x => x.Error is null && !x.Token.IsCancellationRequested && x.Next < x.Count && x.Active < x.Limit );
					if ( batch is not null ) break;
					Monitor.Wait( gate );
				}
				index = batch.Next++;
				batch.Active++;
				// Rotate batches so a large shader cannot starve another caller.
				batches.Remove( batch );
				batches.Add( batch );
			}

			Exception error = null;
			try
			{
				// Cancellation cannot interrupt a native call. Check immediately before entry.
				if ( !batch.Token.IsCancellationRequested ) batch.Compile( index );
			}
			catch ( Exception e ) { error = e; }

			lock ( gate )
			{
				batch.Error ??= error;
				batch.Active--;
				Finish( batch );
				Monitor.PulseAll( gate );
			}
		}
	}

	public void Dispose()
	{
		if ( IsWorker ) throw new InvalidOperationException( "Compiler workers cannot stop their own pool." );
		lock ( gate )
		{
			stopped = true;
			foreach ( var batch in batches.ToArray() )
			{
				batch.Error ??= new ObjectDisposedException( nameof( ShaderCompilerWorkerPool ) );
				Finish( batch );
			}
			Monitor.PulseAll( gate );
		}
		foreach ( var worker in workers ) worker.Join();
	}
}

/// <summary>One pool per engine process. Shutdown is terminal and does not load the native compiler.</summary>
internal static class ShaderCompilerWorkers
{
	internal const int MaximumWorkers = 2;
	internal const long MemoryPerWorker = 768L * 1024 * 1024;
	static readonly object gate = new();
	static ShaderCompilerWorkerPool pool;
	static bool stopped;

	internal static int Concurrency( ShaderCompileOptions options, int processors, long? availableMemory )
	{
		if ( options.SingleThreaded ) return 1;
		var count = Math.Min( MaximumWorkers, processors );
		if ( options.MaxThreads > 0 ) count = Math.Min( count, options.MaxThreads );
		if ( availableMemory is long available ) count = (int)Math.Min( count, available / MemoryPerWorker );
		return Math.Max( 1, count );
	}

	internal static ShaderCompilerWorkerPool Pool
	{
		get
		{
			lock ( gate )
			{
				ObjectDisposedException.ThrowIf( stopped, typeof( ShaderCompilerWorkers ) );
				return pool ??= new ShaderCompilerWorkerPool( Math.Min( MaximumWorkers, Environment.ProcessorCount ) );
			}
		}
	}

	internal static void Shutdown()
	{
		ShaderCompilerWorkerPool current;
		lock ( gate )
		{
			stopped = true;
			current = pool;
		}
		current?.Dispose();
	}
}
