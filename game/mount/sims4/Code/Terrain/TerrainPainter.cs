using Sandbox;
using System;

namespace Sims4.Terrain;

/// <summary>
/// Runtime terrain material painting component.
/// Thin input handler that uses TerrainPaintExtensions for the actual painting.
/// </summary>
public class TerrainPainter : Component
{
	[Property] public Sandbox.Terrain Terrain { get; set; }

	[Property, Range( 8f, 512f, 1f )]
	public float BrushSize { get; set; } = 64f;

	[Property, Range( 0f, 1f, 0.01f )]
	public float BrushOpacity { get; set; } = 0.5f;

	[Property]
	public int SelectedMaterialIndex { get; set; } = 0;

	[Property]
	public TerrainPaintLayer ActiveLayer { get; set; } = TerrainPaintLayer.Base;

	[Property]
	public bool IsErasing { get; set; }

	[Property]
	public bool PaintingEnabled { get; set; }

	public Texture BrushTexture { get; set; }
	public Vector3? CurrentHitPosition { get; private set; }

	bool _dragging;
	RectInt _dirtyRegion;

	protected override void OnUpdate()
	{
		if ( !PaintingEnabled )
		{
			CurrentHitPosition = null;
			return;
		}

		if ( !Terrain.IsValid() || Terrain.Storage is null )
		{
			CurrentHitPosition = null;
			return;
		}

		var camera = Scene.Camera;
		if ( camera is null )
			return;

		var ray = camera.ScreenPixelToRay( Mouse.Position );
		if ( !Terrain.RayIntersects( ray, 10000f, out var hitPosition ) )
		{
			CurrentHitPosition = null;
			if ( _dragging )
			{
				_dragging = false;
				Terrain.SyncPaintRegion( _dirtyRegion );
			}
			return;
		}

		CurrentHitPosition = hitPosition;

		if ( Input.Down( "attack1" ) )
		{
			var uv = Terrain.GetUV( hitPosition );

			if ( !_dragging )
			{
				_dragging = true;
				var x = (int)Math.Floor( Terrain.Storage.Resolution * uv.x );
				var y = (int)Math.Floor( Terrain.Storage.Resolution * uv.y );
				_dirtyRegion = new RectInt( new Vector2Int( x, y ) );
			}

			float strength = BrushOpacity * (IsErasing ? -1.0f : 1.0f);
			var region = Terrain.PaintMaterial( uv, SelectedMaterialIndex, BrushSize, strength, BrushTexture, ActiveLayer );
			_dirtyRegion.Add( region );
		}
		else if ( _dragging )
		{
			_dragging = false;
			Terrain.SyncPaintRegion( _dirtyRegion );
		}
	}
}
