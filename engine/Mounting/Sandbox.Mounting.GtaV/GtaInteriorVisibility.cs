using System.Collections.Generic;
using Sandbox;

namespace Sandbox.Mounting;

public sealed class GtaInteriorVisibility : Component, Component.ExecuteInEditor
{
	public sealed class Room
	{
		[Property] public BBox Bounds { get; set; }
		[Property] public List<GameObject> Objects { get; set; } = new();
	}

	public sealed class Portal
	{
		[Property] public int From { get; set; }
		[Property] public int To { get; set; }
		[Property] public List<Vector3> Corners { get; set; } = new();
	}

	[Property] public List<Room> Rooms { get; set; } = new();
	[Property] public List<Portal> Portals { get; set; } = new();
	[Property] public List<string> EnabledSets { get; set; } = new();
	[Property] public Dictionary<string, GameObject> EntitySets { get; set; } = new();

	readonly HashSet<int> visibleRooms = new();
	readonly HashSet<GameObject> visibleObjects = new();
	readonly Queue<int> pending = new();

	protected override void OnUpdate()
	{
		foreach ( var (name, obj) in EntitySets )
			if ( obj.IsValid() ) obj.Enabled = EnabledSets.Contains( name );

		if ( Scene.Camera is not { } camera || Rooms.Count == 0 ) return;
		var eye = WorldTransform.PointToLocal( camera.WorldPosition );
		var frustum = camera.GetFrustum();
		int start = 0;
		for ( int i = 1; i < Rooms.Count; i++ )
			if ( Rooms[i].Bounds.Contains( eye ) ) { start = i; break; }

		visibleRooms.Clear();
		pending.Clear();
		visibleRooms.Add( start );
		pending.Enqueue( start );
		while ( pending.TryDequeue( out int room ) )
		{
			foreach ( var portal in Portals )
			{
				int other = portal.From == room ? portal.To : portal.To == room ? portal.From : -1;
				if ( (uint)other >= (uint)Rooms.Count || portal.Corners.Count < 3 || visibleRooms.Contains( other ) ) continue;
				var first = WorldTransform.PointToWorld( portal.Corners[0] );
				var bounds = new BBox( first, first );
				for ( int i = 1; i < portal.Corners.Count; i++ )
					bounds = bounds.AddPoint( WorldTransform.PointToWorld( portal.Corners[i] ) );
				// Keep portals that intersect the view or are close to the camera's near plane.
				if ( bounds.ClosestPoint( camera.WorldPosition ).DistanceSquared( camera.WorldPosition ) > 64f
					&& !frustum.IsInside( bounds, partially: true ) ) continue;
				visibleRooms.Add( other );
				pending.Enqueue( other );
			}
		}

		visibleObjects.Clear();
		foreach ( int room in visibleRooms )
			foreach ( var obj in Rooms[room].Objects )
				visibleObjects.Add( obj );
		foreach ( var room in Rooms )
			foreach ( var obj in room.Objects )
				if ( obj.IsValid() )
					foreach ( var renderer in obj.GetComponentsInChildren<ModelRenderer>( true ) )
						renderer.Enabled = visibleObjects.Contains( obj );
	}
}
