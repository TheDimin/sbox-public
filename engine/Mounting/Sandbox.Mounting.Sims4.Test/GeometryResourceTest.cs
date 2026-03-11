using Sims4Reader;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

[TestClass]
public class GeometryResourceTest
{
	[TestMethod]
	public void AllGeomEntries_AllPackages_ParseWithoutException()
	{
		var packages = TestHelper.GetAllPackagePaths();
		if ( packages.Count == 0 )
			Assert.Inconclusive( "No Sims 4 packages found." );

		var versionsFound = new HashSet<uint>();
		int totalEntries = 0;
		int successCount = 0;
		int emptyCount = 0;
		var failures = new List<string>();

		foreach ( var packagePath in packages )
		{
			var packageName = System.IO.Path.GetFileName( packagePath );
			using var package = DbpfPackage.Open( packagePath );

			var geomEntries = package.FindAll( ResourceType.Geometry ).ToList();
			totalEntries += geomEntries.Count;

			foreach ( var entry in geomEntries )
			{
				try
				{
					// Delta packages may contain zero-byte tombstone entries
					if ( entry.MemSize == 0 || entry.FileSize == 0 )
					{
						emptyCount++;
						continue;
					}

					var rcol = package.GetResource<RcolContainer>( entry );
					var geomChunk = rcol.GetChunk<GeometryRcolChunk>();
					if ( geomChunk == null )
					{
						// Empty RCOL (zero-byte data after decompression) is OK for delta packages
						if ( rcol.ChunkEntries.Count == 0 )
						{
							emptyCount++;
							continue;
						}
						failures.Add( $"[{packageName}] {entry.Key}: No GeometryRcolChunk in RCOL ({rcol.ChunkEntries.Count} chunks)" );
						continue;
					}

					versionsFound.Add( geomChunk.Geometry.Version );

					// Ensure we can actually decode vertices and indices
					var vertices = geomChunk.Geometry.GetVertices();
					var indices = geomChunk.Geometry.GetIndices();

					if ( vertices.Length == 0 && indices.Length == 0 )
					{
						failures.Add( $"[{packageName}] {entry.Key} (v0x{geomChunk.Geometry.Version:X2}): empty mesh" );
						continue;
					}

					successCount++;
				}
				catch ( Exception ex )
				{
					failures.Add( $"[{packageName}] {entry.Key}: {ex.GetType().Name} — {ex.Message}" );
				}
			}
		}

		Console.WriteLine( $"Packages tested: {packages.Count}" );
		Console.WriteLine( $"GEOM entries: {totalEntries} total, {successCount} parsed OK, {emptyCount} empty/tombstone" );
		Console.WriteLine( $"Versions found: {string.Join( ", ", versionsFound.OrderBy( v => v ).Select( v => $"0x{v:X2}" ) )}" );

		if ( failures.Count > 0 )
		{
			Console.WriteLine( $"Failures ({failures.Count}):" );
			foreach ( var f in failures.Take( 30 ) )
				Console.WriteLine( $"  {f}" );
		}

		Assert.AreEqual( 0, failures.Count,
			$"{failures.Count}/{totalEntries} GEOM entries failed. First: {failures.FirstOrDefault()}" );
	}

	[TestMethod]
	public void GeomEntries_ParseAsRcol_WithGeometryChunk()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var geomEntries = package.FindAll( ResourceType.Geometry ).Take( 10 ).ToList();
		if ( geomEntries.Count == 0 )
			Assert.Inconclusive( "No GEOM resources found in package." );

		foreach ( var entry in geomEntries )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var geomChunk = rcol.GetChunk<GeometryRcolChunk>();

			Assert.IsNotNull( geomChunk, $"Expected GeometryRcolChunk in RCOL for entry {entry.Key}" );
			Assert.IsNotNull( geomChunk.Geometry, $"Expected Geometry property to be set for entry {entry.Key}" );
		}
	}

	[TestMethod]
	public void Geometry_HasVerticesAndIndices()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.Geometry ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Inconclusive( "No GEOM resources found in package." );

		var rcol = package.GetResource<RcolContainer>( entry );
		var geomChunk = rcol.GetChunk<GeometryRcolChunk>();
		Assert.IsNotNull( geomChunk );

		var geom = geomChunk.Geometry;
		var vertices = geom.GetVertices();
		var indices = geom.GetIndices();

		Assert.IsTrue( vertices.Length > 0, "Expected GEOM to have vertices." );
		Assert.IsTrue( indices.Length > 0, "Expected GEOM to have indices." );
	}

	[TestMethod]
	public void Geometry_VerticesHavePositions()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.Geometry ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Inconclusive( "No GEOM resources found in package." );

		var rcol = package.GetResource<RcolContainer>( entry );
		var geom = rcol.GetChunk<GeometryRcolChunk>()!.Geometry;
		var vertices = geom.GetVertices();

		foreach ( var v in vertices )
		{
			Assert.IsNotNull( v.Position, "Every vertex should have a Position." );
			Assert.AreEqual( 3, v.Position!.Length, "Position should have 3 components (X, Y, Z)." );
		}
	}

	[TestMethod]
	public void Geometry_IndicesAreWithinVertexBounds()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.Geometry ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Inconclusive( "No GEOM resources found in package." );

		var rcol = package.GetResource<RcolContainer>( entry );
		var geom = rcol.GetChunk<GeometryRcolChunk>()!.Geometry;
		var vertices = geom.GetVertices();
		var indices = geom.GetIndices();
		int vertexCount = vertices.Length;

		foreach ( var idx in indices )
		{
			Assert.IsTrue( idx >= 0 && idx < vertexCount,
				$"Index {idx} is out of bounds (vertex count: {vertexCount})." );
		}
	}

	[TestMethod]
	public void Geometry_MultipleEntries_AllProduceVertices()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var geomEntries = package.FindAll( ResourceType.Geometry ).Take( 20 ).ToList();
		if ( geomEntries.Count == 0 )
			Assert.Inconclusive( "No GEOM resources found in package." );

		int successCount = 0;
		foreach ( var entry in geomEntries )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var geomChunk = rcol.GetChunk<GeometryRcolChunk>();
			if ( geomChunk == null ) continue;

			var vertices = geomChunk.Geometry.GetVertices();
			var indices = geomChunk.Geometry.GetIndices();
			Assert.IsTrue( vertices.Length > 0, $"Entry {entry.Key} (v0x{geomChunk.Geometry.Version:X2}): expected vertices." );
			Assert.IsTrue( indices.Length > 0, $"Entry {entry.Key} (v0x{geomChunk.Geometry.Version:X2}): expected indices." );
			successCount++;
		}

		Assert.IsTrue( successCount > 0, "At least one GEOM should produce vertices and indices." );
	}
}
