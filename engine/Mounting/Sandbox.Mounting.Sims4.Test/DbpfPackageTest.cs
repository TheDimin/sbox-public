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
			Assert.Fail( "Loaded package has no records." );

		var first = package.Entries[0];
		Assert.AreNotEqual( ResourceType.Unknown, first.Key.Type, "Expected known resource type for first entry." );
		Assert.IsTrue( first.FileSize > 0, "Expected file size to be greater than zero." );
		Assert.IsTrue( first.MemSize > 0, "Expected decompressed size to be greater than zero." );
	}

	[TestMethod]
	public void GetBytes_FirstEntry_ReturnsData()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Entries.Count == 0 )
			Assert.Fail( "Loaded package has no records." );

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
			Assert.Fail( "Loaded package has no records." );

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
