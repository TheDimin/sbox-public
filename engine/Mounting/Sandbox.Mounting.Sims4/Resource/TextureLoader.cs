using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Image;
using S4ResourceType = Sims4Reader.ResourceType;

namespace Mounting.Sims4;

public static class Sims4TextureLoader
{
	static readonly Logger Log = new Logger( "Sims4-TextureLoader" );

	/// <summary>
	/// Create a texture from DDS bytes using <see cref="ImageFormat.DXT1_ONEBITALPHA"/>
	/// instead of <see cref="ImageFormat.DXT1"/>. Both formats use identical BC1 block
	/// data — the only difference is that DXT1_ONEBITALPHA tells the GPU to preserve
	/// the 1-bit alpha in 3-color mode blocks (where index 3 = fully transparent).
	/// </summary>
	private static Texture? CreateDxt1WithAlpha( byte[] dds )
	{
		if ( dds.Length < 128 )
			return null;

		// Parse DDS header: offset 8=flags, 12=height, 16=width, 28=mipMapCount
		uint flags = BitConverter.ToUInt32( dds, 8 );
		int height = (int)BitConverter.ToUInt32( dds, 12 );
		int width = (int)BitConverter.ToUInt32( dds, 16 );
		int mipCount = (flags & 0x20000u) != 0 ? Math.Max( 1, (int)BitConverter.ToUInt32( dds, 28 ) ) : 1;

		const int dataStart = 128;
		const int bytesPerBlock = 8; // BC1: 8 bytes per 4×4 pixel block

		// Calculate per-mip sizes
		var mipSizes = new int[mipCount];
		int w = width, h = height;
		int totalBytes = 0;
		for ( int i = 0; i < mipCount; i++ )
		{
			int bw = Math.Max( 1, (w + 3) / 4 );
			int bh = Math.Max( 1, (h + 3) / 4 );
			mipSizes[i] = bw * bh * bytesPerBlock;
			totalBytes += mipSizes[i];
			w = Math.Max( 1, w >> 1 );
			h = Math.Max( 1, h >> 1 );
		}

		if ( dds.Length < dataStart + totalBytes )
			return null;

		// Reverse mip order (s&box expects smallest mip first, DDS has largest first)
		var result = new byte[totalBytes];
		for ( int i = 0, src = 0; i < mipCount; i++ )
		{
			int size = mipSizes[i];
			int dst = totalBytes - (src + size);
			Buffer.BlockCopy( dds, dataStart + src, result, dst, size );
			src += size;
		}

		Log.Info( "CreateDxt1WithAlpha" );

		return Texture.Create( width, height, ImageFormat.DXT1_ONEBITALPHA )
			.WithData( result )
			.WithMips( mipCount )
			.WithStaticUsage()
			.Finish();
	}

	/// <summary>
	/// Load a texture directly from a package entry without the mount system.
	/// </summary>
	public static Texture? LoadFromPackage( DbpfPackage package, ResourceEntry entry )
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

				// DST1 (BC1) textures may have 1-bit punch-through alpha
				if ( dst.Format == FourCC.DXT1 || dst.Format == FourCC.DST1 )
					return CreateDxt1WithAlpha( dds ) ?? Sandbox.Mounting.TextureLoader.FromDds( dds );

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

	/// <summary>
	/// Find and load a texture by resource key, searching primary package first then all packages.
	/// </summary>
	public static Texture? LoadFromPackages( ResourceKey key, DbpfPackage primaryPackage, IReadOnlyList<DbpfPackage> allPackages )
	{
		// Try primary package first
		var entry = primaryPackage.Find( key );
		if ( entry.HasValue )
			return LoadFromPackage( primaryPackage, entry.Value );

		// Search all other packages
		foreach ( var pkg in allPackages )
		{
			if ( ReferenceEquals( pkg, primaryPackage ) )
				continue;

			entry = pkg.Find( key );
			if ( entry.HasValue )
				return LoadFromPackage( pkg, entry.Value );
		}

		return null;
	}
}
