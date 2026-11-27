namespace Sandbox;

[Expose, Title( "Prop Particle Effects" ), Category( "Effects" )]
public sealed class PropParticleEffects : Component
{
	[Property] public GameObject DamageEffect { get; set; }
	[Property] public GameObject BreakEffect { get; set; }
	[Property] public GameObject GibEffect { get; set; }
	[Property] public bool UseSurfaceEffects { get; set; }
	[Property] public bool ShakeOnExplosion { get; set; }

	Prop prop;

	protected override void OnEnabled()
	{
		prop = Components.Get<Prop>();
		if ( !prop.IsValid() ) return;

		prop.OnPropTakeDamage += OnDamage;
		prop.OnPropBreak += OnBreak;
		prop.OnGibsCreated += OnGibs;
	}

	protected override void OnDisabled()
	{
		if ( prop is null ) return;

		prop.OnPropTakeDamage -= OnDamage;
		prop.OnPropBreak -= OnBreak;
		prop.OnGibsCreated -= OnGibs;
		prop = null;
	}

	void OnDamage( DamageInfo damage )
	{
		if ( damage.Damage <= 0 || damage.Normal.LengthSquared <= 0 || !damage.Position.IsFinite )
			return;

		var path = GetPrefabPath( DamageEffect ) ?? prop.DestructionProfile?.DamageEffect?.ResourcePath
			?? prop.Model?.Data.DestructionCues?.DamageEffect;
		Surface surface = null;
		if ( path is null && UseSurfaceEffects && !damage.Tags.Has( "bullet" ) )
		{
			surface = prop.GetEffectSurface( damage.Shape?.Surface );
			path = GetPrefabPath( SurfaceEffect( surface ) );
		}

		Play( path, damage.Position, Rotation.LookAt( -damage.Normal ), surface );
	}

	void OnBreak()
	{
		var path = GetPrefabPath( BreakEffect ) ?? prop.DestructionProfile?.BreakEffect?.ResourcePath
			?? prop.Model?.Data.DestructionCues?.BreakEffect;
		Surface surface = null;
		if ( path is null && UseSurfaceEffects && !prop.IsExplosive )
		{
			surface = prop.GetEffectSurface();
			path = GetPrefabPath( SurfaceEffect( surface ) );
		}

		Play( path, prop.WorldPosition, Rotation.LookAt( Vector3.Down ), surface );
	}

	void OnGibs( List<Gib> gibs )
	{
		// The prop owner publishes effects for the pieces it creates.
		if ( prop.IsProxy ) return;
		var path = GetPrefabPath( GibEffect ) ?? prop.DestructionProfile?.GibEffect?.ResourcePath
			?? prop.Model?.Data.DestructionCues?.GibEffect;
		if ( path is null ) return;

		foreach ( var gib in gibs )
		{
			if ( gib.IsValid() )
				Play( path, gib.WorldPosition, gib.WorldRotation );
		}
	}

	void Play( string path, Vector3 position, Rotation rotation, Surface surface = null )
	{
		if ( !string.IsNullOrEmpty( path ) )
			prop.EmitPresentation( path, null, position, rotation, surface?.ResourcePath );
	}

	internal bool HasPrefab( string path ) =>
		path == GetPrefabPath( DamageEffect ) || path == GetPrefabPath( BreakEffect ) || path == GetPrefabPath( GibEffect );

	internal static string GetPrefabPath( GameObject effect ) =>
		effect.IsValid() ? effect.PrefabInstanceSource ?? (effect as PrefabScene)?.Source?.ResourcePath : null;

	internal static GameObject SurfaceEffect( Surface surface ) =>
		surface?.PrefabCollection.BulletImpact ?? surface?.GetBaseSurface()?.PrefabCollection.BulletImpact;
}
