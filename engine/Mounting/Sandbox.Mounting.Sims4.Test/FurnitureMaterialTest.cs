using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Resources;

namespace Sims4MountTest;

/// <summary>
/// Tests that validate furniture (BuyCat) models have valid materials
/// with texture references that resolve across the full set of game packages.
///
/// Addresses the runtime issue: furniture models appear white because
/// BuildMaterial returns null when no textures load from the same package
/// and no matching float shader params exist.
/// </summary>
[TestClass]
public class FurnitureMaterialTest
{
	/// <summary>
	/// Build the COBJ→OBJD→MODL category chain and return categorized MODL keys.
	/// Mirrors SimsMount.BuildCategoryIndex logic.
	/// </summary>
	private static Dictionary<ResourceKey, string> BuildCategoryIndex( DbpfPackage package )
	{
		var modlCategories = new Dictionary<ResourceKey, string>();
		var cobjCategories = new Dictionary<ulong, string>();

		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			try
			{
				var cobj = package.GetResource<CatalogObjectResource>( entry );
				var category = BuyCategoryTag.GetCategory( cobj.Tags );
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
				var objd = package.GetResource<ObjectDefinitionResource>( entry );
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

	/// <summary>
	/// Verify that categorized furniture MODLs have MaterialDefinition with shader entries.
	/// This is the MODL → MATD link for furniture specifically.
	/// </summary>
	[TestMethod]
	public void FurnitureModls_HaveMaterialDefinitions()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var categories = BuildCategoryIndex( package );
		var furnitureKeys = categories
			.Where( kv => kv.Value == "furniture" )
			.Select( kv => kv.Key )
			.ToList();

		Console.WriteLine( $"Categorized MODLs: {categories.Count} total, {furnitureKeys.Count} furniture" );

		int furnitureFound = 0;
		int withMaterial = 0;
		int withShaderEntries = 0;
		int withTextureKeys = 0;

		foreach ( var modlKey in furnitureKeys )
		{
			var entry = package.Find( modlKey );
			if ( entry == null ) continue;

			try
			{
				var model = ModlModelLoader.LoadModel( package, entry.Value );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 ) continue;
					furnitureFound++;

					if ( mesh.Material != null )
					{
						withMaterial++;
						if ( mesh.Material.ShaderEntries.Count > 0 )
							withShaderEntries++;
					}

					if ( mesh.TextureKeys.Count > 0 )
						withTextureKeys++;
				}
			}
			catch { }
		}

		var summary = $"Furniture meshes: {furnitureFound}\n" +
			$"  with MATD: {withMaterial}\n" +
			$"  with shader entries: {withShaderEntries}\n" +
			$"  with texture keys: {withTextureKeys}";
		Console.WriteLine( summary );

