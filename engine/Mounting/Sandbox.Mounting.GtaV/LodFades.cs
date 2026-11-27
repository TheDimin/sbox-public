using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Sandbox;

namespace GtaV;

internal sealed class LodFades
{
	const double DurationSeconds = 0.25;
	readonly PlacementHierarchy placements;
	readonly Dictionary<GameObject, Fade> active = new();
	readonly List<GameObject> finished = new();
	internal LodVisibility<(LodEntityId Id, GameObject Object)> Visibility { get; }
	internal System.Action<LodEntityId> OnSelected { get; set; }
	internal bool Pending => active.Count != 0;

	sealed class Fade( GameObject obj, bool incoming )
	{
		readonly (ModelRenderer Renderer, float Alpha)[] renderers = obj.GetComponentsInChildren<ModelRenderer>( true ).Select( r => (r, r.Tint.a) ).ToArray();
		internal readonly bool Incoming = incoming;
		internal readonly long Start = Stopwatch.GetTimestamp();
		internal bool Retired;

		internal void Apply( float weight )
		{
			foreach ( var (renderer, alpha) in renderers )
				if ( renderer.IsValid() ) renderer.Tint = renderer.Tint.WithAlpha( alpha * weight );
		}
	}

	internal LodFades( PlacementHierarchy placements )
	{
		this.placements = placements;
		Visibility = new( SetVisible, ShowReplacement );
	}

	void ShowReplacement( (LodEntityId Id, GameObject Object) placement )
	{
		var obj = placement.Object;
		if ( !obj.IsValid() ) return;
		SetVisible( placement, true );
		var fade = new Fade( obj, true );
		active.Add( obj, fade );
		fade.Apply( 0 );
	}

	void SetVisible( (LodEntityId Id, GameObject Object) placement, bool visible )
	{
		var (id, obj) = placement;
		if ( !obj.IsValid() ) return;
		if ( visible ) OnSelected?.Invoke( id );
		if ( placements.IsVisible( id, obj ) == visible )
		{
			if ( !visible && !active.ContainsKey( obj ) ) placements.Display( obj, false );
			return;
		}

		if ( visible )
		{
			if ( active.Remove( obj, out var previous ) ) previous.Apply( 1 );
			placements.SetVisible( id, obj, true );
			return;
		}

		// Only the renderer participates in the fade.
		placements.SetVisible( id, obj, false, fadingOut: true );
		if ( active.Remove( obj, out var interrupted ) ) interrupted.Apply( 1 );
		var fade = new Fade( obj, false );
		active.Add( obj, fade );
	}

	internal bool RetireAfterFade( GameObject obj )
	{
		if ( !active.TryGetValue( obj, out var fade ) ) return false;
		fade.Retired = true;
		return true;
	}

	internal void Update()
	{
		var now = Stopwatch.GetTimestamp();
		foreach ( var (obj, fade) in active )
		{
			var elapsed = Stopwatch.GetElapsedTime( fade.Start, now ).TotalSeconds;
			if ( obj.IsValid() && elapsed < DurationSeconds )
			{
				// Borealis uses the same dither mask for both objects. Keep one fully
				// drawn while the other fades so their discarded pixels cannot coincide.
				float progress = (float)(2 * elapsed / DurationSeconds);
				fade.Apply( fade.Incoming ? System.Math.Min( 1, progress ) : System.Math.Min( 1, 2 - progress ) );
				continue;
			}
			if ( obj.IsValid() )
			{
				if ( fade.Retired ) Retire( obj );
				else
				{
					if ( !fade.Incoming ) placements.Display( obj, false );
					fade.Apply( 1 );
				}
			}
			finished.Add( obj );
		}
		foreach ( var obj in finished ) active.Remove( obj );
		finished.Clear();
	}

	internal void Reacquire( GameObject obj )
	{
		if ( active.TryGetValue( obj, out var fade ) ) fade.Retired = false;
	}

	void Retire( GameObject obj )
	{
		if ( !placements.Release( obj, physics: false ) ) obj.Destroy();
	}

	internal void Dispose()
	{
		foreach ( var (obj, fade) in active )
			if ( fade.Retired && obj.IsValid() ) Retire( obj );
		active.Clear();
	}
}
