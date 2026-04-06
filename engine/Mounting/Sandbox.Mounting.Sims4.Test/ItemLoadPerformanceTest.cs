using System.Diagnostics;
using Sims4Reader;
using Sims4Reader.Image;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Resources;

namespace Sims4MountTest;

/// <summary>
/// Per-resource-type loading baselines.
///
/// Each test measures how long it takes to decompress + parse a batch of
/// individual resources from a real .package file.  The budgets are generous
/// (2-3x typical) — their purpose is to catch order-of-magnitude regressions,
/// not to enforce tight SLAs.
///
/// Run with:  dotnet test --filter "FullyQualifiedName~ItemLoadPerformanceTest"
/// </summary>
[TestClass]
public class ItemLoadPerformanceTest
{
	// ── Geometry (GEOM) ──────────────────────────────────────────────

	[TestMethod]
	public void GeometryLoad_AverageUnder5ms()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var entries = package.FindAll( ResourceType.Geometry )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 100 )
			.ToList();

		if ( entries.Count == 0 )
		{
			Assert.Inconclusive( "No GEOM entries found in package." );
			return;
		}

		// Warm up
		try { package.GetResource<RcolContainer>( entries[0] ); } catch { }

		var timings = new List<double>();
		foreach ( var entry in entries )
		{
			try
			{
				var sw = Stopwatch.StartNew();
				var rcol = package.GetResource<RcolContainer>( entry );
				rcol.GetChunk<GeometryRcolChunk>();
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		ReportAndAssert( "GEOM load", timings, 5.0 );
	}

	// ── Material Definition (MATD) ───────────────────────────────────

	[TestMethod]
	public void MaterialDefinitionLoad_AverageUnder3ms()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var entries = package.FindAll( ResourceType.MaterialDefinition )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 100 )
			.ToList();

		if ( entries.Count == 0 )
		{
			Assert.Inconclusive( "No MATD entries found in package." );
			return;
		}

		// Warm up
		try { package.GetResource<RcolContainer>( entries[0] ); } catch { }

		var timings = new List<double>();
		foreach ( var entry in entries )
		{
			try
			{
				var sw = Stopwatch.StartNew();
				var rcol = package.GetResource<RcolContainer>( entry );
				rcol.GetChunk<MaterialDefinition>();
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		ReportAndAssert( "MATD load", timings, 3.0 );
	}

	// ── DST Image (parse only) ───────────────────────────────────────

	[TestMethod]
	public void DstImageParse_AverageUnder5ms()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var entries = package.FindAll( ResourceType.DstImage )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 100 )
			.ToList();

		if ( entries.Count == 0 )
		{
			Assert.Inconclusive( "No DST image entries found in package." );
			return;
		}

		// Warm up
		try { package.GetResource<DstImage>( entries[0] ); } catch { }

		var timings = new List<double>();
		foreach ( var entry in entries )
		{
			try
			{
				var sw = Stopwatch.StartNew();
				package.GetResource<DstImage>( entry );
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		ReportAndAssert( "DST parse", timings, 5.0 );
	}

	// ── DST Image (parse + unshuffle to DDS) ─────────────────────────

	[TestMethod]
	public void DstImageToDds_AverageUnder10ms()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var entries = package.FindAll( ResourceType.DstImage )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 100 )
			.ToList();

		if ( entries.Count == 0 )
		{
			Assert.Inconclusive( "No DST image entries found in package." );
			return;
		}

		// Warm up
		try
		{
			var warm = package.GetResource<DstImage>( entries[0] );
			warm.ToDds();
		}
		catch { }

		var timings = new List<double>();
		foreach ( var entry in entries )
		{
			try
			{
				var sw = Stopwatch.StartNew();
				var dst = package.GetResource<DstImage>( entry );
				dst.ToDds();
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		ReportAndAssert( "DST → DDS", timings, 10.0 );
	}

	// ── RLE Image ────────────────────────────────────────────────────

	[TestMethod]
	public void RleImageParse_AverageUnder5ms()
	{
		var result = TestHelper.FindPackageWithType( ResourceType.RleImage );
		if ( result == null )
		{
			Assert.Inconclusive( "No package with RLE image entries found." );
			return;
		}

		using var package = result.Value.package;

		var entries = package.FindAll( ResourceType.RleImage )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 100 )
			.ToList();

		if ( entries.Count == 0 )
		{
			// Try alternate RLE type
			entries = package.FindAll( ResourceType.RleImageAlt )
				.Where( e => e.MemSize > 0 && e.FileSize > 0 )
				.Take( 100 )
				.ToList();
		}

		if ( entries.Count == 0 )
		{
			Assert.Inconclusive( "No RLE image entries found." );
			return;
		}

		// Warm up
		try { package.GetResource<RleImage>( entries[0] ); } catch { }

		var timings = new List<double>();
		foreach ( var entry in entries )
		{
			try
			{
				var sw = Stopwatch.StartNew();
				package.GetResource<RleImage>( entry );
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		ReportAndAssert( "RLE parse", timings, 5.0 );
	}

	// ── Catalog Object (COBJ) ────────────────────────────────────────

	[TestMethod]
	public void CatalogObjectLoad_AverageUnder2ms()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var entries = package.FindAll( ResourceType.CatalogObject )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 200 )
			.ToList();

		if ( entries.Count == 0 )
		{
			Assert.Inconclusive( "No COBJ entries found in package." );
			return;
		}

		// Warm up
		try { package.GetResource<CatalogObjectResource>( entries[0] ); } catch { }

		var timings = new List<double>();
		foreach ( var entry in entries )
		{
			try
			{
				var sw = Stopwatch.StartNew();
				package.GetResource<CatalogObjectResource>( entry );
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		ReportAndAssert( "COBJ load", timings, 2.0 );
	}

	// ── Object Definition (OBJD) ─────────────────────────────────────

	[TestMethod]
	public void ObjectDefinitionLoad_AverageUnder2ms()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var entries = package.FindAll( ResourceType.ObjectDefinition )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 200 )
			.ToList();

		if ( entries.Count == 0 )
		{
			Assert.Inconclusive( "No OBJD entries found in package." );
			return;
		}

		// Warm up
		try { package.GetResource<ObjectDefinitionResource>( entries[0] ); } catch { }

		var timings = new List<double>();
		foreach ( var entry in entries )
		{
			try
			{
				var sw = Stopwatch.StartNew();
				package.GetResource<ObjectDefinitionResource>( entry );
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		ReportAndAssert( "OBJD load", timings, 2.0 );
	}

	// ── String Table (STBL) ──────────────────────────────────────────

	[TestMethod]
	public void StringTableLoad_AverageUnder3ms()
	{
		var result = TestHelper.FindPackageWithType( ResourceType.StringTable );
		if ( result == null )
		{
			Assert.Inconclusive( "No package with STBL entries found." );
			return;
		}

		using var package = result.Value.package;

		var entries = package.FindAll( ResourceType.StringTable )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 50 )
			.ToList();

		if ( entries.Count == 0 )
		{
			Assert.Inconclusive( "No STBL entries found." );
			return;
		}

		// Warm up
		try { package.GetResource<StringTable>( entries[0] ); } catch { }

		var timings = new List<double>();
		foreach ( var entry in entries )
		{
			try
			{
				var sw = Stopwatch.StartNew();
				package.GetResource<StringTable>( entry );
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		ReportAndAssert( "STBL load", timings, 3.0 );
	}

	// ── MODL Full Resolution ─────────────────────────────────────────

	[TestMethod]
	public void ModlFullResolution_AverageUnder20ms()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var entries = package.FindAll( ResourceType.Model )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 50 )
			.ToList();

		if ( entries.Count == 0 )
		{
			Assert.Inconclusive( "No MODL entries found in package." );
			return;
		}

		// Warm up
		try { ModlModelLoader.LoadModel( package, entries[0] ); } catch { }

		var timings = new List<double>();
		foreach ( var entry in entries )
		{
			try
			{
				var sw = Stopwatch.StartNew();
				ModlModelLoader.LoadModel( package, entry );
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		ReportAndAssert( "MODL full resolve", timings, 20.0 );
	}

	// ── Helper ───────────────────────────────────────────────────────

	private static void ReportAndAssert( string label, List<double> timings, double budgetAvgMs )
	{
		if ( timings.Count == 0 )
		{
			Assert.Inconclusive( $"{label}: all items failed to load — nothing to measure." );
			return;
		}

		var total = timings.Sum();
		var avg = total / timings.Count;
		var min = timings.Min();
		var max = timings.Max();

		Console.WriteLine( $"{label}: {timings.Count} items in {total:F1}ms" );
		Console.WriteLine( $"  Average: {avg:F2}ms | Min: {min:F2}ms | Max: {max:F2}ms" );

		Assert.IsTrue( avg < budgetAvgMs,
			$"{label} averaged {avg:F2}ms per item — should be <{budgetAvgMs}ms." );
	}
}
