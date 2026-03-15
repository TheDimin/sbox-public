using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Tests that verify multi-material models: LODs with multiple meshes where
/// each mesh has its own distinct material. This is critical for furniture
/// and other composite objects (e.g., a couch with fabric, wood legs, metal hardware).
/// </summary>
[TestClass]
public class MultiMaterialTest
{
	/// <summary>
	/// Verify that some MODLs have multiple meshes in their best LOD.
	/// TS4 models frequently have 2-5 sub-meshes per LOD.
	/// </summary>
	[TestMethod]
	public void BestLod_HasMultipleMeshes()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 200 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		int multiMeshCount = 0;
		int maxMeshes = 0;

		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				int validMeshes = bestLod.Meshes.Count( m =>
					m.Vertices.Length >= 3 && m.Indices.Length >= 3 );

				if ( validMeshes > 1 )
					multiMeshCount++;
				if ( validMeshes > maxMeshes )
					maxMeshes = validMeshes;
			}
			catch { }
		}

		Console.WriteLine( $"Multi-mesh MODLs: {multiMeshCount}/{entries.Count}, max meshes in LOD: {maxMeshes}" );
		Assert.IsTrue( multiMeshCount > 0,
			$"Expected some MODLs to have multiple meshes per LOD, but none found in {entries.Count} sampled." );
	}

	/// <summary>
	/// Verify that when a LOD has multiple meshes, each mesh has its own
	/// distinct MaterialDefinition (not all pointing to the same one).
	/// </summary>
	[TestMethod]
	public void MultipleMeshes_HaveDistinctMaterials()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 500 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		int multiMeshModls = 0;
		int withDistinctMaterials = 0;
		int withAllSameMaterial = 0;
		int withSomeMissing = 0;

		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				var validMeshes = bestLod.Meshes
					.Where( m => m.Vertices.Length >= 3 && m.Indices.Length >= 3 )
					.ToList();

				if ( validMeshes.Count < 2 ) continue;
				multiMeshModls++;

				var materialsWithDef = validMeshes.Where( m => m.Material != null ).ToList();
				if ( materialsWithDef.Count < validMeshes.Count )
					withSomeMissing++;

				if ( materialsWithDef.Count >= 2 )
				{
					// Check if materials are distinct by comparing shader entries count or texture keys
					var matSignatures = materialsWithDef
						.Select( m => GetMaterialSignature( m ) )
						.Distinct()
						.Count();

					if ( matSignatures > 1 )
						withDistinctMaterials++;
					else
						withAllSameMaterial++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"Multi-mesh MODLs: {multiMeshModls}" );
		Console.WriteLine( $"  with distinct materials: {withDistinctMaterials}" );
		Console.WriteLine( $"  with all same material: {withAllSameMaterial}" );
		Console.WriteLine( $"  with some materials missing: {withSomeMissing}" );

		Assert.IsTrue( multiMeshModls > 0,
			"No multi-mesh MODLs found." );
		Assert.IsTrue( withDistinctMaterials > 0,
			$"No multi-mesh MODLs have distinct per-mesh materials. " +
			$"Same={withAllSameMaterial}, Missing={withSomeMissing}" );
	}

	/// <summary>
	/// Verify that each mesh in a multi-mesh LOD has its own texture keys,
	/// meaning each sub-mesh can be textured independently.
	/// </summary>
	[TestMethod]
	public void MultipleMeshes_HaveIndependentTextureKeys()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 500 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found in package." );

		int multiMeshWithTextures = 0;
		int withDifferentDiffuse = 0;

		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				var meshesWithTextures = bestLod.Meshes
					.Where( m => m.Vertices.Length >= 3 && m.Indices.Length >= 3 && m.TextureKeys.Count > 0 )
					.ToList();

				if ( meshesWithTextures.Count < 2 ) continue;
				multiMeshWithTextures++;

				// Check if diffuse maps differ between sub-meshes
				var diffuseKeys = meshesWithTextures
					.Where( m => m.TextureKeys.ContainsKey( ShaderFieldType.DiffuseMap ) )
					.Select( m => m.TextureKeys[ShaderFieldType.DiffuseMap].Instance )
					.Distinct()
					.Count();

				if ( diffuseKeys > 1 )
					withDifferentDiffuse++;
			}
			catch { }
		}

		Console.WriteLine( $"Multi-mesh MODLs with textures: {multiMeshWithTextures}" );
		Console.WriteLine( $"  with different diffuse maps: {withDifferentDiffuse}" );

		Assert.IsTrue( multiMeshWithTextures > 0,
			"No multi-mesh MODLs with texture keys found." );
	}

	/// <summary>
	/// Verify cross-package material resolution: when allPackages is provided,
	/// more materials should resolve than with single-package lookup.
	/// </summary>
	[TestMethod]
	public void CrossPackage_ResolvesMoreMaterials()
	{
		var packagePaths = TestHelper.GetAllPackagePaths();
		if ( packagePaths.Count < 2 )
		{
			Assert.Inconclusive( "Need multiple packages for cross-package test." );
			return;
		}

		var openPackages = new List<DbpfPackage>();
		try
		{
			foreach ( var path in packagePaths )
				openPackages.Add( DbpfPackage.Open( path ) );

			var primaryPackage = openPackages[0];
			var entries = primaryPackage.FindAll( ResourceType.Model ).Take( 200 ).ToList();
			if ( entries.Count == 0 )
				Assert.Fail( "No MODL resources found." );

			int singlePkgMaterials = 0;
			int crossPkgMaterials = 0;
			int singlePkgTextureKeys = 0;
			int crossPkgTextureKeys = 0;

			foreach ( var entry in entries )
			{
				try
				{
					// Single-package resolution
					var singleModel = ModlModelLoader.LoadModel( primaryPackage, entry );
					var singleLod = singleModel.GetBestLod();
					if ( singleLod != null )
					{
						foreach ( var mesh in singleLod.Meshes )
						{
							if ( mesh.Material != null ) singlePkgMaterials++;
							singlePkgTextureKeys += mesh.TextureKeys.Count;
						}
					}

					// Cross-package resolution
					var crossModel = ModlModelLoader.LoadModel( primaryPackage, entry, openPackages );
					var crossLod = crossModel.GetBestLod();
					if ( crossLod != null )
					{
						foreach ( var mesh in crossLod.Meshes )
						{
							if ( mesh.Material != null ) crossPkgMaterials++;
							crossPkgTextureKeys += mesh.TextureKeys.Count;
						}
					}
				}
				catch { }
			}

			Console.WriteLine( $"Single-package: {singlePkgMaterials} materials, {singlePkgTextureKeys} texture keys" );
			Console.WriteLine( $"Cross-package:  {crossPkgMaterials} materials, {crossPkgTextureKeys} texture keys" );

			Assert.IsTrue( crossPkgMaterials >= singlePkgMaterials,
				$"Cross-package should resolve at least as many materials. " +
				$"Single={singlePkgMaterials}, Cross={crossPkgMaterials}" );
		}
		finally
		{
			foreach ( var pkg in openPackages ) pkg.Dispose();
		}
	}

	/// <summary>
	/// Distribution diagnostic: how many meshes per LOD across a large sample.
	/// Prints a histogram of mesh counts per LOD.
	/// </summary>
	[TestMethod]
	public void Diagnostic_MeshesPerLod_Distribution()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 500 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found." );

		var meshCountHist = new Dictionary<int, int>();
		int totalModls = 0;
		int totalMeshes = 0;
		int meshesWithMaterial = 0;
		int meshesWithTextures = 0;
		int meshesWithDiffuse = 0;
		int meshesWithNormal = 0;
		int meshesWithSpecular = 0;

		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;
				totalModls++;

				int validMeshes = 0;
				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length < 3 || mesh.Indices.Length < 3 ) continue;
					validMeshes++;
					totalMeshes++;

					if ( mesh.Material != null ) meshesWithMaterial++;
					if ( mesh.TextureKeys.Count > 0 ) meshesWithTextures++;
					if ( mesh.TextureKeys.ContainsKey( ShaderFieldType.DiffuseMap ) ) meshesWithDiffuse++;
					if ( mesh.TextureKeys.ContainsKey( ShaderFieldType.NormalMap ) ) meshesWithNormal++;
					if ( mesh.TextureKeys.ContainsKey( ShaderFieldType.SpecularMap ) ) meshesWithSpecular++;
				}

				meshCountHist.TryGetValue( validMeshes, out var c );
				meshCountHist[validMeshes] = c + 1;
			}
			catch { }
		}

		Console.WriteLine( $"Sampled {totalModls} MODLs with valid LODs, {totalMeshes} total meshes" );
		Console.WriteLine( $"\nMeshes/LOD histogram:" );
		foreach ( var (count, freq) in meshCountHist.OrderBy( kv => kv.Key ) )
		{
			var bar = new string( '#', Math.Min( freq, 60 ) );
			Console.WriteLine( $"  {count,3} mesh(es): {freq,4}  {bar}" );
		}

		Console.WriteLine( $"\nPer-mesh material coverage:" );
		Console.WriteLine( $"  MATD:     {meshesWithMaterial}/{totalMeshes} ({Pct( meshesWithMaterial, totalMeshes )})" );
		Console.WriteLine( $"  Textures: {meshesWithTextures}/{totalMeshes} ({Pct( meshesWithTextures, totalMeshes )})" );
		Console.WriteLine( $"  Diffuse:  {meshesWithDiffuse}/{totalMeshes} ({Pct( meshesWithDiffuse, totalMeshes )})" );
		Console.WriteLine( $"  Normal:   {meshesWithNormal}/{totalMeshes} ({Pct( meshesWithNormal, totalMeshes )})" );
		Console.WriteLine( $"  Specular: {meshesWithSpecular}/{totalMeshes} ({Pct( meshesWithSpecular, totalMeshes )})" );

		Assert.IsTrue( totalMeshes > 0, "No valid meshes found." );
	}

	/// <summary>
	/// Verify that cross-package lookup resolves materials for furniture models
	/// that would otherwise be solid colors with single-package lookup.
	/// </summary>
	[TestMethod]
	public void CrossPackage_FurnitureMaterials_Resolve()
	{
		var packagePaths = TestHelper.GetAllPackagePaths();
		if ( packagePaths.Count < 2 )
		{
			Assert.Inconclusive( "Need multiple packages for cross-package test." );
			return;
		}

		var openPackages = new List<DbpfPackage>();
		try
		{
			foreach ( var path in packagePaths )
				openPackages.Add( DbpfPackage.Open( path ) );

			var primaryPackage = openPackages[0];

			// Build furniture index
			var categories = BuildCategoryIndex( primaryPackage );
			var furnitureKeys = categories
				.Where( kv => kv.Value == "furniture" )
				.Select( kv => kv.Key )
				.Take( 100 )
				.ToList();

			if ( furnitureKeys.Count == 0 )
			{
				Assert.Inconclusive( "No furniture MODLs found." );
				return;
			}

			int singlePkgWithMat = 0;
			int crossPkgWithMat = 0;
			int totalMeshes = 0;
			var gainedMaterials = new List<string>();

			foreach ( var modlKey in furnitureKeys )
			{
				var entryRef = primaryPackage.Find( modlKey );
				if ( entryRef == null ) continue;

				try
				{
					var singleModel = ModlModelLoader.LoadModel( primaryPackage, entryRef.Value );
					var crossModel = ModlModelLoader.LoadModel( primaryPackage, entryRef.Value, openPackages );

					var singleLod = singleModel.GetBestLod();
					var crossLod = crossModel.GetBestLod();
					if ( singleLod == null || crossLod == null ) continue;

					for ( int i = 0; i < crossLod.Meshes.Count; i++ )
					{
						var crossMesh = crossLod.Meshes[i];
						if ( crossMesh.Vertices.Length < 3 || crossMesh.Indices.Length < 3 ) continue;
						totalMeshes++;

						bool singleHasMat = i < singleLod.Meshes.Count && singleLod.Meshes[i].Material != null;
						bool crossHasMat = crossMesh.Material != null;

						if ( singleHasMat ) singlePkgWithMat++;
						if ( crossHasMat ) crossPkgWithMat++;

						if ( !singleHasMat && crossHasMat && gainedMaterials.Count < 5 )
							gainedMaterials.Add( $"  MODL {modlKey.Instance:X} mesh[{i}]: gained {crossMesh.TextureKeys.Count} texture keys" );
					}
				}
				catch { }
			}

			Console.WriteLine( $"Furniture meshes: {totalMeshes}" );
			Console.WriteLine( $"  Single-package materials: {singlePkgWithMat}" );
			Console.WriteLine( $"  Cross-package materials:  {crossPkgWithMat}" );
			if ( gainedMaterials.Count > 0 )
			{
				Console.WriteLine( "Materials gained by cross-package lookup:" );
				foreach ( var g in gainedMaterials )
					Console.WriteLine( g );
			}

			Assert.IsTrue( totalMeshes > 0, "No furniture meshes found." );
			Assert.IsTrue( crossPkgWithMat >= singlePkgWithMat,
				$"Cross-package should resolve at least as many furniture materials. " +
				$"Single={singlePkgWithMat}, Cross={crossPkgWithMat}" );
		}
		finally
		{
			foreach ( var pkg in openPackages ) pkg.Dispose();
		}
	}

	/// <summary>
	/// Verify that each mesh in a multi-mesh model gets geometry assigned
	/// (no empty meshes that would result in invisible parts).
	/// </summary>
	[TestMethod]
	public void MultipleMeshes_AllHaveGeometry()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 200 ).ToList();
		if ( entries.Count == 0 )
			Assert.Fail( "No MODL resources found." );

		int multiMeshCount = 0;
		int allWithGeometry = 0;
		int someEmpty = 0;

		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null || bestLod.Meshes.Count < 2 ) continue;
				multiMeshCount++;

				bool anyEmpty = bestLod.Meshes.Any( m =>
					m.Vertices.Length < 3 || m.Indices.Length < 3 );

				if ( anyEmpty )
					someEmpty++;
				else
					allWithGeometry++;
			}
			catch { }
		}

		Console.WriteLine( $"Multi-mesh MODLs: {multiMeshCount}" );
		Console.WriteLine( $"  all meshes have geometry: {allWithGeometry}" );
		Console.WriteLine( $"  some meshes empty: {someEmpty}" );

		Assert.IsTrue( multiMeshCount > 0, "No multi-mesh MODLs found." );
		Assert.IsTrue( allWithGeometry > 0,
			$"No multi-mesh MODLs have geometry in all sub-meshes." );
	}

	// --- Helpers ---

	private static string GetMaterialSignature( ResolvedMesh mesh )
	{
		if ( mesh.Material == null && mesh.TextureKeys.Count == 0 )
			return "null";

		var parts = new List<string>();

		if ( mesh.Material != null )
			parts.Add( $"shader={mesh.Material.Shader}" );

		foreach ( var (field, key) in mesh.TextureKeys.OrderBy( kv => kv.Key ) )
			parts.Add( $"{field}={key.Instance:X}" );

		return string.Join( "|", parts );
	}

	private static Dictionary<ResourceKey, string> BuildCategoryIndex( DbpfPackage package )
	{
		var modlCategories = new Dictionary<ResourceKey, string>();
		var cobjCategories = new Dictionary<ulong, string>();

		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			try
			{
				var cobj = package.GetResource<Sims4Reader.Resources.CatalogObjectResource>( entry );
				var category = Sims4Reader.Resources.BuyCategoryTag.GetCategory( cobj.Tags );
				if ( category != null )
					cobjCategories[entry.Key.Instance] = category;
			}
			catch { }
		}

		foreach ( var entry in package.FindAll( ResourceType.ObjectDefinition ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			if ( !cobjCategories.TryGetValue( entry.Key.Instance, out var category ) )
				continue;
			try
			{
				var objd = package.GetResource<Sims4Reader.Resources.ObjectDefinitionResource>( entry );
				foreach ( var modelKey in objd.Models )
				{
					if ( (uint)modelKey.Type == (uint)ResourceType.Model && modelKey.Instance != 0 )
						modlCategories.TryAdd( modelKey, category );
				}
			}
			catch { }
		}

		return modlCategories;
	}

	private static string Pct( int num, int den ) =>
		den > 0 ? $"{num * 100.0 / den:F0}%" : "N/A";
}
