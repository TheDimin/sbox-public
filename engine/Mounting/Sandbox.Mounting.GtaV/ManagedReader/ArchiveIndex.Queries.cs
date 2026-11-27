using System;
using System.Globalization;

namespace GtaV.ManagedReader;

internal sealed partial class ArchiveIndex
{
	internal string NameOf( uint hash )
	{
		int low = 0;
		int high = files.Length;
		while ( low < high )
		{
			int middle = low + (high - low) / 2;
			if ( files[middle].Hash < hash ) low = middle + 1;
			else high = middle;
		}
		string name = TextureNameOf( hash );
		for ( int i = low; i < files.Length && files[i].Hash == hash; i++ )
			if ( name is null || string.CompareOrdinal( files[i].Stem, name ) < 0 ) name = files[i].Stem;
		return name ?? "hash_" + hash.ToString( "X8", CultureInfo.InvariantCulture );
	}

	string TextureNameOf( uint hash )
	{
		int low = 0;
		int high = textureNames.Length;
		while ( low < high )
		{
			int mid = low + (high - low) / 2;
			var row = textureNames[mid];
			if ( row.Hash == hash ) return row.Name;
			if ( row.Hash < hash ) low = mid + 1;
			else high = mid;
		}
		return null;
	}
	internal string DrawablePath( in YtypCatalog.Archetype archetype )
	{
		if ( FindDrawable( archetype ) is not { } draw ) return string.Empty;
		return draw.File.Extension switch
		{
			".yft" => "fragments/" + draw.File.Stem,
			".ydd" => draw.File.Stem + "/" + NameOf( draw.Draw ),
			_ => draw.File.Stem,
		};
	}
	internal ArchetypeEntry? FindArchetype( uint name )
	{
		int low = 0;
		int high = archetypes.Length;
		while ( low < high )
		{
			int middle = low + (high - low) / 2;
			if ( archetypes[middle].Data.Name <= name ) low = middle + 1;
			else high = middle;
		}
		if ( low == 0 ) return null;
		ref readonly var row = ref archetypes[low - 1];
		return row.Data.Name == name && ranks[files[row.File].Rank].Active ? row : null;
	}

	internal (FileEntry File, uint Draw)? FindDrawable( in YtypCatalog.Archetype archetype )
	{
		uint draw = archetype.Asset == 0 ? archetype.Name : archetype.Asset;
		if ( archetype.Drawable != 0 && Find( archetype.Drawable, ".ydd" ) is { } dictionary ) return (dictionary, draw);
		if ( (Find( draw, ".ydr" ) ?? Find( draw, ".yft" )) is { } file ) return (file, draw);
		if ( draw != archetype.Name && (Find( archetype.Name, ".ydr" ) ?? Find( archetype.Name, ".yft" )) is { } named ) return (named, archetype.Name);
		return null;
	}

	/// <summary>Writes the nearest texture parents first. A cycle ends the search.</summary>
	internal int FindTextureParents( uint name, Span<uint> destination )
	{
		int count = 0;
		uint current = name;
		while ( count < Math.Min( destination.Length, 16 ) && FindTextureParent( current ) is uint parent )
		{
			if ( parent == name || destination[..count].Contains( parent ) ) break;
			destination[count++] = current = parent;
		}
		return count;
	}

	uint? FindTextureParent( uint name )
	{
		int low = 0;
		int high = textureParents.Length;
		while ( low < high )
		{
			int middle = low + (high - low) / 2;
			uint child = textureParents[middle].Child;
			if ( child == name ) return textureParents[middle].Parent;
			if ( child < name ) low = middle + 1;
			else high = middle;
		}
		return null;
	}
}
