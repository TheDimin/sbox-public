using System.Diagnostics;
using Sims4Reader;
using Sims4Reader.Resources;

namespace Sims4MountTest;

/// <summary>
/// Deep profiling of every phase of the Sims 4 mount pipeline.
/// Not a pass/fail test — outputs timing data for analysis.
/// </summary>
[TestClass]
public class MountProfileTest
{
	/// <summary>
	/// Profile a single package mount end-to-end, breaking down every operation.
	/// </summary>
	[TestMethod]
	public void Profile_SinglePackage_DetailedBreakdown()
	{
		var path = TestHelper.GetPackagePath();

		// Warm FS cache
		using ( DbpfPackage.Open( path ) ) { }

		var timings = new List<(string Phase, long Ms)>();

		// 1. Open package (index read)
		var sw = Stopwatch.StartNew();
		using var package = DbpfPackage.Open( path );
		timings.Add( ("DbpfPackage.Open (index read)", sw.ElapsedMilliseconds) );

		// 2. Count entries by type (just iteration, no parsing)
		sw.Restart();
		var typeCounts = new Dictionary<ResourceType, int>();
		foreach ( var entry in package.Entries )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			typeCounts.TryGetValue( entry.Key.Type, out var c );
			typeCounts[entry.Key.Type] = c + 1;
		}
		timings.Add( ("Entry iteration + type counting", sw.ElapsedMilliseconds) );

		// 3. FindAll for each mounted type
		sw.Restart();
		var cobjs = package.FindAll( ResourceType.CatalogObject ).ToList();
		timings.Add( ($"FindAll COBJ ({cobjs.Count})", sw.ElapsedMilliseconds) );

		sw.Restart();
		var objds = package.FindAll( ResourceType.ObjectDefinition ).ToList();
		timings.Add( ($"FindAll OBJD ({objds.Count})", sw.ElapsedMilliseconds) );

		sw.Restart();
		var modls = package.FindAll( ResourceType.Model ).Where( e => e.MemSize > 0 && e.FileSize > 0 ).ToList();
		timings.Add( ($"FindAll MODL ({modls.Count})", sw.ElapsedMilliseconds) );

