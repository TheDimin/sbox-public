using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Engine.Shaders;

namespace EngineTests;

[TestClass]
public class ShaderCompilerWorkerPoolTest
{
	static readonly TimeSpan Timeout = TimeSpan.FromSeconds( 10 );

	[DllImport( "kernel32.dll" )]
	static extern uint GetCurrentThreadId();

	static long ThreadId() => OperatingSystem.IsWindows() ? GetCurrentThreadId() : Environment.CurrentManagedThreadId;

	[TestMethod]
	public void OptionsCannotExceedTheLifetimeThreadBound()
	{
		Assert.AreEqual( 2, ShaderCompilerWorkers.Concurrency( default, 32 ) );
		Assert.AreEqual( 1, ShaderCompilerWorkers.Concurrency( new() { SingleThreaded = true, MaxThreads = 32 }, 32 ) );
		Assert.AreEqual( 1, ShaderCompilerWorkers.Concurrency( new() { MaxThreads = 1 }, 32 ) );
		Assert.AreEqual( 2, ShaderCompilerWorkers.Concurrency( new() { MaxThreads = 32 }, 32 ) );
		Assert.AreEqual( 1, ShaderCompilerWorkers.Concurrency( default, 1 ) );
	}

	[TestMethod]
	public async Task RepeatedBatchesKeepTheSameDedicatedThreads()
	{
		using var pool = new ShaderCompilerWorkerPool( 2 );
		long[] first = null;
		for ( int batch = 0; batch < 20; batch++ )
		{
			using var barrier = new Barrier( 2 );
			var ids = new ConcurrentBag<long>();
			await pool.RunAsync( 2, 2, _ =>
			{
				Assert.IsFalse( Thread.CurrentThread.IsThreadPoolThread );
				ids.Add( ThreadId() );
				Assert.IsTrue( barrier.SignalAndWait( Timeout ) );
			} ).WaitAsync( Timeout );
			var current = ids.Order().ToArray();
			Assert.AreEqual( 2, current.Distinct().Count() );
			if ( first is null ) first = current;
			else CollectionAssert.AreEqual( first, current );
		}
	}

	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 100 )]
	public async Task BatchLimitAndGlobalLimitAreBothRespected( int limit )
	{
		using var pool = new ShaderCompilerWorkerPool( 2 );
		int active = 0, peak = 0, calls = 0;
		using var entered = new CountdownEvent( Math.Min( 2, limit ) );
		using var release = new ManualResetEventSlim();
		var task = pool.RunAsync( 20, limit, _ =>
		{
			var now = Interlocked.Increment( ref active );
			int old;
			do { old = Volatile.Read( ref peak ); }
			while ( now > old && Interlocked.CompareExchange( ref peak, now, old ) != old );
			if ( Interlocked.Increment( ref calls ) <= Math.Min( 2, limit ) ) entered.Signal();
			Assert.IsTrue( release.Wait( Timeout ) );
			Interlocked.Decrement( ref active );
		} );
		try { Assert.IsTrue( entered.Wait( Timeout ) ); }
		finally { release.Set(); }
		await task.WaitAsync( Timeout );
		Assert.AreEqual( Math.Min( 2, limit ), peak );
		Assert.AreEqual( 20, calls );
	}

	[TestMethod]
	public async Task ConcurrentSingleThreadedCallersShareTheGlobalPool()
	{
		using var pool = new ShaderCompilerWorkerPool( 2 );
		using var barrier = new Barrier( 2 );
		var ids = new ConcurrentBag<long>();
		var tasks = Enumerable.Range( 0, 2 ).Select( caller => pool.RunAsync( 30, 1, _ =>
		{
			ids.Add( ThreadId() );
			Assert.IsTrue( barrier.SignalAndWait( Timeout ) );
		} ) ).ToArray();
		await Task.WhenAll( tasks ).WaitAsync( Timeout );
		Assert.AreEqual( 60, ids.Count );
		Assert.AreEqual( 2, ids.Distinct().Count() );
	}

	[TestMethod]
	public async Task ExceptionStopsBatchButDoesNotReplaceWorker()
	{
		using var pool = new ShaderCompilerWorkerPool( 1 );
		long failedThread = 0, nextThread = 0;
		int calls = 0, cleanups = 0;
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => pool.RunAsync( 20, 1, _ =>
		{
			Interlocked.Increment( ref calls );
			failedThread = ThreadId();
			throw new InvalidOperationException( "compile failed" );
		}, cleanup: () => Interlocked.Increment( ref cleanups ) ) );
		await pool.RunAsync( 1, 1, _ => nextThread = ThreadId() ).WaitAsync( Timeout );
		Assert.AreEqual( 1, calls );
		Assert.AreEqual( 1, cleanups );
		Assert.AreEqual( failedThread, nextThread );
	}

	[TestMethod]
	public async Task CancellationWaitsForActiveCallBeforeContextCleanup()
	{
		using var pool = new ShaderCompilerWorkerPool( 1 );
		using var cancellation = new CancellationTokenSource();
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		int calls = 0, disposed = 0;
		var task = pool.RunAsync( 30, 1, _ =>
		{
			Interlocked.Increment( ref calls );
			entered.Set();
			Assert.IsTrue( release.Wait( Timeout ) );
			Assert.AreEqual( 0, Volatile.Read( ref disposed ) );
		}, cancellation.Token, () => Interlocked.Increment( ref disposed ) );
		try
		{
			Assert.IsTrue( entered.Wait( Timeout ) );
			cancellation.Cancel();
			Assert.IsFalse( task.IsCompleted );
			Assert.AreEqual( 0, disposed );
		}
		finally { release.Set(); }
		await Assert.ThrowsExceptionAsync<TaskCanceledException>( () => task.WaitAsync( Timeout ) );
		Assert.AreEqual( 1, calls );
		Assert.AreEqual( 1, disposed );
	}

	[TestMethod]
	public async Task QueuedCancellationCompletesWithoutEnteringCompiler()
	{
		using var pool = new ShaderCompilerWorkerPool( 1 );
		using var cancellation = new CancellationTokenSource();
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		var active = pool.RunAsync( 1, 1, _ => { entered.Set(); Assert.IsTrue( release.Wait( Timeout ) ); } );
		try
		{
			Assert.IsTrue( entered.Wait( Timeout ) );
			var queued = pool.RunAsync( 10, 1, _ => Assert.Fail( "Cancelled work ran." ), cancellation.Token );
			cancellation.Cancel();
			await Assert.ThrowsExceptionAsync<TaskCanceledException>( () => queued.WaitAsync( Timeout ) );
		}
		finally { release.Set(); }
		await active.WaitAsync( Timeout );
	}

	[TestMethod]
	public async Task PreCancelledAndEmptyBatchesReleaseContextWithoutWork()
	{
		using var pool = new ShaderCompilerWorkerPool( 1 );
		int disposed = 0;
		await Assert.ThrowsExceptionAsync<TaskCanceledException>( () => pool.RunAsync( 1, 1,
			_ => Assert.Fail(), new CancellationToken( true ), () => disposed++ ) );
		await pool.RunAsync( 0, 1, _ => Assert.Fail(), cleanup: () => disposed++ );
		Assert.AreEqual( 2, disposed );
	}

	[TestMethod]
	public async Task ShutdownRejectsQueueAndJoinsActiveCallBeforeContextCleanup()
	{
		using var pool = new ShaderCompilerWorkerPool( 1 );
		using var entered = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		int disposed = 0;
		var active = pool.RunAsync( 20, 1, _ =>
		{
			entered.Set();
			Assert.IsTrue( release.Wait( Timeout ) );
			Assert.AreEqual( 0, Volatile.Read( ref disposed ) );
		}, cleanup: () => Interlocked.Increment( ref disposed ) );
		Task shutdown = null;
		try
		{
			Assert.IsTrue( entered.Wait( Timeout ) );
			var queued = pool.RunAsync( 10, 1, _ => Assert.Fail( "Shutdown work ran." ) );
			shutdown = Task.Run( pool.Dispose );
			await Assert.ThrowsExceptionAsync<ObjectDisposedException>( () => queued.WaitAsync( Timeout ) );
			Assert.IsFalse( shutdown.IsCompleted );
			Assert.IsFalse( active.IsCompleted );
		}
		finally { release.Set(); }
		await shutdown.WaitAsync( Timeout );
		await Assert.ThrowsExceptionAsync<ObjectDisposedException>( () => active );
		Assert.AreEqual( 1, disposed );
		await Assert.ThrowsExceptionAsync<ObjectDisposedException>( () => pool.RunAsync( 1, 1, _ => Assert.Fail() ) );
	}

	[TestMethod]
	public async Task WorkersDoNotRetainTheCreatingCallersContext()
	{
		var callerState = new AsyncLocal<object> { Value = new object() };
		using var pool = new ShaderCompilerWorkerPool( 1 );
		await pool.RunAsync( 1, 1, _ => Assert.IsNull( callerState.Value ) ).WaitAsync( Timeout );
		Assert.IsNotNull( callerState.Value );
	}

	[TestMethod]
	public async Task FailureWaitsForOtherActiveCallsBeforeCleanup()
	{
		using var pool = new ShaderCompilerWorkerPool( 2 );
		using var entered = new CountdownEvent( 2 );
		using var failed = new ManualResetEventSlim();
		using var release = new ManualResetEventSlim();
		int disposed = 0;
		var task = pool.RunAsync( 2, 2, i =>
		{
			entered.Signal();
			Assert.IsTrue( entered.Wait( Timeout ) );
			if ( i == 0 )
			{
				failed.Set();
				throw new InvalidOperationException();
			}
			Assert.IsTrue( release.Wait( Timeout ) );
			Assert.AreEqual( 0, Volatile.Read( ref disposed ) );
		}, cleanup: () => Interlocked.Increment( ref disposed ) );
		try
		{
			Assert.IsTrue( failed.Wait( Timeout ) );
			Assert.IsFalse( task.IsCompleted );
		}
		finally { release.Set(); }
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => task.WaitAsync( Timeout ) );
		Assert.AreEqual( 1, disposed );
	}

	[TestMethod]
	public async Task CleanupFailureAndNestedSubmissionDoNotKillWorker()
	{
		using var pool = new ShaderCompilerWorkerPool( 1 );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => pool.RunAsync( 1, 1,
			_ => pool.RunAsync( 1, 1, _ => { } ).GetAwaiter().GetResult() ) );
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => pool.RunAsync( 1, 1,
			_ => { }, cleanup: () => throw new InvalidOperationException() ) );
		await pool.RunAsync( 1, 1, _ => { } ).WaitAsync( Timeout );
	}
}
