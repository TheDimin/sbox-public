using Sandbox.Gltf;

namespace Sandbox;

public sealed partial class SkinnedModelRenderer
{
	// Matches the existing native wrapper's default duration. Managed glTF uses
	// linear transitions in scene time; native interpolation-curve parity still
	// requires a measured control. Explicit zero duration is immediate and exact.
	private const float GltfDefaultMorphFade = .05f;
	private sealed class GltfMorphFade
	{
		internal float Start, Target, Duration;
		internal double Elapsed;
		internal bool Clearing;
	}
	private Dictionary<int, GltfMorphFade> gltfMorphOverrides = new();

	private static void ValidateGltfMorphChange( float weight, float fadeTime )
	{
		if ( !float.IsFinite( weight ) ) throw new ArgumentOutOfRangeException( nameof( weight ), "Morph weights must be finite." );
		if ( !float.IsFinite( fadeTime ) || fadeTime < 0 ) throw new ArgumentOutOfRangeException( nameof( fadeTime ), "Morph fade duration must be finite and nonnegative." );
	}

	private int FindGltfMorph( string name )
	{
		if ( Model?.GltfResource is not { } resource ) return -1;
		for ( var i = 0; i < resource.Metadata.Morphs.Count; i++ )
			if ( string.Equals( resource.Metadata.Morphs[i].Name, name, StringComparison.OrdinalIgnoreCase ) ) return i;
		return -1;
	}

	private float GetGltfMorph( string name )
	{
		var index = FindGltfMorph( name );
		if ( index < 0 || gltfPose is null ) return 0;
		var target = gltfAnimationResource.Metadata.Morphs[index];
		return gltfPose.GetMorphWeights( target.Node )[target.Target];
	}

	private void SetGltfMorph( string name, float weight, float fadeTime )
	{
		ThreadSafe.AssertIsMainThread();
		ValidateGltfMorphChange( weight, fadeTime );
		var index = FindGltfMorph( name );
		if ( index < 0 || gltfPose is null ) return;
		gltfMorphOverrides.TryGetValue( index, out var previous );
		gltfMorphOverrides[index] = new() { Start = GetGltfMorph( name ), Target = weight, Duration = fadeTime };
		try { ApplyGltfPose(); }
		catch
		{
			if ( previous is null ) gltfMorphOverrides.Remove( index );
			else gltfMorphOverrides[index] = previous;
			throw;
		}
	}

	private void ClearGltfMorph( string name, float fadeTime )
	{
		ThreadSafe.AssertIsMainThread();
		ValidateGltfMorphChange( 0, fadeTime );
		var index = FindGltfMorph( name );
		if ( index < 0 || gltfPose is null || !gltfMorphOverrides.ContainsKey( index ) ) return;
		var previous = gltfMorphOverrides[index];
		if ( fadeTime == 0 ) gltfMorphOverrides.Remove( index );
		else gltfMorphOverrides[index] = new() { Start = GetGltfMorph( name ), Duration = fadeTime, Clearing = true };
		try { ApplyGltfPose(); }
		catch { gltfMorphOverrides[index] = previous; throw; }
	}

	private bool AdvanceGltfMorphs( double delta )
	{
		if ( delta <= 0 ) return false;
		var changed = false;
		foreach ( var fade in gltfMorphOverrides.Values )
		{
			if ( fade.Elapsed >= fade.Duration ) continue;
			fade.Elapsed = Math.Min( fade.Duration, fade.Elapsed + delta );
			changed = true;
		}
		return changed;
	}

	private GltfPose ApplyGltfMorphs( GltfPose authored )
	{
		if ( gltfMorphOverrides.Count == 0 ) return authored;
		var weights = new Dictionary<int, float[]>();
		foreach ( var (index, fade) in gltfMorphOverrides.ToArray() )
		{
			var amount = fade.Duration == 0 ? 1 : (float)Math.Clamp( fade.Elapsed / fade.Duration, 0, 1 );
			if ( fade.Clearing && amount == 1 ) { gltfMorphOverrides.Remove( index ); continue; }
			var target = gltfAnimationResource.Metadata.Morphs[index];
			if ( !weights.TryGetValue( target.Node, out var values ) ) weights.Add( target.Node, values = authored.GetMorphWeights( target.Node ) );
			var destination = fade.Clearing ? values[target.Target] : fade.Target;
			// Evaluate in double so finite signed endpoints do not overflow during
			// interpolation. No normalization or [0,1] clamp applies to morph weights.
			values[target.Target] = (float)((double)fade.Start * (1 - amount) + (double)destination * amount);
		}
		return authored.WithMorphWeights( weights );
	}
}
