using Sandbox;
using Sims4.Dbpf;
using Sims4.Dbpf.Enums;
using System.IO;

namespace Sims4MountTest;

[TestClass]
public class ModelLoaderTest
{
	[TestMethod]
	public void GeomEntries_RealPackage_ReturnsValidEntries()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var geomEntries = GetEntries( package, ResourceType.GEOM, 5 );
		if ( geomEntries.Count == 0 )
			Assert.Inconclusive( "No GEOM resources found in package." );

		foreach ( var entry in geomEntries )
		{
			Assert.AreEqual( ResourceType.GEOM, entry.Type );
			Assert.IsTrue( entry.Instance > 0, "Instance ID should be greater than 0." );
			Assert.IsTrue( entry.MemSize > 0, "Decompressed size should be greater than 0." );
		}
	}

	[TestMethod]
	public void ReadGeom_RealPackage_ReturnsValidMeshData()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var geomEntries = GetEntries( package, ResourceType.GEOM, 1 );
		if ( geomEntries.Count == 0 )
			Assert.Inconclusive( "No GEOM resources found in package." );

		var geom = package.ReadGeom( geomEntries[0] );
		Assert.IsTrue( geom.VertexCount > 0, "Expected GEOM to contain vertices." );
		Assert.IsTrue( geom.VertexStride > 0, "Expected positive vertex stride." );
		Assert.IsTrue( geom.Elements.Length > 0, "Expected GEOM to contain element definitions." );
		Assert.IsTrue( geom.FaceGroups.Length > 0, "Expected GEOM to contain at least one face group." );
	}

	[TestMethod]
	public void ReadGeom_RealPackage_FirstFewEntries_AreParseable()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var geomEntries = GetEntries( package, ResourceType.GEOM, 5 );
		if ( geomEntries.Count == 0 )
			Assert.Inconclusive( "No GEOM resources found in package." );

		int successCount = 0;
		foreach ( var entry in geomEntries )
		{
			var geom = package.ReadGeom( entry );
			Assert.IsTrue( geom.VertexCount >= 0 );
			Assert.IsTrue( geom.FaceGroups.Length > 0 );
			successCount++;
		}

		Assert.IsTrue( successCount > 0, "At least one GEOM should be readable." );
	}

	[TestMethod]
	public void ReadModelContainer_RealPackage_ModlCanBeParsedAsRcol()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var modlEntry = package.FindFirst( ResourceType.MODL );
		if ( modlEntry is null )
			Assert.Inconclusive( "No MODL resources found in package." );

		var rcol = package.ReadRcol( modlEntry.Value );
		Assert.IsTrue( rcol.ObjectLocations.Length > 0, "Expected RCOL object locations." );
		Assert.IsTrue( rcol.ChunkData.Length > 0, "Expected RCOL chunk data." );
	}

	private static List<Sims4.Dbpf.Structures.DbpfEntry> GetEntries( DbpfPackage package, ResourceType type, int maxCount )
	{
		var result = new List<Sims4.Dbpf.Structures.DbpfEntry>( maxCount );
		foreach ( var entry in package.GetEntriesOfType( type ) )
		{
			result.Add( entry );
			if ( result.Count >= maxCount )
				break;
		}
		return result;
	}

	private static string GetRealPackagePathOrInconclusive()
	{
		var fromEnv = Environment.GetEnvironmentVariable( "SIMS4_TEST_PACKAGE_PATH" );
		if ( !string.IsNullOrWhiteSpace( fromEnv ) && File.Exists( fromEnv ) )
			return fromEnv;

		var localFixture = Path.Combine( @"C:\Users\DPHoo\Downloads\set", "miiko-harmony-chair.package" );
		if ( File.Exists( localFixture ) )
			return localFixture;

		Assert.Inconclusive(
			"Real Sims 4 package not found. Set SIMS4_TEST_PACKAGE_PATH or fix hardpath" );
		return string.Empty;
	}
}
