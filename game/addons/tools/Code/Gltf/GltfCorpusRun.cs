using System.IO;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor.Gltf;

/// <summary>
/// In-editor runner for the pinned Khronos glTF corpus. It replaces the former Python Run driver.
/// Cases are captured in groups that share editor frames. An external shell watchdog, started here,
/// records a hang that the editor cannot detect itself (a synchronous Model.Load never returns).
/// No automatic visual approval, no material substitution, no historical-result relabelling.
/// </summary>
internal static class GltfCorpusRun
{
	const string Pin = "90d7ede14c7e280af263824604b427a1ca02cb66";
	const int CaseCount = 337;
	static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
	static bool running;
	static CancellationTokenSource cancel;

	static string Root => Path.GetFullPath( Path.Combine( Path.GetDirectoryName( typeof( Model ).Assembly.Location ), "../../.." ) );

	sealed class Options
	{
		public int Parallel = 8;
		public int Timeout = 180;
		public double ReserveGib = 6;
		public bool Resume;
		/// <summary>True: every variant. False (default): one primary variant per model, see <see cref="PrimaryVariants"/>.</summary>
		public bool AllVariants;
		public string Out;
		public HashSet<string> Models = new(), Cases = new();

		public static Options Parse( string text )
		{
			var options = new Options();
			foreach ( var token in (text ?? "").Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
			{
				var parts = token.TrimStart( '-' ).Split( '=', 2 );
				var key = parts[0]; var value = parts.Length > 1 ? parts[1] : "";
				switch ( key )
				{
					case "parallel": options.Parallel = Math.Clamp( int.Parse( value ), 1, 64 ); break;
					case "timeout": options.Timeout = Math.Max( 30, int.Parse( value ) ); break;
					case "reserve_gib": options.ReserveGib = Math.Max( 2, double.Parse( value, System.Globalization.CultureInfo.InvariantCulture ) ); break;
					case "resume": options.Resume = true; break;
					case "variants": options.AllVariants = value switch { "all" => true, "primary" => false, _ => throw new ArgumentException( "variants must be all or primary" ) }; break;
					case "out": options.Out = value; break;
					case "model": options.Models.UnionWith( value.Split( ',', StringSplitOptions.RemoveEmptyEntries ) ); break;
					case "case": options.Cases.UnionWith( value.Split( ',', StringSplitOptions.RemoveEmptyEntries ) ); break;
					default: throw new ArgumentException( "Unknown option: " + key );
				}
			}
			return options;
		}
	}

	/// <summary>
	/// gltf_corpus_run "parallel=8 model=Fox,BrainStem case=Box/glTF out=Temp/gltf-validation/exp resume timeout=180 reserve_gib=6 variants=primary|all"
	/// </summary>
	[ConCmd( "gltf_corpus_run" )]
	public static void Command( string options = "" )
	{
		if ( running ) { Log.Warning( "gltf_corpus_run: a run is already active. Use gltf_corpus_stop." ); return; }
		Options parsed;
		try { parsed = Options.Parse( options ); }
		catch ( Exception error ) { Log.Error( "gltf_corpus_run: " + error.Message ); return; }
		running = true; cancel = new CancellationTokenSource();
		_ = RunGuarded( parsed );
	}

	[ConCmd( "gltf_corpus_stop" )]
	public static void Stop() => cancel?.Cancel();

	static async Task RunGuarded( Options options )
	{
		try { await Run( options, cancel.Token ); }
		catch ( Exception error ) { Log.Error( "gltf_corpus_run stopped: " + error ); }
		finally { running = false; }
	}

	static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }

	static void AtomicJson( string path, JsonNode value )
	{
		Directory.CreateDirectory( Path.GetDirectoryName( path ) );
		var temporary = Path.Combine( Path.GetDirectoryName( path ), "." + Path.GetFileName( path ) + ".tmp" );
		File.WriteAllText( temporary, value.ToJsonString( Indented ) + "\n" );
		File.Move( temporary, path, true );
	}

