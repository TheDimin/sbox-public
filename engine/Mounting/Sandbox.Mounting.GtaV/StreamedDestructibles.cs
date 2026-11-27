using System;
using System.Collections.Generic;
using Sandbox;

namespace GtaV;

// Visual and collision placements stream independently. Damage belongs to the placement
// for the lifetime of this world session, including time spent outside the physics radius.
internal sealed class StreamedDestructibles
{
	readonly Dictionary<LodEntityId, float> remainingHealth = new();
	internal bool IsBroken( LodEntityId id ) => remainingHealth.TryGetValue( id, out var health ) && health <= 0;

	internal void AttachProp( LodEntityId id, Prop prop )
	{
		if ( remainingHealth.TryGetValue( id, out var health ) )
		{
			if ( health <= 0 )
			{
				prop.GameObject.Destroy();
				return;
			}
			prop.Health = health;
		}

		prop.OnPropTakeDamage += hit => remainingHealth[id] = Math.Max( 0f, prop.Health - hit.Damage );
		prop.OnPropBreak += () => remainingHealth[id] = 0;
	}
}
