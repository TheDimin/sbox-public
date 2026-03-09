using Sandbox;
using Sims4.Dbpf;
using Sims4.Dbpf.Enums;
using System.IO;

namespace Sims4MountTest;


/// <summary>
/// Tests to debug GEOM parsing issues by analyzing raw binary data
/// 
/// </summary>
[TestClass]
public class GeomDebugTest
{
	[TestMethod]
	public void DebugGEOMFormat_AnalyzeRawData()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var geomEntries = GetEntries( package, ResourceType.GEOM, 3 );
		if ( geomEntries.Count == 0 )
			Assert.Inconclusive( "No GEOM resources found" );

		System.Diagnostics.Debug.WriteLine( "\n=== GEOM Format Analysis ===" );

		// Analyze first 3 models
		for ( int idx = 0; idx < Math.Min( 3, geomEntries.Count ); idx++ )
		{
			var entry = geomEntries[idx];
			var data = package.GetRawDataSafe( entry );

			System.Diagnostics.Debug.WriteLine( $"\n--- GEOM {idx}: 0x{entry.Instance:X16} ---" );
			System.Diagnostics.Debug.WriteLine( $"Data size: {data.Length} bytes" );

			// Show first bytes as hex
			System.Diagnostics.Debug.WriteLine( "First 32 bytes (hex):" );
			for ( int i = 0; i < Math.Min( 32, data.Length ); i += 4 )
			{
				uint value = BitConverter.ToUInt32( data.Slice( i, 4 ) );
				System.Diagnostics.Debug.WriteLine( $"  Offset 0x{i:X2}: 0x{value:X8} (dec: {value})" );
			}

			var bytes = data.ToArray();
			using var stream = new MemoryStream( bytes );
			using var reader = new BinaryReader( stream );

			try
			{
				uint version = reader.ReadUInt32();
				uint meshCount = reader.ReadUInt32();

				System.Diagnostics.Debug.WriteLine( $"Header: Version=0x{version:X8}, MeshCount={meshCount}" );

				if ( meshCount > 0 && meshCount < 100 )
				{
					uint vertexCount = reader.ReadUInt32();
					uint indexCount = reader.ReadUInt32();

					System.Diagnostics.Debug.WriteLine( 
						$"Mesh 0: VertexCount={vertexCount}, IndexCount={indexCount}" );
					System.Diagnostics.Debug.WriteLine( 
						$"After mesh header, stream position: 0x{stream.Position:X4}, remaining: {stream.Length - stream.Position} bytes" );

					// Check if index count looks invalid
					if ( indexCount > 10_000_000 )
					{
						System.Diagnostics.Debug.WriteLine( 
							$"⚠️  WARNING: Index count looks invalid (likely format mismatch)" );
					}
				}
			}
			catch ( Exception ex )
			{
				System.Diagnostics.Debug.WriteLine( $"Error during parse: {ex.Message}" );
			}
		}
	}

	[TestMethod]
	public void DebugModelLoading_CheckFallback()
	{
		var packagePath = GetRealPackagePathOrInconclusive();
		using var package = DbpfPackage.Open( packagePath );

		var geomEntries = GetEntries( package, ResourceType.GEOM, 1 );
		if ( geomEntries.Count == 0 )
			Assert.Inconclusive( "No GEOM resources found" );

		System.Diagnostics.Debug.WriteLine( "\n=== Model Loading Debug ===" );
		System.Diagnostics.Debug.WriteLine( "Testing model loader with actual package data" );

		// Try to load first model
		var entry = geomEntries[0];
		System.Diagnostics.Debug.WriteLine( $"GEOM: 0x{entry.Instance:X16}" );

		try
		{
			var geom = package.ReadGeom( entry );
			var data = package.GetRawDataSafe( entry );
			System.Diagnostics.Debug.WriteLine( $"Data read: {data.Length} bytes" );
			System.Diagnostics.Debug.WriteLine( $"✓ GEOM parsed: Version=0x{geom.Version:X8}, VertexCount={geom.VertexCount}, FaceGroups={geom.FaceGroups.Length}" );

			// Analyze GEOM header to understand format
			using var stream = new MemoryStream( data.ToArray() );
			using var reader = new BinaryReader( stream );

			try
			{
				uint version = reader.ReadUInt32();
				uint meshCount = reader.ReadUInt32();

				System.Diagnostics.Debug.WriteLine( $"✓ GEOM Header parsed: Version=0x{version:X8}, MeshCount={meshCount}" );

				if ( meshCount > 0 && meshCount < 100 )
				{
					uint vertexCount = reader.ReadUInt32();
					uint indexCount = reader.ReadUInt32();

					System.Diagnostics.Debug.WriteLine(
						$"✓ Mesh header parsed: VertexCount={vertexCount}, IndexCount={indexCount}" );

					// If index count is invalid, parser will use fallback
					if ( indexCount > 10_000_000 )
					{
						System.Diagnostics.Debug.WriteLine(
							$"⚠️  WARNING: Invalid index count detected - parser will use fallback geometry" );
					}
				}
			}
			catch ( System.IO.EndOfStreamException )
			{
				System.Diagnostics.Debug.WriteLine(
					$"✓ Stream ended unexpectedly during parse - parser will use fallback geometry" );
			}
		}
		catch ( Exception ex )
		{
			System.Diagnostics.Debug.WriteLine( $"✗ Unexpected error: {ex.Message}" );
			Assert.Fail( ex.Message );
		}

		System.Diagnostics.Debug.WriteLine( "\n=== Result ===" );
		System.Diagnostics.Debug.WriteLine( "Models should load with either:" );
		System.Diagnostics.Debug.WriteLine( "  ✓ Real GEOM geometry (if format matches)");
		System.Diagnostics.Debug.WriteLine( "  ✓ Placeholder cube (if parse fails)" );
	}

	private static string GetRealPackagePathOrInconclusive()
	{
		var fromEnv = Environment.GetEnvironmentVariable( "SIMS4_TEST_PACKAGE_PATH" );
		if ( !string.IsNullOrWhiteSpace( fromEnv ) && File.Exists( fromEnv ) )
			return fromEnv;

		var localFixture = Path.Combine( @"C:\Users\DPHoo\Downloads\set", "miiko-harmony-chair.package" );
		if ( File.Exists( localFixture ) )
			return localFixture;

		Assert.Inconclusive( "Real Sims 4 package not found. Set SIMS4_TEST_PACKAGE_PATH" );
		return string.Empty;
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
}
