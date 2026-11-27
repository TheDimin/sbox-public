namespace Sandbox;

public partial class Prop
{
	PropParticleEffects generatedParticleEffects;
	PropSoundEffects generatedSoundEffects;

	void CreatePresentationComponents()
	{
		var useSurface = !string.IsNullOrEmpty( Model.Data.DestructionSurface );
		var settings = DestructionProfile;
		if ( !useSurface && settings is null ) return;

		var particles = Components.Get<PropParticleEffects>( true );
		if ( !particles.IsValid() )
		{
			particles = generatedParticleEffects = Components.Create<PropParticleEffects>( false );
			particles.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotCloned | ComponentFlags.NotNetworked;
			particles.Enabled = true;
		}

		if ( particles == generatedParticleEffects )
		{
			particles.UseSurfaceEffects = useSurface;
			particles.ShakeOnExplosion = settings?.ShakeOnExplosion ?? useSurface;
			AddProcedural( particles );
		}

		var sound = Components.Get<PropSoundEffects>( true );
		if ( !sound.IsValid() )
		{
			sound = generatedSoundEffects = Components.Create<PropSoundEffects>( false );
			sound.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotCloned | ComponentFlags.NotNetworked;
			sound.Enabled = true;
		}

		if ( sound == generatedSoundEffects )
		{
			sound.UseSurfaceSounds = useSurface;
			AddProcedural( sound );
		}
	}

	internal PropDestructionProfile DestructionProfile
	{
		get
		{
			var path = Model?.Data.DestructionProfile;
			return !string.IsNullOrEmpty( path ) && ResourceLibrary.TryGet<PropDestructionProfile>( path, out var settings ) ? settings : null;
		}
	}

	internal Surface GetEffectSurface( Surface hitSurface = null )
	{
		if ( hitSurface.IsValid() && hitSurface.Index != 0 )
			return hitSurface;

		var path = Model?.Data.DestructionSurface;
		if ( !string.IsNullOrEmpty( path ) && ResourceLibrary.Get<Surface>( path ) is { } surface )
			return surface;

		if ( ProceduralComponents is not null )
		{
			foreach ( var component in ProceduralComponents )
			{
				if ( component is not Collider collider ) continue;
				foreach ( var shape in collider.Shapes )
				{
					if ( shape.Surface.IsValid() ) return shape.Surface;
				}
			}
		}

		return hitSurface ?? ResourceLibrary.Get<Surface>( "surfaces/default.surface" );
	}

	internal void EmitPresentation( string prefab, string sound, Vector3 position, Rotation rotation, string surface = null )
	{
		if ( IsProxy || Scene.IsEditor || !position.IsFinite || !rotation.IsFinite ) return;

		if ( Network.Active )
			BroadcastPresentation( prefab, sound, position, rotation, surface );
		else
			PlayPresentation( prefab, sound, position, rotation );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void BroadcastPresentation( string prefab, string sound, Vector3 position, Rotation rotation, string surface )
	{
		if ( Application.IsDedicatedServer || Scene.IsEditor || !position.IsFinite || !rotation.IsFinite ) return;

		// Resolve only prefabs from the receiving prop or its known surface data.
		if ( !string.IsNullOrEmpty( prefab ) )
		{
			var effects = Components.Get<PropParticleEffects>();
			var surfaceResource = string.IsNullOrEmpty( surface ) ? null : Sandbox.ResourceLibrary.Get<Surface>( surface );
			var fallback = PropParticleEffects.GetPrefabPath( PropParticleEffects.SurfaceEffect( surfaceResource ) );
			var settings = DestructionProfile;
			var cues = Model?.Data.DestructionCues;
			if ( effects?.HasPrefab( prefab ) != true && prefab != fallback
				&& prefab != settings?.DamageEffect?.ResourcePath
				&& prefab != settings?.BreakEffect?.ResourcePath
				&& prefab != settings?.GibEffect?.ResourcePath
				&& prefab != cues?.DamageEffect && prefab != cues?.BreakEffect && prefab != cues?.GibEffect ) return;
		}

		PlayPresentation( prefab, sound, position, rotation );
	}

	void PlayPresentation( string prefab, string sound, Vector3 position, Rotation rotation )
	{
		if ( Application.IsDedicatedServer ) return;

		if ( !string.IsNullOrEmpty( sound ) )
			Sound.Play( sound, position );

		if ( string.IsNullOrEmpty( prefab ) || GameObject.GetPrefab( prefab ) is not { } source ) return;

		using var sceneScope = Scene.Push();
		var effect = source.Clone( new CloneConfig( new Transform( position, rotation ), startEnabled: false ) );
		if ( !effect.IsValid() ) return;

		effect.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		if ( !effect.Components.Get<TemporaryEffect>( true ).IsValid() )
			effect.Components.Create<TemporaryEffect>().WaitForChildEffects = false;
		effect.Enabled = true;
	}

	void EmitExplosionShake( Vector3 position, float radius )
	{
		if ( Scene.IsEditor || Components.Get<PropParticleEffects>()?.ShakeOnExplosion != true ) return;

		if ( Network.Active )
			BroadcastExplosionShake( position, radius );
		else
			PlayExplosionShake( position, radius );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	void BroadcastExplosionShake( Vector3 position, float radius ) => PlayExplosionShake( position, radius );

	void PlayExplosionShake( Vector3 position, float radius )
	{
		if ( Application.IsDedicatedServer || Scene.IsEditor || !position.IsFinite || !float.IsFinite( radius ) ) return;

		var effects = CameraEffectSystem.Get( Scene );
		effects?.AddShake( position, Math.Clamp( radius * 2f, 1f, 4096f ), 2f, 22f, 0.35f );
	}
}
