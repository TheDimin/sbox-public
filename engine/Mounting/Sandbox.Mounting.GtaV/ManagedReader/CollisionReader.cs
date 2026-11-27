using System;
using System.IO;
using static GtaV.ManagedReader.MetaResource;

namespace GtaV.ManagedReader;

/// <summary>Reads standalone and drawable collision payloads.</summary>
internal sealed class CollisionReader( ArchiveIndex index, ArchiveContent content )
{
	const uint FixedFlag = 1u << 5;
	const uint ObjectFlag = 1u << 17;
	readonly Lazy<CollisionTuning> tuning = new( () => new CollisionTuning( index, content ) );
	readonly Lazy<VehiclePaint.Preset[]> paints = new( () => VehiclePaint.Read( index, content ) );

	internal BinaryReader Read( in ArchiveIndex.FileEntry file )
	{
		using var resource = new RscResource( content, file );
		if ( resource.Version != 43 )
			throw new InvalidDataException( $"YBN resource version {resource.Version}. Expected version 43." );
		using var packet = new MountPacket();
		new CollisionBounds( resource ).Write( packet.Writer, RscResource.SystemBase );
		return packet.Finish();
	}

	internal void ReadTriangles( in ArchiveIndex.FileEntry file, Action<System.Numerics.Vector3, System.Numerics.Vector3, System.Numerics.Vector3> triangle, Action endLeaf )
	{
		using var resource = new RscResource( content, file );
		if ( resource.Version != 43 )
			throw new InvalidDataException( $"YBN resource version {resource.Version}. Expected version 43." );
		new CollisionBounds( resource ).ReadTriangles( RscResource.SystemBase, triangle, endLeaf );
	}

	internal BinaryReader ReadObject( in ArchiveIndex.FileEntry file, uint member, bool includeMaterial = false )
	{
		if ( file.Extension == ".yft" ) return ReadFragment( file, includeGroups: false );
		bool dictionary = file.Extension == ".ydd";
		if ( !dictionary && file.Extension != ".ydr" )
			throw new InvalidDataException( "Unsupported drawable collision resource." );
		using var resource = new RscResource( content, file );
		ulong root = dictionary ? DrawableAsset.FindMember( resource, member ) ?? 0 : RscResource.SystemBase;
		ulong bound = root == 0 ? 0 : Pointer( resource.At( root + 0xC8, 8 ), 0 );
		var archetype = index.FindArchetype( dictionary ? member : file.Hash )?.Data;
		using var packet = new MountPacket();
		WriteDrawable( packet.Writer, resource, bound, archetype, includeMaterial );
		return packet.Finish();
	}

	internal BinaryReader ReadFragment( in ArchiveIndex.FileEntry file, bool includeGroups )
	{
		using var resource = new RscResource( content, file );
		var fragment = new FragmentPhysics( resource );
		using var packet = new MountPacket();
		WriteFragment( packet.Writer, resource, fragment, file.Hash, includeGroups );
		return packet.Finish();
	}

	void WriteFragment( BinaryWriter writer, RscResource resource, in FragmentPhysics fragment, uint name, bool includeGroups )
	{
		uint flags = index.FindArchetype( name )?.Data.Flags ?? 0;
		bool isObject = (flags & ObjectFlag) != 0;
		var bounds = new CollisionBounds( resource );

		bounds.Write( writer, fragment.Bound, isObject && (flags & FixedFlag) == 0 );
		if ( includeGroups )
		{
			Span<byte> groups = stackalloc byte[fragment.GroupCount];
			int count = fragment.GetDetachableGroups( groups );
			writer.Write( count );
			foreach ( byte group in groups[..count] )
			{
				writer.Write( (uint)group );
				MountPacket.WriteText( writer, fragment.GroupName( group ) );
			}
		}
		writer.Write( fragment.Mass );
		writer.Write( isObject );
		if ( includeGroups )
			MountPacket.WriteText( writer, fragment.Bound != 0 ? bounds.DestructionMaterial( fragment.Bound, tuning.Value ) : string.Empty );
	}

