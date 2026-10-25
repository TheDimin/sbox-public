using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Facepunch.Steps;

/// <summary>
/// Validates artifact paths and restores file aliases after their payloads are installed.
/// Links retain payload hashes for compatibility with older manifest readers.
/// </summary>
internal sealed class ArtifactLinks
{
	readonly string root;
	readonly Dictionary<string, ArtifactFileInfo> files;
	readonly List<ArtifactFileInfo> ordered = new();
	static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

	internal static ArtifactFileInfo[] Capture( string repositoryRoot, IEnumerable<(string RepoPath, string AbsolutePath)> candidates )
	{
		var entries = candidates.Select( item => new ArtifactFileInfo
		{
			Path = Normalize( item.RepoPath ),
			LinkTarget = new FileInfo( item.AbsolutePath ).LinkTarget is { } target ? Normalize( target ) : null
		} ).ToArray();
		_ = new ArtifactLinks( repositoryRoot, entries );
		return entries;
	}

	internal ArtifactLinks( string repositoryRoot, IEnumerable<ArtifactFileInfo> entries )
	{
		root = Path.TrimEndingDirectorySeparator( Path.GetFullPath( repositoryRoot ) );
		files = new Dictionary<string, ArtifactFileInfo>( Comparer );
		foreach ( var entry in entries )
		{
			var path = Normalize( entry.Path );
			Destination( path );
			if ( !files.TryAdd( path, entry ) )
				throw new InvalidOperationException( $"Duplicate artifact path: '{path}'." );
		}

		foreach ( var path in files.Keys )
		{
			for ( var parent = Path.GetDirectoryName( path ); !string.IsNullOrEmpty( parent ); parent = Path.GetDirectoryName( parent ) )
				if ( files.ContainsKey( Normalize( parent ) ) )
					throw new InvalidOperationException( $"Artifact is also a parent directory: '{parent}'." );
		}

		var complete = new HashSet<string>( Comparer );
		foreach ( var entry in files.Values.Where( x => x.LinkTarget is not null ) )
		{
			var chain = new List<ArtifactFileInfo>();
			var visiting = new HashSet<string>( Comparer );
			var current = entry;
			while ( current.LinkTarget is not null )
			{
				if ( !visiting.Add( Normalize( current.Path ) ) )
					throw new InvalidOperationException( $"Artifact symlink cycle at '{current.Path}'." );
				chain.Add( current );
				var target = TargetPath( current );
				if ( !files.TryGetValue( target, out var next ) )
					throw new InvalidOperationException( $"Symlink target is not in the selected artifacts: '{current.Path}' -> '{current.LinkTarget}'." );
				if ( !string.Equals( current.Sha256, next.Sha256, StringComparison.OrdinalIgnoreCase ) || current.Size != next.Size )
					throw new InvalidOperationException( $"Symlink payload metadata differs from its target: '{current.Path}'." );
				current = next;
			}
			chain.Reverse();
			foreach ( var link in chain )
				if ( complete.Add( Normalize( link.Path ) ) ) ordered.Add( link );
		}
	}

	internal static string Normalize( string path ) => path.Replace( '\\', '/' );

	internal string Destination( string path )
	{
		path = Normalize( path );
		if ( string.IsNullOrWhiteSpace( path ) || Path.IsPathRooted( path ) || path.Contains( ':' )
			|| path.Split( '/' ).Any( part => part is "" or "." or ".." ) )
			throw new InvalidOperationException( $"Invalid artifact path: '{path}'." );
		var destination = Path.GetFullPath( Path.Combine( root, path ) );
		CheckParents( destination );
		return destination;
	}

	void CheckParents( string destination )
	{
		// Never traverse existing directory aliases, including dangling symlinks. The leaf
		// itself may be an old alias: regular-file installation replaces it with File.Move.
		for ( var parent = Path.GetDirectoryName( destination ); parent is not null; parent = Path.GetDirectoryName( parent ) )
		{
			if ( new DirectoryInfo( parent ).LinkTarget is not null )
				throw new InvalidOperationException( $"Artifact parent directory is a symlink: '{parent}'." );
			if ( Comparer.Equals( parent, root ) ) return;
		}
		throw new InvalidOperationException( $"Artifact path is outside the repository: '{destination}'." );
	}

	string TargetPath( ArtifactFileInfo entry )
	{
		var target = Normalize( entry.LinkTarget );
		if ( string.IsNullOrWhiteSpace( target ) || Path.IsPathRooted( target ) || target.Contains( ':' ) )
			throw new InvalidOperationException( $"Invalid artifact symlink target: '{entry.Path}' -> '{target}'." );
		// Check the uncollapsed path too: "directory-link/../payload" can resolve
		// differently on disk than Path.GetFullPath's lexical normalization.
		var directory = Path.GetDirectoryName( Destination( entry.Path ) );
		foreach ( var part in target.Split( '/' ).SkipLast( 1 ) )
		{
			directory = Path.GetFullPath( Path.Combine( directory, part ) );
			if ( new DirectoryInfo( directory ).LinkTarget is not null )
				throw new InvalidOperationException( $"Artifact symlink target traverses a directory symlink: '{entry.Path}'." );
		}
		var full = Path.GetFullPath( Path.Combine( Path.GetDirectoryName( Destination( entry.Path ) ), target ) );
		var relative = Normalize( Path.GetRelativePath( root, full ) );
		Destination( relative );
		return relative;
	}

	internal void Restore()
	{
		foreach ( var entry in ordered )
		{
			var destination = Destination( entry.Path );
			var target = Destination( TargetPath( entry ) );
			if ( !File.Exists( target ) )
				throw new IOException( $"Artifact symlink target is missing: '{target}'." );
			var wanted = Normalize( Path.GetRelativePath( Path.GetDirectoryName( destination ), target ) );
			if ( new FileInfo( destination ).LinkTarget == wanted ) continue;

			Directory.CreateDirectory( Path.GetDirectoryName( destination ) );
			var temporary = Path.Combine( Path.GetDirectoryName( destination ), $".sbox-link-{Guid.NewGuid():N}.tmp" );
			try
			{
				File.CreateSymbolicLink( temporary, wanted );
				File.Move( temporary, destination, overwrite: true );
			}
			finally
			{
				File.Delete( temporary );
			}
		}
	}
}
