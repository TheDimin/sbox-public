using System;
using System.Collections.Generic;
using System.IO;
using static GtaV.ManagedReader.MetaResource;

namespace GtaV.ManagedReader;

internal sealed partial class WorldMetadata
{
	const uint Ladder = 0x821D5421;
	const uint Explosion = 0x06383F69;
	const uint Particle = 0x3A26E5E1;
	const uint Audio = 0x15DEDA27;
	const uint NamedExplosion = 0xAD18FA1C;
	const int SignedInt = 0x14;
	const int FloatVector = 0x33;
	const int StringPointer = 0x44;
	readonly Lazy<Dictionary<uint, AudioCue>> audio;
	readonly record struct AudioCue( uint Detach, uint Destroy, int Rank );

	internal BinaryReader ReadExtensions( in ArchiveIndex.FileEntry file )
	{
		using var resource = new RscResource( content.Extract( file ) );
		var meta = new MetaResource( resource );
		using var packet = new MountPacket();
		bool particles = meta.HasBlock( Particle );
		bool audioSettings = meta.HasBlock( Audio );
		bool explosions = meta.HasBlock( Explosion );
		bool ladders = meta.HasBlock( Ladder );
		if ( !particles && !audioSettings && !explosions && !ladders ) return packet.Finish();
		var root = meta.Root( MapTypes, 80 );
		var definitions = meta.Array( root[24..], sizeof(ulong) );
		if ( particles || audioSettings ) WriteAudioAndParticles( packet.Writer, meta, definitions );
		if ( explosions ) WriteExplosions( packet.Writer, meta, definitions );
		if ( ladders ) WriteLadders( packet.Writer, meta, definitions );
		return packet.Finish();
	}

	void WriteAudioAndParticles( BinaryWriter writer, MetaResource meta, ReadOnlySpan<byte> definitions )
	{
		int audioHash = Field( meta, Audio, "settings" );
		if ( audioHash < 0 ) audioHash = Field( meta, Audio, "effectHash" );
		int fxType = Field( meta, Particle, "fxType" );
		int fxBone = Field( meta, Particle, "boneTag" );
		int fxName = Field( meta, Particle, "fxName", StringPointer );
		for ( int at = 0; at < definitions.Length; at += sizeof(ulong) )
		{
			var extensions = ExtensionsAt( meta, definitions, at, out uint owner );
			for ( int i = 0; i < extensions.Length; i += sizeof(ulong) )
			{
				ulong pointer = Pointer( extensions, i );
				uint type = meta.TypeAt( pointer );
				if ( type == Audio && audioHash >= 0 )
				{
					uint hash = Word( meta.At( pointer, audioHash + sizeof(uint) ), audioHash );
					var cue = audio.Value.GetValueOrDefault( hash );
					WriteExtensionHeader( writer, owner, Audio, 12 );
					writer.Write( hash ); writer.Write( cue.Detach ); writer.Write( cue.Destroy );
				}
				else if ( type == Particle && fxType >= 0 && fxBone >= 0 && fxName >= 0 )
				{
					var row = meta.At( pointer, Math.Max( Math.Max( fxType, fxBone ) + sizeof(uint), fxName + 16 ) );
					uint trigger = Word( row, fxType );
					if ( trigger is < 2 or > 4 ) continue;
					var name = meta.StringBytes( row[fxName..] );
					if ( name.IsEmpty ) continue;
					WriteExtensionHeader( writer, owner, Particle, 12 + name.Length );
					writer.Write( trigger ); writer.Write( Word( row, fxBone ) );
					writer.Write( name.Length ); writer.Write( name );
				}
			}
		}
	}

	static void WriteExplosions( BinaryWriter writer, MetaResource meta, ReadOnlySpan<byte> definitions )
	{
		int trigger = RequiredField( meta, Explosion, "explosionType", SignedInt );
		int bone = RequiredField( meta, Explosion, "boneTag", SignedInt );
		int nameOffset = RequiredField( meta, Explosion, "explosionName", StringPointer );
		int size = Math.Max( Math.Max( trigger, bone ) + sizeof(uint), nameOffset + 16 );
		for ( int at = 0; at < definitions.Length; at += sizeof(ulong) )
		{
			var extensions = ExtensionsAt( meta, definitions, at, out uint owner );
			for ( int i = 0; i < extensions.Length; i += sizeof(ulong) )
			{
				ulong pointer = Pointer( extensions, i );
				if ( meta.TypeAt( pointer ) != Explosion ) continue;
				var row = meta.At( pointer, size );
				if ( Int( row, trigger ) != 2 || Int( row, bone ) is not (0 or -1) ) continue;
				var name = meta.StringBytes( row[nameOffset..] );
				if ( name.IsEmpty ) continue;
				WriteExtensionHeader( writer, owner, Explosion, 0 );
				WriteExtensionHeader( writer, owner, NamedExplosion, 4 + name.Length );
				writer.Write( name.Length ); writer.Write( name );
				break;
			}
		}
	}

