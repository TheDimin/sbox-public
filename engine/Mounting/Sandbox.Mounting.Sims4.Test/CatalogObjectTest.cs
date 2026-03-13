using Sims4Reader;
using Sims4Reader.Resources;

namespace Sims4MountTest;

[TestClass]
public class CatalogObjectTest
{
	[TestMethod]
	public void CatalogObject_Parses()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.CatalogObject ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Fail( "No COBJ resources found in package." );

		var cobj = package.GetResource<CatalogObjectResource>( entry );
		Assert.IsNotNull( cobj, "Expected COBJ to parse successfully." );
		Assert.IsTrue( cobj.Version > 0, "Expected COBJ version > 0." );
		Assert.IsTrue( cobj.CommonBlockVersion > 0, "Expected common block version > 0." );
	}

	[TestMethod]
	public void CatalogObject_HasNameHash()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.CatalogObject ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No COBJ resources found in package." );

		bool anyHasName = false;
		foreach ( var entry in entries )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			if ( cobj.NameHash != 0 )
			{
				anyHasName = true;
				break;
			}
		}

		Assert.IsTrue( anyHasName, "Expected at least one COBJ to have a non-zero NameHash." );
	}

	[TestMethod]
	public void CatalogObject_MultipleParseable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.CatalogObject ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No COBJ resources found in package." );

		int successCount = 0;
		foreach ( var entry in entries )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			if ( cobj != null && cobj.Version > 0 )
				successCount++;
		}

		Assert.IsTrue( successCount > 0, "At least one COBJ should be parseable." );
		Assert.AreEqual( entries.Count, successCount,
			"All COBJ entries should parse at least the common header." );
	}

	[TestMethod]
	public void CatalogObject_HasTags()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		// Scan a significant chunk of COBJs — tags are read from the common block
		// which parses even when the full body fails.
		bool anyHasTags = false;
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ).Take( 500 ) )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			if ( cobj.Tags.Length > 0 )
			{
				anyHasTags = true;
				break;
			}
		}

		Assert.IsTrue( anyHasTags,
			"Expected at least one COBJ to have tags in its common block." );
	}

	[TestMethod]
	public void CatalogObject_HasBuyCategoryTags()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		// Tags are available from the common block regardless of full parse success.
		bool anyHasCategory = false;
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ).Take( 1000 ) )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			if ( cobj.Tags.Length == 0 )
				continue;

			var category = BuyCategoryTag.GetCategory( cobj.Tags );
			if ( category != null )
			{
				anyHasCategory = true;
				break;
			}
		}

		Assert.IsTrue( anyHasCategory,
			"Expected at least one COBJ to have a recognizable BuyCat category tag." );
	}

	[TestMethod]
	public void CatalogObject_CategoryDistribution()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		int totalCobjs = 0;
		int fullyParsed = 0;
		int withFuncFlags = 0;
		int withBuildFlags = 0;
		int withRoomFlags = 0;

		// Count tag.Category frequency
		var tagCategoryCounts = new Dictionary<ushort, int>();
		// Count tag.Value frequency for known category types
		var tagValueCounts = new Dictionary<ushort, int>();

		// Sample a few tags near the buy range
		var sampleTags = new List<string>();

		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ) )
		{
			totalCobjs++;
			var cobj = package.GetResource<CatalogObjectResource>( entry );

			if ( cobj.IsFullyParsed )
			{
				fullyParsed++;
				if ( cobj.FunctionCategoryFlags != 0 ) withFuncFlags++;
				if ( cobj.BuildCategoryFlags != 0 ) withBuildFlags++;
				if ( cobj.RoomCategoryFlags != 0 ) withRoomFlags++;
			}

			foreach ( var tag in cobj.Tags )
			{
				tagCategoryCounts.TryGetValue( tag.Category, out var c );
				tagCategoryCounts[tag.Category] = c + 1;

				tagValueCounts.TryGetValue( tag.Value, out var v );
				tagValueCounts[tag.Value] = v + 1;

				// Sample tags in the buy category range
				if ( sampleTags.Count < 30 && tag.Category >= 0x0050 && tag.Category <= 0x0070 )
					sampleTags.Add( $"Cat=0x{tag.Category:X4} Val=0x{tag.Value:X4}" );
			}
		}

		// Top 30 most common tag.Category values
		var topCategories = tagCategoryCounts
			.OrderByDescending( kv => kv.Value )
			.Take( 30 )
			.Select( kv => $"0x{kv.Key:X4}={kv.Value}" );

		var summary = $"Total COBJs: {totalCobjs}, fully parsed: {fullyParsed}\n" +
			$"FuncFlags>0: {withFuncFlags}, BuildFlags>0: {withBuildFlags}, RoomFlags>0: {withRoomFlags}\n" +
			$"Top tag.Category by frequency: {string.Join( ", ", topCategories )}\n" +
			$"Sample buy-range tags: {string.Join( "; ", sampleTags )}";

		// Count categories matched by GetCategory
		var categoryCounts = new Dictionary<string, int>();
		int totalWithCategory = 0;
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ) )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			var category = BuyCategoryTag.GetCategory( cobj.Tags );
			if ( category != null )
			{
				totalWithCategory++;
				categoryCounts.TryGetValue( category, out var c2 );
				categoryCounts[category] = c2 + 1;
			}
		}

		var catDist = string.Join( ", ", categoryCounts.OrderByDescending( kv => kv.Value ).Select( kv => $"{kv.Key}={kv.Value}" ) );
		summary += $"\nBuyCat matches: {totalWithCategory}, distribution: {catDist}";

		Console.WriteLine( summary );
		Assert.IsTrue( totalWithCategory > 0, $"No BuyCat matches.\n{summary}" );
	}

	[TestMethod]
	public void CatalogObject_SomeFullyParsed()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		int total = 0;
		int parsed = 0;
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ).Take( 500 ) )
		{
			total++;
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			if ( cobj.IsFullyParsed )
				parsed++;
		}

		Assert.IsTrue( total > 0, "No COBJ resources found." );
		Assert.IsTrue( parsed > 0,
			$"Expected at least some COBJs to be fully parsed (total={total})." );
	}
}
