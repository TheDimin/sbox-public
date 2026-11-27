/// <summary>Routes the vehicle's Use action to an available seat by its exterior door.</summary>
public sealed class GtaVehicleEntry : Component, Component.IPressable
{
	[Property] public GtaVehicleSeat[] Seats { get; set; } = [];

	bool IPressable.CanPress( IPressable.Event e ) => FindSeat( e ).IsValid();

	bool IPressable.Press( IPressable.Event e ) => FindSeat( e )?.Press( e ) == true;

	IPressable.Tooltip? IPressable.GetTooltip( IPressable.Event e ) => FindSeat( e )?.GetTooltip( e );

	GtaVehicleSeat FindSeat( IPressable.Event e )
	{
		if ( e.Source is not PlayerController player ) return null;

		GtaVehicleSeat nearest = null;
		GtaVehicleSeat aimed = null;
		float nearestDistance = float.PositiveInfinity;
		float aimedDepth = float.PositiveInfinity;
		const float doorAimRadius = 18f;

		foreach ( var seat in Seats )
		{
			if ( !seat.IsValid() || !seat.CanEnter( player ) ) continue;

			var door = seat.Door.WorldPosition;
			float distance = Vector3.DistanceBetweenSquared( player.EyePosition, door );
			if ( distance < nearestDistance )
			{
				nearest = seat;
				nearestDistance = distance;
			}

			if ( e.Ray is not { } ray ) continue;
			float depth = Vector3.Dot( door - ray.Position, ray.Forward );
			if ( depth < 0f || depth >= aimedDepth || depth > player.ReachLength ) continue;
			if ( Vector3.DistanceBetweenSquared( ray.Project( depth ), door ) > doorAimRadius * doorAimRadius ) continue;

			aimed = seat;
			aimedDepth = depth;
		}

		return aimed ?? nearest;
	}
}
