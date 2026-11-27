using System;
using Sandbox;

namespace GtaV;

/// <summary>Sample the loaded beach bed near the view. A water tile boundary cannot produce surf.</summary>
internal sealed class ShoreFoam : IDisposable
{
	const int Side = 9;
	const float Spacing = 6 * PacketReader.UnitsPerMetre;
	readonly GameObject[] patches = new GameObject[Side * Side];
	readonly Vector3[] anchors = new Vector3[Side * Side];
	int cursor;
	float nextTick;
	Vector3 origin;

	internal void Update( GameObject root, Vector3? camera )
	{
		if ( camera is not { } eye || Math.Abs( eye.z ) > 80 * PacketReader.UnitsPerMetre )
		{
			if ( cursor != 0 || nextTick != 0 ) Dispose();
			return;
		}
		var center = new Vector3( MathF.Round( eye.x / Spacing ), MathF.Round( eye.y / Spacing ), 0 ) * Spacing;
		if ( center.DistanceSquared( origin ) > Spacing * Spacing * Side * Side ) Dispose();
		origin = center;
		if ( Time.Now < nextTick ) return;
		nextTick = Time.Now + 0.08f;
		for ( int n = 0; n < 4; n++ )
		{
			int i = cursor++ % patches.Length;
			cursor %= patches.Length;
			var p = center + new Vector3( i % Side - Side / 2, i / Side - Side / 2, 0 ) * Spacing;
			var hit = root.Scene.Trace.Ray( p + Vector3.Up * Spacing, p + Vector3.Down * Spacing ).WithoutTags( "water", "trigger" ).Run();
			// Intersect the local beach slope with a contour 20 cm below mean water.
			// This also finds a narrow surf line between two grid samples.
			var gradient = -hit.Normal.WithZ( 0 ) / Math.Max( 0.01f, hit.Normal.z );
			var offset = gradient.LengthSquared > 0.0001f
				? gradient * ((-8 - hit.EndPosition.z) / gradient.LengthSquared) : Vector3.Zero;
			p += offset;
			if ( !hit.Hit || hit.Normal.z < 0.7f || offset.Length > Spacing * 0.72f ||
				(gradient.LengthSquared <= 0.0001f && (hit.EndPosition.z < -36 || hit.EndPosition.z > 4)) ||
				WaterBody.FindSurface( root.Scene, p ) is null )
			{
				if ( patches[i].IsValid() ) patches[i].Destroy();
				patches[i] = null;
				continue;
			}
			if ( !patches[i].IsValid() )
			{
				var go = new GameObject( root, false, "Beach foam" );
				go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
				go.NetworkMode = NetworkMode.Never;
				var overlay = go.Components.Create<WaterOverlay>();
				overlay.Texture = Texture.Load( "textures/water/waterfoam1.vtex" );
				overlay.Size = new Vector2( Spacing * 1.6f, Spacing * 0.55f );
				overlay.NormalDistortion = 8;
				overlay.Tint = Color.White.WithAlpha( 0.68f );
				go.Enabled = true;
				patches[i] = go;
			}
			var along = Vector3.Cross( Vector3.Up, hit.Normal );
			patches[i].WorldRotation = along.LengthSquared > 0.001f ? Rotation.LookAt( along ) : Rotation.Identity;
			anchors[i] = p;
		}
		for ( int i = 0; i < patches.Length; i++ )
			if ( patches[i].IsValid() )
				patches[i].WorldPosition = anchors[i] + patches[i].WorldRotation.Left * (18 * MathF.Sin( Time.Now * 1.4f + i * 0.15f ));
	}

	public void Dispose()
	{
		foreach ( var patch in patches ) if ( patch.IsValid() ) patch.Destroy();
		Array.Clear( patches );
		cursor = 0;
		nextTick = 0;
	}
}
