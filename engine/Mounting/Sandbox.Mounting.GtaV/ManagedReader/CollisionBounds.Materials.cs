using System;
using static GtaV.ManagedReader.MetaResource;

namespace GtaV.ManagedReader;

internal sealed partial class CollisionBounds
{
	const uint DoorFlag = 1u << 26;

	internal float Mass( ulong pointer, in YtypCatalog.Archetype archetype, CollisionTuning tuning )
	{
		if ( tuning.TryMassOverride( archetype.Name, out float overridden ) ) return overridden;
		var root = resource.At( pointer, BoundSize );
		float density = tuning.Density( MaterialType( pointer, root ) );
		if ( (Shape)root[0x10] == Shape.Composite )
		{
			var children = new ChildList( resource, pointer );
			float weighted = 0, volume = 0;
			for ( int i = 0; i < children.Count; i++ )
			{
				ulong child = children.PointerAt( i );
				if ( child == 0 ) continue;
				var row = resource.At( child, BoundSize );
				float part = Float( row, 0x6C );
				weighted += tuning.Density( MaterialType( child, row ) ) * part;
				volume += part;
			}
			if ( volume > 0 ) density = weighted / volume;
		}
		float mass = MathF.Abs( Float( root, 0x6C ) ) * density;
		if ( (archetype.Flags & DoorFlag) == 0 ) return mass;

		bool large = Float( root, 0x20 ) - Float( root, 0x30 ) > 2;
		mass *= tuning.DoorMultiplier( archetype.Name );
		return large ? Math.Clamp( mass * 0.3f, 50f, 100f ) : Math.Clamp( mass * 0.1f, 35f, 70f );
	}

	internal string DestructionMaterial( ulong pointer, CollisionTuning tuning )
	{
		if ( pointer == 0 ) return string.Empty;
		Span<int> votes = stackalloc int[256];
		votes.Clear();
		Span<ulong> path = stackalloc ulong[MaxDepth];
		Vote( pointer, votes, path, 0 );
		int best = 0;
		for ( int i = 1; i < votes.Length; i++ )
			if ( votes[i] > votes[best] ) best = i;
		return votes[best] == 0 ? string.Empty : tuning.Name( best );
	}

	byte MaterialType( ulong pointer, ReadOnlySpan<byte> row )
	{
		if ( (Shape)row[0x10] is Shape.Geometry or Shape.GeometryBvh )
		{
			var materials = GeometryMaterials( resource.At( pointer, 0x122 ) );
			if ( !materials.IsEmpty ) return materials[0];
		}
		return row[0x4C];
	}

	ReadOnlySpan<byte> GeometryMaterials( ReadOnlySpan<byte> geometry ) =>
		resource.Array( Pointer( geometry, 0xF0 ), geometry[0x120], 8 );

	void Vote( ulong pointer, Span<int> votes, Span<ulong> path, int depth )
	{
		Visit( path, depth, pointer );
		var row = resource.At( pointer, BoundSize );
		var shape = (Shape)row[0x10];
		if ( shape == Shape.Composite )
		{
			var children = new ChildList( resource, pointer );
			for ( int i = 0; i < children.Count; i++ )
			{
				ulong child = children.PointerAt( i );
				if ( child != 0 ) Vote( child, votes, path, depth + 1 );
			}
			if ( children.Count != 0 ) return;
		}
		if ( shape is Shape.Geometry or Shape.GeometryBvh )
		{
			var geometry = resource.At( pointer, 0x122 );
			var indices = resource.Array( Pointer( geometry, 0x118 ), checked((int)Word( geometry, 0xD4 )), 1 );
			if ( !indices.IsEmpty )
			{
				var materials = GeometryMaterials( geometry );
				int count = materials.Length / 8;
				foreach ( byte index in indices )
					if ( index < count ) votes[materials[index * 8]]++;
				return;
			}
		}
		votes[row[0x4C]]++;
	}
}
