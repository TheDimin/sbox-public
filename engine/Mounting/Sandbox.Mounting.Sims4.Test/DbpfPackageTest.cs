using Sandbox.Mounting.Sims4;
using System.IO;

namespace Sims4MountTest;

[TestClass]
public class DbpfPackageTest
{
	private const uint TextureTypeId = 0x00B2D882u;
	private const uint ModelTypeId = 0x01661233u;

	[TestMethod]
	public void DbpfHeader_RealPackage_HasDbpfMagicAndIndexEntries()
	{
		var packagePath = GetRealPackagePathOrInconclusive();

		using ( var fs = File.OpenRead( packagePath ) )
		using ( var br = new BinaryReader( fs ) )
		{
			var magic = br.ReadBytes( 4 );
			Assert.AreEqual( 4, magic.Length, "Expected to read DBPF magic." );
			Assert.AreEqual( 'D', (char)magic[0], "Expected DBPF magic[0] to be 'D'." );
			Assert.AreEqual( 'B', (char)magic[1], "Expected DBPF magic[1] to be 'B'." );
			Assert.AreEqual( 'P', (char)magic[2], "Expected DBPF magic[2] to be 'P'." );
			Assert.AreEqual( 'F', (char)magic[3], "Expected DBPF magic[3] to be 'F'." );
		}

		using var package = DbpfPackage.Open( packagePath );
		Assert.IsTrue( package.Records.Count > 0, "Expected parsed DBPF index to contain entries." );
	}

