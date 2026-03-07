using Sandbox;
using Sandbox.Mounting.Sims4;
using Sandbox.Diagnostics;

/// <summary>
/// Loads a DDS texture from a record inside a Sims 4 .package file.
///
/// Sims 4 stores textures (type ID 0x00B2D882) as raw DDS data blocks.
/// The decompressed bytes are a complete DDS file including the "DDS " header,
/// so we can hand them directly to TextureLoader.FromDds.
/// </summary>
class Sims4TextureLoader( DbpfPackage package, DbpfRecord record ) : ResourceLoader<GameMount>
{
	private static new Logger Log = new Logger( "Sims4-TextureLoader" );

	protected override object Load()
	{
		try
		{
			Log.Trace( $"Loading texture 0x{record.InstanceId:X16}" );
			var ddsData = package.ReadData( record );
			ddsData = DstImageConverter.ConvertToDxtIfDst( ddsData );
			Log.Trace( $"Read {ddsData.Length} bytes of DDS data for 0x{record.InstanceId:X16}" );

			var texture = TextureLoader.FromDds( ddsData );
			Log.Trace( $"Successfully loaded texture 0x{record.InstanceId:X16}" );
			return texture;
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"Failed to load texture 0x{record.InstanceId:X16}: {ex.Message}" );
			throw;
		}
	}
}