	/// <summary>
	/// One variant per model: glTF-Binary, else another .glb variant, else plain glTF, else the first.
	/// The engine work in progress is not about container forms, so the other forms add run time only.
	/// </summary>
	static HashSet<string> PrimaryVariants( IEnumerable<JsonElement> entries )
	{
		static int Rank( JsonElement e )
		{
			var variant = e.GetProperty( "variant" ).GetString();
			var path = e.GetProperty( "path" ).GetString();
			if ( variant == "glTF-Binary" ) return 0;
			if ( path.EndsWith( ".glb", StringComparison.OrdinalIgnoreCase ) ) return 1;
			if ( variant == "glTF" ) return 2;
			return 3;
		}
		return entries.GroupBy( e => e.GetProperty( "model" ).GetString() )
			.Select( g => g.OrderBy( Rank ).ThenBy( e => e.GetProperty( "variant" ).GetString(), StringComparer.Ordinal ).First().GetProperty( "id" ).GetString() )
			.ToHashSet();
	}

	static string SafeCaseDirectory( string output, string id )
	{
		var parts = id.Split( '/' );
		Require( parts.Length == 2 && parts.All( p => p.Length > 0 && p is not ("." or "..") && !p.Contains( '\\' ) ), "Unsafe corpus case ID" );
		var path = Path.GetFullPath( Path.Combine( output, "assets", parts[0], parts[1] ) );
		Require( path.StartsWith( Path.GetFullPath( Path.Combine( output, "assets" ) ) + "/", StringComparison.Ordinal ), "Case output escaped the output directory" );
		return path;
	}

	static double MemAvailableGib()
	{
		foreach ( var line in File.ReadLines( "/proc/meminfo" ) )
			if ( line.StartsWith( "MemAvailable:" ) ) return long.Parse( line.Split( ' ', StringSplitOptions.RemoveEmptyEntries )[1] ) / (1024.0 * 1024.0);
		return double.PositiveInfinity;
	}

	static async Task SafePoint( CancellationToken token )
	{
		token.ThrowIfCancellationRequested();
		await MainThread.Wait();
		Require( Sandbox.Application.IsEditor && !Game.IsPlaying, "Public corpus capture requires an idle editor; human Play blocks the run." );
		Require( ConsoleSystem.GetValue( "r_managed_scene", "false" ).ToLowerInvariant() is "false" or "0", "Native capture requires the existing native renderer; no renderer toggle is permitted." );
		var wait = System.Diagnostics.Stopwatch.StartNew();
		while ( Graphics.IsActive )
		{
			token.ThrowIfCancellationRequested();
			Require( wait.Elapsed.TotalSeconds < 30, "Deadline waiting for a safe graphics boundary." );
			await Task.Delay( 5, token ); await MainThread.Wait();
		}
	}

	/// <summary>
	/// Wait three engine frames (shared by the whole group). With <paramref name="render"/>, every case
	/// renders once offscreen per frame. Nothing here needs a visible window, so a hidden or minimized
	/// editor no longer stops the run.
	/// </summary>
	static async Task WaitFrames( List<GltfCorpusCase> active, CancellationToken token, bool render, Action<Action<GltfCorpusCase>> each )
	{
		var wait = System.Diagnostics.Stopwatch.StartNew();
		for ( var frame = 0; frame < 3; frame++ )
		{
			await SafePoint( token );
			var start = Sandbox.Application.FrameCount;
			if ( render ) each( c => c.WarmRender() );
			while ( Sandbox.Application.FrameCount == start )
			{
				Require( wait.Elapsed.TotalSeconds < 30, "The editor engine loop stopped advancing frames." );
				await Task.Delay( 1, token ); await SafePoint( token );
			}
		}
	}

	static void Heartbeat( string output, double deadlineSeconds, string phase, IEnumerable<string> ids, bool done = false )
	{
		var deadline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (long)Math.Ceiling( deadlineSeconds );
		var path = Path.Combine( output, "heartbeat.txt" );
		File.WriteAllText( path + ".tmp", $"{(done ? 0 : deadline)}\n{phase}:{string.Join( ",", ids )}\n{(done ? "done" : "running")}\n" );
		File.Move( path + ".tmp", path, true );
	}

	static System.Diagnostics.Process StartWatchdog( string output )
	{
		var script = Path.Combine( Root, "engine/Tools/Gltf/watchdog/gltf-corpus-watchdog.sh" );
		var info = new System.Diagnostics.ProcessStartInfo( "/bin/sh" ) { UseShellExecute = false };
		info.ArgumentList.Add( script ); info.ArgumentList.Add( Environment.ProcessId.ToString() ); info.ArgumentList.Add( output );
		return System.Diagnostics.Process.Start( info );
	}

