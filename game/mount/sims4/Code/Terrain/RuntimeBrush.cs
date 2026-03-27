using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace Sims4.Terrain;

public class RuntimeBrush
{
	public string Name { get; set; }
	public Texture Texture { get; set; }
}

/// <summary>
/// Loads terrain brush textures at runtime. The editor Brush class uses Pixmap (editor-only),
/// so this is the game-side equivalent.
/// </summary>
public static class RuntimeBrushLibrary
{
	static List<RuntimeBrush> _brushes;

	public static IReadOnlyList<RuntimeBrush> Brushes
	{
		get
		{
			_brushes ??= LoadAll();
			return _brushes;
		}
	}

	public static RuntimeBrush Default => Brushes.FirstOrDefault();

	static List<RuntimeBrush> LoadAll()
	{
		var list = new List<RuntimeBrush>();

		if ( FileSystem.Content is null )
			return list;

		foreach ( var filename in FileSystem.Content.FindFile( "materials/tools/terrain/brushes", "*.png" ) )
		{
			var path = $"materials/tools/terrain/brushes/{filename}";
			var tex = Texture.Load( path );
			if ( tex is null ) continue;

			list.Add( new RuntimeBrush
			{
				Name = System.IO.Path.GetFileNameWithoutExtension( filename ),
				Texture = tex
			} );
		}

		return list;
	}
}
