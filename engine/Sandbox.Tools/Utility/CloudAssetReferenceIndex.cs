using System;

namespace Editor;

internal sealed class CloudAssetReferenceIndex<TAsset> where TAsset : class
{
	private sealed class Entry
	{
		internal TAsset Asset { get; }
		private string[] references;
		private Func<TAsset, IEnumerable<string>> readAsset;
		private Func<IEnumerable<string>> readReferences;

		internal Entry( TAsset asset, string[] references )
		{
			Asset = asset;
			this.references = references;
		}

		internal Entry( TAsset asset, Func<TAsset, IEnumerable<string>> reader )
		{
			Asset = asset;
			readAsset = reader;
		}

		internal Entry( TAsset asset, Func<IEnumerable<string>> reader )
		{
			Asset = asset;
			readReferences = reader;
		}

		internal string[] GetReferences()
		{
			if ( references is not null ) return references;
			references = NormalizeReferences( readReferences is not null ? readReferences() : readAsset( Asset ) );
			readReferences = null;
			readAsset = null;
			return references;
		}
	}
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
			foreach ( var asset in assets ) SetCore( getPath( asset ), new Entry( asset, getReferences ) );
			IsBuilt = true;
		}
	}

	internal void Update( TAsset asset, string path, IEnumerable<string> references )
	{
		lock ( _lock )
		{
			if ( IsBuilt ) SetCore( path, new Entry( asset, NormalizeReferences( references ) ) );
		}
	}

	internal void Update( TAsset asset, string path, Func<IEnumerable<string>> getReferences )
	{
		lock ( _lock )
		{
			if ( IsBuilt ) SetCore( path, new Entry( asset, getReferences ) );
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
				if ( !_entries.TryGetValue( path, out var existing ) || !ReferenceEquals( existing.Asset, asset ) )
					SetCore( path, new Entry( asset, getReferences ) );
			}
			foreach ( var path in _entries.Keys.Where( x => !seen.Contains( x ) ).ToArray() ) _entries.Remove( path );
		}
	}

	internal Dictionary<string, List<TAsset>> Snapshot( Func<TAsset, bool> include )
	{
		lock ( _lock )
		{
			var result = new Dictionary<string, List<TAsset>>( StringComparer.OrdinalIgnoreCase );
			var seen = new Dictionary<string, HashSet<TAsset>>( StringComparer.OrdinalIgnoreCase );
			// Readers can invoke editor callbacks that replace entries or add assets. Walk a
			// stable set, and keep each pending read on its entry so a replacement stays unread.
			foreach ( var entry in _entries.Values.ToArray() )
			{
				if ( !include( entry.Asset ) ) continue;
				foreach ( var packageIdent in entry.GetReferences() )
				{
					if ( !result.TryGetValue( packageIdent, out var sources ) )
					{
						result[packageIdent] = sources = new List<TAsset>();
						seen[packageIdent] = new HashSet<TAsset>();
					}
					if ( seen[packageIdent].Add( entry.Asset ) ) sources.Add( entry.Asset );
				}
			}
			return result;
		}
	}

	private void SetCore( string path, Entry entry )
	{
		path = NormalizePath( path );
		if ( path is null ) return;
		_entries[path] = entry;
	}

	private static string[] NormalizeReferences( IEnumerable<string> references ) => references
		.Where( x => !string.IsNullOrWhiteSpace( x ) ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();

	private static string NormalizePath( string path ) => string.IsNullOrWhiteSpace( path ) ? null : path.Replace( '\\', '/' );
}
