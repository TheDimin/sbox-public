using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using MountDirectory = Sandbox.Mounting.Directory;

namespace Editor;

public static partial class EditorUtility
{
	public static partial class Mounting
	{
		// Each build has its own directory. Windows can keep the previous native image
		// mapped until references to its collectible load context have been released.
		sealed class DevelopmentMountContext( string build ) : AssemblyLoadContext( isCollectible: true )
		{
			protected override Assembly Load( AssemblyName name ) => null;

			protected override nint LoadUnmanagedDll( string name )
			{
				if ( Path.GetFileName( name ) != name ) return 0;
				var path = Path.Combine( build, "bin", "win64", name + ".dll" );
				return File.Exists( path ) ? LoadUnmanagedDllFromPath( path ) : 0;
			}
		}

		static readonly System.Threading.SemaphoreSlim ReloadLock = new( 1 );

		/// <summary>Replace a locally built mount from a private build directory in the editor.</summary>
		public static async Task ReloadDevelopmentBuild( string ident, string build )
		{
			await ReloadLock.WaitAsync();
			try
			{
				var previous = Get( ident ) ?? throw new ArgumentException( "Unknown mount", nameof( ident ) );
				var previousAssembly = previous.GetType().Assembly;
				build = Path.GetFullPath( build );
				var context = new DevelopmentMountContext( build );
				var mounted = previous.IsMounted;
				Assembly replacement = null;
				try
				{
					replacement = context.LoadFromAssemblyPath( Path.Combine( build, "mounts", ident, ident + ".dll" ) );
					var type = replacement.GetType( previous.GetType().FullName, throwOnError: true );
					if ( !typeof( Sandbox.Mounting.BaseGameMount ).IsAssignableFrom( type ) )
						throw new InvalidDataException( "The new assembly does not contain the mount type." );
					if ( mounted ) await MountDirectory.SetMountState( ident, false );
					MountDirectory.RemoveAssembly( previousAssembly );
					MountDirectory.AddAssembly( replacement, exposeMountTypes: true );
					if ( Get( ident )?.GetType().Assembly != replacement )
						throw new InvalidDataException( "The new mount failed to register." );
					if ( mounted ) await MountDirectory.SetMountState( ident, true );
				}
				catch
				{
					if ( replacement is not null && Get( ident )?.GetType().Assembly != previousAssembly )
					{
						MountDirectory.RemoveAssembly( replacement );
						MountDirectory.AddAssembly( previousAssembly, exposeMountTypes: true );
					}
					if ( mounted && Get( ident )?.IsMounted == false ) await MountDirectory.SetMountState( ident, true );
					context.Unload();
					throw;
				}

				if ( AssemblyLoadContext.GetLoadContext( previousAssembly ) is DevelopmentMountContext old )
					old.Unload();
			}
			finally
			{
				ReloadLock.Release();
			}
		}
	}
}
