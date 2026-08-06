using System;

namespace Editor;

internal sealed class CloudAssetReferenceIndex<TAsset> where TAsset : class
{
	private sealed record Entry( TAsset Asset, string[] References );
	private readonly object _lock = new();
	private readonly Dictionary<string, Entry> _entries = new( StringComparer.OrdinalIgnoreCase );
	internal bool IsBuilt { get; private set; }

	internal void Reset()
	{
		lock ( _lock )
		{
			_entries.Clear();
			IsBuilt = false;
		}
	}

	internal void Build( IEnumerable<TAsset> assets, Func<TAsset, string> getPath, Func<TAsset, IEnumerable<string>> getReferences )
	{
		lock ( _lock )
		{
			if ( IsBuilt ) return;
			_entries.Clear();
			foreach ( var asset in assets ) SetCore( asset, getPath( asset ), getReferences( asset ) );
			IsBuilt = true;
		}
	}

	internal void Update( TAsset asset, string path, IEnumerable<string> references )
	{
		lock ( _lock )
		{
			if ( IsBuilt ) SetCore( asset, path, references );
		}
	}

	internal void Reconcile( IEnumerable<TAsset> assets, Func<TAsset, string> getPath, Func<TAsset, IEnumerable<string>> getReferences )
	{
		lock ( _lock )
		{
			if ( !IsBuilt ) return;
			var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var asset in assets )
			{
				var path = NormalizePath( getPath( asset ) );
				if ( path is null ) continue;
				seen.Add( path );
				if ( !_entries.ContainsKey( path ) ) SetCore( asset, path, getReferences( asset ) );
			}
			foreach ( var path in _entries.Keys.Where( x => !seen.Contains( x ) ).ToArray() ) _entries.Remove( path );
		}
	}

	internal Dictionary<string, List<TAsset>> Snapshot( Func<TAsset, bool> include )
	{
		lock ( _lock )
		{
			var result = new Dictionary<string, List<TAsset>>( StringComparer.OrdinalIgnoreCase );
			foreach ( var entry in _entries.Values )
			{
				if ( !include( entry.Asset ) ) continue;
				foreach ( var packageIdent in entry.References )
				{
					if ( !result.TryGetValue( packageIdent, out var sources ) ) result[packageIdent] = sources = new List<TAsset>();
					if ( !sources.Contains( entry.Asset ) ) sources.Add( entry.Asset );
				}
			}
			return result;
		}
	}

	private void SetCore( TAsset asset, string path, IEnumerable<string> references )
	{
		path = NormalizePath( path );
		if ( path is null ) return;
		_entries[path] = new Entry( asset, references.Where( x => !string.IsNullOrWhiteSpace( x ) ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray() );
	}

	private static string NormalizePath( string path ) => string.IsNullOrWhiteSpace( path ) ? null : path.Replace( '\\', '/' );
}
