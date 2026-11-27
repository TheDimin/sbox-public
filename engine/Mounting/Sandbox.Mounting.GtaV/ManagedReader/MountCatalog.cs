using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace GtaV.ManagedReader;

/// <summary>Registers resource names and file IDs from the managed index.</summary>
internal sealed class MountCatalog( ArchiveIndex index, ArchiveContent content )
{
	internal BinaryReader Read( bool physicsOnly, bool rebuild = false )
	{
		string path = index.PacketCachePath( physicsOnly ? "physics-resources-v1" : "resources-v1" );
		if ( path is not null && !rebuild && MountPacketCache.Read( path ) is { } cached ) return MountPacket.Open( cached );
		var result = Build( physicsOnly, out bool complete );
		if ( path is not null && complete ) MountPacketCache.Write( path, result.BaseStream );
		return result;
	}

	BinaryReader Build( bool physicsOnly, out bool complete )
	{
		complete = true;
		using var packet = new MountPacket();
		var writer = packet.Writer;
		var propFragments = new HashSet<uint>();
		var emittedDictionaries = physicsOnly ? new HashSet<string>( StringComparer.Ordinal ) : null;
		WriteArchetypes( writer, propFragments, emittedDictionaries );

		using var dictionaries = new TextureDictionaryReader( content );
		Span<byte> groups = stackalloc byte[256];
		var files = index.Files;
		for ( int id = 0; id < files.Length; id++ )
		{
			ref readonly var file = ref files[id];
			bool dictionary = file.Extension is ".ytd" or ".ydd";
			if ( dictionary && physicsOnly ) continue;
			var kind = FileKind( file.Extension );
			if ( kind == Kind.Invalid || !index.Ranks[file.Rank].Active ) continue;
			// Files with the same hash and extension are consecutive and sorted by load order.
			if ( id + 1 < files.Length && files[id + 1].Hash == file.Hash && files[id + 1].Extension == file.Extension ) continue;

			string name = kind == Kind.Fragment ? "fragments/" + file.Stem : file.Stem;
			try
			{
				if ( dictionary )
				{
					foreach ( var item in dictionaries.ReadMembers( file ) )
						Write( writer, kind, id, item.Hash, name + "/" + (item.Name.Length == 0 ? index.NameOf( item.Hash ) : item.Name) );
					continue;
				}

				Write( writer, kind, id, 0, name );
				if ( kind != Kind.Fragment || !propFragments.Contains( file.Hash ) ) continue;
				using var resource = new RscResource( content, file );
				var fragment = new FragmentPhysics( resource );
				int count = fragment.GetDetachableGroups( groups );
				foreach ( byte group in groups[..count] )
					Write( writer, Kind.FragmentGroup, id, group, name + "/gibs/" + group.ToString( CultureInfo.InvariantCulture ) );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or OverflowException )
			{
				// A failed archive read can be repaired without changing the rest of the install.
				complete = false;
				string location = index.Archives[file.Archive].Path + "/";
				if ( file.Directory.Length != 0 ) location += file.Directory + "/";
				Write( writer, Kind.Invalid, id, 0, location + file.Stem + file.Extension + ": " + e.Message );
			}
		}
		return packet.Finish();
	}

	void WriteArchetypes( BinaryWriter writer, HashSet<uint> propFragments, HashSet<string> emittedDictionaries )
	{
		var archetypes = index.Archetypes;
		for ( int start = 0; start < archetypes.Length; )
		{
			uint hash = archetypes[start].Data.Name;
			int end = start + 1;
			while ( end < archetypes.Length && archetypes[end].Data.Name == hash ) end++;
			int winner = archetypes[end - 1].File;
			if ( index.Ranks[index.Files[winner].Rank].Active )
			{
				string name = index.NameOf( hash );
				for ( int i = start; i < end; i++ )
				{
					ref readonly var row = ref archetypes[i];
					if ( row.File != winner ) continue;
					Write( writer, Kind.Prefab, row.File, hash, name );
					var draw = index.FindDrawable( row.Data );
					if ( draw is not { } drawable ) continue;
					if ( drawable.File.Extension == ".yft" ) propFragments.Add( drawable.File.Hash );
					else if ( emittedDictionaries is not null && row.Data.Drawable != 0 && drawable.File.Extension == ".ydd" )
					{
						string path = drawable.File.Stem + "/" + index.NameOf( drawable.Draw );
						if ( emittedDictionaries.Add( path ) && index.FindId( drawable.File.Hash, ".ydd" ) is { } id )
							Write( writer, Kind.DictionaryModel, id, drawable.Draw, path );
					}
				}
			}
			start = end;
		}
	}

	static Kind FileKind( string extension ) => extension switch
	{
		".ymap" => Kind.Scene,
		".ydr" => Kind.Model,
		".ydd" => Kind.DictionaryModel,
		".ytd" => Kind.Texture,
		".ybn" => Kind.Collision,
		".yft" => Kind.Fragment,
		_ => Kind.Invalid,
	};

	static void Write( BinaryWriter writer, Kind kind, int id, uint member, string name )
	{
		writer.Write( (int)kind );
		writer.Write( id );
		writer.Write( member );
		MountPacket.WriteText( writer, name );
	}
}
