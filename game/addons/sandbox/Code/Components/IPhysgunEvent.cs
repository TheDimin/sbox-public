public interface IPhysgunEvent : ISceneEvent<IPhysgunEvent>
{
	public class GrabEvent
	{
		/// <summary>
		/// The connection attempting to grab this object.
		/// </summary>
		public Connection Grabber { get; init; }

		/// <summary>
		/// Set to true to cancel the grab.
		/// </summary>
		public bool Cancelled { get; set; }
	}

	/// <summary>
	/// Called when a player attempts to grab this object with the physgun.
	/// Set <see cref="GrabEvent.Cancelled"/> to true to reject the grab.
	/// </summary>
	void OnPhysgunGrab( GrabEvent e ) { }

	/// <summary>
	/// Called when a physgun stops moving this object, because it was let go, frozen or launched.
	/// Runs on the instance that simulates the object, the only one that can change its body.
	/// </summary>
	void OnPhysgunDrop() { }
	void OnPhysgunDrop( Connection grabber ) => OnPhysgunDrop();
	void OnPhysgunPickup( ) { }
}

/// <summary>A part that the physics gun can release from its parent before moving it.</summary>
public interface IPhysgunAttachment
{
	bool IsAttached { get; }
	void Detach();
}
