using Sims4.Dbpf;
using Sims4.Dbpf.Enums;
using Sims4.Dbpf.Structures;
using System.IO;

namespace Sims4MountTest;

[TestClass]
public class DbpfPackageTest
{
	[TestMethod]
	public void Open_RealPackage_HasValidHeaderAndEntries()
	{
		var packagePath = GetRealPackagePathOrInconclusive();

		using var package = DbpfPackage.Open( packagePath );
		Assert.IsTrue( package.Header.IsValid, "Expected DBPF header magic to be valid." );
		Assert.IsTrue( package.Count > 0, "Expected parsed DBPF index to contain entries." );
		Assert.AreEqual( package.Count, package.Entries.Length, "Count should match entries array length." );
	}

	[TestMethod]
	public void IndexEntries_RealPackage_AreParsedWithRequiredFields()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Count == 0 )
			Assert.Inconclusive( "Loaded package has no records." );

		ref readonly var first = ref package[0];
		Assert.AreNotEqual( ResourceType.Unknown, first.Type, "Expected known resource type for first entry." );
		Assert.IsTrue( first.CompressedSize > 0, "Expected index entry compressed size to be greater than zero." );
		Assert.IsTrue( first.MemSize > 0, "Expected decompressed size to be greater than zero." );
	}

	[TestMethod]
	public void StblDetection_RealPackage_FindsStblResources()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var stblCount = package.CountOf( ResourceType.STBL );
		Assert.IsTrue( stblCount > 0, "Expected at least one STBL resource (TypeID 0x220557DA)." );
	}

	[TestMethod]
	public void StblParsing_RealPackage_FirstStblParsesStrings()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var stblEntry = package.FindFirst( ResourceType.STBL );
		if ( stblEntry is null )
			Assert.Inconclusive( "No STBL resource found in package." );

		var stbl = package.ReadStbl( stblEntry.Value );

		Assert.IsTrue( stbl.DeclaredEntryCount >= (ulong)stbl.Entries.Length, "Declared count should be >= parsed entries." );
		Assert.IsTrue( stbl.Entries.Length > 0, "Expected at least one decoded STBL string." );
	}

	[TestMethod]
	public void RawAccess_RealPackage_FirstEntryHasReadableData()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Count == 0 )
			Assert.Inconclusive( "Loaded package has no records." );

		ref readonly var first = ref package[0];
		var raw = package.GetRawDataSafe( in first );
		Assert.IsTrue( raw.Length > 0, "Expected raw entry data to be readable." );
	}

	[TestMethod]
	public void GetResourceName_RealPackage_ReturnsValueOrHexFallback()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Count == 0 )
			Assert.Inconclusive( "Loaded package has no records." );

		ref readonly var first = ref package[0];
		var name = package.GetResourceName( first.Instance );
		Assert.IsFalse( string.IsNullOrWhiteSpace( name ), "Expected resource name API to return non-empty value." );
	}

	private static string GetRealPackagePathOrInconclusive()
	{
		var fromEnv = Environment.GetEnvironmentVariable( "SIMS4_TEST_PACKAGE_PATH" );
		if ( !string.IsNullOrWhiteSpace( fromEnv ) && File.Exists( fromEnv ) )
			return fromEnv;

		var coffeeTableFixture = Path.Combine( @"C:\Users\DPHoo\Downloads\set", "miiko-harmony-coffee-table.package" );
		if ( File.Exists( coffeeTableFixture ) )
			return coffeeTableFixture;

		var localFixture = Path.Combine( @"C:\Users\DPHoo\Downloads\set", "miiko-harmony-chair.package" );
		if ( File.Exists( localFixture ) )
			return localFixture;

		Assert.Inconclusive(
			"Real Sims 4 package not found. Set SIMS4_TEST_PACKAGE_PATH or fix hardpath" );
		return string.Empty;
	}

}
