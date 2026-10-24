using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Sandbox.Engine.Shaders;

/// <summary>
/// Records which compiler binaries produced a shader. Source and include validity still belongs to the native check.
/// A record is published only after the complete shader, and is bound to its bytes so a replaced or damaged output
/// cannot inherit the identity of an earlier compile.
/// </summary>
internal sealed class ShaderCompilerCache( string directory, string compilerIdentity )
{
	internal async Task<bool> IsCurrentAsync( string compiledPath, CancellationToken token )
	{
		if ( string.IsNullOrEmpty( compilerIdentity ) )
			return false;

		try
		{
			var recordPath = GetRecordPath( compiledPath );
			if ( !File.Exists( recordPath ) || new FileInfo( recordPath ).Length > 4096 )
				return false;

			var record = JsonSerializer.Deserialize<Record>( await File.ReadAllTextAsync( recordPath, token ) );
			if ( record?.Compiler != compilerIdentity || string.IsNullOrEmpty( record.Output ) )
				return false;

			await using var stream = new FileStream( compiledPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan );
			var hash = Convert.ToHexString( await SHA256.HashDataAsync( stream, token ) );
			return hash == record.Output;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or JsonException )
		{
			return false;
		}
	}

	internal async Task RecordAsync( string compiledPath, byte[] compiledBytes, CancellationToken token )
	{
		if ( string.IsNullOrEmpty( compilerIdentity ) )
			return;

		var record = new Record { Compiler = compilerIdentity, Output = Convert.ToHexString( SHA256.HashData( compiledBytes ) ) };
		Directory.CreateDirectory( directory );
		await ShaderCompiledFile.WriteAsync( GetRecordPath( compiledPath ), JsonSerializer.SerializeToUtf8Bytes( record ), token );
	}

	internal string GetRecordPath( string compiledPath )
	{
		var path = Path.GetFullPath( compiledPath );
		if ( OperatingSystem.IsWindows() )
			path = path.ToUpperInvariant();

		return Path.Combine( directory, Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( path ) ) ) + ".json" );
	}

	sealed class Record
	{
		public string Compiler { get; set; }
		public string Output { get; set; }
	}
}
