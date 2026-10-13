using System;
using System.Collections.Generic;
using Sandbox.Clutter;

namespace SceneTests;

/// <summary>Executable configuration contract for selected native painted-model draw distances.</summary>
[TestClass]
[DoNotParallelize]
public class ClutterDrawDistanceTest : SceneTest
{
	[TestMethod]
	public void DefaultAndUnselectedModelsKeepUnlimitedDrawing()
	{
		var scene = new Scene();
		try
		{
			var grid = scene.GetSystem<ClutterGridSystem>();
			Assert.AreEqual( 0, grid.PaintedDrawDistances.Count );
			grid.PaintedDrawDistances = new() { ["models/test/grass.vmdl"] = 2000 };
			Assert.AreEqual( 2000, grid.PaintedDrawDistances["MODELS/TEST/GRASS.VMDL"] );
			Assert.IsFalse( grid.PaintedDrawDistances.ContainsKey( "models/test/rock.vmdl" ) );
			grid.PaintedDrawDistances["models/test/grass.vmdl"] = 0;
			Assert.AreEqual( 0, grid.PaintedDrawDistances["models/test/grass.vmdl"] );
		}
		finally { scene.Destroy(); }
	}

	[DataTestMethod]
	[DataRow( -1f )]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	public void InvalidReplacementPreservesLastValidConfiguration( float distance )
	{
		var scene = new Scene();
		try
		{
			var grid = scene.GetSystem<ClutterGridSystem>();
			grid.PaintedDrawDistances = new() { ["models/test/grass.vmdl"] = 2000 };
			var original = grid.PaintedDrawDistances;
			Assert.ThrowsException<ArgumentException>( () => grid.PaintedDrawDistances = new() { ["models/test/grass.vmdl"] = distance } );
			Assert.AreSame( original, grid.PaintedDrawDistances );
			Assert.AreEqual( 2000, grid.PaintedDrawDistances["models/test/grass.vmdl"] );
		}
		finally { scene.Destroy(); }
	}

	[TestMethod]
	public void ConfigurationRequiresPathsAndRejectsNull()
	{
		var scene = new Scene();
		try
		{
			var grid = scene.GetSystem<ClutterGridSystem>();
			Assert.ThrowsException<ArgumentNullException>( () => grid.PaintedDrawDistances = null );
			Assert.ThrowsException<ArgumentException>( () => grid.PaintedDrawDistances = new() { [" "] = 2000 } );
		}
		finally { scene.Destroy(); }
	}

	[TestMethod]
	public void TwoScenesDoNotShareSelectedModelDistances()
	{
		var first = new Scene();
		var second = new Scene();
		try
		{
			var a = first.GetSystem<ClutterGridSystem>();
			var b = second.GetSystem<ClutterGridSystem>();
			a.PaintedDrawDistances["models/test/grass.vmdl"] = 2000;
			Assert.IsFalse( b.PaintedDrawDistances.ContainsKey( "models/test/grass.vmdl" ) );
			b.PaintedDrawDistances["models/test/grass.vmdl"] = 4000;
			Assert.AreEqual( 2000, a.PaintedDrawDistances["models/test/grass.vmdl"] );
			Assert.AreEqual( 4000, b.PaintedDrawDistances["models/test/grass.vmdl"] );
		}
		finally { first.Destroy(); second.Destroy(); }
	}
}

