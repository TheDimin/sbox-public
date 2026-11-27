using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Editor;

internal static class GtaDecalGizmos
{
	sealed record Overlay( string Label, Material LegacyMaterial, Vector3 Anchor, string ScopeName );
	static readonly ConditionalWeakTable<Model.ModelMeshInfo, Overlay[]> Cache = new();
	static Texture Icon => field ??= Texture.Load( "materials/gizmo/decal.png" );

	public static void Draw( Scene scene, SelectionSystem selection )
	{
		var settings = Gizmo.Settings;
		if ( !scene.IsValid() || !settings.GizmosEnabled || !settings.IsGizmoEnabled( typeof( Decal ) ) ) return;

		var camera = Gizmo.Camera;
		var frustum = camera.GetFrustum( new Rect( Vector2.Zero, camera.Size ) );
		float range = settings.GizmoRenderDistance;
		foreach ( var renderer in scene.GetAllComponents<ModelRenderer>() )
		{
			if ( !renderer.Active || renderer.Model is not { } model ||
				model.Name?.StartsWith( "mount://gtav/models/", StringComparison.OrdinalIgnoreCase ) != true ) continue;
			var bounds = renderer.Bounds;
			if ( !frustum.IsInside( bounds, true ) || (range > 0 && bounds.ClosestPoint( camera.Position ).DistanceSquared( camera.Position ) > range * range) ) continue;
			var info = model.MeshInfo;
			var overlays = Cache.GetValue( info, Read );
			if ( overlays.Length == 0 ) continue;
			using var owner = Gizmo.ObjectScope( renderer.GameObject, renderer.WorldTransform );
			foreach ( var overlay in overlays )
			{
				var world = renderer.WorldTransform.PointToWorld( overlay.Anchor );
				if ( !frustum.IsInside( world ) || (range > 0 && camera.Position.DistanceSquared( world ) > range * range) ) continue;
				using var handle = Gizmo.Scope( overlay.ScopeName, overlay.Anchor );
				Gizmo.Transform = Gizmo.Transform.WithScale( 1 );
				var size = (settings.WorldSpaceGizmos ? 8 : 32) * settings.GizmoScale;
				Gizmo.Hitbox.DepthBias = 0.1f;
				Gizmo.Hitbox.Sprite( Vector3.Zero, size, settings.WorldSpaceGizmos );
				Gizmo.Draw.IgnoreDepth = !settings.GizmoDepthTest;
				Gizmo.Draw.Color = Gizmo.IsHovered ? Color.Yellow : Color.White;
				Gizmo.Draw.Sprite( Vector3.Zero, size, Icon, settings.WorldSpaceGizmos );
				if ( Gizmo.IsHovered ) Gizmo.Draw.Text( Describe( overlay ), new Transform( Vector3.Zero ), flags: TextFlag.LeftBottom );
				if ( Gizmo.WasClicked && settings.Selection ) selection.Set( renderer.GameObject );
			}
		}
	}
	static string Describe( Overlay overlay )
	{
		if ( overlay.LegacyMaterial is not { } material ) return overlay.Label;
		// Already resident models can predate descriptive mesh names. Read the live binding on hover.
		var texture = material.GetTexture( "g_tColor" )?.ResourcePath;
		return string.IsNullOrEmpty( texture ) ? overlay.Label : $"{overlay.Label} | {System.IO.Path.GetFileNameWithoutExtension( texture )}";
	}

	static string PresetName( Material material ) => material.Name.Split( '/' )[^1] switch
	{
		"eb54415f" => "decal_glue",
		"827d5961" => "normal_spec_decal",
		"bb293696" => "decal",
		"444d255d" => "normal_decal",
		"9e4b2782" => "decal_dirt",
		"23ce7874" => "decal_amb_only",
		var preset => preset
	};

	static Overlay[] Read( Model.ModelMeshInfo info )
	{
		var result = new List<Overlay>();
		foreach ( var mesh in info.Meshes )
		{
			if ( (mesh.LodMask & 1) == 0 ) continue;
			bool named = mesh.Name?.StartsWith( "GTA decal ", StringComparison.Ordinal ) == true;
			foreach ( var draw in mesh.DrawCalls )
			{
				if ( !named && draw.Material?.Name?.StartsWith( "gtav/decal/", StringComparison.OrdinalIgnoreCase ) != true ) continue;
				var anchor = (mesh.BoundsMin + mesh.BoundsMax) * 0.5f;
				result.Add( new Overlay( named ? mesh.Name : PresetName( draw.Material ), named ? null : draw.Material, anchor, $"gtav-decal-{result.Count}" ) );
			}
		}
		return result.ToArray();
	}
}