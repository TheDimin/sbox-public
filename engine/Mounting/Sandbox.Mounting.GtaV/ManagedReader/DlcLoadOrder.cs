using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace GtaV.ManagedReader;

// Rank semantics follow the existing mount's ragekit index. See ThirdPartyNotices.txt.
internal sealed class DlcLoadOrder
{
	internal readonly record struct Rank( int Tier, int Order = 0, int Mount = 0, bool Patch = false, string Pack = "" ) : IComparable<Rank>
	{
		internal bool Active => Tier >= 0;
		public int CompareTo( Rank other )
		{
			int comparison = Tier.CompareTo( other.Tier );
			if ( comparison == 0 ) comparison = Order.CompareTo( other.Order );
			if ( comparison == 0 ) comparison = Mount.CompareTo( other.Mount );
			if ( comparison == 0 ) comparison = Patch.CompareTo( other.Patch );
			return comparison == 0 ? string.CompareOrdinal( Pack, other.Pack ) : comparison;
		}
	}

	sealed class Pack( string name, int order )
	{
		internal readonly string Name = name;
		internal readonly int Order = order;
		internal int ListPosition = -1;
		internal int Mount = -1;
	}

	const string DlcPrefix = "update/x64/dlcpacks/";
	const string PatchPrefix = "update/update.rpf/dlc_patch/";
	readonly Dictionary<string, Pack> byName = new( StringComparer.Ordinal );
	readonly Dictionary<string, Pack> byPath = new( StringComparer.Ordinal );

	internal DlcLoadOrder( IEnumerable<(string Path, int Order)> archives, string[] listedPacks )
	{
		foreach ( var (path, order) in archives )
		{
			if ( !path.EndsWith( "/dlc.rpf", StringComparison.OrdinalIgnoreCase ) ) continue;
			string prefix = path[..^8].ToLowerInvariant();
			string name = prefix[(prefix.LastIndexOf( '/' ) + 1)..];
			var pack = new Pack( name, order );
			byPath[prefix] = pack;
			byName[name] = pack;
		}
		for ( int i = 0; i < listedPacks.Length; i++ )
			if ( byName.TryGetValue( listedPacks[i], out var pack ) ) pack.ListPosition = i;

		int mount = 0;
		foreach ( var pack in byName.Values.Where( pack => pack.ListPosition >= 0 ).OrderBy( pack => pack.Order ).ThenBy( pack => pack.ListPosition ) )
			pack.Mount = mount++;
	}

	internal Rank For( string directory )
	{
		if ( PackNameOffset( directory ) is int offset && offset < directory.Length )
		{
			int end = directory.IndexOf( '/', offset );
			string prefix = end < 0 ? directory : directory[..end];
			return byPath.TryGetValue( prefix, out var pack ) ? RankOf( pack, pack.Name ) : new Rank( -1, Pack: prefix[offset..] );
		}
		if ( directory.StartsWith( "update/update2.rpf", StringComparison.Ordinal ) ) return new Rank( 2 );
		if ( directory.Length > PatchPrefix.Length && directory.StartsWith( PatchPrefix, StringComparison.Ordinal ) )
		{
			var name = directory.AsSpan( PatchPrefix.Length );
			int slash = name.IndexOf( '/' );
			string patch = (slash < 0 ? name : name[..slash]).ToString();
			return RankOf( byName.GetValueOrDefault( patch ), patch, patch: true );
		}
		return directory.StartsWith( "update/update.rpf", StringComparison.Ordinal ) ? new Rank( 1 ) : new Rank( 0 );
	}

	static int? PackNameOffset( string directory )
	{
		if ( directory.StartsWith( DlcPrefix, StringComparison.Ordinal ) ) return DlcPrefix.Length;
		if ( !directory.StartsWith( "x64", StringComparison.Ordinal ) ) return null;
		int firstSlash = directory.IndexOf( '/' );
		const string folder = "/dlcpacks/";
		return firstSlash >= 4 && directory.AsSpan( firstSlash - 4 ).StartsWith( ".rpf/dlcpacks/", StringComparison.Ordinal )
			? firstSlash + folder.Length : null;
	}

	static Rank RankOf( Pack pack, string name, bool patch = false ) => pack is { Mount: >= 0 }
		? new Rank( 3, pack.Order, pack.Mount, patch, name ) : new Rank( -1, Patch: patch, Pack: name );

	internal static string[] ReadListedPacks( byte[] bytes ) => ArchiveXml.Read( bytes ).Descendants()
		.Where( node => node.Name.LocalName.Equals( "Item", StringComparison.OrdinalIgnoreCase ) )
		.Select( node => node.Value.Trim().Replace( '\\', '/' ).TrimEnd( '/' ) )
		.Select( path => path[(path.LastIndexOf( '/' ) + 1)..].ToLowerInvariant() )
		.Where( name => name.Length > 0 )
		.ToArray();

	internal static int ReadSetupOrder( byte[] bytes )
	{
		var node = ArchiveXml.Read( bytes ).Descendants().FirstOrDefault( node => node.Name.LocalName.Equals( "order", StringComparison.OrdinalIgnoreCase ) );
		string value = node?.Attributes().FirstOrDefault( attribute => attribute.Name.LocalName.Equals( "value", StringComparison.OrdinalIgnoreCase ) )?.Value;
		return int.TryParse( value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int order ) ? order : 0;
	}
}

internal static class ArchiveXml
{
	internal static XDocument Read( byte[] bytes )
	{
		using var stream = new MemoryStream( bytes, writable: false );
		using var reader = XmlReader.Create( stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit } );
		return XDocument.Load( reader );
	}
}
