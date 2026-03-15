using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Mesh;

namespace Mounting.Sims4;

public class ModelLoader( DbpfPackage package, ResourceEntry entry ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-ModelLoader" );

	protected override object? Load()
	{
		// Skip zero-byte tombstone entries from delta packages
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var geomChunk = rcol.GetChunk<GeometryRcolChunk>();
			if ( geomChunk == null )
				return null;

			return GeomModelBuilder.Build( geomChunk.Geometry, name: Path );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load model {entry.Key}: {e.Message}" );
			return null;
		}
	}
}
