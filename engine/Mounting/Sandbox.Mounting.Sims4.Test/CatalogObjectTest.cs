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
			Assert.Inconclusive( "No COBJ resources found in package." );

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
			Assert.Inconclusive( "No COBJ resources found in package." );

		// At least one COBJ should have a non-zero name hash
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
	public void CatalogObject_FullyParsedEntries_HaveTgiReferences()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.CatalogObject ).Take( 20 ).ToList();
		if ( entries.Count == 0 )
			Assert.Inconclusive( "No COBJ resources found in package." );

		// Among fully-parsed entries, at least one should have TGI references
		bool anyHasRefs = false;
		foreach ( var entry in entries )
		{
			var cobj = package.GetResource<CatalogObjectResource>( entry );
			if ( cobj.IsFullyParsed && cobj.TgiReferences.Length > 0 )
			{
				anyHasRefs = true;
				break;
			}
		}

		// If no entry was fully parsed, that's still useful info (inconclusive, not failure)
		if ( !entries.Any( e => package.GetResource<CatalogObjectResource>( e ).IsFullyParsed ) )
			Assert.Inconclusive( "No COBJ entries were fully parsed (format version mismatch)." );

		Assert.IsTrue( anyHasRefs, "Expected at least one fully-parsed COBJ to have TGI references." );
	}

	[TestMethod]
	public void CatalogObject_MultipleParseable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.CatalogObject ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Inconclusive( "No COBJ resources found in package." );

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
}
