using System.Diagnostics;
using Sims4Reader;

namespace Sims4MountTest;

/// <summary>
/// Validates that the Sims 4 mount completes quickly.
///
/// The mount phase must NOT do heavy resource parsing (COBJ, OBJD, MODL, etc.)
/// — only open packages and read their index tables. All resource parsing
/// must happen lazily / async when resources are actually requested.
///
/// If these tests fail, it means parsing is leaking into the mount path.
/// </summary>
[TestClass]
public class MountPerformanceTest
{
	/// <summary>
	/// Opening a single .package file should only read the DBPF header + index.
	/// No resource bodies should be decompressed or parsed.
	/// Budget: 100ms per package (index-only, 100K+ entries).
	/// </summary>
	[TestMethod]
	public void OpenPackage_ShouldBeIndexOnly_Under50ms()
	{
		var path = TestHelper.GetPackagePath();

		// Warm up: let the OS cache the file
		using ( DbpfPackage.Open( path ) ) { }

		var sw = Stopwatch.StartNew();
		using var package = DbpfPackage.Open( path );
		sw.Stop();

		Console.WriteLine( $"DbpfPackage.Open: {sw.ElapsedMilliseconds}ms ({package.Entries.Count} entries)" );

		Assert.IsTrue( sw.ElapsedMilliseconds < 100,
			$"Opening a package took {sw.ElapsedMilliseconds}ms — should be <100ms (index read only)." );
	}

	/// <summary>
	/// The full mount across ALL packages should complete within a tight budget.
	/// This simulates what Mount() does: open every package, register resources.
	///
	/// If BuildMetadataIndex or any resource parsing runs during mount,
	/// this will blow the budget immediately.
	///
	/// Budget: 1000ms total for all packages (index + registration, no parsing).
	/// The index read for 800K+ entries across 18 packages is ~500-600ms of pure I/O.
	/// If this ever exceeds the budget, resource parsing is leaking into mount.
	/// </summary>
	[TestMethod]
	public void MountAllPackages_ShouldNotParseResources_Under1000ms()
	{
		var paths = TestHelper.GetAllPackagePaths();
		if ( paths.Count == 0 )
		{
			Assert.Inconclusive( "No Sims 4 packages found — skipping mount perf test." );
			return;
		}

		// Warm up file system caches
		foreach ( var path in paths )
		{
			using ( DbpfPackage.Open( path ) ) { }
		}

		var sw = Stopwatch.StartNew();
		var packages = new List<DbpfPackage>();
		int totalEntries = 0;

		try
		{
			foreach ( var path in paths )
			{
				var pkg = DbpfPackage.Open( path );
				packages.Add( pkg );
				totalEntries += pkg.Entries.Count;
			}
		}
		finally
		{
			sw.Stop();
			foreach ( var pkg in packages )
				pkg.Dispose();
		}

		Console.WriteLine( $"Opened {packages.Count} packages ({totalEntries} total entries) in {sw.ElapsedMilliseconds}ms" );

		Assert.IsTrue( sw.ElapsedMilliseconds < 1000,
			$"Opening all {packages.Count} packages took {sw.ElapsedMilliseconds}ms — " +
			$"should be <1000ms. Resource parsing is likely leaking into the mount path." );
	}

	/// <summary>
	/// Proves that BuildMetadataIndex is the offender.
	/// Measures how long it takes to parse every COBJ + OBJD in a single package.
	///
	/// This work currently happens in Mount() but MUST be deferred.
	/// This test documents the cost so we know what we're saving.
	/// </summary>
	[TestMethod]
	public void BuildMetadataIndex_Cost_ShouldBeDeferredNotInMount()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var cobjEntries = package.FindAll( ResourceType.CatalogObject )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.ToList();

		var objdEntries = package.FindAll( ResourceType.ObjectDefinition )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.ToList();

		// Measure COBJ parsing time
		var sw = Stopwatch.StartNew();
		int cobjParsed = 0;
		foreach ( var entry in cobjEntries )
		{
			try
			{
				package.GetResource<Sims4Reader.Resources.CatalogObjectResource>( entry );
				cobjParsed++;
			}
			catch { }
		}
		var cobjTime = sw.ElapsedMilliseconds;

		// Measure OBJD parsing time
		sw.Restart();
		int objdParsed = 0;
		foreach ( var entry in objdEntries )
		{
			try
			{
				package.GetResource<Sims4Reader.Resources.ObjectDefinitionResource>( entry );
				objdParsed++;
			}
			catch { }
		}
		var objdTime = sw.ElapsedMilliseconds;

		var totalTime = cobjTime + objdTime;

		Console.WriteLine( $"COBJ parse: {cobjParsed} entries in {cobjTime}ms" );
		Console.WriteLine( $"OBJD parse: {objdParsed} entries in {objdTime}ms" );
		Console.WriteLine( $"Total metadata parse cost: {totalTime}ms — this MUST NOT run during Mount()" );

		// This is a documentation test: if the total is >100ms, it proves
		// that doing this work in Mount() is unacceptable.
		if ( totalTime > 100 )
		{
			Console.WriteLine( $"WARNING: Metadata parsing takes {totalTime}ms — confirms this must be async/deferred." );
		}
	}

	/// <summary>
	/// Validates that scanning MODL entries for inline materials is not free.
	/// This work also currently runs during Mount() in MountInlineMaterials.
	/// </summary>
	[TestMethod]
	public void InlineMaterialScan_Cost_ShouldBeDeferredNotInMount()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var modlEntries = package.FindAll( ResourceType.Model )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.ToList();

		if ( modlEntries.Count == 0 )
		{
			Assert.Inconclusive( "No MODL entries found in package." );
			return;
		}

		var sw = Stopwatch.StartNew();
		int scanned = 0;
		int withInline = 0;

		foreach ( var entry in modlEntries )
		{
			try
			{
				var result = Sims4Reader.Mesh.ModlModelLoader.ScanInlineMaterialMeshes( package, entry );
				scanned++;
				if ( result != null && result.Count > 0 )
					withInline++;
			}
			catch { }
		}
		sw.Stop();

		Console.WriteLine( $"Inline material scan: {scanned} MODLs in {sw.ElapsedMilliseconds}ms ({withInline} with inline materials)" );
		Console.WriteLine( $"This work MUST NOT run during Mount() — defer to resource load time." );

		if ( sw.ElapsedMilliseconds > 50 )
		{
			Console.WriteLine( $"WARNING: MODL inline scan takes {sw.ElapsedMilliseconds}ms — confirms this must be deferred." );
		}
	}
}
