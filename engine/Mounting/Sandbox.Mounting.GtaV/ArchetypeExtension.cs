using System;
using System.Collections.Generic;
using System.IO;
using Sandbox;

namespace GtaV;

internal abstract record ArchetypeExtension
{
	public abstract void Apply( GameObject owner, bool startEnabled );

	// A converter consumes one length-delimited native record.
	static readonly Dictionary<uint, Func<BinaryReader, ArchetypeExtension>> Readers = new()
	{
		[0x821D5421] = r => new LadderExtension( r.Vector(), r.Vector(), r.Vector() )
	};

	internal static Dictionary<uint, List<ArchetypeExtension>> ReadDictionary( BinaryReader reader )
	{
		var result = new Dictionary<uint, List<ArchetypeExtension>>();
		while ( reader.BaseStream.Position < reader.BaseStream.Length )
		{
			uint owner = reader.ReadUInt32(), type = reader.ReadUInt32();
			int length = reader.ReadInt32();
			long end = reader.BaseStream.Position + length;
			if ( length < 0 || end > reader.BaseStream.Length ) throw new InvalidDataException( "Invalid archetype extension length" );
			if ( Readers.TryGetValue( type, out var read ) )
			{
				var extension = read( reader );
				if ( reader.BaseStream.Position != end ) throw new InvalidDataException( "Invalid archetype extension payload" );
				if ( !result.TryGetValue( owner, out var list ) ) result.Add( owner, list = new() );
				list.Add( extension );
			}
			reader.BaseStream.Position = end;
		}
		return result;
	}
}

internal sealed record LadderExtension( Vector3 Bottom, Vector3 Top, Vector3 Normal ) : ArchetypeExtension
{
	public override void Apply( GameObject owner, bool startEnabled )
	{
		var axis = Top - Bottom;
		var up = axis.Normal;
		var facing = Normal - up * Normal.Dot( up );
		if ( !Bottom.IsFinite || !Top.IsFinite || !Normal.IsFinite || axis.LengthSquared < 1e-6f || facing.LengthSquared < 1e-6f ) return;
		var child = new GameObject( owner, false, "Ladder" );
		child.LocalPosition = (Bottom + Top) * (0.5f * PacketReader.UnitsPerMetre);
		child.LocalRotation = Rotation.LookAt( facing.Normal, up );
		child.Tags.Add( "ladder" );
		var trigger = child.AddComponent<BoxCollider>( startEnabled );
		trigger.IsTrigger = true;
		trigger.Static = true;
		// GTA supplies the climb line. Width and depth are mount defaults in metres.
		trigger.Scale = new Vector3( 0.4f, 0.65f, axis.Length ) * PacketReader.UnitsPerMetre;
		child.Enabled = true;
	}
}
