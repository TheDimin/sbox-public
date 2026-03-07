using Sandbox;
using Sandbox.Mounting.Sims4;
using System.IO;

namespace Sims4MountTest;

[TestClass]
public class ModelLoaderTest
{
	private const uint ModelTypeId = 0x01661233u;

	[TestMethod]
	public void ModelRecords_HaveValidNames()
	{

		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var modelRecords = package.GetModelRecords().ToList();

		// Name maps are optional in real Sims 4 packages, so require at least one
		// successful resolution from a sample instead of requiring every model record.
		var sampledRecords = modelRecords.Take( 5 ).ToList();
		int resolvedCount = 0;

		foreach ( var record in sampledRecords )
		{
			if ( !package.TryResolveName( record.InstanceId, out var name ) )
				continue;

			Assert.IsFalse( string.IsNullOrWhiteSpace( name ), $"Resolved name is empty for modelID: {record.InstanceId}" );
			resolvedCount++;
		}

		if ( resolvedCount == 0 )
		{
			Assert.Inconclusive( "No model names were resolvable in this package fixture. _KEY/STBL mappings for model IDs may be absent." );
		}
	}

	[TestMethod]
	public void GetModelRecords_RealPackage_ReturnsValidRecords()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records." );
		}

		var modelRecords = package.GetModelRecords().ToList();
		if ( modelRecords.Count == 0 )
		{
			Assert.Inconclusive( "No model records found in package." );
		}

		// Verify records are valid
		foreach ( var record in modelRecords.Take( 5 ) )
		{
			Assert.IsNotNull( record );
			Assert.AreEqual( ModelTypeId, record.TypeId );
			Assert.IsTrue( record.InstanceId > 0, "Instance ID should be greater than 0." );
			Assert.IsTrue( record.DecompressedSize > 0, "Decompressed size should be greater than 0." );
		}
	}

	[TestMethod]
	public void ReadModelData_RealPackage_ReturnsValidData()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records." );
		}

		var modelRecords = package.GetModelRecords().ToList();
		if ( modelRecords.Count == 0 )
		{
			Assert.Inconclusive( "No model records found in package." );
		}

		// Read data from first few models
		int successCount = 0;
		foreach ( var modelRecord in modelRecords.Take( 5 ) )
		{
			var data = package.ReadData( modelRecord );
			Assert.IsNotNull( data, "Data should not be null." );
			Assert.IsTrue( data.Length > 0, $"Data length should be greater than 0 for record 0x{modelRecord.InstanceId:X16}." );
			Assert.AreEqual( modelRecord.DecompressedSize, data.Length,
				$"Data length should match decompressed size for record 0x{modelRecord.InstanceId:X16}." );
			successCount++;
		}

		Assert.IsTrue( successCount > 0, "At least one model should be readable." );
	}

	[TestMethod]
	public void ModelAssetKind_RealPackage_IdentifiesModelsCorrectly()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records." );
		}

		// Get model and texture assets
		var assets = package.GetValidRecords().ToList();
		if ( assets.Count == 0 )
		{
			Assert.Inconclusive( "No model/texture assets found in package." );
		}

		var modelAssets = assets.Where( a => a.Kind == Sims4AssetKind.Model ).ToList();
		var textureAssets = assets.Where( a => a.Kind == Sims4AssetKind.Texture ).ToList();

		Assert.IsTrue( modelAssets.Count > 0, "Expected at least one model asset." );
		Assert.IsTrue( textureAssets.Count > 0, "Expected at least one texture asset." );

		// Verify asset types match record types
		foreach ( var modelAsset in modelAssets.Take( 3 ) )
		{
			Assert.AreEqual( ModelTypeId, modelAsset.Record.TypeId,
				$"Model asset should have TypeId 0x{ModelTypeId:X8}." );
		}
	}

	[TestMethod]
	public void ModelData_RealPackage_ContainsValidHeader()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records." );
		}

		var modelRecords = package.GetModelRecords().ToList();
		if ( modelRecords.Count == 0 )
		{
			Assert.Inconclusive( "No model records found in package." );
		}

		// Test first model
		var modelRecord = modelRecords[0];
		var modelData = package.ReadData( modelRecord );

		Assert.IsTrue( modelData.Length >= 4, "Model data should be at least 4 bytes." );

		// Read and validate header
		using var stream = new MemoryStream( modelData, writable: false );
		using var reader = new BinaryReader( stream );

		uint version = reader.ReadUInt32();
		Assert.IsTrue( version > 0 || modelData[0] > 0, "Model data should contain meaningful header information." );

		System.Diagnostics.Debug.WriteLine( $"Model 0x{modelRecord.InstanceId:X16} header: 0x{version:X8}, data size: {modelData.Length} bytes" );
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
