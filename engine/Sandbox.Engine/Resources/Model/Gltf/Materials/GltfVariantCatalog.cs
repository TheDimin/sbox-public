using System.Collections.ObjectModel;

namespace Sandbox;

/// <summary>
/// Immutable variant names in one resource generation. Index is the exact
/// within-generation identity; a unique ordinal name is the only safe way to
/// carry a non-default selection to a different generation.
/// </summary>
internal sealed class GltfVariantCatalog
{
	internal sealed record Selection( int Index, string Name, bool UniqueInSource );
	private readonly ReadOnlyCollection<string> names;
	private readonly Dictionary<string, int> counts;
	internal IReadOnlyList<string> Names => names;

	internal GltfVariantCatalog( IEnumerable<string> source )
	{
		ArgumentNullException.ThrowIfNull( source );
		var copy = source.ToArray();
		if ( copy.Any( x => x is null ) ) throw new ArgumentException( "Variant names must not be null.", nameof( source ) );
		names = Array.AsReadOnly( copy );
		counts = copy.GroupBy( x => x, StringComparer.Ordinal ).ToDictionary( x => x.Key, x => x.Count(), StringComparer.Ordinal );
	}

	internal Selection Select( int? index )
	{
		if ( index is null ) return null;
		if ( (uint)index.Value >= (uint)names.Count ) throw new ArgumentOutOfRangeException( nameof( index ) );
		var name = names[index.Value];
		return new Selection( index.Value, name, counts[name] == 1 );
	}

	internal Selection Transfer( Selection previous )
	{
		if ( previous is null ) return null;
		if ( !previous.UniqueInSource || !counts.TryGetValue( previous.Name, out var count ) || count != 1 )
			throw new InvalidOperationException( $"Cannot preserve material variant '{previous.Name}' across resource generations because its identity is missing or ambiguous." );
		for ( var i = 0; i < names.Count; i++ )
			if ( string.Equals( names[i], previous.Name, StringComparison.Ordinal ) ) return Select( i );
		throw new InvalidOperationException( "Material variant catalog is inconsistent." );
	}
}
