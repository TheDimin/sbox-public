using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Tests that detect how many MATD materials are inline (embedded in MODL RCOLs)
/// vs external (standalone package entries). This drives the fix to mount inline
/// MATDs so ModlLoader can load them from mount paths.
/// </summary>
[TestClass]
public class InlineMaterialTest
{
	/// <summary>
	/// Count standalone MATD entries in the package index vs MATD chunks
	/// embedded inside MODL RCOLs. Shows the inline vs external distribution.
	/// </summary>
	[TestMethod]
	public void DetectInlineVsExternalMatdDistribution()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		// Count standalone MATD entries in the package index
		int standaloneMATDs = 0;
		foreach ( var entry in package.FindAll( ResourceType.MaterialDefinition ) )
		{
			if ( entry.MemSize > 0 && entry.FileSize > 0 )
				standaloneMATDs++;
		}

		// Count MODL entries and their material reference types
		int totalModls = 0;
		int totalMeshes = 0;
		int inlineMaterials = 0;   // Public/Private chunk refs
		int externalMaterials = 0; // Delayed (external) refs
		int noMaterialRef = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			totalModls++;

			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length < 3 || mesh.Indices.Length < 3 )
						continue;

					totalMeshes++;

					if ( mesh.MaterialResourceKey != null )
						externalMaterials++;
					else if ( mesh.Material != null )
						inlineMaterials++;
					else
						noMaterialRef++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"Package: {System.IO.Path.GetFileName( packagePath )}" );
		Console.WriteLine( $"Standalone MATD entries in package index: {standaloneMATDs}" );
		Console.WriteLine( $"MODL entries: {totalModls}" );
		Console.WriteLine( $"Total meshes (with geometry): {totalMeshes}" );
		Console.WriteLine( $"  Inline materials (embedded in MODL RCOL): {inlineMaterials}" );
		Console.WriteLine( $"  External materials (delayed ref, has ResourceKey): {externalMaterials}" );
		Console.WriteLine( $"  No material at all: {noMaterialRef}" );
		Console.WriteLine( $"  Inline %: {(totalMeshes > 0 ? inlineMaterials * 100.0 / totalMeshes : 0):F1}%" );

		Assert.IsTrue( totalMeshes > 0, "No MODL meshes found" );
		// This test documents the problem: most materials are inline
		Console.WriteLine( $"\nConclusion: {inlineMaterials} of {totalMeshes} meshes have INLINE materials that need mounting." );
	}

	/// <summary>
	/// For MODL entries with inline MATDs, verify we can generate deterministic
	/// mount paths using the MODL entry key + chunk index.
	/// </summary>
	[TestMethod]
	public void InlineMATDs_CanGenerateDeterministicMountPaths()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var mountPaths = new HashSet<string>();
		int duplicates = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				for ( int i = 0; i < bestLod.Meshes.Count; i++ )
				{
					var mesh = bestLod.Meshes[i];
					if ( mesh.Vertices.Length < 3 || mesh.Indices.Length < 3 )
						continue;
					if ( mesh.Material == null )
						continue;

					string path;
					if ( mesh.MaterialResourceKey is { } matKey )
					{
						// External: use the MATD's own resource key
						path = $"materials/{matKey.Group:X}_{matKey.Instance:X}";
					}
					else
					{
						// Inline: use MODL key + mesh index for uniqueness
						path = $"materials/inline/{entry.Key.Group:X}_{entry.Key.Instance:X}_m{i}";
					}

					if ( !mountPaths.Add( path ) )
						duplicates++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"Total unique mount paths: {mountPaths.Count}" );
		Console.WriteLine( $"Duplicates: {duplicates}" );
		Console.WriteLine( $"Sample paths:" );
		foreach ( var p in mountPaths.Take( 10 ) )
			Console.WriteLine( $"  {p}" );

		Assert.IsTrue( mountPaths.Count > 0, "No mount paths generated" );
		Assert.AreEqual( 0, duplicates, "Mount path scheme generates duplicates!" );
	}

	/// <summary>
	/// Verify that after adding MaterialMountPath to ResolvedMesh,
	/// ALL meshes with materials get a non-null mount path.
	/// </summary>
	[TestMethod]
	public void AllMeshesWithMaterial_HaveMaterialMountPath()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		int total = 0;
		int withMountPath = 0;
		int withoutMountPath = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length < 3 || mesh.Indices.Length < 3 )
						continue;
					if ( mesh.Material == null )
						continue;

					total++;
					if ( !string.IsNullOrEmpty( mesh.MaterialMountPath ) )
						withMountPath++;
					else
						withoutMountPath++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"Meshes with material: {total}" );
		Console.WriteLine( $"  With MaterialMountPath: {withMountPath}" );
		Console.WriteLine( $"  Without MaterialMountPath: {withoutMountPath}" );

		Assert.IsTrue( total > 0, "No meshes with materials found" );
		Assert.AreEqual( 0, withoutMountPath,
			$"{withoutMountPath} of {total} meshes have a material but no MaterialMountPath!" );
	}
}
