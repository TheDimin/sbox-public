using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Rcol;

namespace Mounting.Sims4;

/// <summary>
/// Loads a material from a MATD chunk embedded inside a MODL RCOL container.
/// Used for inline materials that are not standalone package entries.
///
/// Shares the same material-building logic as Sims4MaterialLoader.
/// The difference is that this loader re-parses the parent MODL RCOL
/// and extracts the material for a specific mesh by index.
/// </summary>
public class InlineMaterialLoader( DbpfPackage package, ResourceEntry modlEntry, int meshIndex, IReadOnlyList<DbpfPackage> allPackages ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-InlineMatLoader" );

	protected override object? Load()
	{
		try
		{
			var model = ModlModelLoader.LoadModel( package, modlEntry, allPackages );
			var bestLod = model.GetBestLod();
			if ( bestLod == null )
				return null;

			if ( meshIndex < 0 || meshIndex >= bestLod.Meshes.Count )
				return null;

			var mesh = bestLod.Meshes[meshIndex];
			if ( mesh.Material == null )
				return null;

			return Sims4MaterialLoader.BuildMaterialFromMatd( Path, mesh.Material, mesh.TextureKeys );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load inline material from MODL {modlEntry.Key} mesh {meshIndex}: {e.Message}" );
			return null;
		}
	}
}
