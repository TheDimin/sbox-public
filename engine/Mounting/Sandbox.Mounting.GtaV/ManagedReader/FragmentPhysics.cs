using System;
using System.IO;
using System.Text;
using static GtaV.ManagedReader.MetaResource;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsVector3 = System.Numerics.Vector3;

namespace GtaV.ManagedReader;

/// <summary>Reads a fragment's first physics LOD and the groups the mount can detach.</summary>
internal readonly ref struct FragmentPhysics
{
	readonly RscResource resource;
	readonly ReadOnlySpan<byte> groups, children, bones;
	readonly ulong physicsLod;

	internal readonly record struct Child( ushort Group, ushort BoneTag, ulong Drawable );
	internal ulong PhysicsBound { get; }
	internal ulong Bound { get; }
	internal float Mass { get; }
	internal bool HasPhysicsLod { get; }
	internal ulong Drawable { get; }
	internal int GroupCount => groups.Length / sizeof(ulong);
	internal int ChildCount => children.Length / sizeof(ulong);

	internal FragmentPhysics( RscResource resource )
	{
		this.resource = resource;
		var root = resource.At( RscResource.SystemBase, 0xF8 );
		Drawable = Pointer( root, 0x30 );
		ulong physics = Pointer( root, 0xF0 );
		ulong level = physics == 0 ? 0 : Pointer( resource.At( physics + 0x10, 8 ), 0 );
		HasPhysicsLod = level != 0;
		physicsLod = level;
		Mass = -1f;
		if ( level != 0 )
		{
			var lod = resource.At( level, 0x11E );
			groups = resource.Array( Pointer( lod, 0xC8 ), lod[0x11A], sizeof(ulong) );
			children = resource.Array( Pointer( lod, 0xD0 ), lod[0x11D], sizeof(ulong) );
			PhysicsBound = Pointer( lod, 0xE8 );
			ulong archetype = Pointer( lod, 0xD8 );
			if ( archetype != 0 )
			{
				var row = resource.At( archetype, 0x44 );
				Mass = Float( row, 0x40 );
				if ( PhysicsBound == 0 ) PhysicsBound = Pointer( row, 0x20 );
			}
		}

		Bound = PhysicsBound != 0 || Drawable == 0 ? PhysicsBound : Pointer( resource.At( Drawable + 0xF0, 8 ), 0 );
		ulong skeleton = Drawable == 0 ? 0 : Pointer( resource.At( Drawable + 0x18, 8 ), 0 );
		bones = FragmentPose.ReadBones( resource, skeleton );
	}

	internal Child ChildAt( int index )
	{
		ulong pointer = Pointer( children, index * sizeof(ulong) );
		if ( pointer == 0 ) throw new InvalidDataException( "Fragment has a null physics child." );
		var row = resource.At( pointer, 0xA8 );
		return new Child( Short( row, 0x10 ), Short( row, 0x12 ), Pointer( row, 0xA0 ) );
	}

	internal float GroupMass( int id ) => Float( Group( id ), 0x44 );

	internal string GroupName( int id )
	{
		var bytes = Group( id ).Slice( 0x80, 40 );
		int end = bytes.IndexOf( (byte)0 );
		return Encoding.UTF8.GetString( end < 0 ? bytes : bytes[..end] );
	}

	internal int GetDetachableGroups( Span<byte> ids )
	{
		if ( groups.IsEmpty || children.IsEmpty ) return 0;

		Span<bool> represented = stackalloc bool[GroupCount];
		represented.Clear();
		Span<bool> modelBones = stackalloc bool[256];
		modelBones.Clear();
		if ( !bones.IsEmpty ) FindModelBones( modelBones );
		for ( int i = 0; i < ChildCount; i++ )
		{
			var child = ChildAt( i );
			if ( child.Group >= represented.Length || represented[child.Group] ) continue;
			represented[child.Group] = child.Drawable != 0 || HasModelForTag( child.BoneTag, modelBones );
		}

		int count = 0;
		for ( int i = 0; i < represented.Length; i++ )
		{
			if ( !represented[i] ) continue;
			var group = Group( i );
			float strength = Float( group, 0x10 );
			if ( group[0x4D] != 255 && float.IsFinite( strength ) && strength >= 0f ) ids[count++] = (byte)i;
		}
		return count;
	}

	bool HasModelForTag( ushort tag, ReadOnlySpan<bool> modelBones )
	{
		for ( int i = 0; i < Math.Min( modelBones.Length, bones.Length / FragmentPose.BoneSize ); i++ )
			if ( modelBones[i] && Short( bones, i * FragmentPose.BoneSize + 0x44 ) == tag ) return true;
		return false;
	}

	void FindModelBones( Span<bool> modelBones )
	{
		var row = resource.At( Drawable, 0x70 );
		for ( int i = 0; i < 4; i++ )
		{
			ulong lod = Pointer( row, 0x50 + i * 8 );
			if ( lod == 0 ) continue;
			var list = resource.At( lod, 10 );
			var models = resource.Array( Pointer( list, 0 ), Short( list, 8 ), sizeof(ulong) );
			for ( int m = 0; m < models.Length; m += sizeof(ulong) )
			{
				ulong model = Pointer( models, m );
				if ( model != 0 ) modelBones[resource.At( model + 0x2B, 1 )[0]] = true;
			}
		}
	}

	internal NumericsMatrix ChildPlacement( int index )
	{
		var lod = resource.At( physicsLod, 0x108 );
		ulong pointer = Pointer( lod, 0x100 );
		var translation = Vector( lod, 0x30 );
		var transform = NumericsMatrix.Identity;
		if ( pointer != 0 && (uint)index < Word( resource.At( pointer + 0x10, 4 ), 0 ) )
		{
			var row = resource.At( pointer + 0x20 + (ulong)index * 64, 64 );
			transform = new NumericsMatrix( Float( row, 0 ), Float( row, 4 ), Float( row, 8 ), Float( row, 12 ),
				Float( row, 16 ), Float( row, 20 ), Float( row, 24 ), Float( row, 28 ),
				Float( row, 32 ), Float( row, 36 ), Float( row, 40 ), Float( row, 44 ),
				Float( row, 48 ), Float( row, 52 ), Float( row, 56 ), Float( row, 60 ) );
		}
		transform.Translation += translation;
		return transform;
	}

	ReadOnlySpan<byte> Group( int id )
	{
		if ( (uint)id >= (uint)GroupCount ) throw new InvalidDataException( "Fragment physics group is missing." );
		ulong pointer = Pointer( groups, id * sizeof(ulong) );
		if ( pointer == 0 ) throw new InvalidDataException( "Fragment has a null physics group." );
		return resource.At( pointer, 176 );
	}
}