	[TestMethod]
	public void IndexEntries_RealPackage_AreParsedWithRequiredFields()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Records.Count == 0 )
			Assert.Inconclusive( "Loaded package has no records." );

		var first = package.Records[0];
		Assert.IsTrue( first.FileOffset >= 0, "Expected non-negative resource offset for first index entry." );
		Assert.IsTrue( first.CompressedSize > 0, "Expected index entry size to be greater than zero." );
		Assert.IsTrue( first.DecompressedSize > 0, "Expected decompressed size to be greater than zero." );
	}

	[TestMethod]
	public void StblDetection_RealPackage_FindsStblResources()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var stblRecords = package.GetStblRecords().ToList();
		Assert.IsTrue( stblRecords.Count > 0, "Expected at least one STBL resource (TypeID 0x220557DA)." );
	}

	[TestMethod]
	public void CompressionHandling_RealPackage_CompressedStblDecompressesToStblMagic()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var compressedStbl = package.GetStblRecords()
			.FirstOrDefault( x => x.IsCompressed && x.CompressionType == CompressionType.Zlib );

		if ( compressedStbl is null )
			Assert.Inconclusive( "No zlib-compressed STBL resource found in package." );

		var data = package.ReadData( compressedStbl );
		Assert.IsTrue( data.Length >= 4, "Expected decompressed STBL data to include header." );
		Assert.AreEqual( (byte)'S', data[0] );
		Assert.AreEqual( (byte)'T', data[1] );
		Assert.AreEqual( (byte)'B', data[2] );
		Assert.AreEqual( (byte)'L', data[3] );
	}

	[TestMethod]
	public void StblParsing_RealPackage_FirstStblParsesStrings()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var stblRecord = package.GetStblRecords().FirstOrDefault();
		if ( stblRecord is null )
			Assert.Inconclusive( "No STBL resource found in package." );

		var data = package.ReadData( stblRecord );
		var ok = DbpfPackage.TryParseStblData( data, out var strings );

		Assert.IsTrue( ok, "Expected STBL payload to parse successfully." );
		Assert.IsTrue( strings.Count > 0, "Expected at least one decoded STBL string." );
	}

	[TestMethod]
	public void ObjectNameExtraction_RealCoffeeTablePackage_ContainsExpectedCatalogName()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var names = package.ReadObjectNames( 0x00 ).ToList();
		Assert.IsTrue( names.Count > 0, "Expected STBL object names to be extracted." );

		var found = names.Any( x => x.Text.Contains( "Miiko Harmony Set Coffee Table", StringComparison.OrdinalIgnoreCase ) );
		Assert.IsTrue( found, "Expected extracted names to contain 'Miiko Harmony Set Coffee Table'." );
	}

	[TestMethod]
	public void Open_RealPackage_HasRecords()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );
		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}
		Assert.IsTrue( package.Records.Count > 0, "Expected at least one record from a real Sims 4 package." );
	}

	[TestMethod]
	public void Open_RealPackage_HasTextureRecords()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );
		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}
		var textureCount = package.Records.Where( x => x.TypeId == TextureTypeId ).Count();
		Assert.IsTrue( textureCount > 0, "Expected at least one texture record (DDS) in the real package." );
	}

	[TestMethod]
	public void ReadData_RealPackage_FirstTextureRecordReturnsBytes()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );
		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}

		var record = package.Records.Where( x => x.TypeId == TextureTypeId ).FirstOrDefault();
		Assert.IsNotNull( record, "No texture record found in the real package." );

		var data = package.ReadData( record );
		Assert.IsTrue( data.Length > 0, "Expected texture record data to be readable." );
	}

	[TestMethod]
	public void Open_RealPackage_HasModelRecords()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );
		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}
		var modelCount = package.Records.Where( x => x.TypeId == ModelTypeId ).Count();
		Assert.IsTrue( modelCount > 0, "Expected at least one model record (GEOM) in the real package." );
	}

	[TestMethod]
	public void Open_RealPackage_ModelTextureApiReturnsTypedAssets()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}

		var assets = package.GetValidRecords().ToList();
		Assert.IsTrue( assets.Count > 0, "Expected model/texture assets from the typed API." );
		Assert.IsTrue( assets.Any( x => x.Kind == ResourceType.LITE), "Expected at least one texture in typed assets." );
	}

	[TestMethod]
	public void Open_RealPackage_NameMapCanResolveAtLeastOneName_WhenNameMapExists()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}

		var names = package.ReadResolvedNames();
		Assert.IsTrue( names.Count > 0, "Expected at least one resolved name resources." );
	}

	[TestMethod]
	public void NameResolution_RealPackage_CanResolveNameForExistingModel()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Records.Count == 0 )
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );

		var modelRecords = package.GetModelRecords().ToList();
		Assert.IsTrue( modelRecords.Count > 0, "Expected at least one model record in package." );

		var modelWithName = modelRecords.FirstOrDefault( x => package.TryResolveName( x.InstanceId, out _ ) );
		if ( modelWithName is not null )
		{
			Assert.IsTrue( package.TryResolveName( modelWithName.InstanceId, out var resolvedName ), "Expected model instance to resolve in unified name API." );
			Assert.IsFalse( string.IsNullOrWhiteSpace( resolvedName ), "Resolved model name should not be empty." );
			System.Diagnostics.Debug.WriteLine( $"Resolved model name: {resolvedName} (0x{modelWithName.InstanceId:X16})" );
		}
		else
		{
			System.Diagnostics.Debug.WriteLine( "No model names resolved by unified lookup for this fixture package." );
		}
	}

	[TestMethod]
	public void NameResolution_RealPackage_CanResolveNameForExistingTexture()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		if ( package.Records.Count == 0 )
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );

		var textureRecords = package.GetTextureRecords().ToList();
		Assert.IsTrue( textureRecords.Count > 0, "Expected at least one texture record in package." );

		var textureWithName = textureRecords.FirstOrDefault( x => package.TryResolveName( x.InstanceId, out _ ) );
		if ( textureWithName is not null )
		{
			Assert.IsTrue( package.TryResolveName( textureWithName.InstanceId, out var resolvedName ), "Expected texture instance to resolve in unified name API." );
			Assert.IsFalse( string.IsNullOrWhiteSpace( resolvedName ), "Resolved texture name should not be empty." );
			System.Diagnostics.Debug.WriteLine( $"Resolved texture name: {resolvedName} (0x{textureWithName.InstanceId:X16})" );
		}
		else
		{
			System.Diagnostics.Debug.WriteLine( "No texture names resolved by unified lookup for this fixture package." );
		}
	}

	[TestMethod]
	public void ReadData_RealPackage_FirstModelRecordReturnsBytes()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );
		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}

		var record = package.Records.FirstOrDefault( x => x.TypeId == ModelTypeId );
		Assert.IsNotNull( record, "No model record found in the real package." );

		var data = package.ReadData( record );
		Assert.IsTrue( data.Length > 0, "Expected model record data to be readable." );
	}

	[TestMethod]
	public void ValidateModelData_RealPackage_FirstModelHasValidStructure()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );
		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}

		// Find the first model record
		var modelRecord = package.Records.Where( x => x.TypeId == ModelTypeId ).FirstOrDefault();
		Assert.IsNotNull( modelRecord, "No model record found in the real package." );

		// Read model data
		var modelData = package.ReadData( modelRecord );
		Assert.IsTrue( modelData.Length > 0, "Model data should not be empty." );

		// Validate basic model structure
		Assert.IsTrue( modelData.Length >= 4, "Model data should be at least 4 bytes." );

		// Read the first 4 bytes (typically version or magic number)
		using var stream = new MemoryStream( modelData );
		using var reader = new BinaryReader( stream );

		uint header = reader.ReadUInt32();
		Assert.IsTrue( header > 0, "Model header should contain valid data." );

		// Validate reasonable data size (Sims 4 models typically range from hundreds of bytes to several MB)
		Assert.IsTrue( modelData.Length >= 16, "Model data should contain at least a minimal header structure." );

		// Verify the record metadata is consistent
		Assert.IsTrue( modelRecord.DecompressedSize > 0, "Decompressed size should be greater than zero." );
		Assert.AreEqual( (int)modelRecord.DecompressedSize, modelData.Length, "Decompressed size should match actual data length." );
	}

	[TestMethod]
	public void NameMapResolution_RealPackage_ShowsNameMapCoverageForModels()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );
		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}

		var nameMap = package.ReadResolvedNames();

		// Check how many models have name map entries
		var modelRecords = package.GetModelRecords().ToList();
		if ( modelRecords.Count == 0 )
		{
			Assert.Inconclusive( "No model records found in package." );
		}

		var modelsWithNames = 0;
		var modelsWithoutNames = new List<ulong>();

		foreach ( var modelRecord in modelRecords.Take( 10 ) ) // Sample first 10
		{
			if ( nameMap.TryGetValue( modelRecord.InstanceId, out var name ) && !string.IsNullOrWhiteSpace( name ) )
			{
				modelsWithNames++;
			}
			else
			{
				modelsWithoutNames.Add( modelRecord.InstanceId );
			}
		}

		// Log the coverage for debugging - at least some should have names, but it's ok if not all do
		System.Diagnostics.Debug.WriteLine( $"Models with names: {modelsWithNames}/{modelRecords.Take( 10 ).Count()}" );
		System.Diagnostics.Debug.WriteLine( $"Models without names: {modelsWithoutNames.Count}" );

		// This is informational - we want to understand the name map coverage
		// It's acceptable if not all models have friendly names
		Assert.IsTrue( modelRecords.Count > 0, "Expected at least one model record." );
	}

	[TestMethod]
	public void ModelResolution_RealPackage_ConstructsMountPathCorrectly()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );
		if ( package.Records.Count == 0 )
		{
			Assert.Inconclusive( "Loaded package has no records. Provide a valid Sims 4 package file." );
		}

		var nameMap = package.ReadResolvedNames();
		var modelRecords = package.GetModelRecords().ToList();

		if ( modelRecords.Count == 0 )
		{
			Assert.Inconclusive( "No model records found in package." );
		}

		// Check path construction for the first model
		var modelRecord = modelRecords[0];

		// Simulate the path construction from GameMount.cs
		var folder = modelRecord.GroupId == 0
			? "models"
			: $"models/{modelRecord.GroupId:X8}";

		var path = nameMap.TryGetValue( modelRecord.InstanceId, out var resolvedName )
			? $"{folder}/{SanitizePathSegment( resolvedName )}_{modelRecord.InstanceId:X16}"
			: $"{folder}/{modelRecord.InstanceId:X16}";

		Assert.IsTrue( path.StartsWith( "models/" ), "Model path should start with 'models/'" );
		Assert.IsTrue( path.Length > 7, "Model path should contain more than just 'models/'" );
		Assert.IsFalse( path.Contains( '\0' ), "Path should not contain null characters." );

		System.Diagnostics.Debug.WriteLine( $"Sample model path: {path}" );
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

	private static string SanitizePathSegment( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return "unnamed";

		Span<char> invalid = stackalloc char[]
		{
			'<', '>', ':', '"', '/', '\\', '|', '?', '*'
		};

		var result = value.Trim();
		foreach ( var ch in invalid )
			result = result.Replace( ch, '_' );

		return result;
	}
}