		Assert.IsTrue( furnitureFound > 0, $"No furniture MODL meshes found.\nCategories: {categories.Count}" );
		Assert.IsTrue( withMaterial > 0, $"No furniture meshes have MaterialDefinition.\n{summary}" );
	}

	/// <summary>
	/// Verify that furniture MODL texture keys resolve to actual texture resources
	/// across ALL game packages. TS4 splits textures and models across packages.
	/// </summary>
	[TestMethod]
	public void FurnitureModls_TextureKeys_ResolveAcrossPackages()
	{
		var packagePaths = TestHelper.GetAllPackagePaths();
		if ( packagePaths.Count == 0 )
		{
			Assert.Inconclusive( "No multi-package test data available." );
			return;
		}

		// Build global texture index from ALL packages
		var allTextureKeys = new HashSet<(uint group, ulong instance)>();
		var openPackages = new List<DbpfPackage>();
		try
		{
			foreach ( var path in packagePaths )
			{
				var pkg = DbpfPackage.Open( path );
				openPackages.Add( pkg );
				foreach ( var entry in pkg.Entries )
				{
					if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
					switch ( entry.Key.Type )
					{
						case ResourceType.DstImage:
						case ResourceType.RleImage:
						case ResourceType.RleImageAlt:
							allTextureKeys.Add( (entry.Key.Group, entry.Key.Instance) );
							break;
					}
				}
			}

			Console.WriteLine( $"Global texture index: {allTextureKeys.Count} textures across {packagePaths.Count} packages" );

			// Build category index from first package (where models typically are)
			var categories = BuildCategoryIndex( openPackages[0] );
			var furnitureKeys = categories
				.Where( kv => kv.Value == "furniture" )
				.Select( kv => kv.Key )
				.ToList();

			Console.WriteLine( $"Furniture MODLs: {furnitureKeys.Count}" );

			int totalTexRefs = 0;
			int resolvedTexRefs = 0;
			var missingExamples = new List<string>();
			var fieldDistribution = new Dictionary<ShaderFieldType, int>();

			foreach ( var modlKey in furnitureKeys.Take( 200 ) )
			{
				var entry = openPackages[0].Find( modlKey );
				if ( entry == null ) continue;

				try
				{
					var model = ModlModelLoader.LoadModel( openPackages[0], entry.Value );
					var bestLod = model.GetBestLod();
					if ( bestLod == null ) continue;

					foreach ( var mesh in bestLod.Meshes )
					foreach ( var (field, key) in mesh.TextureKeys )
					{
						totalTexRefs++;
						fieldDistribution.TryGetValue( field, out var c );
						fieldDistribution[field] = c + 1;

						if ( allTextureKeys.Contains( (key.Group, key.Instance) ) )
							resolvedTexRefs++;
						else if ( missingExamples.Count < 5 )
							missingExamples.Add( $"  {field}: G={key.Group:X} I={key.Instance:X} T=0x{(uint)key.Type:X8}" );
					}
				}
				catch { }
			}

			var fieldDist = string.Join( ", ",
				fieldDistribution.OrderByDescending( kv => kv.Value )
					.Select( kv => $"{kv.Key}={kv.Value}" ) );
			var summary = $"Furniture texture refs: {resolvedTexRefs}/{totalTexRefs} resolve\n" +
				$"Field distribution: {fieldDist}";
			if ( missingExamples.Count > 0 )
				summary += $"\nUnresolved examples:\n{string.Join( "\n", missingExamples )}";
			Console.WriteLine( summary );

			Assert.IsTrue( totalTexRefs > 0, $"No texture refs found in furniture MODLs." );
			Assert.IsTrue( resolvedTexRefs > 0, $"No furniture texture refs resolve.\n{summary}" );
		}
		finally
		{
			foreach ( var pkg in openPackages ) pkg.Dispose();
		}
	}

	/// <summary>
	/// Diagnostic: For each buy category, check how many MODLs have materials
	/// and whether BuildMaterial would succeed at runtime.
	///
	/// BuildMaterial returns non-null when anySet=true, which requires either:
	/// 1. A texture to load successfully from mount://sims4/textures/...
	/// 2. A float shader param to match (Shininess, Diffuse, EmissiveBloom, NormalMapScale)
	///
	/// If textures are in different packages AND no float params match,
	/// BuildMaterial returns null → white model.
	/// </summary>
	[TestMethod]
	public void Diagnostic_PerCategory_MaterialReadiness()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var categories = BuildCategoryIndex( package );

		// Per-category stats
		var stats = new Dictionary<string, (int meshes, int withMat, int withTexKeys, int samePackageTex, int withFloatParams)>();

		foreach ( var (modlKey, category) in categories )
		{
			var entry = package.Find( modlKey );
			if ( entry == null ) continue;

			if ( !stats.ContainsKey( category ) )
				stats[category] = (0, 0, 0, 0, 0);

			try
			{
				var model = ModlModelLoader.LoadModel( package, entry.Value );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 ) continue;

					var s = stats[category];
					s.meshes++;

					if ( mesh.Material != null )
					{
						s.withMat++;

						// Check for float params that would trigger anySet=true
						bool hasMatchingFloat = false;
						foreach ( var se in mesh.Material.ShaderEntries )
						{
							switch ( se )
							{
								case ShaderFloat3 when se.Field == ShaderFieldType.Diffuse:
								case ShaderFloat f when se.Field == ShaderFieldType.Shininess:
								case ShaderFloat f2 when se.Field == ShaderFieldType.EmissiveBloomMultiplier
									|| se.Field == ShaderFieldType.EmissiveLightMultiplier:
								case ShaderFloat f3 when se.Field == ShaderFieldType.NormalMapScale
									|| se.Field == ShaderFieldType.NormalBumpScale:
									hasMatchingFloat = true;
									break;
							}
						}
						if ( hasMatchingFloat ) s.withFloatParams++;
					}

					if ( mesh.TextureKeys.Count > 0 )
					{
						s.withTexKeys++;

						// Check if ANY texture exists in the same package
						bool anyInPackage = false;
						foreach ( var (_, texKey) in mesh.TextureKeys )
						{
							if ( package.Find( texKey ) != null )
							{ anyInPackage = true; break; }
						}
						if ( anyInPackage ) s.samePackageTex++;
					}

					stats[category] = s;
				}
			}
			catch { }
		}

		Console.WriteLine( "Category | Meshes | WithMATD | TexKeys | SamePkgTex | FloatParams | WouldBeWhite" );
		Console.WriteLine( new string( '-', 95 ) );

		int totalWhite = 0;
		int totalMeshes = 0;

		foreach ( var (cat, s) in stats.OrderByDescending( kv => kv.Value.meshes ) )
		{
			// BuildMaterial returns non-null when anySet=true.
			// anySet is true if: (a) any texture loads from mount, OR (b) any float param matches.
			// At test time we can't load from mount, but we can check if textures are in same package
			// or if float params exist. If neither → would be white.
			int wouldBeWhite = s.meshes - Math.Max( s.samePackageTex, s.withFloatParams );
			totalWhite += Math.Max( 0, wouldBeWhite );
			totalMeshes += s.meshes;

			Console.WriteLine(
				$"{cat,-14} | {s.meshes,6} | {s.withMat,8} | {s.withTexKeys,7} | {s.samePackageTex,10} | {s.withFloatParams,11} | {Math.Max( 0, wouldBeWhite )}" );
		}

		Console.WriteLine( $"\nTotal: {totalMeshes} meshes, {totalWhite} would be white (same-package only)" );
		Console.WriteLine( "NOTE: At runtime, mount://sims4/ resolves textures across ALL packages." );
		Console.WriteLine( "So 'WouldBeWhite' count is worst-case if mounting hasn't loaded textures yet." );

		Assert.IsTrue( totalMeshes > 0, "No categorized meshes found." );
	}

	/// <summary>
	/// Verify that furniture MODLs with materials have the shader field types
	/// that BuildMaterial knows how to map (DiffuseMap, NormalMap, SpecularMap, etc.)
	/// </summary>
	[TestMethod]
	public void FurnitureModls_TextureFields_AreMappable()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var categories = BuildCategoryIndex( package );
		var furnitureKeys = categories
			.Where( kv => kv.Value == "furniture" )
			.Select( kv => kv.Key )
			.ToList();

		// Fields that BuildMaterial maps to s&box shader params
		var mappableFields = new HashSet<ShaderFieldType>
		{
			ShaderFieldType.DiffuseMap,
			ShaderFieldType.NormalMap,
			ShaderFieldType.SpecularMap,
			ShaderFieldType.EmissionMap,
			ShaderFieldType.SelfIlluminationMap,
			ShaderFieldType.AlphaMap,
		};

		int totalTexKeys = 0;
		int mappableTexKeys = 0;
		int unmappableTexKeys = 0;
		var unmappedFields = new Dictionary<ShaderFieldType, int>();

		foreach ( var modlKey in furnitureKeys.Take( 200 ) )
		{
			var entry = package.Find( modlKey );
			if ( entry == null ) continue;

			try
			{
				var model = ModlModelLoader.LoadModel( package, entry.Value );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				foreach ( var (field, _) in mesh.TextureKeys )
				{
					totalTexKeys++;
					if ( mappableFields.Contains( field ) )
						mappableTexKeys++;
					else
					{
						unmappableTexKeys++;
						unmappedFields.TryGetValue( field, out var c );
						unmappedFields[field] = c + 1;
					}
				}
			}
			catch { }
		}

		var unmappedDist = string.Join( ", ",
			unmappedFields.OrderByDescending( kv => kv.Value )
				.Select( kv => $"{kv.Key}={kv.Value}" ) );

		Console.WriteLine( $"Furniture texture keys: {totalTexKeys}" );
		Console.WriteLine( $"  Mappable (BuildMaterial handles): {mappableTexKeys}" );
		Console.WriteLine( $"  Unmapped (skipped by BuildMaterial): {unmappableTexKeys}" );
		if ( unmappedFields.Count > 0 )
			Console.WriteLine( $"  Unmapped fields: {unmappedDist}" );

		Assert.IsTrue( totalTexKeys > 0, "No texture keys found in furniture MODLs." );
		Assert.IsTrue( mappableTexKeys > 0,
			$"No furniture texture keys map to known shader params.\n" +
			$"Unmapped: {unmappedDist}" );
	}

	/// <summary>
	/// End-to-end: Verify the full COBJ→OBJD→MODL→MATD→texture chain for ALL
	/// buy categories, checking that textures resolve across all packages.
	/// </summary>
	[TestMethod]
	public void AllCategories_EndToEnd_TextureChainResolves()
	{
		var packagePaths = TestHelper.GetAllPackagePaths();
		if ( packagePaths.Count == 0 )
		{
			Assert.Inconclusive( "No multi-package test data available." );
			return;
		}

		var allTextureKeys = new HashSet<(uint group, ulong instance)>();
		var openPackages = new List<DbpfPackage>();
		try
		{
			foreach ( var path in packagePaths )
			{
				var pkg = DbpfPackage.Open( path );
				openPackages.Add( pkg );
				foreach ( var entry in pkg.Entries )
				{
					if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
					switch ( entry.Key.Type )
					{
						case ResourceType.DstImage:
						case ResourceType.RleImage:
						case ResourceType.RleImageAlt:
							allTextureKeys.Add( (entry.Key.Group, entry.Key.Instance) );
							break;
					}
				}
			}

			var categories = BuildCategoryIndex( openPackages[0] );

			// Per-category resolution stats
			var catStats = new Dictionary<string, (int total, int resolved)>();

			foreach ( var (modlKey, category) in categories )
			{
				var entry = openPackages[0].Find( modlKey );
				if ( entry == null ) continue;

				if ( !catStats.ContainsKey( category ) )
					catStats[category] = (0, 0);

				try
				{
					var model = ModlModelLoader.LoadModel( openPackages[0], entry.Value );
					var bestLod = model.GetBestLod();
					if ( bestLod == null ) continue;

					foreach ( var mesh in bestLod.Meshes )
					foreach ( var (_, texKey) in mesh.TextureKeys )
					{
						var s = catStats[category];
						s.total++;
						if ( allTextureKeys.Contains( (texKey.Group, texKey.Instance) ) )
							s.resolved++;
						catStats[category] = s;
					}
				}
				catch { }
			}

			Console.WriteLine( "Category        | TexRefs | Resolved | Rate" );
			Console.WriteLine( new string( '-', 55 ) );

			int grandTotal = 0;
			int grandResolved = 0;

			foreach ( var (cat, s) in catStats.OrderByDescending( kv => kv.Value.total ) )
			{
				grandTotal += s.total;
				grandResolved += s.resolved;
				var rate = s.total > 0 ? (s.resolved * 100.0 / s.total).ToString( "F0" ) + "%" : "N/A";
				Console.WriteLine( $"{cat,-15} | {s.total,7} | {s.resolved,8} | {rate}" );
			}

			Console.WriteLine( $"\nTotal: {grandResolved}/{grandTotal} ({(grandTotal > 0 ? grandResolved * 100.0 / grandTotal : 0):F0}%)" );

			Assert.IsTrue( grandTotal > 0, "No texture refs found in any categorized MODLs." );
			Assert.IsTrue( grandResolved > 0, "No categorized MODL texture refs resolve across packages." );
		}
		finally
		{
			foreach ( var pkg in openPackages ) pkg.Dispose();
		}
	}
}
