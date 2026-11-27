using System;
using static GtaV.ManagedReader.MetaResource;
using NumericsMatrix = System.Numerics.Matrix4x4;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace GtaV.ManagedReader;

/// <summary>Provides draw transforms without modifying the fragment's vertex data.</summary>
internal readonly ref struct FragmentPose
{
	internal const int BoneSize = 80;
	const int TransformSize = 48;
	readonly RscResource resource;
	readonly ReadOnlySpan<byte> bones, transforms;

	internal bool HasSkeleton { get; }

	internal FragmentPose( RscResource resource, ulong drawable )
	{
		this.resource = resource;
		ulong skeleton = drawable == 0 ? 0 : Pointer( resource.At( drawable + 0x18, 8 ), 0 );
		HasSkeleton = skeleton != 0;
		bones = ReadBones( resource, skeleton );
		ulong pointer = Pointer( resource.At( RscResource.SystemBase + 0xA8, 8 ), 0 );
		if ( pointer != 0 )
		{
			int count = resource.At( pointer + 0x10, 1 )[0];
			transforms = resource.Array( pointer + 0x20, count, TransformSize );
		}
	}

	internal static ReadOnlySpan<byte> ReadBones( RscResource resource, ulong skeleton )
	{
		if ( skeleton == 0 ) return [];
		var row = resource.At( skeleton, 0x60 );
		return resource.Array( Pointer( row, 0x20 ), Short( row, 0x5E ), BoneSize );
	}

	internal int FindBone( ushort tag )
	{
		for ( int i = 0; i < bones.Length / BoneSize; i++ )
			if ( Short( bones, i * BoneSize + 0x44 ) == tag ) return i;
		return -1;
	}

	internal string BoneName( int bone ) => bone < 0 ? null : resource.ReadString( Pointer( Bone( bone ), 0x38 ) );

	internal NumericsMatrix Placement( int bone, in NumericsMatrix fallback )
	{
		if ( bone < transforms.Length / TransformSize )
		{
			var row = transforms.Slice( bone * TransformSize, TransformSize );
			return new NumericsMatrix(
				Float( row, 0 ), Float( row, 16 ), Float( row, 32 ), 0,
				Float( row, 4 ), Float( row, 20 ), Float( row, 36 ), 0,
				Float( row, 8 ), Float( row, 24 ), Float( row, 40 ), 0,
				Float( row, 12 ), Float( row, 28 ), Float( row, 44 ), 1 );
		}
		if ( bone >= bones.Length / BoneSize ) return fallback;

		var data = Bone( bone );
		var position = Vector( data, 16 );
		var rotation = Vector4( data, 0 );
		int parent = unchecked((short)Short( data, 0x32 ));
		for ( int depth = 0; (uint)parent < (uint)(bones.Length / BoneSize) && depth < 512; depth++ )
		{
			var row = Bone( parent );
			var q = Vector4( row, 0 );
			var axis = new NumericsVector3( q.X, q.Y, q.Z );
			var cross = NumericsVector3.Cross( axis, position );
			position = position + cross * (2f * q.W) + NumericsVector3.Cross( axis, cross ) * 2f + Vector( row, 16 );
			rotation = Multiply( q, rotation );
			parent = unchecked((short)Short( row, 0x32 ));
		}
		return BindMatrix( rotation, position );
	}

	ReadOnlySpan<byte> Bone( int index ) => bones.Slice( index * BoneSize, BoneSize );

	static NumericsVector4 Multiply( NumericsVector4 a, NumericsVector4 b ) => new(
		a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
		a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
		a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
		a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z );

	static NumericsMatrix BindMatrix( NumericsVector4 q, NumericsVector3 position )
	{
		float xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z;
		float xy = q.X * q.Y, zw = q.Z * q.W, zx = q.Z * q.X, yw = q.Y * q.W, yz = q.Y * q.Z, xw = q.X * q.W;
		return new NumericsMatrix(
			1 - 2f * (yy + zz), 2f * (xy + zw), 2f * (zx - yw), 0,
			2f * (xy - zw), 1 - 2f * (zz + xx), 2f * (yz + xw), 0,
			2f * (zx + yw), 2f * (yz - xw), 1 - 2f * (yy + xx), 0,
			position.X, position.Y, position.Z, 1 );
	}
}
