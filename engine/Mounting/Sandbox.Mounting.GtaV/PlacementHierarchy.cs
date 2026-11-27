using System.Collections.Generic;
using Sandbox;

namespace GtaV;

// Render and physics requests acquire the same placement, in either order.
internal sealed class PlacementHierarchy( GameObject world, StreamedDestructibles damage )
{
	sealed class Placement( LodEntityId id, GameObject obj )
	{
		internal readonly LodEntityId Id = id;
		internal readonly GameObject Object = obj;
		internal System.Action OnMovement;
		internal bool Render, Physics;
		internal float LodDistance;
		internal Prop Prop;
		internal BaseChair[] Seats = [];
		internal ModelCollider Collider;
	}

	readonly Dictionary<LodEntityId, Placement> entries = new();
	readonly Dictionary<GameObject, Placement> objects = new();
	readonly Dictionary<LodEntityId, GameObject> visible = new();
	readonly HashSet<Placement> moved = new();
	readonly HashSet<Placement> pendingPromotion = new();
	readonly GameObject dynamicRoot = new GameObject( world.Scene, true, "GtaWorldDynamic" )
	{
		Flags = GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.GroupRoots
	};

	internal GameObject AddModel( LodEntityId id, WorldEntity entity, Model model, bool physics )
	{
		using var scope = new StreamMeasurements.Stage( physics ? "physics_object" : "render_object" );
		if ( damage.IsBroken( id ) ) return null;
		if ( !entries.TryGetValue( id, out var p ) )
		{
			bool anchored = entity.IsStatic || entity.Hash == ArchetypePhysics.ParkingHut2;
			var obj = new GameObject( world, false, entity.ModelPath );
			obj.IsStatic = anchored;
			obj.WorldTransform = entity.Transform;
			p = new( id, obj ) { LodDistance = entity.Distance };
			entries.Add( id, p );
			objects.Add( obj, p );
			if ( entity.UsesProp )
			{
				p.Prop = obj.AddComponent<Prop>();
				p.Prop.IsStatic = anchored;
				p.Prop.StartAsleep = true;
				p.Prop.PhysicsEnabled = physics;
				p.Prop.Model = model;
				if ( entity.IsObject ) p.Prop.Health = ArchetypePhysics.ObjectHealth;
				damage.AttachProp( id, p.Prop );
				p.Prop.OnGibsCreated += GroupGibs;
				p.Seats = ChairArchetypes.Attach( obj, entity.ModelPath );
				SetSeatTriggers( p, physics );
				if ( p.Seats.Length > 0 ) p.Prop.OnPropBreak += () => ChairArchetypes.Eject( p.Seats );
			}
			obj.Enabled = true;
			if ( !anchored && p.Prop.IsValid() )
			{
				var origin = obj.WorldTransform;
				p.OnMovement = () =>
				{
					if ( obj.WorldPosition.DistanceSquared( origin.Position ) > 1 || obj.WorldRotation.Distance( origin.Rotation ) > 1 )
						pendingPromotion.Add( p );
				};
				obj.Transform.OnTransformChanged += p.OnMovement;
				p.Prop.OnPropTakeDamage += hit => { if ( hit.Damage > 0 && !moved.Contains( p ) ) pendingPromotion.Add( p ); };
			}
		}

		if ( pendingPromotion.Contains( p ) || moved.Contains( p ) ) return p.Object;
		if ( physics )
		{
			p.Physics = true;
			SetSeatTriggers( p, true );
			if ( p.Prop.IsValid() ) p.Prop.PhysicsEnabled = true;
			else
			{
				p.Collider = p.Object.Components.GetOrCreate<ModelCollider>();
				p.Collider.Static = true;
				p.Collider.Model = model;
				p.Collider.Enabled = true;
			}
			if ( !p.Render ) Display( p.Object, false );
		}
		else
		{
			p.Render = true;
			if ( !p.Prop.IsValid() ) p.Object.Components.GetOrCreate<ModelRenderer>().Model = model;
		}
		GtaFlagTags.ApplyShadows( p.Object.Components.Get<ModelRenderer>( true ), entity.ArchetypeMask, entity.Flags );
		return p.Object;
	}

