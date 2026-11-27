using System;
using System.Collections.Generic;
using System.IO;

namespace GtaV.ManagedReader;

internal readonly ref struct PsoWorldManifest( ReadOnlySpan<byte> file )
{
	static readonly uint RootType = PsoResource.Hash( "CPackFileMetaData" );
	static readonly uint Groups = PsoResource.Hash( "MapDataGroups" );
	static readonly uint Interiors = PsoResource.Hash( "Interiors" );
	static readonly uint Name = PsoResource.Hash( "Name" );
	static readonly uint Bounds = PsoResource.Hash( "Bounds" );
	readonly PsoResource resource = new( file );

	internal void Read( HashSet<uint> groups, Dictionary<uint, uint> interiors )
	{
		var root = resource.Root;
		if ( root.Type != RootType ) throw new InvalidDataException( "Invalid PSO world manifest root." );
		var mapGroups = resource.Array( root, Groups );
		var interiorRows = resource.Array( root, Interiors );
		var groupNames = new uint[mapGroups.Count];
		for ( int i = 0; i < groupNames.Length; i++ ) groupNames[i] = resource.GetHash( resource.ObjectAt( mapGroups, i ), Name );
		var collision = new List<(uint Bound, uint Interior)>();
		for ( int i = 0; i < interiorRows.Count; i++ )
		{
			var interior = resource.ObjectAt( interiorRows, i );
			uint name = resource.GetHash( interior, Name );
			var bounds = resource.Array( interior, Bounds );
			for ( int j = 0; j < bounds.Count; j++ ) collision.Add( (resource.GetArrayHash( bounds, j ), name) );
		}
		groups.UnionWith( groupNames );
		foreach ( var (bound, interior) in collision ) interiors[bound] = interior;
	}
}
