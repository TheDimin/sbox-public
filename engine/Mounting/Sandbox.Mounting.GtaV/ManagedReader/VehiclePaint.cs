using System.Collections.Generic;
using System.IO;

namespace GtaV.ManagedReader;

/// <summary>Reads the shop's paint choices and their ARGB colors once per mount.</summary>
internal static class VehiclePaint
{
	internal readonly record struct Preset( string Finish, string Name, uint Color );

	internal static Preset[] Read( ArchiveIndex index, ArchiveContent content )
	{
		if ( index.Find( ArchiveIndex.HashName( "carcols" ), ".ymt" ) is not { } colors ||
			index.Find( ArchiveIndex.HashName( "carmodcols" ), ".ymt" ) is not { } choices ) return [];

		var palette = new PsoResource( content.Extract( colors ) );
		var rows = palette.Array( palette.Root, PsoResource.Hash( "Colors" ) );
		if ( rows.Count == 0 ) return [];
		var argb = new uint[rows.Count];
		uint color = PsoResource.Hash( "color" );
		for ( int i = 0; i < argb.Length; i++ ) argb[i] = palette.GetHash( palette.ObjectAt( rows, i ), color );

		var shop = new PsoResource( content.Extract( choices ) );
		var root = shop.Root;
		uint indexField = PsoResource.Hash( "col" ), nameField = PsoResource.Hash( "name" );
		var presets = new List<Preset>();
		foreach ( string finish in new[] { "classic", "metallic", "matte", "metals", "chrome" } )
		{
			var items = shop.Array( root, PsoResource.Hash( finish ) );
			for ( int i = 0; i < items.Count; i++ )
			{
				var paint = shop.ObjectAt( items, i );
				if ( shop.GetInteger( paint, indexField ) is not long id || id < 0 || id >= argb.Length ) continue;
				string name = shop.GetString( paint, nameField );
				if ( name.Length != 0 ) presets.Add( new Preset( finish, name, argb[(int)id] ) );
			}
		}
		return presets.ToArray();
	}

	internal static void Write( BinaryWriter writer, Preset[] presets )
	{
		writer.Write( presets.Length );
		foreach ( var preset in presets )
		{
			MountPacket.WriteText( writer, preset.Finish );
			MountPacket.WriteText( writer, preset.Name );
			writer.Write( preset.Color );
		}
	}
}
