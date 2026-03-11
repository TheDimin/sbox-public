using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Image;
using S4ResourceType = Sims4Reader.ResourceType;

namespace Mounting.Sims4;

public class Sims4TextureLoader( DbpfPackage package, ResourceEntry entry ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-TextureLoader" );

	protected override object? Load()
	{
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			var type = entry.Key.Type;

			if ( type == S4ResourceType.DstImage )
			{
				var dst = package.GetResource<DstImage>( entry );
				var dds = dst.ToDds();
				if ( dds == null || dds.Length == 0 )
					return null;

				return Sandbox.Mounting.TextureLoader.FromDds( dds );
			}

			if ( type == S4ResourceType.RleImage || type == S4ResourceType.RleImageAlt )
			{
				var rle = package.GetResource<RleImage>( entry );
				var dds = rle.ToDds();
				if ( dds == null || dds.Length == 0 )
					return null;

				return Sandbox.Mounting.TextureLoader.FromDds( dds );
			}

			// All other image types — try as raw DDS
			var bytes = package.GetBytes( entry );
			if ( bytes == null || bytes.Length == 0 )
				return null;

			return Sandbox.Mounting.TextureLoader.FromDds( bytes );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load texture {entry.Key}: {e.Message}" );
			return null;
		}
	}
}
