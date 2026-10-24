using System;
using System.Collections.Generic;

namespace Sandbox.Clutter;

// Pure selection policy; owns no render resources and performs no frame work.
internal static class ClutterAnchorNeighbors
{
	internal const int NeighborCount = 8;
	internal const int CandidateBudget = 256;
	private static uint AnchorHash( uint value )
	{
		value ^= value >> 16;
		value *= 0x7feb352d;
		value ^= value >> 15;
		value *= 0x846ca68b;
		return value ^ (value >> 16);
	}

	private static uint PlacementHash( Vector3 root )
	{
		var x = BitConverter.SingleToUInt32Bits( root.x == 0f ? 0f : root.x );
		var y = BitConverter.SingleToUInt32Bits( root.y == 0f ? 0f : root.y );
		var z = BitConverter.SingleToUInt32Bits( root.z == 0f ? 0f : root.z );
		return AnchorHash( x ^ AnchorHash( y ^ AnchorHash( z ) ) );
	}

	internal static uint[] Build( Vector3[] roots, bool[] sourceActive, float maximumHeight, float fraction )
	{
		ArgumentNullException.ThrowIfNull( roots );
		ArgumentNullException.ThrowIfNull( sourceActive );
		if ( roots.Length != sourceActive.Length ) throw new ArgumentException( "Root and active-slot counts must match." );
		if ( !float.IsFinite( fraction ) || fraction < 0f || fraction > 1f ) throw new ArgumentOutOfRangeException( nameof( fraction ) );
		var active = (bool[])sourceActive.Clone();
		// Full 3D separation includes tilted roots and terrain. A wider candidate
		// radius only admits work; the shader applies the actual 2-height depth test.
		float cellWidth = maximumHeight * 4f;
		var result = new uint[checked(roots.Length * NeighborCount)];
		Array.Fill( result, uint.MaxValue );
		if ( float.IsFinite( cellWidth * cellWidth ) && cellWidth > 0f && fraction > 0f )
		{
			var cells = new Dictionary<(int X, int Y, int Z), List<int>>();
			for ( int id = 0; id < roots.Length; ++id )
			{
				var coordinate = roots[id] / cellWidth;
				// Leave one integer cell on either side for neighbor arithmetic.
				if ( !coordinate.IsFinite || Math.Abs( (double)coordinate.x ) >= int.MaxValue - 2d
					|| Math.Abs( (double)coordinate.y ) >= int.MaxValue - 2d || Math.Abs( (double)coordinate.z ) >= int.MaxValue - 2d ) active[id] = false;
			}
			(int X, int Y, int Z) Cell( Vector3 root ) => ((int)MathF.Floor( root.x / cellWidth ), (int)MathF.Floor( root.y / cellWidth ), (int)MathF.Floor( root.z / cellWidth ));
			for ( int id = 0; id < roots.Length; ++id )
			{
				if ( !active[id] || (PlacementHash( roots[id] ) >> 8) * (1f / 16777216f) >= fraction ) continue;
				var cell = Cell( roots[id] );
				if ( !cells.TryGetValue( cell, out var entries ) ) cells.Add( cell, entries = [] );
				entries.Add( id );
			}
			var nearest = new List<(float Distance, uint Hash, int Id)>();
			for ( int id = 0; id < roots.Length; ++id )
			{
				if ( !active[id] || (PlacementHash( roots[id] ) >> 8) * (1f / 16777216f) < fraction ) continue;
				nearest.Clear();
				var cell = Cell( roots[id] );
				// Count before scanning. A dense cell keeps this placement unchanged;
				// taking only the first budget entries would depend on storage order.
				int candidateCount = 0;
				for ( int z = -1; z <= 1; ++z )
					for ( int y = -1; y <= 1; ++y )
						for ( int x = -1; x <= 1; ++x )
							if ( cells.TryGetValue( (cell.X + x, cell.Y + y, cell.Z + z), out var candidates ) ) candidateCount += candidates.Count;
				if ( candidateCount > CandidateBudget ) continue;
				for ( int z = -1; z <= 1; ++z )
					for ( int y = -1; y <= 1; ++y )
						for ( int x = -1; x <= 1; ++x )
						{
							if ( !cells.TryGetValue( (cell.X + x, cell.Y + y, cell.Z + z), out var entries ) ) continue;
							foreach ( var anchor in entries )
							{
								float distance = (roots[anchor] - roots[id]).LengthSquared;
								if ( !float.IsFinite( distance ) || distance > cellWidth * cellWidth ) continue;
								var candidate = (Distance: distance, Hash: PlacementHash( roots[anchor] ), Id: anchor);
								int position = 0;
								while ( position < nearest.Count && nearest[position].CompareTo( candidate ) <= 0 ) ++position;
								if ( position >= NeighborCount ) continue;
								nearest.Insert( position, candidate );
								if ( nearest.Count > NeighborCount ) nearest.RemoveAt( NeighborCount );
							}
						}
				for ( int index = 0; index < nearest.Count; ++index ) result[id * NeighborCount + index] = (uint)nearest[index].Id;
			}
		}
		return result;
	}
}
