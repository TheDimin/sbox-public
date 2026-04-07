using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Mesh;

namespace Mounting.Sims4;

/// <summary>
/// Engine-level loader for MODL (Model) resources.
/// Resolves the MODL → MLOD → VBUF/IBUF/VRTF chain and builds a sandbox Model.
/// Supports multiple meshes per LOD, each with its own material.
/// Materials and textures are loaded directly from packages — no mount registration needed.
/// </summary>
public class ModlLoader( DbpfPackage package, ResourceEntry entry, IReadOnlyList<DbpfPackage> allPackages ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-ModlLoader" );

	protected override Task<object?> LoadAsync()
	{
		return Task.FromResult( Load() );
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

				try
				{
					var material = BuildMaterial( mesh );
					meshMaterials.Add( (mesh, material) );
				}
				catch ( Exception matEx )
				{
					Log.Warning( $"MODL {entry.Key}: failed to build material for mesh #{i} (0x{mesh.NameHash:X8}): {matEx}" );
				}
			}

			if ( meshMaterials.Count == 0 )
			{
				Log.Warning( $"MODL {entry.Key}: no meshes with valid geometry" );
				return null;
			}

			try
			{
				return ModlModelBuilder.Build( meshMaterials, name: Path );
			}
			catch ( Exception buildEx )
			{
				Log.Error( $"MODL {entry.Key}: ModlModelBuilder.Build crashed: {buildEx}" );
				return null;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load MODL {entry.Key}: {e}" );
			return null;
		}
	}

	/// <summary>
	/// Build the material for a mesh directly from its resolved data.
	/// Textures are loaded from packages, not from the mount system.
	/// </summary>
	private Material? BuildMaterial( ResolvedMesh mesh )
	{
		if ( mesh.Material == null )
		{
			Log.Error( $"MODL {entry.Key}: mesh 0x{mesh.NameHash:X8} has no material" );
			return null;
		}

		var name = mesh.MaterialMountPath ?? $"sims4_mat_{mesh.NameHash:X}";

		return Sims4MaterialLoader.BuildMaterial( name, mesh.Material, mesh.TextureKeys,
			key => Sims4TextureLoader.LoadFromPackages( key, package, allPackages ) );
	}
}