		// 4. COBJ parsing (GetResource)
		sw.Restart();
		int cobjParsed = 0;
		foreach ( var entry in cobjs )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			try { package.GetResource<CatalogObjectResource>( entry ); cobjParsed++; } catch { }
		}
		timings.Add( ($"COBJ GetResource x{cobjParsed}", sw.ElapsedMilliseconds) );

		// 5. OBJD parsing (GetResource)
		sw.Restart();
		int objdParsed = 0;
		foreach ( var entry in objds )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			try { package.GetResource<ObjectDefinitionResource>( entry ); objdParsed++; } catch { }
		}
		timings.Add( ($"OBJD GetResource x{objdParsed}", sw.ElapsedMilliseconds) );

		// 6. String formatting for keyName (simulate the hot path in MountResources)
		sw.Restart();
		int formatted = 0;
		foreach ( var entry in package.Entries )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			var _ = $"{entry.Key.Group:X}_{entry.Key.Instance:X}";
			formatted++;
		}
		timings.Add( ($"String format keyName x{formatted}", sw.ElapsedMilliseconds) );

		// 7. Dictionary lookups (simulate modlMetadata + objdMetadata lookups)
		var fakeModlDict = new Dictionary<ResourceKey, int>();
		var fakeObjdDict = new Dictionary<ulong, int>();
		foreach ( var entry in modls ) fakeModlDict.TryAdd( entry.Key, 0 );
		foreach ( var entry in objds ) fakeObjdDict.TryAdd( entry.Key.Instance, 0 );

		sw.Restart();
		int lookups = 0;
		foreach ( var entry in package.Entries )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			fakeModlDict.TryGetValue( entry.Key, out _ );
			fakeObjdDict.TryGetValue( entry.Key.Instance, out _ );
			lookups++;
		}
		timings.Add( ($"Dict lookups x{lookups}", sw.ElapsedMilliseconds) );

		// 8. BuyCategoryTag.GetCategory cost (already parsed, just tag matching)
		sw.Restart();
		int tagChecks = 0;
		foreach ( var entry in cobjs )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			try
			{
				var cobj = package.GetResource<CatalogObjectResource>( entry );
				BuyCategoryTag.GetCategory( cobj.Tags );
				tagChecks++;
			}
			catch { }
		}
		timings.Add( ($"BuyCategoryTag.GetCategory x{tagChecks}", sw.ElapsedMilliseconds) );

		// 9. CleanObjectName cost
		sw.Restart();
		int cleaned = 0;
		foreach ( var entry in objds )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			try
			{
				var objd = package.GetResource<ObjectDefinitionResource>( entry );
				CleanObjectName( objd.Name );
				cleaned++;
			}
			catch { }
		}
		timings.Add( ($"CleanObjectName x{cleaned}", sw.ElapsedMilliseconds) );

		// Print results
		Console.WriteLine( $"=== Mount Profile: {System.IO.Path.GetFileName( path )} ===" );
		Console.WriteLine( $"Total entries: {package.Entries.Count} ({typeCounts.Count} types)" );
		Console.WriteLine();

		long total = 0;
		foreach ( var (phase, ms) in timings )
		{
			Console.WriteLine( $"  {ms,6}ms  {phase}" );
			total += ms;
		}
		Console.WriteLine( $"  ------" );
		Console.WriteLine( $"  {total,6}ms  TOTAL" );

		Console.WriteLine();
		Console.WriteLine( "Entry distribution (top 15):" );
		foreach ( var kv in typeCounts.OrderByDescending( kv => kv.Value ).Take( 15 ) )
			Console.WriteLine( $"  {kv.Key} (0x{(uint)kv.Key:X8}): {kv.Value}" );
	}

	/// <summary>
	/// Profile ALL packages to see per-package variance and find outliers.
	/// </summary>
	[TestMethod]
	public void Profile_AllPackages_PerPackageBreakdown()
	{
		var paths = TestHelper.GetAllPackagePaths();
		if ( paths.Count == 0 )
		{
			Assert.Inconclusive( "No packages found." );
			return;
		}

		// Warm FS
		foreach ( var p in paths )
			using ( DbpfPackage.Open( p ) ) { }

		Console.WriteLine( $"{"Package",-45} {"Open",6} {"Entries",8} {"COBJ",6} {"OBJD",6} {"MODL",6} {"COBJms",7} {"OBJDms",7} {"Total",7}" );
		Console.WriteLine( new string( '-', 110 ) );

		long grandOpen = 0, grandCobj = 0, grandObjd = 0;

		foreach ( var path in paths )
		{
			var sw = Stopwatch.StartNew();
			using var package = DbpfPackage.Open( path );
			var openMs = sw.ElapsedMilliseconds;

			var cobjEntries = package.FindAll( ResourceType.CatalogObject ).Where( e => e.MemSize > 0 && e.FileSize > 0 ).ToList();
			var objdEntries = package.FindAll( ResourceType.ObjectDefinition ).Where( e => e.MemSize > 0 && e.FileSize > 0 ).ToList();
			var modlCount = package.FindAll( ResourceType.Model ).Count( e => e.MemSize > 0 && e.FileSize > 0 );

			sw.Restart();
			foreach ( var e in cobjEntries )
				try { package.GetResource<CatalogObjectResource>( e ); } catch { }
			var cobjMs = sw.ElapsedMilliseconds;

			sw.Restart();
			foreach ( var e in objdEntries )
				try { package.GetResource<ObjectDefinitionResource>( e ); } catch { }
			var objdMs = sw.ElapsedMilliseconds;

			var totalMs = openMs + cobjMs + objdMs;
			var name = System.IO.Path.GetFileName( path );

			Console.WriteLine( $"{name,-45} {openMs,6} {package.Entries.Count,8} {cobjEntries.Count,6} {objdEntries.Count,6} {modlCount,6} {cobjMs,7} {objdMs,7} {totalMs,7}" );

			grandOpen += openMs;
			grandCobj += cobjMs;
			grandObjd += objdMs;
		}

		Console.WriteLine( new string( '-', 110 ) );
		Console.WriteLine( $"{"TOTAL",-45} {grandOpen,6} {"",8} {"",6} {"",6} {"",6} {grandCobj,7} {grandObjd,7} {grandOpen + grandCobj + grandObjd,7}" );
	}

	/// <summary>
	/// Profile GetResource to understand if the cost is decompression or parsing.
	/// Compares GetBytes (decompress only) vs GetResource (decompress + parse).
	/// </summary>
	[TestMethod]
	public void Profile_DecompressionVsParsing()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var cobjEntries = package.FindAll( ResourceType.CatalogObject )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 2000 ).ToList();

		var objdEntries = package.FindAll( ResourceType.ObjectDefinition )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 2000 ).ToList();

		// COBJ: decompress only
		var sw = Stopwatch.StartNew();
		long totalCobjBytes = 0;
		foreach ( var e in cobjEntries )
		{
			var bytes = package.GetBytes( e );
			totalCobjBytes += bytes.Length;
		}
		var cobjDecompMs = sw.ElapsedMilliseconds;

		// COBJ: decompress + parse
		sw.Restart();
		foreach ( var e in cobjEntries )
			try { package.GetResource<CatalogObjectResource>( e ); } catch { }
		var cobjParseMs = sw.ElapsedMilliseconds;

		// OBJD: decompress only
		sw.Restart();
		long totalObjdBytes = 0;
		foreach ( var e in objdEntries )
		{
			var bytes = package.GetBytes( e );
			totalObjdBytes += bytes.Length;
		}
		var objdDecompMs = sw.ElapsedMilliseconds;

		// OBJD: decompress + parse
		sw.Restart();
		foreach ( var e in objdEntries )
			try { package.GetResource<ObjectDefinitionResource>( e ); } catch { }
		var objdParseMs = sw.ElapsedMilliseconds;

		Console.WriteLine( "=== Decompression vs Parsing ===" );
		Console.WriteLine( $"COBJ ({cobjEntries.Count} entries, {totalCobjBytes / 1024}KB decompressed):" );
		Console.WriteLine( $"  GetBytes (decompress):      {cobjDecompMs}ms" );
		Console.WriteLine( $"  GetResource (decomp+parse): {cobjParseMs}ms" );
		Console.WriteLine( $"  Parse overhead:             {cobjParseMs - cobjDecompMs}ms ({(cobjParseMs > 0 ? (cobjParseMs - cobjDecompMs) * 100 / cobjParseMs : 0)}%)" );
		Console.WriteLine();
		Console.WriteLine( $"OBJD ({objdEntries.Count} entries, {totalObjdBytes / 1024}KB decompressed):" );
		Console.WriteLine( $"  GetBytes (decompress):      {objdDecompMs}ms" );
		Console.WriteLine( $"  GetResource (decomp+parse): {objdParseMs}ms" );
		Console.WriteLine( $"  Parse overhead:             {objdParseMs - objdDecompMs}ms ({(objdParseMs > 0 ? (objdParseMs - objdDecompMs) * 100 / objdParseMs : 0)}%)" );
	}

	/// <summary>
	/// Check if GetResource caches results or re-parses on every call.
	/// </summary>
	[TestMethod]
	public void Profile_GetResource_CachingBehavior()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var cobjEntries = package.FindAll( ResourceType.CatalogObject )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 1000 ).ToList();

		// First pass
		var sw = Stopwatch.StartNew();
		foreach ( var e in cobjEntries )
			try { package.GetResource<CatalogObjectResource>( e ); } catch { }
		var firstPass = sw.ElapsedMilliseconds;

		// Second pass (same entries)
		sw.Restart();
		foreach ( var e in cobjEntries )
			try { package.GetResource<CatalogObjectResource>( e ); } catch { }
		var secondPass = sw.ElapsedMilliseconds;

		// Third pass
		sw.Restart();
		foreach ( var e in cobjEntries )
			try { package.GetResource<CatalogObjectResource>( e ); } catch { }
		var thirdPass = sw.ElapsedMilliseconds;

		Console.WriteLine( $"GetResource<COBJ> x{cobjEntries.Count}:" );
		Console.WriteLine( $"  1st pass: {firstPass}ms" );
		Console.WriteLine( $"  2nd pass: {secondPass}ms" );
		Console.WriteLine( $"  3rd pass: {thirdPass}ms" );

		if ( secondPass < firstPass / 2 )
			Console.WriteLine( "  → CACHED (2nd pass much faster)" );
		else
			Console.WriteLine( "  → NOT CACHED (re-parses every time)" );
	}

	private static string? CleanObjectName( string? name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return null;
		if ( name.StartsWith( "object_", StringComparison.OrdinalIgnoreCase ) )
			name = name.Substring( 7 );
		name = name.Replace( ' ', '_' ).Replace( '\\', '_' ).Replace( '/', '_' );
		return string.IsNullOrWhiteSpace( name ) ? null : name;
	}
}
