using Sims4Reader;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Tests for the MODL (Model) RCOL chunk parser using real package data.
/// </summary>
[TestClass]
public class ModlChunkTest
{
	[TestMethod]
	public void ModlChunk_ParsesFromPackage()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.Model ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Fail( "No MODL resources found in package." );

		var rcol = package.GetResource<RcolContainer>( entry );
		var modl = rcol.GetChunk<ModlChunk>();
		Assert.IsNotNull( modl, "Expected RCOL to contain a ModlChunk." );
		Assert.AreEqual( 0x4C444F4Du, modl.Tag, "Expected MODL tag." );
	}

	[TestMethod]
	public void ModlChunk_HasLodEntries()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		bool anyHasLods = false;
		foreach ( var entry in entries )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var modl = rcol.GetChunk<ModlChunk>();
			if ( modl != null && modl.LodEntries.Count > 0 )
			{
				anyHasLods = true;
				break;
			}
		}

		Assert.IsTrue( anyHasLods, "Expected at least one MODL to have LOD entries." );
	}

	[TestMethod]
	public void ModlChunk_LodEntries_HaveValidIndices()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		bool foundLod = false;
		foreach ( var entry in entries )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var modl = rcol.GetChunk<ModlChunk>();
			if ( modl == null || modl.LodEntries.Count == 0 )
				continue;

			foreach ( var lod in modl.LodEntries )
			{
				// Decoded MLOD index should be small (TGI index) or -1 for null
				Assert.IsTrue( lod.MlodIndex >= -1 && lod.MlodIndex < 100,
					$"MlodIndex {lod.MlodIndex} looks like an undecoded chunk reference." );
				foundLod = true;
			}
		}

		if ( !foundLod )
			Assert.Fail( "No MODL LOD entries found." );
	}

	[TestMethod]
	public void ModlChunk_HasBoundingBox()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		bool anyHasBounds = false;
		foreach ( var entry in entries )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var modl = rcol.GetChunk<ModlChunk>();
			if ( modl == null ) continue;

			// A valid bounding box should have max >= min
			if ( modl.Bounds.MaxX >= modl.Bounds.MinX &&
				modl.Bounds.MaxY >= modl.Bounds.MinY &&
				modl.Bounds.MaxZ >= modl.Bounds.MinZ )
			{
				anyHasBounds = true;
				break;
			}
		}

		Assert.IsTrue( anyHasBounds,
			"Expected at least one MODL to have a valid bounding box." );
	}

	[TestMethod]
	public void ModlChunk_GetBestLod_ReturnsHighestDetail()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 20 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		bool foundMultiLod = false;
		foreach ( var entry in entries )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var modl = rcol.GetChunk<ModlChunk>();
			if ( modl == null || modl.LodEntries.Count < 2 )
				continue;

			foundMultiLod = true;
			var best = modl.GetBestLod();
			Assert.IsNotNull( best, "GetBestLod should return a LOD for a MODL with entries." );

			// Best should be HighDetail if present, otherwise Medium, then Low
			bool hasHigh = modl.LodEntries.Any( l => l.Id == LodId.HighDetail );
			if ( hasHigh )
				Assert.AreEqual( LodId.HighDetail, best.Id );
			break;
		}

		if ( !foundMultiLod )
			Assert.Fail( "No MODL with multiple LOD entries found." );
	}

	[TestMethod]
	public void ModlChunk_MultipleParseable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		int successCount = 0;
		foreach ( var entry in entries )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var modl = rcol.GetChunk<ModlChunk>();
			if ( modl != null && modl.Version > 0 )
				successCount++;
		}

		Assert.AreEqual( entries.Count, successCount,
			"All MODL entries should parse successfully." );
	}
}