	internal BinaryReader ReadFragmentGroup( in ArchiveIndex.FileEntry file, uint group )
	{
		using var resource = new RscResource( content, file );
		var fragment = new FragmentPhysics( resource );
		using var packet = new MountPacket();
		WriteGroup( packet.Writer, resource, fragment, group );
		return packet.Finish();
	}

	static void WriteGroup( BinaryWriter writer, RscResource resource, in FragmentPhysics fragment, uint group )
	{
		if ( !fragment.HasPhysicsLod ) throw new InvalidDataException( "Fragment has no physics LOD." );

		Span<bool> selected = stackalloc bool[fragment.ChildCount];
		for ( int i = 0; i < selected.Length; i++ ) selected[i] = fragment.ChildAt( i ).Group == group;
		new CollisionBounds( resource ).WriteSelectedHulls( writer, fragment.PhysicsBound, selected );
		writer.Write( fragment.GroupMass( checked((int)group) ) );
	}

	internal void WriteDrawable( BinaryWriter writer, RscResource resource, ulong bound, YtypCatalog.Archetype? archetype, bool material = false )
	{
		uint flags = archetype?.Flags ?? 0;
		bool isObject = (flags & ObjectFlag) != 0;
		bool movable = isObject && (flags & FixedFlag) == 0;
		var bounds = new CollisionBounds( resource );
		bounds.Write( writer, bound, movable );
		writer.Write( movable && bound != 0 && archetype is { } item ? bounds.Mass( bound, item, tuning.Value ) : -1f );
		writer.Write( isObject );
		if ( material && isObject )
			MountPacket.WriteText( writer, bound != 0 ? bounds.DestructionMaterial( bound, tuning.Value ) : string.Empty );
	}

	internal BinaryReader ReadVehicle( in ArchiveIndex.FileEntry file, bool body )
	{
		using var packet = new MountPacket();
		var writer = packet.Writer;
		if ( body )
		{
			using var resource = new RscResource( content, file );
			WriteVehicleBody( writer, resource, new FragmentPhysics( resource ) );
		}
		else writer.Write( 0 );
		return packet.Finish();
	}

	void WriteVehicleBody( BinaryWriter writer, RscResource resource, in FragmentPhysics fragment )
	{
		var pose = new FragmentPose( resource, fragment.Drawable );
		if ( !pose.HasSkeleton || !fragment.HasPhysicsLod ) throw new InvalidDataException( "Vehicle has no wheel skeleton or physics LOD." );
		Span<bool> selected = stackalloc bool[fragment.ChildCount];
		for ( int i = 0; i < selected.Length; i++ )
		{
			var name = pose.BoneName( pose.FindBone( fragment.ChildAt( i ).BoneTag ) );
			selected[i] = name is not null && name is not ("chassis_lowlod" or "chassis_dummy")
				&& !name.StartsWith( "wheel_", StringComparison.Ordinal );
		}
		new CollisionBounds( resource ).WriteSelectedHulls( writer, fragment.PhysicsBound, selected, requireMatchingCounts: true );
		VehiclePaint.Write( writer, paints.Value );
	}

	internal BinaryReader ModelMetadata( DrawableAsset drawable, in ArchiveIndex.FileEntry file, Entry entry )
	{
		using var packet = new MountPacket();
		var writer = packet.Writer;
		var resource = drawable.Resource;
		switch ( entry.Kind )
		{
			case Kind.Model:
			case Kind.DictionaryModel:
				var archetype = index.FindArchetype( entry.Kind == Kind.DictionaryModel ? entry.Member : file.Hash )?.Data;
				WriteDrawable( writer, resource, drawable.BoundsPointer, archetype, material: true );
				break;
			case Kind.Fragment:
				WriteFragment( writer, resource, new FragmentPhysics( resource ), file.Hash, includeGroups: true );
				break;
			case Kind.FragmentGroup:
				WriteGroup( writer, resource, new FragmentPhysics( resource ), entry.Member );
				break;
			case Kind.VehicleBody:
				WriteVehicleBody( writer, resource, new FragmentPhysics( resource ) );
				break;
			case Kind.VehicleWheel:
				writer.Write( 0 );
				break;
			default:
				throw new InvalidDataException( "Invalid mounted model kind." );
		}
		return packet.Finish();
	}
}
