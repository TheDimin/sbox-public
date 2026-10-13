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
			if ( File.Exists( destination ) )
				File.Replace( temporary, destination, null );
			else
				File.Move( temporary, destination );
		}
		finally
		{
			File.Delete( temporary );
		}
	}
}
