using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Resources;

namespace Mounting.Sims4;

public class CatalogObjectLoader( DbpfPackage package, ResourceEntry entry ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-CatalogObjectLoader" );

	protected override object? Load()
	{
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			return package.GetResource<CatalogObjectResource>( entry );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load catalog object {entry.Key}: {e.Message}" );
			return null;
		}
	}
}