	static JsonObject BlankRow( JsonElement entry, JsonElement files )
	{
		var path = entry.GetProperty( "path" ).GetString();
		return new JsonObject
		{
			["id"] = entry.GetProperty( "id" ).GetString(),
			["recipeId"] = entry.GetProperty( "id" ).GetString(),
			["input"] = new JsonObject { ["path"] = path, ["sha256"] = files.GetProperty( path ).GetProperty( "sha256" ).GetString(), ["dependenciesVerified"] = false },
			["load"] = new JsonObject { ["status"] = "not-run", ["error"] = null },
			["render"] = new JsonObject { ["status"] = "not-run", ["error"] = null },
			["capture"] = null,
		};
	}

	static readonly string[] SameProducerKeys = { "binaries", "driverSha256", "deformShaderSha256", "stockResourceProofVersion", "stockResources", "stockDependencies" };
	static readonly string[] StockProofFields = { "stockResourceProofVersion", "stockResources", "stockDependencies", "stockDependencyErrors" };

	static bool SameProducer( JsonNode a, JsonNode b ) => a is not null && b is not null && SameProducerKeys.All( key => JsonNode.DeepEquals( a[key], b[key] ) );

	static JsonObject StockProof( JsonObject producer )
	{
		var proof = new JsonObject();
		foreach ( var key in StockProofFields ) proof[key] = producer[key]?.DeepClone();
		return proof;
	}

