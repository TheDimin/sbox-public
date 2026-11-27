using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace GtaV.ManagedReader;

/// <summary>Applies the DLC map change sets before choosing the files in the streamed world.</summary>
internal sealed class WorldArchiveSelection
{
	const string Platform = "platform:/";
	const string PatchRoot = "update/update.rpf/dlc_patch/";
	const string PacksFolder = "dlcpacks/";
	static readonly string[] ChangeSetGroups = ["GROUP_EARLY_ON", "GROUP_TITLEUPDATE_STARTUP", "GROUP_STARTUP",
		"GROUP_UPDATE_STREAMING", "GROUP_UPDATE_DLC_PATCH", "GROUP_UPDATE_DLC_METADATA", "GROUP_UPDATE_WEAPON_PATCH",
		"GROUP_POST_DLC_PATCH", "GROUP_MAP"];

	sealed class Pack
	{
		internal DlcLoadOrder.Rank Rank, SetupRank, ContentRank;
		internal XElement Setup, Content;
	}

	readonly ArchiveIndex index;
	readonly bool[] mounted;
	readonly HashSet<uint> disabled = new();
	readonly Dictionary<string, bool> state = new( StringComparer.Ordinal );
	readonly Dictionary<string, List<string>> overlays = new( StringComparer.Ordinal );
	internal IReadOnlyList<string> Warnings { get; }

	internal WorldArchiveSelection( ArchiveIndex index, ArchiveContent content )
	{
		this.index = index;
		var warnings = new List<string>();
		var namedPacks = ReadPacks( content, warnings );
		var packs = namedPacks.Values.OrderBy( p => p.Rank ).ToArray();
		var devices = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( var (name, pack) in namedPacks )
			if ( pack.Setup?.Element( "deviceName" )?.Value.Trim() is { Length: > 0 } device ) devices[name] = device.ToLowerInvariant();

		var archives = index.Archives;
		var logical = new string[archives.Length];
		for ( int i = 0; i < archives.Length; i++ ) logical[i] = LogicalPath( archives[i].Path.ToLowerInvariant(), devices );
		foreach ( var pack in packs )
		{
			foreach ( var file in Items( pack.Content, "dataFiles" ) )
			{
				string path = Normalize( file.Element( "filename" )?.Value );
				if ( !IsArchive( path ) ) continue;
				if ( string.Equals( (string)file.Element( "disabled" )?.Attribute( "value" ), "true", StringComparison.OrdinalIgnoreCase ) ) state.TryAdd( path, false );
				else SetArchive( path, true );
			}
		}
		foreach ( string group in ChangeSetGroups )
		{
			foreach ( var pack in packs )
			{
				foreach ( var entry in Items( pack.Setup, "contentChangeSetGroups" ) )
				{
					if ( !string.Equals( entry.Element( "NameHash" )?.Value.Trim(), group, StringComparison.OrdinalIgnoreCase ) ) continue;
					foreach ( var change in Items( entry, "ContentChangeSets" ) )
					{
						var set = Items( pack.Content, "contentChangeSets" ).FirstOrDefault( item =>
							string.Equals( item.Element( "changeSetName" )?.Value.Trim(), change.Value.Trim(), StringComparison.OrdinalIgnoreCase ) );
						if ( set is not null ) Apply( set );
					}
				}
			}
		}

		mounted = new bool[archives.Length];
		for ( int i = 0; i < mounted.Length; i++ )
		{
			string path = logical[i];
			int parent = archives[i].Parent;
			bool on = true;
			if ( path.Length > 0 && !state.TryGetValue( path, out on ) )
			{
				string container = parent < 0 ? string.Empty : logical[parent];
				bool packRoot = container.EndsWith( ":/", StringComparison.Ordinal ) && container.IndexOf( ":/", StringComparison.Ordinal ) == container.Length - 2;
				on = path.StartsWith( Platform, StringComparison.Ordinal ) || !packRoot;
			}
			mounted[i] = on && (parent < 0 || mounted[parent]);
		}
		Warnings = warnings.ToArray();
	}

	internal int[] Select( string extension )
	{
		var files = index.Files;
		var result = new List<int>();
		for ( int i = 0; i < files.Length; )
		{
			ref readonly var first = ref files[i];
			if ( first.Extension != extension ) { i++; continue; }
			int end = i + 1;
			while ( end < files.Length && files[end].Hash == first.Hash && files[end].Extension == extension ) end++;
			if ( !disabled.Contains( first.Hash ) )
			{
				for ( int candidate = end - 1; candidate >= i; candidate-- )
				{
					ref readonly var row = ref files[candidate];
					if ( !mounted[row.Archive] || !index.Ranks[row.Rank].Active ) continue;
					result.Add( candidate );
					break;
				}
			}
			i = end;
		}
		return result.ToArray();
	}

