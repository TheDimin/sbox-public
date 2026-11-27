using System.Collections.Generic;
using System.IO;
using Sandbox;
using Sandbox.Mounting;

namespace GtaV;

internal static class InteriorRooms
{
	internal static void Read( GtaMount host, BinaryReader data, GameObject root, Dictionary<int, GameObject> objects )
	{
		var interior = root.AddComponent<GtaInteriorVisibility>();
		int rooms = data.ReadInt32();
		for ( int i = 0; i < rooms; i++ )
		{
			var room = new GtaInteriorVisibility.Room { Bounds = new( Point( data ), Point( data ) ) };
			ReadObjects( data, objects, room.Objects );
			interior.Rooms.Add( room );
		}
		int portals = data.ReadInt32();
		for ( int i = 0; i < portals; i++ )
		{
			var portal = new GtaInteriorVisibility.Portal { From = data.ReadInt32(), To = data.ReadInt32() };
			int corners = data.ReadInt32();
			for ( int j = 0; j < corners; j++ ) portal.Corners.Add( Point( data ) );
			var attached = new List<GameObject>();
			ReadObjects( data, objects, attached );
			if ( (uint)portal.From < (uint)rooms ) interior.Rooms[portal.From].Objects.AddRange( attached );
			if ( (uint)portal.To < (uint)rooms ) interior.Rooms[portal.To].Objects.AddRange( attached );
			interior.Portals.Add( portal );
		}
		int sets = data.ReadInt32();
		for ( int i = 0; i < sets; i++ )
		{
			string name = data.ReadUInt32().ToString( "x8" );
			var locations = new int[data.ReadInt32()];
			for ( int j = 0; j < locations.Length; j++ ) locations[j] = data.ReadInt32();
			var set = new GameObject( root, false, name );
			Placements.Build( host, data, set, (index, child) =>
			{
				if ( index < locations.Length && (uint)locations[index] < (uint)rooms )
					interior.Rooms[locations[index]].Objects.Add( child );
			} );
			interior.EntitySets.Add( name, set );
		}
	}

	static Vector3 Point( BinaryReader data ) => data.Vector() * PacketReader.UnitsPerMetre;

	static void ReadObjects( BinaryReader data, Dictionary<int, GameObject> objects, List<GameObject> result )
	{
		int count = data.ReadInt32();
		for ( int i = 0; i < count; i++ )
			if ( objects.TryGetValue( data.ReadInt32(), out var obj ) ) result.Add( obj );
	}
}