	static void WriteLadders( BinaryWriter writer, MetaResource meta, ReadOnlySpan<byte> definitions )
	{
		int bottom = RequiredField( meta, Ladder, "bottom", FloatVector );
		int top = RequiredField( meta, Ladder, "top", FloatVector );
		int normal = RequiredField( meta, Ladder, "normal", FloatVector );
		int size = Math.Max( Math.Max( bottom, top ), normal ) + 12;
		for ( int at = 0; at < definitions.Length; at += sizeof(ulong) )
		{
			var extensions = ExtensionsAt( meta, definitions, at, out uint owner );
			for ( int i = 0; i < extensions.Length; i += sizeof(ulong) )
			{
				ulong pointer = Pointer( extensions, i );
				if ( meta.TypeAt( pointer ) != Ladder ) continue;
				var row = meta.At( pointer, size );
				WriteExtensionHeader( writer, owner, Ladder, 36 );
				writer.Write( row.Slice( bottom, 12 ) ); writer.Write( row.Slice( top, 12 ) ); writer.Write( row.Slice( normal, 12 ) );
			}
		}
	}

	static ReadOnlySpan<byte> ExtensionsAt( MetaResource meta, ReadOnlySpan<byte> definitions, int at, out uint owner )
	{
		ulong pointer = Pointer( definitions, at );
		uint type = meta.TypeAt( pointer );
		// The old 128-byte base layout is left opaque by the native mount.
		if ( meta.StructSize( type, 144 ) < 136 ) { owner = 0; return []; }
		var row = meta.At( pointer, 136 );
		owner = Word( row, 88 );
		return meta.Array( row[120..], sizeof(ulong) );
	}

	static int Field( MetaResource meta, uint structure, string name, int format = -1 ) =>
		meta.FindField( structure, ArchiveIndex.HashName( name, lower: false ), format );

	static int RequiredField( MetaResource meta, uint structure, string name, int format )
	{
		int field = Field( meta, structure, name, format );
		return field >= 0 ? field : throw new InvalidDataException( $"Missing field {name} on extension {structure:X8}." );
	}

	static void WriteExtensionHeader( BinaryWriter writer, uint owner, uint type, int length )
	{
		writer.Write( owner ); writer.Write( type ); writer.Write( length );
	}

	static Dictionary<uint, AudioCue> ReadDestructionAudio( ArchiveIndex index, ArchiveContent content )
	{
		var cues = new Dictionary<uint, AudioCue>();
		foreach ( ref readonly var file in index.Files )
		{
			if ( file.Extension != ".rel" || !file.Stem.EndsWith( ".dat151", StringComparison.Ordinal ) ) continue;
			try { AddAudioCues( content.Extract( file ), file.Rank, index.Ranks, cues ); }
			catch ( Exception e ) when ( e is IOException or InvalidDataException ) { }
		}
		return cues;
	}

	static void AddAudioCues( ReadOnlySpan<byte> bytes, int rank, ReadOnlySpan<DlcLoadOrder.Rank> ranks, Dictionary<uint, AudioCue> cues )
	{
		if ( bytes.Length < 8 || Word( bytes, 0 ) != 151 ) return;
		uint dataLength = Word( bytes, 4 );
		if ( dataLength > bytes.Length - 8 ) throw new InvalidDataException( "Truncated REL data block." );
		var data = bytes.Slice( 8, (int)dataLength );
		var rest = bytes[(8 + (int)dataLength)..];
		if ( rest.Length < 8 ) throw new InvalidDataException( "Truncated REL name table." );
		uint names = Word( rest, 4 );
		if ( names > (rest.Length - 8) / 4 ) throw new InvalidDataException( "Invalid REL name count." );
		rest = rest[(8 + 4 * (int)names)..];
		for ( int i = 0; i < names; i++ )
		{
			int end = rest.IndexOf( (byte)0 );
			if ( end < 0 ) throw new InvalidDataException( "Unterminated REL name." );
			rest = rest[(end + 1)..];
		}
		if ( rest.Length < 4 || Word( rest, 0 ) > (rest.Length - 4) / 12 ) throw new InvalidDataException( "Invalid REL index size." );
		var entries = rest.Slice( 4, (int)Word( rest, 0 ) * 12 );
		CheckRelTables( rest[(4 + entries.Length)..] );
		for ( int at = 0; at < entries.Length; at += 12 )
		{
			uint hash = Word( entries, at );
			uint offset = Word( entries, at + 4 );
			uint length = Word( entries, at + 8 );
			if ( offset > data.Length || length < 28 || length > data.Length - offset || data[(int)offset] != 52 ) continue;
			if ( cues.TryGetValue( hash, out var old ) && ranks[old.Rank].CompareTo( ranks[rank] ) > 0 ) continue;
			cues[hash] = new AudioCue( Word( data, (int)offset + 20 ), Word( data, (int)offset + 24 ), rank );
		}
	}

	static void CheckRelTables( ReadOnlySpan<byte> bytes )
	{
		for ( int table = 0; table < 2; table++ )
		{
			if ( bytes.Length < 4 || Word( bytes, 0 ) > (bytes.Length - 4) / 4 ) throw new InvalidDataException( "Truncated REL reference table." );
			bytes = bytes[(4 + 4 * (int)Word( bytes, 0 ))..];
		}
	}
}
