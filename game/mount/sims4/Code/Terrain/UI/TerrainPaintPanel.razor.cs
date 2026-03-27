using Sandbox;
using Sandbox.UI;
using System.Collections.Generic;
using System.Linq;

namespace Sims4.Terrain.UI;

public partial class TerrainPaintPanel : Panel
{
	public TerrainPainter Painter { get; set; }
	public List<PaintableMaterial> Materials { get; set; } = new();

	float BrushSize => Painter?.BrushSize ?? 64f;
	float BrushOpacity => Painter?.BrushOpacity ?? 0.5f;
	TerrainPaintLayer ActiveLayer => Painter?.ActiveLayer ?? TerrainPaintLayer.Base;
	bool IsErasing => Painter?.IsErasing ?? false;
	int SelectedIndex => Painter?.SelectedMaterialIndex ?? 0;

	protected override void OnAfterTreeRender( bool firstTime )
	{
		if ( !firstTime ) return;

		if ( Painter is null )
			Painter = Game.ActiveScene?.GetAllComponents<TerrainPainter>().FirstOrDefault();

		if ( Painter?.Terrain is not null && Materials.Count == 0 )
			Materials = Painter.Terrain.GetPaintableMaterials();

		if ( Painter is not null && Painter.BrushTexture is null )
			Painter.BrushTexture = RuntimeBrushLibrary.Default?.Texture;
	}

	void TogglePainting()
	{
		if ( Painter is null ) return;
		Painter.PaintingEnabled = !Painter.PaintingEnabled;
		StateHasChanged();
	}

	void OnBrushSizeChanged( float value )
	{
		if ( Painter is null ) return;
		Painter.BrushSize = value;
	}

	void OnBrushOpacityChanged( float value )
	{
		if ( Painter is null ) return;
		Painter.BrushOpacity = value;
	}

	void SetLayer( TerrainPaintLayer layer )
	{
		if ( Painter is null ) return;
		Painter.ActiveLayer = layer;
		StateHasChanged();
	}

	void ToggleErase()
	{
		if ( Painter is null ) return;
		Painter.IsErasing = !Painter.IsErasing;
		StateHasChanged();
	}

	void SelectMaterial( int index )
	{
		if ( Painter is null ) return;
		Painter.SelectedMaterialIndex = index;
		StateHasChanged();
	}

	void RefreshMaterials()
	{
		if ( Painter?.Terrain is null ) return;
		Materials = Painter.Terrain.GetPaintableMaterials();
		StateHasChanged();
	}
}