	static async Task Run( Options options, CancellationToken token )
	{
		var root = Root;
		var output = Path.GetFullPath( Path.Combine( root, options.Out ?? "Temp/gltf-validation/current" ) );
		Directory.CreateDirectory( output );
		using var runLock = new FileStream( Path.Combine( output, ".run.lock" ), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None );
		var manifestPath = Path.Combine( root, "research/gltf-validation/corpus-case-manifest.json" );
		var recipesPath = Path.Combine( root, "research/gltf-validation/reference-recipes.json" );
		var recipeBytes = await File.ReadAllBytesAsync( recipesPath, token );
		using var manifestDocument = JsonDocument.Parse( await File.ReadAllBytesAsync( manifestPath, token ) );
		using var recipeDocument = JsonDocument.Parse( recipeBytes );
		var manifest = manifestDocument.RootElement; var recipes = recipeDocument.RootElement;
		Require( manifest.GetProperty( "corpus" ).GetProperty( "commit" ).GetString() == Pin && recipes.GetProperty( "corpusPin" ).GetString() == Pin, "Corpus/recipe pin mismatch" );
		var entries = manifest.GetProperty( "cases" ).EnumerateArray().ToList();
		var files = manifest.GetProperty( "files" );
		var ids = entries.Select( e => e.GetProperty( "id" ).GetString() ).ToList();
		Require( ids.Count == CaseCount && ids.Distinct().Count() == CaseCount, "Expected exactly 337 unique corpus variants" );
		var recipeMap = recipes.GetProperty( "cases" ).EnumerateArray().ToDictionary( r => r.GetProperty( "id" ).GetString() );
		Require( recipeMap.Count == CaseCount && ids.All( recipeMap.ContainsKey ), "Recipes must cover exactly all 337 cases" );
		Require( options.Cases.All( ids.Contains ), "Unknown case ID" );
		Require( options.Models.All( m => entries.Any( e => e.GetProperty( "model" ).GetString() == m ) ), "Unknown model ID" );
		var primary = options.AllVariants ? null : PrimaryVariants( entries );
		var selected = entries.Where( e => options.Cases.Contains( e.GetProperty( "id" ).GetString() ) ||
				(options.Models.Count + options.Cases.Count == 0 || options.Models.Contains( e.GetProperty( "model" ).GetString() )) &&
				(primary is null || primary.Contains( e.GetProperty( "id" ).GetString() )) )
			.Select( e => e.GetProperty( "id" ).GetString() ).ToHashSet();
		var corpusRoot = Path.GetFullPath( recipes.GetProperty( "corpusRoot" ).GetString() );
		Require( await GitHead( corpusRoot ) == Pin, "Pinned Khronos checkout HEAD changed" );

		// Producer proof once at start and once at the end. Between cases a size/write-time stamp detects drift cheaply.
		var config = new GltfCorpusProof.Config();
		var driverFiles = Directory.GetFiles( Path.Combine( root, "game/addons/tools/Code/Gltf" ), "*.cs" );
		var watchdogPath = Path.Combine( root, "engine/Tools/Gltf/watchdog/gltf-corpus-watchdog.sh" );
		var guarded = GltfCorpusProof.Binaries.Select( b => Path.Combine( root, "game/bin/managed", b ) ).Concat( driverFiles ).Append( watchdogPath ).Append( recipesPath )
			.Concat( GltfCorpusProof.StockCompiled.Concat( config.StockSource ).Select( p => Path.Combine( root, p ) ) ).ToList();
		var stamp = GltfCorpusProof.Stamp( guarded );
		var producer = await Task.Run( () => GltfCorpusProof.DiskProducer( root, config, driverFiles, watchdogPath ), token );
		var recipesSha = GltfCorpusProof.Hash( recipeBytes );

		var runPath = Path.Combine( output, "run.json" );
		var previous = File.Exists( runPath ) ? JsonNode.Parse( await File.ReadAllTextAsync( runPath, token ) ) : null;
		var hung = ReadWatchdog( output );
		var previousRows = previous?["cases"]?.AsArray().OfType<JsonObject>().ToDictionary( r => r["id"].GetValue<string>() ) ?? new();
		var sameRender = previous is not null && previous["corpusPin"]?.GetValue<string>() == Pin && SameProducer( previous["run"]?["producer"], producer );
		var rows = new JsonArray();
		var queue = new List<(JsonElement Entry, JsonObject Row)>();
		foreach ( var entry in entries )
		{
			var id = entry.GetProperty( "id" ).GetString();
			var recipeSha = GltfCorpusProof.RecipeFingerprint( recipeMap[id] );
			var row = BlankRow( entry, files );
			var reuse = previous is not null && (options.Resume || !selected.Contains( id ));
			if ( reuse && previousRows.TryGetValue( id, out var prior ) )
			{
				string reason = null;
				if ( !sameRender ) reason = "Rendering inputs or stock resource proof changed or unavailable; rerun required";
				else if ( prior["recipeSha256"]?.GetValue<string>() != recipeSha ) reason = "Requested comparison recipe changed; rerun required";
				else if ( prior["load"]?["status"]?.GetValue<string>() is not ("passed" or "failed") || (prior["cleanupConfirmed"]?.GetValue<bool>() != true && prior["stopReason"]?.GetValue<string>() != "watchdog") ) reason = "Previous case is unfinished; rerun required";
				if ( reason is null ) row = (JsonObject)prior.DeepClone();
				else if ( !selected.Contains( id ) ) { row["load"] = new JsonObject { ["status"] = "blocked", ["error"] = reason }; row["render"] = new JsonObject { ["status"] = "blocked", ["error"] = reason }; row["reason"] = reason; }
				if ( reason is null && options.Resume ) selected.Remove( id );
			}
			else if ( !selected.Contains( id ) && previous is not null )
			{
				row["load"] = new JsonObject { ["status"] = "blocked", ["error"] = "Not selected and no reusable previous result" };
				row["render"] = new JsonObject { ["status"] = "blocked", ["error"] = "Not selected and no reusable previous result" };
			}
			if ( options.Resume && hung.Contains( id ) && selected.Contains( id ) )
			{
				// The watchdog saw this case stop the editor. Record it and do not dispatch it again in this run.
				row["load"] = new JsonObject { ["status"] = "failed", ["error"] = "Watchdog: editor hung or exited during this case" };
				row["render"] = new JsonObject { ["status"] = "failed", ["error"] = "Watchdog: editor hung or exited during this case" };
				row["stopReason"] = "watchdog"; row["cleanupConfirmed"] = false;
				selected.Remove( id );
			}
			row["recipeSnapshot"] = JsonNode.Parse( recipeMap[id].GetRawText() );
			row["recipeSha256"] = recipeSha;
			rows.Add( row );
			if ( selected.Contains( id ) ) queue.Add( (entry, row) );
		}

		var state = new JsonObject
		{
			["schemaVersion"] = 1,
			["corpusPin"] = Pin,
			["run"] = new JsonObject
			{
				["id"] = Guid.NewGuid().ToString( "N" ),
				["status"] = "running",
				["startedAt"] = DateTimeOffset.UtcNow.ToString( "O" ),
				["completedAt"] = null,
				["producer"] = producer,
				["recipesSha256"] = recipesSha,
				["recipeProofVersion"] = 1,
				["editor"] = new JsonObject { ["pid"] = Environment.ProcessId },
				["runner"] = "gltf_corpus_run (C#)",
				["parallel"] = options.Parallel,
				["requested"] = queue.Count,
				["selectedCaseIds"] = new JsonArray( queue.Select( q => (JsonNode)q.Row["id"].GetValue<string>() ).OrderBy( x => x.GetValue<string>(), StringComparer.Ordinal ).ToArray() ),
				["attempted"] = 0,
				["recoveredWatchdog"] = new JsonArray( hung.Select( h => (JsonNode)h ).ToArray() ),
				["pendingCases"] = new JsonArray(),
				["error"] = null,
			},
			["cases"] = rows,
		};
		var run = state["run"].AsObject();
		await File.WriteAllBytesAsync( Path.Combine( output, "recipes.json" ), recipeBytes, token );
		File.Delete( Path.Combine( output, "watchdog.json" ) );
		AtomicJson( runPath, state );
		Log.Info( $"gltf_corpus_run: {queue.Count} cases, parallel={options.Parallel}, output={output}" );

		Heartbeat( output, options.Timeout + 30, "start", Array.Empty<string>() );
		var watchdog = StartWatchdog( output );
		var wall = System.Diagnostics.Stopwatch.StartNew();
		var active = new List<(GltfCorpusCase Case, JsonObject Row, JsonElement Entry)>();
		var next = 0;
		var lastWrite = System.Diagnostics.Stopwatch.StartNew();

		void Save()
		{
			run["updatedAt"] = DateTimeOffset.UtcNow.ToString( "O" );
			run["pendingCases"] = new JsonArray( active.Select( a => (JsonNode)a.Case.Id ).ToArray() );
			AtomicJson( runPath, state );
			lastWrite.Restart();
		}

		async Task Finish( GltfCorpusCase item, JsonObject row, JsonElement entry )
		{
			try { await item.FlushWrites(); } catch ( Exception error ) { item.Fail( error ); }
			await MainThread.Wait();
			item.Cleanup();
			var result = JsonSerializer.SerializeToNode( item.Result() ).AsObject();
			result["input"] = row["input"]?.DeepClone();
			result["recipeId"] = item.Id;
			result["execution"] = new JsonObject { ["runnerSha256"] = producer["runnerSha256"]?.DeepClone(), ["driverSha256"] = producer["driverSha256"]?.DeepClone(), ["parallel"] = options.Parallel };
			if ( item.RenderStatus == "passed" ) result["stockResourceProof"] = StockProof( producer );
			if ( item.RenderStatus == "passed" && item.Animated && item.PresentationDurations.Count == 0 )
			{
				// Multi-time capture without an APNG recording: the frame-0 image is the capture (former Run behaviour).
				try { Directory.Delete( Path.Combine( item.OutputDir, "frames" ), true ); } catch ( DirectoryNotFoundException ) { }
			}
			else if ( item.RenderStatus == "passed" && item.Animated )
			{
				try { await Task.Run( () => EncodeMotion( item, result ), token ); }
				catch ( Exception error ) { item.Fail( error ); result["render"] = JsonSerializer.SerializeToNode( item.Result()["render"] ); }
			}
			if ( item.RenderStatus != "passed" ) result["capture"] = null;
			foreach ( var pair in result.ToList() ) { result.Remove( pair.Key ); row[pair.Key] = pair.Value; }
			if ( item.Producers.Count > 0 )
			{
				var runtime = JsonSerializer.SerializeToNode( item.Producers );
				if ( producer["runtime"] is null ) producer["runtime"] = runtime;
				else if ( !JsonNode.DeepEquals( producer["runtime"], runtime ) ) throw new InvalidOperationException( "Loaded producer changed during run" );
			}
			AtomicJson( Path.Combine( item.OutputDir, "result.json" ), row );
			run["attempted"] = run["attempted"].GetValue<int>() + 1;
			Log.Info( $"gltf_corpus_run [{run["attempted"]}/{queue.Count}] {item.Id}: load={item.LoadStatus} render={item.RenderStatus} {item.Timer.Elapsed.TotalSeconds:F1}s ({wall.Elapsed.TotalSeconds:F0}s total)" );
			if ( item.CleanupErrors.Count > 0 ) throw new InvalidOperationException( "Case cleanup failed; remaining cases will not be dispatched: " + item.Id );
		}

		try
		{
			while ( next < queue.Count || active.Count > 0 )
			{
				// Fill the group. A load blocks the main thread, so other cases' timers pause during it.
				while ( next < queue.Count && active.Count < options.Parallel && (active.Count == 0 || MemAvailableGib() > options.ReserveGib) )
				{
					var (entry, row) = queue[next++];
					var id = entry.GetProperty( "id" ).GetString();
					Require( GltfCorpusProof.Stamp( guarded ) == stamp, "Installed binary, driver, recipes or stock resource changed; stop before another case" );
					var recipe = recipeMap[id];
					var frames = recipe.TryGetProperty( "animation", out var an ) && an.TryGetProperty( "recording", out var rec ) && rec.TryGetProperty( "frameCount", out var fc ) ? fc.GetInt32() : 1;
					var timeout = Math.Min( 1800, Math.Max( options.Timeout, Math.Ceiling( frames * .3 * Math.Max( 1, options.Parallel / 2.0 ) + 30 ) ) );
					var item = new GltfCorpusCase( id, corpusRoot, entry.GetProperty( "path" ).GetString(), SafeCaseDirectory( output, id ), recipe, timeout );
					foreach ( var key in row.Select( p => p.Key ).Where( k => k is not ("id" or "recipeId" or "input" or "recipeSnapshot" or "recipeSha256") ).ToList() ) row.Remove( key );
					row["load"] = new JsonObject { ["status"] = "not-run", ["error"] = null }; row["render"] = new JsonObject { ["status"] = "not-run", ["error"] = null }; row["capture"] = null;
					try
					{
						var deps = entry.TryGetProperty( "dependencies", out var d ) ? d.EnumerateArray().Select( x => x.GetString() ).ToList() : new List<string>();
						row["input"] = await Task.Run( () => GltfCorpusProof.VerifyInputs( corpusRoot, files, entry.GetProperty( "path" ).GetString(), deps ), token );
					}
					catch ( Exception error ) when ( error is not OperationCanceledException )
					{
						item.Fail( error ); await Finish( item, row, entry ); continue;
					}
					await SafePoint( token );
					foreach ( var other in active ) other.Case.Timer.Stop();
					Heartbeat( output, timeout + 30, "load", new[] { id } );
					try { item.Prepare(); active.Add( (item, row, entry) ); }
					catch ( Exception error ) { item.Fail( error ); await Finish( item, row, entry ); }
					foreach ( var other in active ) if ( other.Case != item ) other.Case.Timer.Start();
				}

				if ( active.Count == 0 ) continue;
				// One shared frame step for the whole group.
				var group = active.Select( a => a.Case ).ToList();
				Heartbeat( output, group.Max( c => c.TimeoutSeconds - c.Timer.Elapsed.TotalSeconds ) + 30, "frames", group.Select( c => c.Id ) );
				var failed = new Dictionary<GltfCorpusCase, Exception>();
				void Each( Action<GltfCorpusCase> step )
				{
					foreach ( var c in group )
					{
						if ( failed.ContainsKey( c ) ) continue;
						try { c.ThrowIfExpired(); step( c ); } catch ( Exception error ) { failed[c] = error; }
					}
				}
				await SafePoint( token );
				Each( c => c.Seek() );
				await WaitFrames( group, token, true, Each );
				Each( c => c.Submit() );
				await WaitFrames( group, token, false, Each );
				Each( c => c.Read() );

				foreach ( var a in active.ToList() )
				{
					if ( failed.TryGetValue( a.Case, out var error ) ) a.Case.Fail( error );
					else if ( a.Case.Finished ) a.Case.Pass();
					else continue;
					active.Remove( a );
					await Finish( a.Case, a.Row, a.Entry );
				}
				if ( lastWrite.Elapsed.TotalSeconds > 10 ) Save();
			}

			var final = await Task.Run( () => GltfCorpusProof.DiskProducer( root, config, driverFiles, watchdogPath ), token );
			Require( SameProducer( final, producer ) && final["runnerSha256"]?.GetValue<string>() == producer["runnerSha256"]?.GetValue<string>(), "Producer proof changed during the run; results are not valid" );
			run["status"] = "complete";
		}
		catch ( Exception error )
		{
			run["status"] = "interrupted"; run["error"] = $"{error.GetType().Name}: {error.Message}";
			foreach ( var a in active.ToList() )
			{
				a.Case.Fail( error ); active.Remove( a );
				try { await Finish( a.Case, a.Row, a.Entry ); } catch ( Exception cleanup ) { Log.Error( cleanup.Message ); }
			}
			for ( ; next < queue.Count; next++ )
			{
				queue[next].Row["load"] = new JsonObject { ["status"] = "blocked", ["error"] = "Batch stopped before dispatch: " + run["error"] };
				queue[next].Row["render"] = new JsonObject { ["status"] = "blocked", ["error"] = run["error"]?.GetValue<string>() };
			}
			throw;
		}
		finally
		{
			run["completedAt"] = DateTimeOffset.UtcNow.ToString( "O" );
			run["wallSeconds"] = wall.Elapsed.TotalSeconds;
			Save();
			Heartbeat( output, 0, "done", Array.Empty<string>(), done: true );
			try { if ( !watchdog.WaitForExit( 10000 ) ) watchdog.Kill(); } catch { }
			var cases = rows.OfType<JsonObject>().ToList();
			Log.Info( $"gltf_corpus_run {run["status"]}: attempted={run["attempted"]} loadPassed={cases.Count( r => r["load"]?["status"]?.GetValue<string>() == "passed" )} renderCaptured={cases.Count( r => r["render"]?["status"]?.GetValue<string>() == "passed" )} wall={wall.Elapsed.TotalSeconds:F0}s. Visual approval: user review only." );
		}
	}

