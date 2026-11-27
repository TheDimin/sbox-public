/// <summary>A wheel follows the suspension until a bullet or the physics gun removes it.</summary>
public sealed class VehicleWheelGib : Gib, Component.IDamageable, IPhysgunAttachment, IPhysgunEvent
{
	[Property, Sync( SyncFlags.FromHost )] public int Index { get; set; }
	float spin;

	protected override void OnFixedUpdate()
	{
		var vehicle = GameObject.Parent?.GetComponent<FourWheelVehicle>();
		var body = GetComponent<Rigidbody>();
		if ( body.PhysicsBody.IsValid() && body.PhysicsBody.EnableSolidCollisions == vehicle.IsValid() )
			body.PhysicsBody.EnableSolidCollisions = !vehicle.IsValid();
		if ( !Networking.IsHost || !vehicle.IsValid() ) return;
		var travel = vehicle.HasSuspensionState ? vehicle.SuspensionTravel[Index] : 0f;
		var scale = MathF.Max( MathF.Abs( vehicle.WorldScale.z ), 0.001f );
		LocalPosition = vehicle.WheelMounts[Index] + Vector3.Down * (travel / scale);
		var axle = Index < 2 ? vehicle.FrontAxle : vehicle.RearAxle;
		if ( !vehicle.WheelsLocked )
			spin = (spin + (vehicle.Speed * Time.Delta / (axle.WheelRadius * scale)).RadianToDegree()) % 360f;
		var steer = Index < 2 ? -vehicle.SteerAngle : 0;
		LocalRotation = Rotation.FromYaw( steer ) * Rotation.FromAxis( Vector3.Left, spin )
			* Rotation.FromYaw( Index % 2 == 0 ? -90 : 90 );
	}

	void IDamageable.OnDamage( in DamageInfo damage )
	{
		if ( !IsAttached ) { base.OnDamage( damage ); return; }
		if ( damage.Damage > 0 && damage.Tags.Has( DamageTags.Bullet ) ) Detach();
	}

	public bool IsAttached => GameObject.Parent?.GetComponent<FourWheelVehicle>() is not null;

	void IPhysgunAttachment.Detach() => Detach();

	void IPhysgunEvent.OnPhysgunDrop( Connection grabber )
	{
		if ( !Networking.IsHost || IsAttached || !Model.IsValid() ) return;
		var body = GetComponent<Rigidbody>();
		if ( !body.IsValid() || !body.PhysicsBody.IsValid() ) return;

		FourWheelVehicle nearest = null;
		int socket = -1;
		float closest = float.PositiveInfinity;
		foreach ( var vehicle in Scene.GetAllComponents<FourWheelVehicle>() )
		{
			if ( vehicle.WheelGibModel != Model || vehicle.WheelMounts?.Length != FourWheelVehicle.WheelCount ) continue;
			if ( !vehicle.GameObject.HasAccess( grabber ) ) continue;
			for ( int i = 0; i < FourWheelVehicle.WheelCount; i++ )
			{
				if ( !vehicle.IsWheelMissing( i ) ) continue;
				var target = vehicle.WorldTransform.PointToWorld( vehicle.WheelMounts[i] );
				float distance = WorldPosition.Distance( target );
				float radius = (i < 2 ? vehicle.FrontAxle : vehicle.RearAxle).WheelRadius;
				if ( distance >= closest || distance > radius * MathF.Abs( vehicle.WorldScale.z ) ) continue;
				nearest = vehicle;
				socket = i;
				closest = distance;
			}
		}
		if ( nearest is null ) return;
		body.Velocity = Vector3.Zero;
		body.AngularVelocity = Vector3.Zero;
		body.MotionEnabled = false;
		body.PhysicsBody.EnableSolidCollisions = false;
		Index = socket;
		GameObject.SetParent( nearest.GameObject, false );
		LocalScale = Vector3.One;
		LocalPosition = nearest.WheelMounts[socket];
		nearest.AttachWheel( socket );
	}

	private void Detach()
	{
		if ( !Networking.IsHost ) return;
		var vehicle = GameObject.Parent?.GetComponent<FourWheelVehicle>();
		if ( !vehicle.IsValid() ) return;
		var chassis = vehicle.GetComponent<Rigidbody>();
		var velocity = chassis.PhysicsBody.GetVelocityAtPoint( WorldPosition );
		vehicle.RemoveWheel( Index );
		GameObject.SetParent( null, true );
		var body = GetComponent<Rigidbody>();
		body.MotionEnabled = true;
		body.PhysicsBody.EnableSolidCollisions = true;
		body.Velocity = velocity;
		body.AngularVelocity = chassis.AngularVelocity;
	}
}

