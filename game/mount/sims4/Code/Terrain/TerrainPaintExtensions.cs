using Sandbox;
using System;

namespace Sims4.Terrain;

public enum TerrainPaintLayer
{
	Base = 0,
	Overlay = 1
}

/// <summary>
/// Extension methods for runtime terrain material painting.
/// Wraps the terrain/cs_terrain_splat compute shader so any game code can paint materials.
/// </summary>
public static class TerrainPaintExtensions
{
	/// <summary>
	/// Paint a material onto the terrain at a UV position using the splat compute shader.
	/// Returns the dirty region in texel space that was affected.
	/// </summary>
	public static RectInt PaintMaterial( this Sandbox.Terrain terrain, Vector2 uv, int materialIndex, float brushSize, float opacity, Texture brush = null, TerrainPaintLayer layer = TerrainPaintLayer.Base )
	{
		int size = (int)Math.Floor( brushSize * 2.0f / terrain.Storage.TerrainSize * terrain.Storage.Resolution );
		size = Math.Max( 1, size );

		var cs = new ComputeShader( "terrain/cs_terrain_splat" );
		cs.Attributes.Set( "ControlMap", terrain.ControlMap );
		cs.Attributes.Set( "ControlUV", uv );
		cs.Attributes.Set( "BrushStrength", opacity );
		cs.Attributes.Set( "BrushSize", size );
		cs.Attributes.Set( "Brush", brush ?? Texture.White );
		cs.Attributes.Set( "SplatChannel", materialIndex );
		cs.Attributes.Set( "PaintLayer", (int)layer );

		cs.Dispatch( size, size, 1 );

		var x = (int)Math.Floor( terrain.Storage.Resolution * uv.x ) - size / 2;
		var y = (int)Math.Floor( terrain.Storage.Resolution * uv.y ) - size / 2;
		return new RectInt( x, y, size + 1, size + 1 );
	}

	/// <summary>
	/// Paint a material at a world position. Returns the dirty region, or null if the ray missed.
	/// </summary>
	public static RectInt? PaintMaterialAtWorld( this Sandbox.Terrain terrain, Vector3 worldPosition, int materialIndex, float brushSize, float opacity, Texture brush = null, TerrainPaintLayer layer = TerrainPaintLayer.Base )
	{
		var localPos = terrain.WorldTransform.PointToLocal( worldPosition );
		var uv = new Vector2( localPos.x, localPos.y ) / terrain.Storage.TerrainSize;

		if ( uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1 )
			return null;

		return terrain.PaintMaterial( uv, materialIndex, brushSize, opacity, brush, layer );
	}

	/// <summary>
	/// Paint a material using a screen-space ray (e.g. from mouse position).
	/// Returns the dirty region, or null if the ray missed.
	/// </summary>
	public static RectInt? PaintMaterialFromRay( this Sandbox.Terrain terrain, Ray ray, int materialIndex, float brushSize, float opacity, Texture brush = null, TerrainPaintLayer layer = TerrainPaintLayer.Base )
	{
		if ( !terrain.RayIntersects( ray, 10000f, out var hitPosition ) )
			return null;

		var uv = new Vector2( hitPosition.x, hitPosition.y ) / terrain.Storage.TerrainSize;
		return terrain.PaintMaterial( uv, materialIndex, brushSize, opacity, brush, layer );
	}

	/// <summary>
	/// Sync the control map after painting. Call this when a paint stroke ends.
	/// Clamps the dirty region to terrain bounds before syncing.
	/// </summary>
	public static void SyncPaintRegion( this Sandbox.Terrain terrain, RectInt dirtyRegion )
	{
		int res = terrain.Storage.Resolution;
		dirtyRegion.Left = Math.Clamp( dirtyRegion.Left, 0, res - 1 );
		dirtyRegion.Right = Math.Clamp( dirtyRegion.Right, 0, res - 1 );
		dirtyRegion.Top = Math.Clamp( dirtyRegion.Top, 0, res - 1 );
		dirtyRegion.Bottom = Math.Clamp( dirtyRegion.Bottom, 0, res - 1 );

		terrain.SyncCPUTexture( Sandbox.Terrain.SyncFlags.Control, dirtyRegion );
	}

	/// <summary>
	/// Get the UV coordinates for a terrain-local hit position.
	/// </summary>
	public static Vector2 GetUV( this Sandbox.Terrain terrain, Vector3 localHitPosition )
	{
		return new Vector2( localHitPosition.x, localHitPosition.y ) / terrain.Storage.TerrainSize;
	}

	/// <summary>
	/// Get available paintable materials from the terrain's storage.
	/// </summary>
	public static List<PaintableMaterial> GetPaintableMaterials( this Sandbox.Terrain terrain )
	{
		var list = new List<PaintableMaterial>();

		if ( terrain?.Storage?.Materials is null )
			return list;

		for ( int i = 0; i < terrain.Storage.Materials.Count; i++ )
		{
			var mat = terrain.Storage.Materials[i];
			if ( mat is null ) continue;

			list.Add( new PaintableMaterial
			{
				Name = mat.ResourceName ?? $"Material {i}",
				Group = "Engine",
				Thumbnail = mat.BCRTexture,
				MaterialIndex = i
			} );
		}

		return list;
	}

	/// <summary>
	/// Add a new terrain material at runtime and return its index.
	/// Returns -1 if the terrain is full (max 32 materials).
	/// </summary>
	public static int AddRuntimeMaterial( this Sandbox.Terrain terrain, TerrainMaterial material )
	{
		if ( terrain?.Storage?.Materials is null )
			return -1;

		if ( terrain.Storage.Materials.Count >= 32 )
			return -1;

		terrain.Storage.Materials.Add( material );
		return terrain.Storage.Materials.Count - 1;
	}
}
