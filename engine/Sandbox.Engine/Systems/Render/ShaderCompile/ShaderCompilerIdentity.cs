using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sandbox.Engine.Shaders;

/// <summary>
/// Identifies the installed shader toolchain by its content, using the checksums supplied by Facepunch.
/// The caller keeps this identity for the lifetime of the loaded compiler.
/// </summary>
internal static class ShaderCompilerIdentity
{
	static readonly (string Name, bool Required)[] Binaries =
	[
		("dxcompiler", true),
		("resourcecompiler", false),
		("slang-compiler", false),
		("slang-glsl-module", false),
		("slang-glslang", false),
		("slang-rt", false),
		("slang", false),
		("vfx_vulkan", true)
	];

	internal static string Read( string gameDirectory )
	{
		var nativeDirectory = Path.GetFullPath( Path.Combine( gameDirectory, NetCore.NativeDllPath ) );
		var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
		var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		var names = Binaries.Select( binary => Sandbox.Interop.GetNativeLibraryName( binary.Name ) ).ToHashSet( comparer );

		try
		{
			using var process = System.Diagnostics.Process.GetCurrentProcess();
			foreach ( System.Diagnostics.ProcessModule module in process.Modules )
			{
				var name = Path.GetFileName( module.FileName );
				if ( !names.Contains( name ) )
					continue;

				var expected = Path.Combine( nativeDirectory, name );
				if ( !string.Equals( Path.GetFullPath( module.FileName ), expected, comparison ) )
				{
					Log.Warning( $"Shader compiler module '{name}' is loaded from '{module.FileName}', not '{expected}'. Shader cache reuse is disabled." );
					return null;
				}
			}
		}
		catch ( Exception error ) when ( error is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException or IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Cannot check loaded shader compiler module paths. Shader cache reuse is disabled: {error.Message}" );
			return null;
		}

		// This is an installed-file snapshot, after rejecting modules loaded from another directory.
		// It does not prove that a mapped file's inode has not been replaced since the module loaded.
		return Read( gameDirectory, message => Log.Warning( message ) );
	}

	/// <summary>
	/// Returns null when a required binary cannot be read. No native compiler or logger is required by this overload.
	/// </summary>
	internal static string Read( string gameDirectory, Action<string> warning )
		=> Read( gameDirectory, NetCore.NativeDllPath, Sandbox.Interop.GetNativeLibraryName, warning );

	/// <summary>Uses the same native directory and filename mapping as the engine loader.</summary>
	internal static string Read( string gameDirectory, string nativeDirectory, Func<string, string> libraryName, Action<string> warning )
	{
		var binaryDirectory = Path.GetFullPath( Path.Combine( gameDirectory, nativeDirectory ) );
		var names = Binaries.Select( binary => (Name: libraryName( binary.Name ), binary.Required) ).ToArray();
		var manifestPrefix = "game/" + Path.GetRelativePath( gameDirectory, binaryDirectory ).Replace( '\\', '/' ).TrimEnd( '/' ) + "/";
		var manifest = ReadManifest( Path.Combine( gameDirectory, "bin", ".sbox-artifacts.json" ), manifestPrefix, names.Select( binary => binary.Name ) );
		var identity = new StringBuilder();
		string currentPath = null;

		try
		{
			foreach ( var (name, required) in names )
			{
				currentPath = Path.Combine( binaryDirectory, name );
				identity.Append( name ).Append( ':' );
				if ( !required && !File.Exists( currentPath ) )
				{
					identity.Append( "absent\n" );
					continue;
				}

				using var stream = new FileStream( currentPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan );
				var actual = Convert.ToHexString( SHA256.HashData( stream ) ).ToLowerInvariant();

				// A manifest can remain after a local binary update. Never use its checksum without checking the file.
				// Missing or stale manifests still yield the same identity for the same installed compiler.
				if ( manifest.TryGetValue( name, out var expected ) && string.Equals( expected, actual, StringComparison.OrdinalIgnoreCase ) )
					identity.Append( expected.ToLowerInvariant() );
				else
					identity.Append( actual );

				identity.Append( '\n' );
			}

			return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( identity.ToString() ) ) ).ToLowerInvariant();
		}
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException )
		{
			warning?.Invoke( $"Cannot read shader compiler binary '{currentPath}'. Shader cache reuse is disabled: {error.Message}" );
			return null;
		}
	}

	static Dictionary<string, string> ReadManifest( string path, string prefix, IEnumerable<string> names )
	{
		var hashes = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		try
		{
			using var stream = File.OpenRead( path );
			using var document = JsonDocument.Parse( stream );
			if ( document.RootElement.ValueKind != JsonValueKind.Object
				|| !document.RootElement.TryGetProperty( "files", out var files )
				|| files.ValueKind != JsonValueKind.Array )
				return hashes;

			foreach ( var file in files.EnumerateArray() )
			{
				if ( file.ValueKind != JsonValueKind.Object
					|| !file.TryGetProperty( "path", out var filePath ) || filePath.ValueKind != JsonValueKind.String
					|| !file.TryGetProperty( "sha256", out var hash ) || hash.ValueKind != JsonValueKind.String )
					continue;

				var relativePath = filePath.GetString().Replace( '\\', '/' );
				foreach ( var name in names )
				{
					if ( string.Equals( relativePath, prefix + name, StringComparison.OrdinalIgnoreCase ) )
						hashes[name] = hash.GetString();
				}
			}
		}
		catch ( Exception error ) when ( error is IOException or UnauthorizedAccessException or JsonException )
		{
			// The installed bytes remain authoritative when the optional artifact manifest is unavailable.
		}

		return hashes;
	}
}