	Dictionary<string, Pack> ReadPacks( ArchiveContent content, List<string> warnings )
	{
		var packs = new Dictionary<string, Pack>( StringComparer.Ordinal );
		foreach ( ref readonly var file in index.Files )
		{
			if ( file.Extension != ".xml" || file.Stem is not ("content" or "setup2") ) continue;
			var rank = index.Ranks[file.Rank];
			if ( rank.Tier != 3 ) continue;
			string filename = file.Stem + file.Extension;
			string path = RpfArchive.JoinPath( index.Archives[file.Archive].Path, RpfArchive.JoinPath( file.Directory, filename ) ).ToLowerInvariant();
			if ( !path.Contains( $"/dlcpacks/{rank.Pack}/dlc.rpf/{filename}", StringComparison.Ordinal ) &&
				!path.Contains( $"/dlc_patch/{rank.Pack}/{filename}", StringComparison.Ordinal ) ) continue;
			if ( !packs.TryGetValue( rank.Pack, out var pack ) ) packs.Add( rank.Pack, pack = new Pack() );
			if ( pack.Rank.CompareTo( rank ) < 0 ) pack.Rank = rank;
			bool setup = file.Stem == "setup2";
			ref var best = ref setup ? ref pack.SetupRank : ref pack.ContentRank;
			if ( best.CompareTo( rank ) > 0 ) continue;
			best = rank;
			try
			{
				var root = ArchiveXml.Read( content.Extract( file ) ).Root;
				if ( setup ) pack.Setup = root;
				else pack.Content = root;
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or XmlException ) { warnings.Add( $"{path}: {e.Message}" ); }
		}
		return packs;
	}

	static string LogicalPath( string path, Dictionary<string, string> devices )
	{
		bool patch = path.StartsWith( PatchRoot, StringComparison.Ordinal );
		int packs = path.IndexOf( PacksFolder, StringComparison.Ordinal );
		if ( patch || (packs >= 0 && (packs == 0 || path[packs - 1] == '/')) )
		{
			var rest = path[(patch ? PatchRoot.Length : packs + PacksFolder.Length)..];
			int slash = rest.IndexOf( '/' );
			if ( slash < 0 || !devices.TryGetValue( rest[..slash], out string device ) ) return string.Empty;
			string tail = rest[(slash + 1)..];
			if ( !patch )
			{
				slash = tail.IndexOf( '/' );
				tail = slash < 0 ? string.Empty : tail[(slash + 1)..];
			}
			return device + ":/" + (tail.StartsWith( "x64/", StringComparison.Ordinal ) ? "%platform%/" + tail[4..] : tail);
		}
		if ( path.StartsWith( "update/update.rpf/x64/", StringComparison.Ordinal ) ) return Platform + path[22..];
		if ( path.StartsWith( "update/update2.rpf/x64/", StringComparison.Ordinal ) ) return Platform + path[23..];
		return path.Length > 9 && path.StartsWith( "x64", StringComparison.Ordinal ) && path.AsSpan( 4, 5 ).SequenceEqual( ".rpf/" )
			? Platform + path[9..] : string.Empty;
	}

	void Apply( XElement changeSet )
	{
		foreach ( var file in Items( changeSet, "filesToEnable" ) ) Set( file.Value, true );
		foreach ( var file in Items( changeSet, "filesToDisable" ) ) Set( file.Value, false );
		foreach ( var map in Items( changeSet, "mapChangeSetData" ) )
		{
			foreach ( var file in Items( map, "filesToInvalidate" ) ) Set( file.Value, false );
			foreach ( var file in Items( map, "filesToDisable" ) ) Set( file.Value, false );
			foreach ( var file in Items( map, "filesToEnable" ) ) Set( file.Value, true );
		}
	}

	void Set( string value, bool on )
	{
		string path = Normalize( value );
		if ( path.Length == 0 ) return;
		if ( IsArchive( path ) ) { SetArchive( path, on ); return; }
		var name = path[(path.LastIndexOf( '/' ) + 1)..];
		name = name[(name.IndexOf( ':' ) + 1)..];
		int dot = name.LastIndexOf( '.' );
		uint hash = ArchiveIndex.HashName( dot >= 0 ? name[..dot] : name );
		if ( on ) disabled.Remove( hash );
		else disabled.Add( hash );
	}

	void SetArchive( string archive, bool on )
	{
		state[archive] = on;
		const string marker = ":/%platform%/";
		int at = archive.IndexOf( marker, StringComparison.Ordinal );
		if ( on && at >= 0 && !archive.StartsWith( Platform, StringComparison.Ordinal ) )
		{
			string key = Platform + archive[(at + marker.Length)..];
			if ( !overlays.TryGetValue( key, out var list ) ) overlays.Add( key, list = new() );
			list.Add( archive );
		}
		else if ( !on && archive.StartsWith( Platform, StringComparison.Ordinal ) && overlays.Remove( archive, out var list ) )
		{
			foreach ( string overlay in list ) state[overlay] = false;
		}
	}

	static IEnumerable<XElement> Items( XElement node, string name ) => node?.Element( name )?.Elements( "Item" ) ?? [];
	static string Normalize( string path ) => path?.Trim().Replace( '\\', '/' ).ToLowerInvariant() ?? string.Empty;
	static bool IsArchive( string path ) => path.Length > 4 && path.EndsWith( ".rpf", StringComparison.Ordinal );
}
