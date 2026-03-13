using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Tests for the ModlModelLoader pipeline using real package data.
/// </summary>
[TestClass]
public class ModlModelLoaderTest
{
	[TestMethod]
	public void LoadModel_ReturnsResolvedModel()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entry = package.FindAll( ResourceType.Model ).FirstOrDefault();
		if ( entry.Key.Type == ResourceType.Unknown )
			Assert.Fail( "No MODL resources found in package." );

		var model = ModlModelLoader.LoadModel( package, entry );
		Assert.IsNotNull( model, "Expected LoadModel to return a resolved model." );
		Assert.AreEqual( entry.Key, model.Key );
	}

	[TestMethod]
	public void LoadModel_HasLods()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		bool anyHasLods = false;
		foreach ( var entry in entries )
		{
			var model = ModlModelLoader.LoadModel( package, entry );
			if ( model.Lods.Count > 0 )
			{
				anyHasLods = true;
				break;
			}
		}

		Assert.IsTrue( anyHasLods,
			"Expected at least one MODL to resolve with LODs." );
	}

	[TestMethod]
	public void LoadModel_MeshesHaveGeometry()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		// Geometry resolution requires VBUF/IBUF/VRTF chunks to be in the same package.
		// FullBuild packages contain all chunks; DeltaBuild may only have overrides.
		var entries = package.FindAll( ResourceType.Model ).Take( 100 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		bool anyHasGeometry = false;
		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length > 0 && mesh.Indices.Length > 0 )
					{
						anyHasGeometry = true;
						break;
					}
				}
				if ( anyHasGeometry ) break;
			}
			catch { /* Some entries may not be valid MODL RCOLs */ }
		}

		if ( !anyHasGeometry )
			Assert.Fail(
				"No MODL resolved geometry (VBUF/IBUF chunks may be in other packages)." );
	}

	[TestMethod]
	public void LoadModel_ExtractsTextureKeys()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		// Materials/textures may reference external packages, so not all MODLs will resolve them.
		var entries = package.FindAll( ResourceType.Model ).Take( 100 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		bool anyHasTextures = false;
		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				if ( model.AllTextureKeys.Count > 0 )
				{
					anyHasTextures = true;
					break;
				}
			}
			catch { /* Some entries may not be valid MODL RCOLs */ }
		}

		if ( !anyHasTextures )
			Assert.Fail(
				"No MODL resolved texture keys (materials may reference other packages)." );
	}

	[TestMethod]
	public void LoadModel_MeshesHaveMaterials()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		// Materials are often in external RCOL references that may not be in this package.
		var entries = package.FindAll( ResourceType.Model ).Take( 100 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		bool anyHasMaterial = false;
		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Material != null )
					{
						anyHasMaterial = true;
						break;
					}
				}
				if ( anyHasMaterial ) break;
			}
			catch { /* Some entries may not be valid MODL RCOLs */ }
		}

		if ( !anyHasMaterial )
			Assert.Fail(
				"No MODL resolved materials (materials may reference other packages)." );
	}

	[TestMethod]
	public void LoadModel_MultipleParseable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 10 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		int successCount = 0;
		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				if ( model != null )
					successCount++;
			}
			catch
			{
				// Count failures
			}
		}

		Assert.IsTrue( successCount > 0, "At least one MODL should load successfully." );
		Assert.AreEqual( entries.Count, successCount,
			"All MODL entries should load without exceptions." );
	}

}
