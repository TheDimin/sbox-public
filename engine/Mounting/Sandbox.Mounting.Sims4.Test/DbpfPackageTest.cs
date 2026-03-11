using Sims4Reader;
using System.IO;

namespace Sims4MountTest;

[TestClass]
public class DbpfPackageTest
{
	[TestMethod]
	public void Open_RealPackage_HasEntries()
	{
		var packagePath = TestHelper.GetPackagePath();

		using var package = DbpfPackage.Open( packagePath );
		Assert.IsTrue( package.Entries.Count > 0, "Expected parsed DBPF index to contain entries." );
	}

	[TestMethod]
	public void Entries_HaveValidKeys()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Entries.Count == 0 )
			Assert.Inconclusive( "Loaded package has no records." );

		var first = package.Entries[0];
		Assert.AreNotEqual( ResourceType.Unknown, first.Key.Type, "Expected known resource type for first entry." );
		Assert.IsTrue( first.FileSize > 0, "Expected file size to be greater than zero." );
		Assert.IsTrue( first.MemSize > 0, "Expected decompressed size to be greater than zero." );
	}

	[TestMethod]
	public void FindAll_StringTable_FindsStblResources()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var stblCount = package.FindAll( ResourceType.StringTable ).Count();
		if ( stblCount == 0 )
			Assert.Inconclusive( "No STBL resources in this package (some packages omit string tables)." );
		Assert.IsTrue( stblCount > 0 );
	}

	[TestMethod]
	public void GetResource_StringTable_ParsesStrings()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var stblEntry = package.FindAll( ResourceType.StringTable ).FirstOrDefault();
		if ( stblEntry.Key.Type == ResourceType.Unknown )
			Assert.Inconclusive( "No STBL resource found in package." );

		var stbl = package.GetResource<StringTable>( stblEntry );
		Assert.IsTrue( stbl.Entries.Count > 0, "Expected at least one decoded STBL string." );
	}

	[TestMethod]
	public void GetBytes_FirstEntry_ReturnsData()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Entries.Count == 0 )
			Assert.Inconclusive( "Loaded package has no records." );

		var first = package.Entries[0];
		var raw = package.GetBytes( first );
		Assert.IsTrue( raw.Length > 0, "Expected raw entry data to be readable." );
	}

	[TestMethod]
	public void Find_ByKey_ReturnsMatchingEntry()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Entries.Count == 0 )
			Assert.Inconclusive( "Loaded package has no records." );

		var first = package.Entries[0];
		var found = package.Find( first.Key );
		Assert.IsNotNull( found, "Expected Find() to locate entry by its key." );
		Assert.AreEqual( first.Key, found.Value.Key );
	}

	[TestMethod]
	public void GetStringTable_ReturnsTableOrNull()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		// GetStringTable() should either return a valid table or null — not throw
		var stbl = package.GetStringTable();
		if ( stbl != null )
		{
			Assert.IsTrue( stbl.Entries.Count > 0, "If STBL exists, it should have entries." );
		}
	}
}
