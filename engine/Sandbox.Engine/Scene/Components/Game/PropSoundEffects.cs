namespace Sandbox;

[Expose, Title( "Prop Sound Effects" ), Category( "Effects" )]
public sealed class PropSoundEffects : Component
{
	[Property] public SoundEvent DamageSound { get; set; }
	[Property] public SoundEvent BreakSound { get; set; }
	[Property] public SoundEvent GibSound { get; set; }
	[Property] public bool UseSurfaceSounds { get; set; }

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

		var sound = DamageSound ?? prop.DestructionProfile?.DamageSound;
		if ( !sound.IsValid() && UseSurfaceSounds && !damage.Tags.Has( "bullet" ) )
		{
			var surface = prop.GetEffectSurface( damage.Shape?.Surface );
			sound = surface?.SoundCollection.ImpactSoft ?? surface?.GetBaseSurface()?.SoundCollection.ImpactSoft;
		}

		Play( sound, damage.Position );
	}

	void OnBreak()
	{
		var custom = ConfiguredBreakSound;
		if ( prop.IsExplosive && !custom.IsValid() ) return;

		var surface = UseSurfaceSounds ? prop.GetEffectSurface() : null;
		var baseSurface = surface?.GetBaseSurface();
		var sound = custom ?? surface?.SoundCollection.Break ?? baseSurface?.SoundCollection.Break
			?? surface?.SoundCollection.ImpactHard ?? baseSurface?.SoundCollection.ImpactHard;
		Play( sound, prop.WorldPosition );
	}

	void OnGibs( List<Gib> gibs )
	{
		var sound = GibSound ?? prop.DestructionProfile?.GibSound ?? SoundFromModel( prop.Model?.Data.DestructionCues?.GibSound );
		if ( !sound.IsValid() || prop.IsProxy ) return;

		// Play a single cue for this group of pieces.
		foreach ( var gib in gibs )
		{
			if ( !gib.IsValid() ) continue;
			Play( sound, gib.WorldPosition );
			break;
		}
	}

	SoundEvent ConfiguredBreakSound => BreakSound ?? prop.DestructionProfile?.BreakSound
		?? SoundFromModel( prop.Model?.Data.DestructionCues?.BreakSound );

	static SoundEvent SoundFromModel( string path ) =>
		string.IsNullOrEmpty( path ) ? null : ResourceLibrary.Get<SoundEvent>( path );

	internal bool ReplacesBreakSound => prop.IsValid() && Active && (ConfiguredBreakSound.IsValid() || UseSurfaceSounds && !prop.IsExplosive);

	void Play( SoundEvent sound, Vector3 position )
	{
		if ( sound.IsValid() )
			prop.EmitPresentation( null, sound.ResourcePath, position, Rotation.Identity );
	}
}
