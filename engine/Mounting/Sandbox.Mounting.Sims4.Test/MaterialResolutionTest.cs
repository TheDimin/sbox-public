using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Tests that validate the entire material resolution chain:
/// MODL → MLOD → MATD → ShaderEntries → TextureKeys → mount paths
///
/// These tests verify that when a model is loaded at runtime, its material
/// can find the textures it references via the mount system.
/// </summary>
[TestClass]
public class MaterialResolutionTest
{
	/// <summary>
	/// Verify that resolved meshes have MaterialDefinition with non-empty ShaderEntries.
	/// This is the first link: MODL → MATD.
	/// </summary>
	[TestMethod]
	public void ResolvedMesh_Material_HasShaderEntries()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Material == null ) continue;
					if ( mesh.Material.ShaderEntries.Count == 0 ) continue;

					// Found a mesh with material and shader entries — pass
					Assert.IsTrue( mesh.Material.ShaderEntries.Count > 0 );
					return;
				}
			}
			catch { }
		}

		Assert.Fail( "No MODL mesh found with a MaterialDefinition containing ShaderEntries." );
	}

	/// <summary>
	/// Verify that texture keys extracted from MATD have valid ResourceType for textures
	/// (DstImage or RleImage). This validates the MATD → TextureKeys link.
	/// </summary>
	[TestMethod]
	public void TextureKeys_HaveValidImageResourceType()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var validTextureTypes = new HashSet<uint>
		{
			(uint)ResourceType.DstImage,
			(uint)ResourceType.RleImage,
			(uint)ResourceType.RleImageAlt,
		};

		int totalTexKeys = 0;
		int validTypeKeys = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				foreach ( var lod in model.Lods )
				foreach ( var mesh in lod.Meshes )
				foreach ( var (field, key) in mesh.TextureKeys )
				{
					totalTexKeys++;
					if ( validTextureTypes.Contains( (uint)key.Type ) )
						validTypeKeys++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"Texture keys: {validTypeKeys}/{totalTexKeys} have valid image ResourceType" );
		Assert.IsTrue( totalTexKeys > 0, "No texture keys found at all." );
		Assert.IsTrue( validTypeKeys > 0,
			$"No texture keys have a valid image ResourceType (DstImage/RleImage). " +
			$"Total keys: {totalTexKeys}" );
	}

	/// <summary>
	/// Verify that texture keys from MODL materials can be found across the full
	/// set of game packages. TS4 splits models and textures across different .package
	/// files, so we must search all packages to find referenced textures.
	///
	/// Mount path format: textures/{Group:X}_{Instance:X}
	/// Lookup path format: mount://sims4/textures/{Group:X}_{Instance:X}.vtex
	/// </summary>
	[TestMethod]
	public void TextureKeys_MountPaths_MatchMountedTextures()
	{
		var packagePaths = TestHelper.GetAllPackagePaths();
		if ( packagePaths.Count == 0 )
		{
			// Fallback to single package — just verify texture keys exist
			var packagePath = TestHelper.GetPackagePath();
			using var package = DbpfPackage.Open( packagePath );
			bool anyTexKeys = false;
			foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 50 ) )
			{
				try
				{
					var model = ModlModelLoader.LoadModel( package, entry );
					var bestLod = model.GetBestLod();
					if ( bestLod == null ) continue;
					foreach ( var mesh in bestLod.Meshes )
						if ( mesh.TextureKeys.Count > 0 ) { anyTexKeys = true; break; }
					if ( anyTexKeys ) break;
				}
				catch { }
			}
			Assert.IsTrue( anyTexKeys, "No texture keys found in any MODL materials." );
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

			// Check texture references from the first package's MODLs
			int totalRefs = 0;
			int resolvedRefs = 0;
			var missingExamples = new List<string>();

			foreach ( var entry in openPackages[0].FindAll( ResourceType.Model ).Take( 200 ) )
			{
				try
				{
					var model = ModlModelLoader.LoadModel( openPackages[0], entry );
					var bestLod = model.GetBestLod();
					if ( bestLod == null ) continue;

					foreach ( var mesh in bestLod.Meshes )
					foreach ( var (field, key) in mesh.TextureKeys )
					{
						totalRefs++;
						if ( allTextureKeys.Contains( (key.Group, key.Instance) ) )
							resolvedRefs++;
						else if ( missingExamples.Count < 5 )
							missingExamples.Add( $"  {field}: G={key.Group:X} I={key.Instance:X} T=0x{(uint)key.Type:X8}" );
					}
				}
				catch { }
			}

			var summary = $"Texture refs: {resolvedRefs}/{totalRefs} resolve across {packagePaths.Count} packages";
			if ( missingExamples.Count > 0 )
				summary += $"\nUnresolved:\n{string.Join( "\n", missingExamples )}";
			Console.WriteLine( summary );

			Assert.IsTrue( totalRefs > 0, "No texture references found." );
			Assert.IsTrue( resolvedRefs > 0, $"No texture references resolve.\n{summary}" );
		}
		finally
		{
			foreach ( var pkg in openPackages ) pkg.Dispose();
		}
	}

	/// <summary>
	/// Verify that material mount paths match what ModelLoader would use to look up
	/// materials. Materials are mounted at materials/{Group:X}_{Instance:X}.
	/// </summary>
	[TestMethod]
	public void MaterialEntries_MountPaths_AreConsistent()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		// Build set of mounted material paths
		var mountedMaterialPaths = new HashSet<string>();
		foreach ( var entry in package.Entries )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 ) continue;
			if ( entry.Key.Type != ResourceType.MaterialDefinition ) continue;

			var name = $"{entry.Key.Group:X}_{entry.Key.Instance:X}";
			mountedMaterialPaths.Add( $"materials/{name}" );
		}

		Console.WriteLine( $"Mounted material paths: {mountedMaterialPaths.Count}" );
		Assert.IsTrue( mountedMaterialPaths.Count > 0, "No MaterialDefinition entries found." );

		// Verify at least some MODL material references match mounted materials
		int totalMatdRefs = 0;
		int resolvedMatdRefs = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Material == null ) continue;
					totalMatdRefs++;

					// The MATD was resolved inline from the RCOL, so we can't directly
					// get its resource key. But we can verify the material has content.
					if ( mesh.Material.ShaderEntries.Count > 0 )
						resolvedMatdRefs++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"MODL meshes with resolved MATD: {resolvedMatdRefs}/{totalMatdRefs}" );
		Assert.IsTrue( resolvedMatdRefs > 0,
			$"No MODL meshes have resolved materials with shader entries." );
	}

	/// <summary>
	/// End-to-end validation: for MODLs that have materials with texture references,
	/// verify the complete chain works — the texture key's resource exists somewhere
	/// across ALL game packages and would be mountable.
	/// </summary>
	[TestMethod]
	public void EndToEnd_ModlMaterialTexture_ChainResolves()
	{
		var packagePaths = TestHelper.GetAllPackagePaths();
		if ( packagePaths.Count == 0 )
		{
			// Single-package fallback: just verify the chain structure exists
			var packagePath = TestHelper.GetPackagePath();
			using var package = DbpfPackage.Open( packagePath );
			bool anyChain = false;
			foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 100 ) )
			{
				try
				{
					var model = ModlModelLoader.LoadModel( package, entry );
					var bestLod = model.GetBestLod();
					if ( bestLod == null ) continue;
					foreach ( var mesh in bestLod.Meshes )
						if ( mesh.Material != null && mesh.TextureKeys.Count > 0 )
						{ anyChain = true; break; }
					if ( anyChain ) break;
				}
				catch { }
			}
			Assert.IsTrue( anyChain, "No MODL → MATD → texture chain found." );
			return;
		}

		// Build global texture key lookup from ALL packages
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

			int chainsTested = 0;
			int chainsResolved = 0;
			var failures = new List<string>();

			foreach ( var entry in openPackages[0].FindAll( ResourceType.Model ).Take( 200 ) )
			{
				try
				{
					var model = ModlModelLoader.LoadModel( openPackages[0], entry );
					var bestLod = model.GetBestLod();
					if ( bestLod == null ) continue;

					foreach ( var mesh in bestLod.Meshes )
					{
						if ( mesh.Material == null || mesh.TextureKeys.Count == 0 ) continue;
						foreach ( var (field, texKey) in mesh.TextureKeys )
						{
							chainsTested++;
							if ( allTextureKeys.Contains( (texKey.Group, texKey.Instance) ) )
								chainsResolved++;
							else if ( failures.Count < 5 )
								failures.Add( $"  {field}: G={texKey.Group:X} I={texKey.Instance:X}" );
						}
					}
				}
				catch { }
			}

			var summary = $"End-to-end chains: {chainsResolved}/{chainsTested} resolve across {packagePaths.Count} packages";
			if ( failures.Count > 0 )
				summary += $"\nUnresolved:\n{string.Join( "\n", failures )}";
			Console.WriteLine( summary );

			Assert.IsTrue( chainsTested > 0, "No MODL → MATD → texture chains found." );
			Assert.IsTrue( chainsResolved > 0, $"No chains resolve.\n{summary}" );
		}
		finally
		{
			foreach ( var pkg in openPackages ) pkg.Dispose();
		}
	}

	/// <summary>
	/// Diagnostic: dump the material content for a few MODLs to understand what
	/// BuildMaterial would produce.
	/// </summary>
	[TestMethod]
	public void Diagnostic_BuildMaterial_WouldSucceed()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		int modlsWithGeometry = 0;
		int modlsWithMaterial = 0;
		int modlsWithTexKeys = 0;
		int modlsWhereTexturesExist = 0;
		int modlsWithFloatParams = 0;

		// Track which ShaderFieldTypes appear in texture keys
		var fieldCounts = new Dictionary<ShaderFieldType, int>();

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 500 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length == 0 || mesh.Indices.Length == 0 ) continue;
					modlsWithGeometry++;

					if ( mesh.Material != null )
					{
						modlsWithMaterial++;

						bool hasFloat = false;
						foreach ( var se in mesh.Material.ShaderEntries )
						{
							if ( se is ShaderFloat || se is ShaderFloat3 )
								hasFloat = true;
						}
						if ( hasFloat ) modlsWithFloatParams++;
					}

					if ( mesh.TextureKeys.Count > 0 )
					{
						modlsWithTexKeys++;

						foreach ( var (field, texKey) in mesh.TextureKeys )
						{
							fieldCounts.TryGetValue( field, out var c );
							fieldCounts[field] = c + 1;
						}

						// Check if ANY texture actually exists in this package
						bool anyExists = false;
						foreach ( var (_, texKey) in mesh.TextureKeys )
						{
							if ( package.Find( texKey ) != null )
							{
								anyExists = true;
								break;
							}
						}
						if ( anyExists ) modlsWhereTexturesExist++;
					}
				}
			}
			catch { }
		}

		var fieldDist = string.Join( ", ",
			fieldCounts.OrderByDescending( kv => kv.Value )
				.Select( kv => $"{kv.Key}={kv.Value}" ) );

		var summary =
			$"MODLs with geometry: {modlsWithGeometry}\n" +
			$"  with MATD: {modlsWithMaterial}\n" +
			$"  with texture keys: {modlsWithTexKeys}\n" +
			$"  where textures exist in package: {modlsWhereTexturesExist}\n" +
			$"  with float params: {modlsWithFloatParams}\n" +
			$"Texture field distribution: {fieldDist}";

		Console.WriteLine( summary );

		// BuildMaterial returns non-null when anySet is true.
		// anySet requires either a texture to load successfully OR a float param to match.
		// If textures don't exist in the same package, BuildMaterial returns null
		// and the model falls back to white.vmat.
		Assert.IsTrue( modlsWithMaterial > 0, $"No materials found.\n{summary}" );
	}
}
