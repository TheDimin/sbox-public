public sealed partial class GtaVehicleFocus
{
	[Sync( SyncFlags.FromHost )] bool DetailedChassis { get; set; }

	BoxCollider simpleChassis;
	GameObject detailedChassis;

	internal void UpdateChassis( IReadOnlyList<Vector3> players )
	{
		if ( Networking.IsHost )
		{
			// Distances are in s&box inches. Keep a wider release radius to avoid repeated swaps.
			float radius = (DetailedChassis ? 100f : 75f) * 39.37008f;
			bool needed = GetComponent<FourWheelVehicle>()?.Seat?.IsOccupied == true;
			foreach ( var position in players )
				needed |= Vector3.DistanceBetweenSquared( WorldPosition, position ) <= radius * radius;

			if ( needed ) DetailedChassis = true;
			else if ( DetailedChassis && GetComponent<Rigidbody>()?.PhysicsBody is { Sleeping: true }
				&& WorldRotation.Up.z > 0.85f ) DetailedChassis = false;
		}

		if ( !DetailedChassis && !detailedChassis.IsValid() ) return;
		if ( !simpleChassis.IsValid() )
			simpleChassis = GetComponentsInChildren<BoxCollider>( true ).FirstOrDefault( c => c.GameObject.Tags.Has( "vehicle_chassis" ) );
		if ( !simpleChassis.IsValid() ) return;

		if ( DetailedChassis && !detailedChassis.IsValid() )
		{
			var body = GetComponentsInChildren<ModelRenderer>().FirstOrDefault( r =>
				r.Model?.ResourcePath?.StartsWith( "mount://gtav/", StringComparison.OrdinalIgnoreCase ) == true &&
				r.Model.ResourcePath.EndsWith( "/body.vmdl", StringComparison.OrdinalIgnoreCase ) );
			if ( !body.IsValid() || body.Model.Physics?.Parts.Any( part => part.Hulls.Count > 0 ) != true ) return;

			// Use the visual body's coordinate frame. The chassis box is in the vehicle root's frame.
			using var scope = Scene.Push();
			detailedChassis = new GameObject( body.GameObject, false, "Detailed chassis collision" );
			detailedChassis.LocalTransform = global::Transform.Zero;
			detailedChassis.NetworkMode = NetworkMode.Never;
			detailedChassis.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
			detailedChassis.Tags.Add( "vehicle_chassis" );
			detailedChassis.AddComponent<ModelCollider>().Model = body.Model;
		}

		if ( detailedChassis.Enabled == DetailedChassis && simpleChassis.Enabled != DetailedChassis ) return;
		if ( DetailedChassis ) detailedChassis.Enabled = true;
		simpleChassis.Enabled = !DetailedChassis;
		if ( !DetailedChassis ) detailedChassis.Enabled = false;
	}

	void ReleaseChassis()
	{
		if ( simpleChassis.IsValid() ) simpleChassis.Enabled = true;
		if ( detailedChassis.IsValid() ) detailedChassis.Destroy();
		detailedChassis = null;
	}
}

internal sealed class GtaVehicleCollisionSystem : GameObjectSystem<GtaVehicleCollisionSystem>
{
	readonly List<Vector3> players = new();
	TimeUntil nextCheck;

	public GtaVehicleCollisionSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 0, UpdateVehicles, "GTA vehicle collision" );
	}

	void UpdateVehicles()
	{
		if ( Scene != Game.ActiveScene || nextCheck > 0 ) return;
		nextCheck = 0.25f;
		players.Clear();
		if ( Networking.IsHost )
			foreach ( var player in Scene.GetAllComponents<Player>() ) players.Add( player.WorldPosition );
		foreach ( var vehicle in Scene.GetAllComponents<GtaVehicleFocus>() ) vehicle.UpdateChassis( players );
	}
}
