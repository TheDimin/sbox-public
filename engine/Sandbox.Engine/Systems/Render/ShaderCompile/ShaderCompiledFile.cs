using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Engine.Shaders;

/// <summary>Publishes compiled shaders without truncating files held by mapped readers.</summary>
internal static class ShaderCompiledFile
{
	internal static async Task WriteAsync( string destination, byte[] bytes, CancellationToken token )
	{
		var temporary = destination + "." + System.Guid.NewGuid().ToString( "N" ) + ".tmp";
		System.Exception failure = null;
		var operation = "write temporary file";
		try
		{
			await using ( var stream = new FileStream( temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous ) )
			{
				await stream.WriteAsync( bytes, token );
				await stream.FlushAsync( token );
			}
			token.ThrowIfCancellationRequested();
			// Same-directory rename keeps the old file alive for existing mapped
			// readers. New opens see the complete replacement, never partial output.
			operation = "publish replacement";
			if ( File.Exists( destination ) )
				File.Replace( temporary, destination, null );
			else
				File.Move( temporary, destination );
		}
		catch ( System.Exception error )
		{
			failure = error;
			if ( error is IOException or System.UnauthorizedAccessException )
			{
				failure = new IOException( $"Failed to {operation} for compiled shader '{destination}' using '{temporary}' (HRESULT 0x{error.HResult:X8}).", error );
				throw failure;
			}
			throw;
		}
		finally
		{
			try
			{
				File.Delete( temporary );
			}
			catch ( System.Exception cleanupError ) when ( cleanupError is IOException or System.UnauthorizedAccessException )
			{
				if ( failure is not null )
					failure.Data["ShaderTemporaryCleanupFailure"] = $"Could not remove '{temporary}': {cleanupError}";
				else
					throw new IOException( $"Could not remove temporary shader file '{temporary}' after publishing '{destination}'.", cleanupError );
			}
		}
	}
}
