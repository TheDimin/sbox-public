using System;
using System.IO;
using static GtaV.ManagedReader.MetaResource;
using NumericsMatrix = System.Numerics.Matrix4x4;

namespace GtaV.ManagedReader;

internal sealed partial class DrawableAsset
{
	ulong ReadFragmentSections( Kind kind, uint group )
	{
		var fragment = new FragmentPhysics( resource );
		var main = fragment.Drawable;
		switch ( kind )
		{
			case Kind.Fragment:
				if ( main == 0 ) main = Pointer( resource.At( RscResource.SystemBase + 0xF8, 8 ), 0 );
				ReadSections( main, new DrawOptions( false, NumericsMatrix.Identity, UseBonePose: true ) );
				for ( int i = 0; i < fragment.ChildCount; i++ )
				{
					var child = fragment.ChildAt( i );
					if ( child.Drawable != 0 )
						ReadSections( child.Drawable, new DrawOptions( false, fragment.ChildPlacement( i ), main ) );
				}
				break;
			case Kind.FragmentGroup:
				ReadGroupSections( fragment, group );
				break;
			case Kind.VehicleBody:
			case Kind.VehicleWheel:
				ReadVehicleSections( fragment, body: kind == Kind.VehicleBody );
				break;
			default: throw new InvalidDataException( "Invalid fragment model kind." );
		}
		return fragment.Bound;
	}

	void ReadGroupSections( in FragmentPhysics fragment, uint group )
	{
		if ( !fragment.HasPhysicsLod || group >= (uint)fragment.GroupCount )
			throw new InvalidDataException( "Fragment physics group is missing." );

		var pose = new FragmentPose( resource, fragment.Drawable );
		Span<ushort> emitted = stackalloc ushort[fragment.ChildCount];
		int count = 0;
		for ( int i = 0; i < fragment.ChildCount; i++ )
		{
			var child = fragment.ChildAt( i );
			if ( child.Group != group ) continue;
			if ( child.Drawable != 0 )
				ReadSections( child.Drawable, new DrawOptions( false, fragment.ChildPlacement( i ), fragment.Drawable ) );
			int bone = pose.FindBone( child.BoneTag );
			if ( bone < 0 || emitted[..count].Contains( child.BoneTag ) ) continue;
			emitted[count++] = child.BoneTag;
			ReadSections( fragment.Drawable, new DrawOptions( false, NumericsMatrix.Identity, Bone: bone, UseBonePose: true ) );
		}
	}

	void ReadVehicleSections( in FragmentPhysics fragment, bool body )
	{
		var pose = new FragmentPose( resource, fragment.Drawable );
		if ( !pose.HasSkeleton || !fragment.HasPhysicsLod ) throw new InvalidDataException( "Vehicle has no wheel skeleton or physics LOD." );
		if ( body ) ReadSections( fragment.Drawable, new DrawOptions( false, NumericsMatrix.Identity, UseBonePose: true, VehicleBody: true ) );
		for ( int i = 0; i < fragment.ChildCount; i++ )
		{
			var child = fragment.ChildAt( i );
			if ( child.Drawable == 0 ) continue;
			string name = pose.BoneName( pose.FindBone( child.BoneTag ) );
			if ( body ? name?.StartsWith( "wheel_", StringComparison.Ordinal ) == true : name != "wheel_lf" ) continue;
			var placement = body ? fragment.ChildPlacement( i ) : NumericsMatrix.Identity;
			ReadSections( child.Drawable, new DrawOptions( false, placement, fragment.Drawable, VehicleBody: body ) );
		}
	}
}
