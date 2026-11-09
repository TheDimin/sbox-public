using Sandbox.Gltf;
using SkiaSharp;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Sandbox;

/// <summary>
/// Bounded generated cache of exact native texture resources. Source content,
/// transfer, mip demand and producer/codec identity form the key; payloads are
/// checked on every read and publication is atomic. An unavailable cache is a
/// miss, never a reason to change texture quality or reject a valid source.
/// </summary>
internal sealed class GltfPreparedTextureCache
{
	private const long MaximumResourceBytes = 256L * 1024 * 1024;
	private static ReadOnlySpan<byte> Magic => "GLTFTX01"u8;
	private readonly string directory;
	private readonly long maxBytes;
	private readonly Func<string> identity;
	private readonly object sync = new();
	private long hits, misses, bypassed, writtenBytes, writtenCount;
	private static readonly Lazy<string> ProducerIdentity = new( FindProducerIdentity );
	internal static readonly GltfPreparedTextureCache Shared = new( Path.Combine( Path.GetTempPath(), "sbox-gltf-prepared-v1-" + Convert.ToHexStringLower( SHA256.HashData( Encoding.UTF8.GetBytes( Environment.UserName ) ) ) ), 2L * 1024 * 1024 * 1024, () => ProducerIdentity.Value );

	internal GltfPreparedTextureCache( string directory, long maxBytes, Func<string> identity )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( directory ); ArgumentOutOfRangeException.ThrowIfNegative( maxBytes );
		this.directory = directory; this.maxBytes = maxBytes; this.identity = identity ?? throw new ArgumentNullException( nameof( identity ) );
	}

	internal (long Hits, long Misses, long Bypassed, long Bytes, int Count) GetStats()
	{
		lock ( sync ) return (hits, misses, bypassed, writtenBytes, checked((int)writtenCount));
	}

	internal byte[] GetOrCreate( GltfImage image, bool srgb, bool mipmaps, Func<byte[]> create )
	{
		ArgumentNullException.ThrowIfNull( image ); ArgumentNullException.ThrowIfNull( image.Bytes ); ArgumentNullException.ThrowIfNull( create );
		// Basis uses a different native codec and is intentionally uncached until
		// that codec has an equally strong loaded-binary identity.
		if ( image.MimeType is not ("image/jpeg" or "image/png" or "image/webp") ) { lock ( sync ) bypassed++; return create(); }
		var producer = identity();
		if ( string.IsNullOrEmpty( producer ) || !EnsureDirectory() ) { lock ( sync ) bypassed++; return create(); }
		using var keyHash = IncrementalHash.CreateHash( HashAlgorithmName.SHA256 );
		keyHash.AppendData( Encoding.UTF8.GetBytes( $"gltf-prepared-v1\0{producer}\0{image.MimeType}\0{srgb}\0{mipmaps}\0" ) );
		keyHash.AppendData( image.Bytes );
		var path = Path.Combine( directory, Convert.ToHexStringLower( keyHash.GetHashAndReset() ) + ".gltftex" );
		var cached = TryRead( path );
		if ( cached is not null ) { lock ( sync ) hits++; return cached; }
		lock ( sync ) misses++;
		var result = create() ?? throw new InvalidOperationException( "Texture preparation returned no resource." );
		TryWrite( path, result );
		return result;
	}

	private byte[] TryRead( string path )
	{
		try
		{
			using var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete );
			Span<byte> header = stackalloc byte[48]; stream.ReadExactly( header );
			var length = BinaryPrimitives.ReadInt64LittleEndian( header[8..] );
			if ( !header[..8].SequenceEqual( Magic ) || length <= 0 || length > MaximumResourceBytes || stream.Length != 48 + length ) return null;
			var result = new byte[(int)length]; stream.ReadExactly( result );
			return SHA256.HashData( result ).AsSpan().SequenceEqual( header[16..] ) ? result : null;
		}
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException ) { return null; }
	}

	private void TryWrite( string path, byte[] result )
	{
		if ( result.Length == 0 || result.LongLength > MaximumResourceBytes || result.LongLength + 48 > maxBytes ) return;
		string temporary = null;
		try
		{
			// One bounded publisher across processes; a busy cache is simply skipped.
			using var publication = new FileStream( Path.Combine( directory, ".publication.lock" ), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None );
			long used = 0;
			foreach ( var existing in Directory.EnumerateFiles( directory, "*.gltftex" ) )
			{
				if ( string.Equals( existing, path, StringComparison.Ordinal ) ) continue;
				used = checked(used + new FileInfo( existing ).Length);
				if ( used > maxBytes - result.LongLength - 48 ) return;
			}
			temporary = Path.Combine( directory, Guid.NewGuid().ToString( "N" ) + ".tmp" );
			Span<byte> header = stackalloc byte[48]; Magic.CopyTo( header ); BinaryPrimitives.WriteInt64LittleEndian( header[8..], result.LongLength ); SHA256.HashData( result ).CopyTo( header[16..] );
			using ( var stream = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None ) ) { stream.Write( header ); stream.Write( result ); stream.Flush(); }
			File.Move( temporary, path, true ); temporary = null;
			lock ( sync ) { writtenBytes += result.LongLength; writtenCount++; }
		}
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException or OverflowException ) { }
		finally { if ( temporary is not null ) try { File.Delete( temporary ); } catch ( IOException ) { } catch ( UnauthorizedAccessException ) { } }
	}

	private bool EnsureDirectory()
	{
		try
		{
			if ( OperatingSystem.IsWindows() ) Directory.CreateDirectory( directory );
			else Directory.CreateDirectory( directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute );
			if ( (File.GetAttributes( directory ) & FileAttributes.ReparsePoint) != 0 ) return false;
			if ( !OperatingSystem.IsWindows() )
			{
				var privateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
				File.SetUnixFileMode( directory, privateMode );
				if ( File.GetUnixFileMode( directory ) != privateMode ) return false;
			}
			return true;
		}
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException or PlatformNotSupportedException ) { return false; }
	}

	private static string FindProducerIdentity()
	{
		try
		{
			// Force the exact native Skia module to be loaded before identifying it.
			using var colorSpace = SKColorSpace.CreateSrgb();
			var expected = OperatingSystem.IsWindows() ? "libSkiaSharp.dll" : OperatingSystem.IsMacOS() ? "libSkiaSharp.dylib" : "libSkiaSharp.so";
			using var process = Process.GetCurrentProcess();
			var paths = process.Modules.Cast<ProcessModule>().Select( x => x.FileName ).Where( x => string.Equals( Path.GetFileName( x ), expected, StringComparison.OrdinalIgnoreCase ) ).Distinct( StringComparer.Ordinal ).ToArray();
			if ( paths.Length != 1 ) return null;
			using var stream = File.OpenRead( paths[0] );
			return $"{typeof( GltfTextureCompiler ).Assembly.ManifestModule.ModuleVersionId}:{typeof( GltfDocument ).Assembly.ManifestModule.ModuleVersionId}:{typeof( SKCodec ).Assembly.ManifestModule.ModuleVersionId}:{Convert.ToHexStringLower( SHA256.HashData( stream ) )}";
		}
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or NotSupportedException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or InvalidOperationException or ArgumentException or System.Security.SecurityException ) { return null; }
	}
}
