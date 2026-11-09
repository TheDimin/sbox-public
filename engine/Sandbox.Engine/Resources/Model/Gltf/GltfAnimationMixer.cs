using Sandbox.Gltf;

namespace Sandbox;

/// <summary>An instance-local shared clock and atomic selection of exact glTF clips.</summary>
internal sealed class GltfAnimationMixer
{
	private readonly GltfModelAnimation model;
	private readonly GltfPlaybackClock clock = new();
	private int[] clips = [];
	private IReadOnlyList<string> names = Array.Empty<string>();

	internal GltfAnimationMixer( GltfModelAnimation model ) => this.model = model ?? throw new ArgumentNullException( nameof( model ) );
	internal IReadOnlyList<string> Names => names;
	internal float Time { get => (float)clock.Time; set => clock.Seek( value ); }
	internal float Duration => (float)clock.Duration;
	internal bool IsFinished => clips.Length == 0 || clock.IsFinished;
	internal bool Looping { get => clock.Looping; set => clock.Looping = value; }
	internal bool IsFor( GltfModelAnimation candidate ) => ReferenceEquals( model, candidate );

	/// <summary>
	/// Generated SequenceNames are useful within one generation, but cannot
	/// identify duplicate or unnamed clips after reordering on reload.
	/// </summary>
	internal bool CanRebindTo( GltfModelAnimation candidate )
	{
		ArgumentNullException.ThrowIfNull( candidate );
		foreach ( var name in names )
			if ( !HasUniqueAuthoredName( model, name ) || !HasUniqueAuthoredName( candidate, name ) ) return false;
		return true;
	}

	private static bool HasUniqueAuthoredName( GltfModelAnimation animation, string alias )
	{
		var foundAlias = false;
		var matches = 0;
		if ( !animation.Document.Root.TryGetProperty( "animations", out var values ) ) return false;
		foreach ( var clip in animation.Clips )
		{
			var value = values[clip.Index];
			var authored = value.TryGetProperty( "name", out var property ) && property.ValueKind == System.Text.Json.JsonValueKind.String
				? property.GetString() : null;
			if ( authored == alias ) matches++;
			if ( clip.Name == alias && !string.IsNullOrWhiteSpace( authored ) && authored == alias ) foundAlias = true;
		}
		return foundAlias && matches == 1;
	}

	internal void Select( IReadOnlyList<string> selected )
	{
		ArgumentNullException.ThrowIfNull( selected );
		var next = new int[selected.Count];
		var nextNames = new string[selected.Count];
		var duration = 0f;
		for ( var i = 0; i < selected.Count; i++ )
		{
			var name = selected[i];
			GltfModelAnimation.Clip? found = null;
			foreach ( var candidate in model.Clips ) if ( candidate.Name == name ) { found = candidate; break; }
			if ( found is null ) throw new ArgumentException( $"Unknown glTF animation '{name}'.", nameof( selected ) );
			next[i] = found.Value.Index;
			nextNames[i] = found.Value.Name;
			duration = MathF.Max( duration, found.Value.Duration );
		}
		// Sampling validates overlaps and every channel before replacing state.
		_ = model.EvaluatePoses( next, 0 );
		clips = next;
		names = Array.AsReadOnly( nextNames );
		clock.Reset( duration );
	}

	internal bool Advance( double delta, double rate ) => clock.Advance( delta, rate );
	internal GltfPose EvaluatePose() => model.EvaluatePoses( clips, Time );
}
