using System;
using System.Collections.Generic;
using System.IO;
using Sandbox;

namespace GtaV;

internal abstract record ArchetypeExtension
{
	public abstract void Apply( GameObject owner );

	// A converter consumes one length-delimited native record.
	static readonly Dictionary<uint, Func<BinaryReader, ArchetypeExtension>> Readers = new()
	{
		[0x06383F69] = _ => DestroyExplosion.Instance,
		[0x3A26E5E1] = r => new DestructionParticleExtension( r.ReadInt32(), r.ReadInt32(), r.Text() ),
		[0x15DEDA27] = r => new DestructionAudioExtension( r.ReadUInt32(), r.ReadUInt32(), r.ReadUInt32() ),
		[0xAD18FA1C] = r => new DestructionExplosionExtension( r.Text() ),
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

internal sealed record DestructionParticleExtension( int Trigger, int Bone, string Name ) : ArchetypeExtension
{
	public override void Apply( GameObject owner ) { }
}

internal sealed record DestructionAudioExtension( uint Settings, uint DetachSound, uint DestroySound ) : ArchetypeExtension
{
	public override void Apply( GameObject owner ) { }
}

internal sealed record DestructionExplosionExtension( string Name ) : ArchetypeExtension
{
	public override void Apply( GameObject owner ) { }
}

internal sealed record LadderExtension( Vector3 Bottom, Vector3 Top, Vector3 Normal ) : ArchetypeExtension
{
	public override void Apply( GameObject owner ) => Create( owner, Transform.Zero );

	internal GameObject Create( GameObject owner, Transform placement )
	{
		var axis = Top - Bottom;
		var up = axis.Normal;
		var facing = Normal - up * Normal.Dot( up );
		if ( !Bottom.IsFinite || !Top.IsFinite || !Normal.IsFinite || axis.LengthSquared < 1e-6f || facing.LengthSquared < 1e-6f ) return null;
		var child = new GameObject( owner, false, "Ladder" );
		child.LocalTransform = placement.ToWorld( new Transform( (Bottom + Top) * (0.5f * PacketReader.UnitsPerMetre), Rotation.LookAt( facing.Normal, up ) ) );
		child.Tags.Add( "ladder" );
		var trigger = child.AddComponent<BoxCollider>();
		trigger.IsTrigger = true;

		// GTA supplies the climb line. Width and depth are mount defaults in metres.
		trigger.Scale = new Vector3( 0.4f, 0.65f, axis.Length ) * PacketReader.UnitsPerMetre;
		return child;
	}
}

internal sealed record DestroyExplosion : ArchetypeExtension
{
	internal static readonly DestroyExplosion Instance = new();

	// Prop reads health and explosion settings from the mounted model.
	public override void Apply( GameObject owner ) { }
}