	static void EncodeMotion( GltfCorpusCase item, JsonObject result )
	{
		var frames = item.AnimationFrames;
		Require( frames.Count == item.Times.Count && item.PresentationDurations.Count == frames.Count, "Every planned animation sample needs an actual captured frame" );
		var destination = Path.Combine( item.OutputDir, "capture.apng" );
		var duration = GltfApng.Write( frames.Select( f => (string)f["path"] ).ToList(), frames.Select( f => (string)f["sha256"] ).ToList(), item.PresentationDurations, item.Loop, destination );
		var poster = Path.Combine( item.OutputDir, "poster.png" );
		File.Copy( (string)frames[0]["path"], poster, true );
		result["capture"] = new JsonObject
		{
			["path"] = destination,
			["sha256"] = GltfCorpusProof.HashFile( destination ),
			["kind"] = "apng",
			["posterPath"] = poster,
			["posterSha256"] = GltfCorpusProof.HashFile( poster ),
			["frames"] = frames.Count,
			["duration"] = item.PresentationDurations.Sum(),
			["sampled"] = true,
			["description"] = "Actual public component samples at the declared recipe times; user judges visual matching.",
		};
		result["animationEncoding"] = new JsonObject { ["frames"] = frames.Count, ["duration"] = duration, ["lossless"] = true };
		File.Delete( Path.Combine( item.OutputDir, "capture.png" ) );
		Directory.Delete( Path.Combine( item.OutputDir, "frames" ), true );
	}

	static HashSet<string> ReadWatchdog( string output )
	{
		var path = Path.Combine( output, "watchdog.json" );
		if ( !File.Exists( path ) ) return new();
		var report = JsonNode.Parse( File.ReadAllText( path ) );
		var text = report?["cases"]?.GetValue<string>() ?? "";
		var colon = text.IndexOf( ':' );
		var ids = colon >= 0 ? text[(colon + 1)..] : text;
		Log.Warning( $"gltf_corpus_run: previous run watchdog reported {report?["status"]} during {text}" );
		return ids.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).ToHashSet();
	}

	static async Task<string> GitHead( string repository )
	{
		var info = new System.Diagnostics.ProcessStartInfo( "git" ) { RedirectStandardOutput = true, UseShellExecute = false };
		info.ArgumentList.Add( "-C" ); info.ArgumentList.Add( repository ); info.ArgumentList.Add( "rev-parse" ); info.ArgumentList.Add( "HEAD" );
		using var process = System.Diagnostics.Process.Start( info );
		var head = (await process.StandardOutput.ReadToEndAsync()).Trim();
		await process.WaitForExitAsync();
		return head;
	}
}