	void GroupGibs( List<Gib> pieces )
	{
		foreach ( var piece in pieces )
			if ( piece.IsValid() && !(Networking.IsActive && piece.Network.Active) ) piece.GameObject.SetParent( dynamicRoot, true );
	}

	static void SetSeatTriggers( Placement placement, bool enabled )
	{
		foreach ( var seat in placement.Seats )
			if ( seat.GameObject != placement.Object ) seat.GameObject.Enabled = enabled;
	}

	internal bool IsVisible( LodEntityId id, GameObject model ) => visible.GetValueOrDefault( id ) == model;

	internal void SetVisible( LodEntityId id, GameObject model, bool enabled, bool fadingOut = false )
	{
		if ( enabled ) visible[id] = model;
		else visible.Remove( id );
		if ( enabled || !fadingOut ) Display( model, enabled );
	}

	internal void Display( GameObject obj, bool enabled )
	{
		if ( !objects.ContainsKey( obj ) ) obj.Enabled = enabled;
		else if ( !moved.Contains( objects[obj] ) ) SetRendererEnabled( obj, enabled );
	}

	static void SetRendererEnabled( GameObject obj, bool enabled )
	{
		if ( obj.GetComponent<ModelRenderer>( includeDisabled: true ) is { } renderer ) renderer.Enabled = enabled;
	}

	internal bool Release( GameObject obj, bool physics )
	{
		if ( !objects.TryGetValue( obj, out var p ) ) return false;
		if ( physics ) p.Physics = false;
		else p.Render = false;
		if ( obj.IsValid() )
		{
			if ( pendingPromotion.Contains( p ) || moved.Contains( p ) ) return true;
			if ( physics )
			{
				ChairArchetypes.Eject( p.Seats );
				SetSeatTriggers( p, false );
				if ( p.Prop.IsValid() ) p.Prop.PhysicsEnabled = false;
				if ( p.Collider.IsValid() ) p.Collider.Enabled = false;
			}
			else Display( obj, false );
		}
		if ( !p.Physics && !p.Render )
		{
			entries.Remove( p.Id );
			objects.Remove( obj );
			if ( obj.IsValid() ) obj.Destroy();
		}
		return true;
	}

	// A carried prop can leave its original map. Use its current position for subsequent requests.
	long lastMovedUpdate;
	internal void UpdateMoved( FocusSet physical, Vector3? camera )
	{
		if ( pendingPromotion.Count == 0 && (moved.Count == 0 || System.Diagnostics.Stopwatch.GetElapsedTime( lastMovedUpdate ).TotalSeconds < 0.2) ) return;
		lastMovedUpdate = System.Diagnostics.Stopwatch.GetTimestamp();
		foreach ( var p in pendingPromotion )
		{
			if ( !p.Object.IsValid() ) continue;
			p.Object.Transform.OnTransformChanged -= p.OnMovement;
			p.Object.SetParent( dynamicRoot, true );
			moved.Add( p );
		}
		pendingPromotion.Clear();
		foreach ( var p in moved )
		{
			if ( !p.Object.IsValid() ) continue;
			var pos = p.Object.WorldPosition;
			p.Prop.PhysicsEnabled = physical.DistanceSquared( pos / PacketReader.UnitsPerMetre ) <= 80 * 80;
			SetSeatTriggers( p, p.Prop.PhysicsEnabled );
			var distance = p.LodDistance * Sandbox.Mounting.GtaWorldStreamingSystem.LodScale * PacketReader.UnitsPerMetre;
			SetRendererEnabled( p.Object, camera.HasValue && (distance <= 0 || pos.DistanceSquared( camera.Value ) <= distance * distance) );
		}
		moved.RemoveWhere( p => !p.Object.IsValid() );
	}

	internal void Dispose()
	{
		foreach ( var p in objects.Values )
		{
			if ( !p.Object.IsValid() ) continue;
			ChairArchetypes.Eject( p.Seats );
			p.Object.Destroy();
		}
		if ( dynamicRoot.IsValid() ) dynamicRoot.Destroy();
	}
}
