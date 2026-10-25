using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Editor;

/// <summary>
/// Observes startup work without scheduling imports or touching editor/native objects from its timer.
/// </summary>
internal sealed class StartupImportProgress : IDisposable
{
	readonly object gate = new();
	readonly string stage;
	readonly int total;
	readonly Action<string> log;
	readonly Func<TimeSpan> now;
	readonly TimeSpan started;
	readonly Timer timer;
	readonly List<(string Path, TimeSpan Duration, string Result)> slowest = new();
	readonly Dictionary<string, int> outcomes = new();
	string path;
	TimeSpan itemStarted;
	int completed;
	int reportedFailures;
	bool disposed;

	internal StartupImportProgress( string stage, int total, Action<string> log, Func<TimeSpan> clock = null, bool heartbeat = true )
	{
		this.stage = stage;
		this.total = total;
		// Diagnostics must not abort an import or escape a ThreadPool timer callback.
		this.log = message =>
		{
			try { log( message ); }
			catch ( Exception ) { }
		};
		var watch = Stopwatch.StartNew();
		now = clock ?? (() => watch.Elapsed);
		started = now();
		this.log( $"{stage}: {total:N0} queued" );
		if ( heartbeat ) timer = new Timer( _ => ReportWaiting(), null, TimeSpan.FromSeconds( 10 ), TimeSpan.FromSeconds( 10 ) );
	}

	internal string Message
	{
		get
		{
			lock ( gate ) return Describe();
		}
	}

	string Describe() => path is null
		? $"{stage}: {completed:N0}/{total:N0} completed, {(now() - started).TotalSeconds:0}s elapsed"
		: $"{stage}: {completed:N0}/{total:N0} completed, {Math.Max( 0, total - completed - 1 ):N0} queued; {(now() - itemStarted).TotalSeconds:0}s on {path}";

	internal void Begin( string path, bool reportStart = false )
	{
		lock ( gate )
		{
			this.path = path;
			itemStarted = now();
			if ( reportStart ) log( Describe() );
		}
	}

	internal void Complete( string result )
	{
		lock ( gate )
		{
			var duration = now() - itemStarted;
			var failed = result is "failed" or "compile returned false";
			if ( failed ) reportedFailures++;
			if ( duration >= TimeSpan.FromSeconds( 2 ) || (failed && reportedFailures <= 10) )
				log( $"{stage}: {result} {path} in {duration.TotalSeconds:0.000}s" );
			else if ( failed && reportedFailures == 11 )
				log( $"{stage}: further fast unsuccessful returns will be counted in the summary" );
			slowest.Add( (path, duration, result) );
			slowest.Sort( ( a, b ) => b.Duration.CompareTo( a.Duration ) );
			if ( slowest.Count > 5 ) slowest.RemoveAt( 5 );
			completed++;
			outcomes[result] = outcomes.GetValueOrDefault( result ) + 1;
			path = null;
		}
	}

	internal void ReportWaiting()
	{
		lock ( gate )
		{
			if ( !disposed && path is not null ) log( $"{Describe()}; stage elapsed {(now() - started).TotalSeconds:0}s (waiting for import to return)" );
		}
	}

	public void Dispose()
	{
		lock ( gate )
		{
			if ( disposed ) return;
			disposed = true;
			timer?.Dispose();
			if ( path is not null ) log( $"{stage}: interrupted while importing {path} after {(now() - itemStarted).TotalSeconds:0.000}s" );
			log( $"{stage}: {completed:N0}/{total:N0} completed in {(now() - started).TotalSeconds:0.000}s" );
			foreach ( var outcome in outcomes ) log( $"{stage}: {outcome.Value:N0} {outcome.Key}" );
			foreach ( var item in slowest.Where( x => x.Duration >= TimeSpan.FromSeconds( 2 ) ) )
				log( $"{stage}: slow import {item.Path}: {item.Duration.TotalSeconds:0.000}s ({item.Result})" );
		}
	}
}
