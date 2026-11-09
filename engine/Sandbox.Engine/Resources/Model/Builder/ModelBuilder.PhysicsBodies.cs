using NativeEngine;

namespace Sandbox;

public sealed partial class ModelBuilder
{
	private readonly List<PhysicsBodyBuilder> _bodies = [];

	/// <summary>
	/// Adds a physics body to the model.
	/// Joints refer to bodies by the order they are added.
	/// </summary>
	/// <param name="mass">The mass in kilograms. A value of 0 calculates mass from the body's shapes and surface density.</param>
	/// <param name="surface">The surface properties applied to the body.</param>
	/// <param name="boneName">
	/// Optional name of the bone this body is attached to.
	/// Leave empty for non-skeletal bodies.
	/// </param>
	/// <returns>A builder for the new body.</returns>
	public PhysicsBodyBuilder AddBody( float mass = default, Surface surface = default, string boneName = default )
	{
		var builder = new PhysicsBodyBuilder
		{
			Mass = mass,
			Surface = surface,
			BoneName = boneName,
			BindPose = Transform.Zero
		};

		_bodies.Add( builder );
		return builder;
	}

	private void ApplyBodyMetadataOverrides( Model model )
	{
		if ( !_bodies.Any( b => b.GravityScaleOverride.HasValue || b.MassCenterOverrideEnabled.HasValue || b.ShapeSurfaceSlots.Count != 0 ) ) return;
		var physics = model.Physics;
		if ( physics == null || physics.Parts.Count != _bodies.Count ) throw new InvalidOperationException( "Constructed physics body count differs." );
		for ( var i = 0; i < _bodies.Count; ++i )
		{
			var source = _bodies[i]; var body = physics.Parts[i].native;
			if ( source.GravityScaleOverride is float gravity ) body.m_flGravityScale = gravity;
			if ( source.MassCenterOverrideEnabled is bool enabled ) { body.m_bOverrideMassCenter = enabled; body.m_vMassCenterOverride = source.MassCenterOverrideValue; }
			foreach ( var (key, slot) in source.ShapeSurfaceSlots )
			{
				if ( slot < 0 || slot >= _surfaces.Count ) throw new InvalidOperationException( "Physics surface slot is outside the model surface list." );
				switch ( key.Kind )
				{
					case PhysicsBodyBuilder.ShapeKind.Box: { var shape = body.GetHull( key.Index ); if ( shape.IsNull ) throw new InvalidOperationException( "Constructed box hull is missing." ); shape.m_nSurfacePropertyIndex = (uint)slot; break; }
					case PhysicsBodyBuilder.ShapeKind.Hull: { var shape = body.GetHull( source.Boxes.Count + key.Index ); if ( shape.IsNull ) throw new InvalidOperationException( "Constructed hull is missing." ); shape.m_nSurfacePropertyIndex = (uint)slot; break; }
					case PhysicsBodyBuilder.ShapeKind.Sphere: { var shape = body.GetSphere( key.Index ); if ( shape.IsNull ) throw new InvalidOperationException( "Constructed sphere is missing." ); shape.m_nSurfacePropertyIndex = (uint)slot; break; }
					case PhysicsBodyBuilder.ShapeKind.Capsule: { var shape = body.GetCapsule( key.Index ); if ( shape.IsNull ) throw new InvalidOperationException( "Constructed capsule is missing." ); shape.m_nSurfacePropertyIndex = (uint)slot; break; }
					case PhysicsBodyBuilder.ShapeKind.Mesh: { var shape = body.GetMesh( key.Index ); if ( shape.IsNull ) throw new InvalidOperationException( "Constructed mesh is missing." ); shape.m_nSurfacePropertyIndex = (uint)slot; break; }
					default: throw new InvalidOperationException( "Unsupported physics shape." );
				}
			}
		}
		physics.RefreshBuilderOverrides();
	}

	private CPhysBodyDescArray CreatePhysicsBodies()
	{
		if ( _bodies.Count == 0 )
		{
			foreach ( var joint in _joints )
				joint.Validate( 0 );
			return CreateLegacyPhysicsBodies();
		}

		return CPhysBodyDescArray.Create( _bodies, _joints, _surfaces );
	}
}
