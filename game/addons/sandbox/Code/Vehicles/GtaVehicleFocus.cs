/// <summary>Give the local driver ground collision at the vehicle while a GTA world is active.</summary>
public sealed partial class GtaVehicleFocus : Component
{
	BaseChair seat;
	Component world;
	PropertyDescription focus;
	GameObject previous;
	bool occupied;

	protected override void OnUpdate()
	{
		if ( !seat.IsValid() )
			seat = GetComponent<FourWheelVehicle>()?.Seat ?? GetComponent<BaseChair>();
		var driver = seat?.GetOccupant();
		bool local = driver.IsValid() && !driver.IsProxy;
		if ( local == occupied ) return;
		occupied = local;
		if ( !local )
		{
			Release();
			return;
		}

		var type = Game.TypeLibrary.GetType( "Sandbox.Mounting.GtaWorld", exactFullName: true );
		if ( type is null ) return;
		world = Scene.GetAllComponents( type.TargetType ).FirstOrDefault();
		focus = type.GetProperty( "PhysicsFocus" );
		if ( !world.IsValid() || focus is null ) return;
		previous = focus.GetValue( world ) as GameObject;
		focus.SetValue( world, GameObject );
	}

	void Release()
	{
		if ( world.IsValid() && focus?.GetValue( world ) as GameObject == GameObject )
			focus.SetValue( world, previous.IsValid() ? previous : null );
		world = null;
		focus = null;
		previous = null;
	}

	protected override void OnDisabled()
	{
		Release();
		occupied = false;
		ReleaseChassis();
	}
}
