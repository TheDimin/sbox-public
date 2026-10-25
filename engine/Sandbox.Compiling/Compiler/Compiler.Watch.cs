namespace Sandbox;

partial class Compiler
{
	private List<FileWatch> sourceWatchers = new();

	/// <summary>
	/// Watch the filesystem for changes to our c# files, and trigger a recompile if they change.
	/// </summary>
	public void WatchForChanges()
	{
		foreach ( var existing in sourceWatchers ) existing.Dispose();
		sourceWatchers.Clear();

		foreach ( var fs in SourceLocations )
		{
			if ( fs is LocalFileSystem local )
				local.ConfigureWatch( $"compiler:{Name}", _config.IgnoreFolders.Append( "obj" ) );

			var watcher = fs.Watch( "*.*" );
			watcher.OnChangedFile += OnFileChanged;
			sourceWatchers.Add( watcher );
		}
	}

	private void OnFileChanged( string file )
	{
		if ( !file.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase ) && !file.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) )
			return;

		var folders = System.IO.Path.GetDirectoryName( file ).Split( new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries );
		if ( folders.Any( x => x.Equals( "obj", StringComparison.OrdinalIgnoreCase ) || _config.IgnoreFolders.Contains( x, StringComparer.OrdinalIgnoreCase ) ) )
			return;

		MarkForRecompile();
	}
}
