using System.Collections.Generic;
using System.Linq;
using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Mesh;

namespace Mounting.Sims4;

/// <summary>
/// Engine-level loader for MODL (Model) resources.
/// Resolves the MODL → MLOD → VBUF/IBUF/VRTF chain and builds a sandbox Model.
/// Supports multiple meshes per LOD, each with its own material.
/// Materials are always loaded from the mount system via their MATD resource key.
/// </summary>
public class ModlLoader( DbpfPackage package, ResourceEntry entry, IReadOnlyList<DbpfPackage> allPackages ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-ModlLoader" );

	protected override async Task<object?> LoadAsync()
	{
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			var resolved = ModlModelLoader.LoadModel( package, entry, allPackages );

			if ( resolved.Lods.Count == 0 )
			{
				Log.Warning( $"MODL {entry.Key}: no LODs resolved" );
				return null;
			}

			var bestLod = resolved.GetBestLod();
			if ( bestLod == null || bestLod.Meshes.Count == 0 )
			{
				Log.Warning( $"MODL {entry.Key}: best LOD has no meshes" );
				return null;
			}

			var validMeshes = bestLod.Meshes
				.Where( m => m.Vertices.Length >= 3 && m.Indices.Length >= 3 )
				.ToList();

			if ( validMeshes.Count == 0 )
			{
				Log.Warning( $"MODL {entry.Key}: no meshes with valid geometry" );
				return null;
			}

			// Kick off ALL material loads concurrently so each Sims4MaterialLoader.LoadAsync()
			// can in turn load its textures in parallel (via BuildMaterialFromMatdAsync).
			var materialTasks = validMeshes
				.Select( m => LoadMaterialAsync( m ) )
				.ToList();

			await Task.WhenAll( materialTasks );

			var meshMaterials = validMeshes
				.Zip( materialTasks, ( mesh, task ) => (mesh, task.Result) )
				.ToList();

			return ModlModelBuilder.Build( meshMaterials, name: Path );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load MODL {entry.Key}: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Load the material for a mesh asynchronously via <see cref="Material.LoadAsync"/>,
	/// which routes through <see cref="Sims4MaterialLoader.LoadAsync"/> and loads all
	/// textures concurrently.
	/// </summary>
	private async Task<Material?> LoadMaterialAsync( ResolvedMesh mesh )
	{
		if ( string.IsNullOrEmpty( mesh.MaterialMountPath ) )
		{
			Log.Error( $"MODL {entry.Key}: mesh 0x{mesh.NameHash:X8} has no MaterialMountPath" );
			return null;
		}

		var mountPath = $"mount://sims4/{mesh.MaterialMountPath}.vmat";
		var material = await Material.LoadAsync( mountPath );

		if ( material == null || !material.IsValid )
		{
			Log.Error( $"MODL {entry.Key}: mesh 0x{mesh.NameHash:X8} failed to load material from {mountPath}" );
			return null;
		}

		return material;
	}

	protected override object? Load()
	{
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			var resolved = ModlModelLoader.LoadModel( package, entry, allPackages );

			if ( resolved.Lods.Count == 0 )
			{
				Log.Warning( $"MODL {entry.Key}: no LODs resolved" );
				return null;
			}

			var bestLod = resolved.GetBestLod();
			if ( bestLod == null || bestLod.Meshes.Count == 0 )
			{
				Log.Warning( $"MODL {entry.Key}: best LOD has no meshes" );
				return null;
			}

			var meshMaterials = new List<(ResolvedMesh Mesh, Material? Material)>();

			for ( int i = 0; i < bestLod.Meshes.Count; i++ )
			{
				var mesh = bestLod.Meshes[i];
				if ( mesh.Vertices.Length < 3 || mesh.Indices.Length < 3 )
					continue;

				var material = LoadMaterial( mesh );
				meshMaterials.Add( (mesh, material) );
			}

			if ( meshMaterials.Count == 0 )
			{
				Log.Warning( $"MODL {entry.Key}: no meshes with valid geometry" );
				return null;
			}

			return ModlModelBuilder.Build( meshMaterials, name: Path );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load MODL {entry.Key}: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Load the material for a mesh from the mount system via its MATD resource key.
	/// </summary>
	private Material? LoadMaterial( ResolvedMesh mesh )
	{
		if ( string.IsNullOrEmpty( mesh.MaterialMountPath ) )
		{
			Log.Error( $"MODL {entry.Key}: mesh 0x{mesh.NameHash:X8} has no MaterialMountPath" );
			return null;
		}

		var mountPath = $"mount://sims4/{mesh.MaterialMountPath}.vmat";
		var material = Material.Load( mountPath );

		if ( material == null || !material.IsValid )
		{
			Log.Error( $"MODL {entry.Key}: mesh 0x{mesh.NameHash:X8} failed to load material from {mountPath}" );
			return null;
		}

		return material;
	}
}
