using Sims4Reader;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Tests for the MLOD (Mesh LOD) chunk parser using real package data.
/// MLODs are found inside RCOL containers, typically referenced from MODL entries.
/// </summary>
[TestClass]
public class MeshLodTest
{
	/// <summary>
	/// Find the first MeshLod from a MODL RCOL (either inline or external reference).
	/// </summary>
	private static (MeshLod? mlod, RcolContainer? mlodRcol) FindFirstMlod( DbpfPackage package )
	{
		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 20 ) )
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var modl = rcol.GetChunk<ModlChunk>();
			if ( modl == null || modl.LodEntries.Count == 0 )
				continue;

			foreach ( var lodEntry in modl.LodEntries )
			{
				int mlodIdx = lodEntry.MlodIndex;
				if ( mlodIdx < 0 ) continue;

				// Try inline chunk first
				if ( mlodIdx < rcol.ChunkEntries.Count )
				{
					if ( rcol.ChunkEntries[mlodIdx].Chunk is MeshLod inlineMlod )
						return (inlineMlod, rcol);
				}

				// Try external reference
				int externalIdx = mlodIdx - rcol.ChunkEntries.Count;
				if ( externalIdx >= 0 && externalIdx < rcol.ExternalReferences.Length )
				{
					var externalKey = rcol.ExternalReferences[externalIdx];
					var externalEntry = package.Find( externalKey );
					if ( externalEntry != null )
					{
						var extRcol = package.GetResource<RcolContainer>( externalEntry.Value );
						var mlod = extRcol.GetChunk<MeshLod>();
						if ( mlod != null )
							return (mlod, extRcol);
					}
				}
			}
		}

		return (null, null);
	}

	[TestMethod]
	public void MeshLod_ParsesFromModl()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var (mlod, _) = FindFirstMlod( package );
		if ( mlod == null )
			Assert.Fail( "No MLOD chunks found via MODL entries." );

		Assert.IsTrue( mlod.Version > 0, "Expected MLOD version > 0." );
	}

	[TestMethod]
	public void MeshLod_HasMeshes()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var (mlod, _) = FindFirstMlod( package );
		if ( mlod == null )
			Assert.Fail( "No MLOD chunks found via MODL entries." );

		Assert.IsTrue( mlod.Meshes.Count > 0, "Expected MLOD to have at least one mesh." );
	}

	[TestMethod]
	public void MeshLod_ChunkReferences_AreDecoded()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var (mlod, _) = FindFirstMlod( package );
		if ( mlod == null || mlod.Meshes.Count == 0 )
			Assert.Fail( "No MLOD meshes found." );

		var mesh = mlod.Meshes[0];

		// Decoded indices should be small non-negative numbers (TGI indices)
		// or -1 for null references — never huge encoded values like 0x10000003
		Assert.IsTrue( mesh.VertexBufferIndex >= -1 && mesh.VertexBufferIndex < 100,
			$"VertexBufferIndex {mesh.VertexBufferIndex} looks like an undecoded chunk reference." );
		Assert.IsTrue( mesh.IndexBufferIndex >= -1 && mesh.IndexBufferIndex < 100,
			$"IndexBufferIndex {mesh.IndexBufferIndex} looks like an undecoded chunk reference." );
		Assert.IsTrue( mesh.VertexFormatIndex >= -1 && mesh.VertexFormatIndex < 100,
			$"VertexFormatIndex {mesh.VertexFormatIndex} looks like an undecoded chunk reference." );
		Assert.IsTrue( mesh.MaterialIndex >= -1 && mesh.MaterialIndex < 100,
			$"MaterialIndex {mesh.MaterialIndex} looks like an undecoded chunk reference." );
	}

	[TestMethod]
	public void MeshLod_Meshes_HaveGeometryData()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var (mlod, _) = FindFirstMlod( package );
		if ( mlod == null || mlod.Meshes.Count == 0 )
			Assert.Fail( "No MLOD meshes found." );

		bool anyHasGeometry = false;
		foreach ( var mesh in mlod.Meshes )
		{
			if ( mesh.VertexCount > 0 && mesh.PrimitiveCount > 0 )
			{
				anyHasGeometry = true;
				break;
			}
		}

		Assert.IsTrue( anyHasGeometry,
			"Expected at least one MLOD mesh to have non-zero vertex/primitive counts." );
	}

	[TestMethod]
	public void MeshLod_Meshes_HaveBoundingBox()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var (mlod, _) = FindFirstMlod( package );
		if ( mlod == null || mlod.Meshes.Count == 0 )
			Assert.Fail( "No MLOD meshes found." );

		var mesh = mlod.Meshes[0];
		Assert.IsTrue( mesh.BoundsMax[0] >= mesh.BoundsMin[0],
			"Bounding box max X should be >= min X." );
		Assert.IsTrue( mesh.BoundsMax[1] >= mesh.BoundsMin[1],
			"Bounding box max Y should be >= min Y." );
		Assert.IsTrue( mesh.BoundsMax[2] >= mesh.BoundsMin[2],
			"Bounding box max Z should be >= min Z." );
	}
}
