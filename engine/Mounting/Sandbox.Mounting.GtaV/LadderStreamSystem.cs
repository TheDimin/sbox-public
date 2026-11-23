using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;

namespace GtaV;

internal sealed class LadderStreamSystem( PlacementCache maps, GameObject root ) : ISubStreamSystem
{
	const float Range = 5;
	const float SearchStep = 4;
	readonly Dictionary<WorldArea, List<Ladder>> loaded = new();
	readonly List<Ladder> interiors = new();
	HashSet<WorldArea> wanted = new();
	Queue<WorldArea> queue = new();
	WorldArea pending;
	Task<MapRecords> read;
	Vector3? last;

	sealed class Ladder( Vector3 bottom, Vector3 top, LadderExtension definition, Transform placement, GameObject obj )
	{
		internal GameObject Object = obj;

		internal void Update( GameObject owner, Vector3 focus )
		{
			var axis = top - bottom;
			float along = axis.LengthSquared > 0 ? Math.Clamp( (focus - bottom).Dot( axis ) / axis.LengthSquared, 0, 1 ) : 0;
			bool near = focus.DistanceSquared( bottom + along * axis ) < Range * Range;
			if ( near && Object is null )
			{
				Object = definition.Create( owner, placement );
				if ( Object.IsValid() ) Object.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
			}
			if ( Object.IsValid() && Object.Enabled != near ) Object.Enabled = near;
		}
	}

	internal void TrackInterior( GameObject child, float height )
	{
		var half = Vector3.Up * (height * 0.5f);
		var bottom = child.WorldTransform.PointToWorld( -half ) / PacketReader.UnitsPerMetre;
		var top = child.WorldTransform.PointToWorld( half ) / PacketReader.UnitsPerMetre;
		interiors.Add( new( bottom, top, null, default, child ) );
	}

	public void Update( WorldCatalog catalog, Vector3 focus )
	{
		if ( last is null || last.Value.DistanceSquared( focus ) >= SearchStep * SearchStep )
		{
			last = focus;
			wanted = catalog.Maps.Query( focus, Range + SearchStep );
			foreach ( var map in loaded.Keys.Where( x => !wanted.Contains( x ) ).ToArray() ) Release( map );
			queue = new( wanted.Where( x => !loaded.ContainsKey( x ) && x != pending )
				.OrderBy( x => x.Entities.ClosestPoint( focus ).DistanceSquared( focus ) ) );
		}

		if ( read?.IsCompleted == true )
		{
			if ( read.IsFaulted ) WorldSession.StreamingLog.Warning( read.Exception );
			if ( wanted.Contains( pending ) )
			{
				var ladders = new List<Ladder>();
				if ( read.IsCompletedSuccessfully )
				{
					foreach ( var entity in read.Result.Entities )
						foreach ( var extension in entity.Extensions )
						{
							if ( extension is not LadderExtension ladder ) continue;
							var t = entity.Transform;
							var a = t.PointToWorld( ladder.Bottom * PacketReader.UnitsPerMetre ) / PacketReader.UnitsPerMetre;
							var b = t.PointToWorld( ladder.Top * PacketReader.UnitsPerMetre ) / PacketReader.UnitsPerMetre;
							ladders.Add( new( a, b, ladder, t, null ) );
						}
				}
				loaded.Add( pending, ladders );
			}
			else maps.Release( pending.File );
			read = null;
			pending = null;
		}
		if ( read is null && queue.TryDequeue( out pending ) ) read = maps.Acquire( pending.File );

		foreach ( var ladders in loaded.Values )
			foreach ( var ladder in ladders ) ladder.Update( root, focus );
		for ( int i = interiors.Count - 1; i >= 0; i-- )
		{
			var ladder = interiors[i];
			if ( !ladder.Object.IsValid() ) interiors.RemoveAt( i );
			else ladder.Update( root, focus );
		}
	}

	void Release( WorldArea map )
	{
		foreach ( var ladder in loaded[map] )
		{
			if ( !ladder.Object.IsValid() ) continue;
			ladder.Object.Enabled = false;
			ladder.Object.Destroy();
		}
		loaded.Remove( map );
		maps.Release( map.File );
	}

	internal void ClearFocus()
	{
		if ( last.HasValue )
		{
			foreach ( var map in loaded.Keys.ToArray() ) Release( map );
			wanted.Clear();
			queue.Clear();
			if ( read is not null ) maps.Release( pending.File );
			read = null;
			pending = null;
			last = null;
		}
		foreach ( var ladder in interiors )
			if ( ladder.Object.IsValid() ) ladder.Object.Enabled = false;
	}

	public void Dispose()
	{
		ClearFocus();
		interiors.Clear();
	}
}
