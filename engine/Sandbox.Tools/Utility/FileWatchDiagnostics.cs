using System;
using System.IO;

namespace Editor;

internal static class FileWatchDiagnostics
{
	[ConCmd( "fs_watch_status", ConVarFlags.Protected )]
	internal static void Print()
	{
		if ( OperatingSystem.IsLinux() ) PrintKernelCounts();
		foreach ( var line in BaseFileSystem.GetWatchDiagnostics().Concat( SharedPhysicalFileWatcher.GetDiagnostics() ) )
			Log.Info( line );
	}

	private static void PrintKernelCounts()
	{
		try
		{
			var descriptors = Directory.GetFiles( "/proc/self/fd" );
			var inotifyDescriptors = 0;
			var watches = 0;
			foreach ( var descriptor in descriptors )
			{
				try
				{
					if ( new FileInfo( descriptor ).LinkTarget is not ("anon_inode:inotify" or "anon_inode:[inotify]") ) continue;
					inotifyDescriptors++;
					watches += File.ReadLines( $"/proc/self/fdinfo/{Path.GetFileName( descriptor )}" ).Count( x => x.StartsWith( "inotify ", StringComparison.Ordinal ) );
				}
				catch ( IOException ) { } // A descriptor can close while we're taking the snapshot.
			}

			Log.Info( $"Process descriptors={descriptors.Length}, inotify descriptors={inotifyDescriptors}, watch entries={watches}" );
			Log.Info( $"User limits: instances={File.ReadAllText( "/proc/sys/fs/inotify/max_user_instances" ).Trim()}, watches={File.ReadAllText( "/proc/sys/fs/inotify/max_user_watches" ).Trim()}. Limits are shared with other processes for this user." );
			var descriptorLimit = File.ReadLines( "/proc/self/limits" ).FirstOrDefault( x => x.StartsWith( "Max open files", StringComparison.Ordinal ) );
			if ( descriptorLimit != null ) Log.Info( $"Process limit: {descriptorLimit.Trim()}" );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Couldn't read kernel watcher counts ({e.Message})" );
		}
	}
}
