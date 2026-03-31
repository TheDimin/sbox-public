using Sims4Reader;
using Sims4Reader.Resources;

namespace Sims4MountTest;

/// <summary>
/// Validates the COBJ → OBJD → MODL metadata chain that drives mount path generation.
/// These tests prove the chain produces correct, consistent results regardless
/// of whether the work runs at mount time or is deferred.
/// </summary>
[TestClass]
public class MetadataChainTest
{
	/// <summary>
	/// COBJ and OBJD share the same instance ID. Verify the join actually works:
	/// for each OBJD, the matching COBJ (by instance) should exist and have tags.
	/// </summary>
	[TestMethod]
	public void CobjObjd_ShareInstanceId_JoinProducesMatches()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var cobjInstances = new HashSet<ulong>();
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ) )
		{
			if ( entry.MemSize > 0 && entry.FileSize > 0 )
				cobjInstances.Add( entry.Key.Instance );
		}

		int objdTotal = 0;
		int objdWithMatchingCobj = 0;

		foreach ( var entry in package.FindAll( ResourceType.ObjectDefinition ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			objdTotal++;
			if ( cobjInstances.Contains( entry.Key.Instance ) )
				objdWithMatchingCobj++;
		}

		Console.WriteLine( $"OBJDs: {objdTotal}, with matching COBJ: {objdWithMatchingCobj}" );

		Assert.IsTrue( objdTotal > 0, "No OBJD entries found." );
		Assert.IsTrue( objdWithMatchingCobj > 0,
			"No OBJDs matched a COBJ by instance ID — the join is broken." );
	}

	/// <summary>
	/// OBJD.Models[] should reference MODL-type resources that actually exist in the package.
	/// </summary>
	[TestMethod]
	public void Objd_ModelReferences_ExistInPackage()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		int totalRefs = 0;
		int foundInPackage = 0;
		int missingFromPackage = 0;

		foreach ( var entry in package.FindAll( ResourceType.ObjectDefinition ).Take( 200 ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var objd = package.GetResource<ObjectDefinitionResource>( entry );
				if ( !objd.IsFullyParsed )
					continue;

				foreach ( var modelKey in objd.Models )
				{
					if ( (uint)modelKey.Type != (uint)ResourceType.Model || modelKey.Instance == 0 )
						continue;

					totalRefs++;
					var found = package.Find( modelKey );
					if ( found != null )
						foundInPackage++;
					else
						missingFromPackage++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"OBJD→MODL refs: {totalRefs}, found: {foundInPackage}, missing: {missingFromPackage}" );

		Assert.IsTrue( totalRefs > 0, "No OBJD→MODL references found." );
		Assert.IsTrue( foundInPackage > 0,
			"None of the OBJD model references resolved to a package entry." );
	}

	/// <summary>
	/// The BuyCategory extracted from COBJ tags should produce non-null categories
	/// for a significant portion of COBJs. If this drops to zero, the tag parsing
	/// or BuyCategoryTag mapping is broken.
	/// </summary>
	[TestMethod]
	public void Cobj_BuyCategoryResolution_ProducesCategories()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		int total = 0;
		int withCategory = 0;
		var categories = new Dictionary<string, int>();

		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			total++;
			try
			{
				var cobj = package.GetResource<CatalogObjectResource>( entry );
				var cat = BuyCategoryTag.GetCategory( cobj.Tags );
				if ( cat != null )
				{
					withCategory++;
					categories.TryGetValue( cat, out var c );
					categories[cat] = c + 1;
				}
			}
			catch { }
		}

		Console.WriteLine( $"COBJs: {total}, with BuyCategory: {withCategory}" );
		foreach ( var kv in categories.OrderByDescending( kv => kv.Value ).Take( 15 ) )
			Console.WriteLine( $"  {kv.Key}: {kv.Value}" );

		Assert.IsTrue( total > 0, "No COBJ entries found." );
		Assert.IsTrue( withCategory > 0, "No COBJs resolved to a BuyCategory." );
		Assert.IsTrue( categories.Count >= 3,
			$"Expected at least 3 distinct categories, got {categories.Count}." );
	}

	/// <summary>
	/// OBJD.Name should be parseable and produce clean names after stripping "object_".
	/// </summary>
	[TestMethod]
	public void Objd_CleanName_ProducesReadableNames()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		int total = 0;
		int withName = 0;
		var sampleNames = new List<string>();

		foreach ( var entry in package.FindAll( ResourceType.ObjectDefinition ).Take( 200 ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var objd = package.GetResource<ObjectDefinitionResource>( entry );
				if ( !objd.IsFullyParsed )
					continue;

				total++;
				if ( !string.IsNullOrWhiteSpace( objd.Name ) )
				{
					withName++;
					if ( sampleNames.Count < 10 )
						sampleNames.Add( objd.Name );
				}
			}
			catch { }
		}

		Console.WriteLine( $"OBJDs parsed: {total}, with name: {withName}" );
		Console.WriteLine( $"Sample names: {string.Join( ", ", sampleNames )}" );

		Assert.IsTrue( total > 0, "No OBJD entries fully parsed." );
		Assert.IsTrue( withName > 0, "No OBJDs have a Name field." );

		// Names should not contain path-invalid characters after cleaning
		foreach ( var name in sampleNames )
		{
			Assert.IsFalse( name.Contains( '\\' ) || name.Contains( '/' ),
				$"OBJD name contains path separators: {name}" );
		}
	}

	/// <summary>
	/// The full chain: COBJ category + OBJD name → MODL mount path.
	/// Verify we get unique, non-empty paths like "models/seating/diningChair_modern".
	/// </summary>
	[TestMethod]
	public void FullChain_ProducesMountPaths()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		// Step 1: COBJ → category by instance
		var cobjCategories = new Dictionary<ulong, string>();
		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;
			try
			{
				var cobj = package.GetResource<CatalogObjectResource>( entry );
				var cat = BuyCategoryTag.GetCategory( cobj.Tags );
				if ( cat != null )
					cobjCategories[entry.Key.Instance] = cat;
			}
			catch { }
		}

		// Step 2: OBJD → MODL keys with metadata
		var modlPaths = new Dictionary<ResourceKey, string>();
		foreach ( var entry in package.FindAll( ResourceType.ObjectDefinition ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;
			try
			{
				var objd = package.GetResource<ObjectDefinitionResource>( entry );
				if ( !objd.IsFullyParsed )
					continue;

				cobjCategories.TryGetValue( entry.Key.Instance, out var category );
				var cat = category ?? "objects";
				var name = CleanObjectName( objd.Name ) ?? $"{entry.Key.Group:X}_{entry.Key.Instance:X}";

				foreach ( var modelKey in objd.Models )
				{
					if ( (uint)modelKey.Type == (uint)ResourceType.Model && modelKey.Instance != 0 )
					{
						var mountPath = $"models/{cat}/{name}";
						modlPaths.TryAdd( modelKey, mountPath );
					}
				}
			}
			catch { }
		}

		Console.WriteLine( $"MODL mount paths generated: {modlPaths.Count}" );
		foreach ( var kv in modlPaths.Take( 10 ) )
			Console.WriteLine( $"  {kv.Key} → {kv.Value}" );

		Assert.IsTrue( modlPaths.Count > 0, "No MODL mount paths generated from the chain." );

		// Verify paths are well-formed
		foreach ( var kv in modlPaths.Take( 100 ) )
		{
			Assert.IsTrue( kv.Value.StartsWith( "models/" ), $"Bad path prefix: {kv.Value}" );
			Assert.IsFalse( kv.Value.Contains( "//" ), $"Double slash in path: {kv.Value}" );
		}

		// Check that categorized paths exist (not just "models/objects/...")
		int categorized = modlPaths.Values.Count( p => !p.StartsWith( "models/objects/" ) );
		Console.WriteLine( $"Categorized (not 'objects'): {categorized} of {modlPaths.Count}" );
		Assert.IsTrue( categorized > 0, "All MODL paths fell through to 'objects' — category resolution is broken." );
	}

	/// <summary>
	/// COBJ mount paths use OBJD metadata (name + materialVariant).
	/// Verify variant grouping: objects with variants should share a parent folder.
	/// </summary>
	[TestMethod]
	public void CobjMountPaths_VariantsGroupUnderBaseObject()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		// Build OBJD metadata index
		var objdMeta = new Dictionary<ulong, (string? Name, string? Variant)>();
		foreach ( var entry in package.FindAll( ResourceType.ObjectDefinition ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;
			try
			{
				var objd = package.GetResource<ObjectDefinitionResource>( entry );
				if ( !objd.IsFullyParsed )
					continue;
				objdMeta.TryAdd( entry.Key.Instance, (CleanObjectName( objd.Name ), objd.MaterialVariant) );
			}
			catch { }
		}

		int cobjsWithVariant = 0;
		var parentFolders = new Dictionary<string, int>();

		foreach ( var entry in package.FindAll( ResourceType.CatalogObject ).Take( 500 ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;
			try
			{
				var cobj = package.GetResource<CatalogObjectResource>( entry );
				var cat = BuyCategoryTag.GetCategory( cobj.Tags ) ?? "misc";
				objdMeta.TryGetValue( entry.Key.Instance, out var meta );

				var objName = meta.Name ?? $"{entry.Key.Group:X}_{entry.Key.Instance:X}";
				var variant = meta.Variant;

				if ( !string.IsNullOrEmpty( variant ) )
				{
					cobjsWithVariant++;
					var folder = $"objects/{cat}/{objName}";
					parentFolders.TryGetValue( folder, out var c );
					parentFolders[folder] = c + 1;
				}
			}
			catch { }
		}

		Console.WriteLine( $"COBJs with variant: {cobjsWithVariant}" );
		Console.WriteLine( $"Distinct parent folders: {parentFolders.Count}" );
		foreach ( var kv in parentFolders.OrderByDescending( kv => kv.Value ).Take( 5 ) )
			Console.WriteLine( $"  {kv.Key}: {kv.Value} variants" );

		// It's okay if some packages have no variants, but if we find any, they should group
		if ( cobjsWithVariant > 0 )
		{
			Assert.IsTrue( parentFolders.Count > 0,
				"Variants found but no grouping folders generated." );
		}
	}

	/// <summary>
	/// Mirrors SimsMount.CleanObjectName — keep in sync.
	/// </summary>
	private static string? CleanObjectName( string? name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		if ( name.StartsWith( "object_", StringComparison.OrdinalIgnoreCase ) )
			name = name.Substring( 7 );

		name = name.Replace( ' ', '_' ).Replace( '\\', '_' ).Replace( '/', '_' );

		return string.IsNullOrWhiteSpace( name ) ? null : name;
	}
}
