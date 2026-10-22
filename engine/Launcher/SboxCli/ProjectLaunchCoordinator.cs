using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Cli;

/// <summary>
/// Serializes CLI launches for one project until its owner exits. Other clients can attach
/// as soon as discovery succeeds, without acquiring ownership of the editor or its job.
/// Editors started outside the CLI do not participate until their MCP endpoint is discoverable.
/// </summary>
internal static class ProjectLaunchCoordinator
{
	internal static string ProjectKey( string project )
	{
		var path = Path.GetFullPath( project );
		if ( OperatingSystem.IsWindows() ) path = path.ToUpperInvariant();
		return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( path ) ) );
	}

	/// <summary>
	/// Return a launch lease only when no editor is discoverable and no other CLI owns a
	/// launch for this project. Keep the lease until the owned process has been cleaned up.
	/// A null result means that findRunning attached to an existing editor.
	/// </summary>
	internal static async Task<IDisposable> WaitForLaunchAsync( string project, Func<CancellationToken, Task<bool>> findRunning,
		TimeSpan timeout, CancellationToken cancellation, string lockDirectory = null )
	{
		lockDirectory ??= Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), "sbox", "cli", "launch-locks" );
		Directory.CreateDirectory( lockDirectory );
		// The key deliberately excludes the engine: normal discovery attaches to a project
		// regardless of which installation started it, and two installations must not race.
		var path = Path.Combine( lockDirectory, ProjectKey( project ) + ".lock" );
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource( cancellation );
		deadline.CancelAfter( timeout );

		try
		{
			while ( true )
			{
				deadline.Token.ThrowIfCancellationRequested();
				if ( await findRunning( deadline.Token ) ) return null;

				FileStream lease = null;
				try
				{
					try
					{
						lease = new FileStream( path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None );
					}
					catch ( IOException e ) when ( (e.HResult & 0xffff) is 32 or 33 or 11 )
					{
						// Another CLI owns the handle. Process exit releases it even after a crash.
					}

					if ( lease is not null )
					{
						deadline.Token.ThrowIfCancellationRequested();
						// Recheck inside the gate: the previous owner may have completed startup
						// between our first discovery and opening the lock file.
						if ( await findRunning( deadline.Token ) ) return null;
						var ownership = lease;
						lease = null;
						return ownership;
					}
				}
				finally
				{
					lease?.Dispose();
				}

				await Task.Delay( 100, deadline.Token );
			}
		}
		catch ( OperationCanceledException ) when ( !cancellation.IsCancellationRequested )
		{
			throw new TimeoutException( "Timed out waiting for another CLI to finish opening this project. Check the editor startup logs or increase --startup-timeout." );
		}
		// Do not delete the lock file: replacing its inode can allow two independent locks.
	}
}
