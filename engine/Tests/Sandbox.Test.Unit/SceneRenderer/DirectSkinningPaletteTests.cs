using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Features;
using System;
using System.Linq;
using static SceneRendererTests.TestScenes;

namespace SceneRendererTests;

/// <summary>Final world-space palettes override native-style bone/IBM composition per instance and mesh.</summary>
[TestClass]
[DoNotParallelize]
public class DirectSkinningPaletteTests
{
	static void Prepare( RenderSystem system, RenderWorld world )
	{
		var view = LookingDownX();
		var stats = new RenderStats();
		system.Collect( world, view, ref stats );
		system.Prepare( world, view );
	}

	static Matrix3x4[] Palette( float offset ) =>
	[
		// Deliberate shear, nonuniform scale and translation: this cannot be
		// represented by a position/rotation/scale Transform without loss.
		new Matrix3x4 { Row0 = new Vector4( 2, .35f, -.2f, 11 + offset ), Row1 = new Vector4( .4f, 3, .6f, -7 ), Row2 = new Vector4( .1f, -.3f, .5f, 5 ) },
		new Matrix3x4 { Row0 = new Vector4( .75f, -.45f, .2f, -3 ), Row1 = new Vector4( .3f, 1.25f, .1f, 8 + offset ), Row2 = new Vector4( -.1f, .2f, 2, 4 ) }
	];

	static void Equal( in Matrix3x4 expected, in Matrix3x4 actual, string message )
	{
		Assert.IsTrue( expected.Row0.Distance( actual.Row0 ) < 1e-5f, $"{message} row 0: expected {expected.Row0}, got {actual.Row0}" );
		Assert.IsTrue( expected.Row1.Distance( actual.Row1 ) < 1e-5f, $"{message} row 1: expected {expected.Row1}, got {actual.Row1}" );
		Assert.IsTrue( expected.Row2.Distance( actual.Row2 ) < 1e-5f, $"{message} row 2: expected {expected.Row2}, got {actual.Row2}" );
	}

	static void AssertPalette( MeshObject obj, int mesh, Matrix3x4[] expected )
	{
		var actual = obj.SkinningPaletteFor( mesh );
		Assert.AreEqual( expected.Length, actual.Length );
		for ( var i = 0; i < expected.Length; i++ ) Equal( expected[i], actual[i], $"palette {mesh}, bone {i}" );
	}

	static void AssertEntries( RenderSystem system, MeshObject obj, int mesh, Matrix3x4[] expected )
	{
		var slot = obj.SkinSlots[mesh];
		Assert.IsTrue( slot >= 0, "Visible mesh must receive a transform slot." );
		AssertEntriesAtSlot( system, slot, expected );
	}

	static void AssertEntriesAtSlot( RenderSystem system, int slot, Matrix3x4[] expected )
	{
		for ( var i = 0; i < expected.Length; i++ )
		{
			var entry = system.Transforms[slot + 2 + i];
			Equal( expected[i], new Matrix3x4 { Row0 = entry.Row0, Row1 = entry.Row1, Row2 = entry.Row2 }, $"transform entry slot {slot}, bone {i}" );
		}
	}

	static Matrix3x4[] LegacyBones( MeshObject obj, int mesh )
	{
		var skin = obj.Mesh.SkinFor( mesh );
		var result = new Matrix3x4[skin.BoneCount];
		for ( var i = 0; i < result.Length; i++ )
			result[i] = Matrix3x4.Concat( obj.BoneMatrices[skin.MasterBones[i]], skin.InverseBindPoses[i] );
		return result;
	}

	static void Rejected( Action action, string description )
	{
		try { action(); }
		catch ( ArgumentException ) { return; }
		catch ( InvalidOperationException ) { return; }
		Assert.Fail( "Expected validation failure: " + description );
	}

	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 4 )]
	public void FinalAffineMatricesReachGpuEntriesUnchangedAndAreCopiedPerInstance( int blendWeights )
	{
		var shared = TestSkinnedMesh( blendWeights );
		var a = new MeshObject( shared, new Transform( new Vector3( 20, 0, 0 ) ) );
		var b = new MeshObject( shared, new Transform( new Vector3( 20, 5, 0 ) ) );
		var sourceA = Palette( 0 ); var expectedA = sourceA.ToArray(); var sourceB = Palette( 9 ); var expectedB = sourceB.ToArray();
		a.SetSkinningPalette( 0, sourceA ); b.SetSkinningPalette( 0, sourceB );
		sourceA[0] = Matrix3x4.Identity; sourceB[1] = Matrix3x4.Identity;
		AssertPalette( a, 0, expectedA ); AssertPalette( b, 0, expectedB );

		var world = new RenderWorld(); world.Add( a ); world.Add( b );
		using var system = new RenderSystem();
		Prepare( system, world );
		AssertEntries( system, a, 0, expectedA ); AssertEntries( system, b, 0, expectedB );
		Assert.AreEqual( (uint)blendWeights, system.Transforms[a.SkinSlots[0]].BlendWeightCount );
		Assert.AreEqual( blendWeights > 1, shared.SkinFor( 0 ).UsesVertexCache );
		var run = system.GetFeature<MeshRenderFeature>().RunsFor( system.MainPass ).Opaque.Single();
		Assert.IsTrue( run.Skinned ); Assert.AreEqual( 2, run.Count );
		CollectionAssert.AreEquivalent( new[] { a.SkinSlots[0], b.SkinSlots[0] }, new[] { system.Transforms.InstanceSlot( run.FirstInstance ), system.Transforms.InstanceSlot( run.FirstInstance + 1 ) } );
	}

	[TestMethod]
	public void ClearRestoresLatestLegacyBonePoseAndDoesNotDiscardSetBones()
	{
		var mesh = TestSkinnedMesh( 4 ); var obj = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) );
		obj.SetBones( [new Transform( new Vector3( 20, 1, 0 ) ), new Transform( new Vector3( 20, 0, 3 ) )] );
		var final = Palette( 3 ); obj.SetSkinningPalette( 0, final );
		obj.SetBones( [new Transform( new Vector3( 21, 2, 0 ) ), new Transform( new Vector3( 22, 0, 4 ) )] );
		AssertPalette( obj, 0, final );
		var world = new RenderWorld(); world.Add( obj ); using var system = new RenderSystem();
		Prepare( system, world ); AssertEntries( system, obj, 0, final );
		obj.ClearSkinningPalette( 0 );
		Assert.AreEqual( 0, obj.SkinningPaletteFor( 0 ).Length );
		Prepare( system, world ); AssertEntries( system, obj, 0, LegacyBones( obj, 0 ) );
	}

	[TestMethod]
	public void ReplacingMeshClearsOverridesButReassigningSameMeshDoesNot()
	{
		var first = TestSkinnedMesh( 4 ); var second = TestSkinnedMesh( 4 );
		var obj = new MeshObject( first, Transform.Zero ); var palette = Palette( 0 );
		obj.SetSkinningPalette( 0, palette ); obj.Mesh = first; AssertPalette( obj, 0, palette );
		obj.Mesh = second; Assert.AreEqual( 0, obj.SkinningPaletteFor( 0 ).Length );
		obj.SetSkinningPalette( 0, palette ); obj.Mesh = null; obj.Mesh = second;
		Assert.AreEqual( 0, obj.SkinningPaletteFor( 0 ).Length );
	}

	[TestMethod]
	public void SharedSkinDrawStateVariantsPreservePaletteIncludingHiddenDraws()
	{
		var source = TestSkinnedMesh( 4 );
		var shown = source.WithDraws( [new RenderMesh.DrawState( 0, 0, null, true, true, false )] );
		var hidden = source.WithDraws( [new RenderMesh.DrawState( 0, 0, null, false, false, false )] );
		Assert.AreNotSame( source, shown ); Assert.AreNotSame( source, hidden );
		Assert.AreSame( source.SkinFor( 0 ), shown.SkinFor( 0 ) );
		Assert.AreSame( source.SkinFor( 0 ), hidden.SkinFor( 0 ) );
		Assert.AreEqual( 0, hidden.Draws.Count );
		var obj = new MeshObject( source, new Transform( new Vector3( 20, 0, 0 ) ) );
		var expected = Palette( 4 ); obj.SetSkinningPalette( 0, expected );
		obj.Mesh = shown; AssertPalette( obj, 0, expected );
		obj.Mesh = hidden; AssertPalette( obj, 0, expected );
		obj.Mesh = source; AssertPalette( obj, 0, expected );
		var world = new RenderWorld(); world.Add( obj ); using var system = new RenderSystem();
		Prepare( system, world ); AssertEntries( system, obj, 0, expected );
		// This exercises non-null variants only. SceneMirror's visibility-null
		// transition needs separate provider reapplication integration coverage.
	}

	[TestMethod]
	public void RejectsInvalidInputsBeforeChangingExistingPaletteOrGeneration()
	{
		var obj = new MeshObject( TestSkinnedMesh( 4 ), Transform.Zero ); var expected = Palette( 4 );
		obj.SetSkinningPalette( 0, expected ); obj.SkinGeneration = 123;
		Rejected( () => obj.SetSkinningPalette( -1, expected ), "negative mesh" );
		Rejected( () => obj.SetSkinningPalette( 1, expected ), "mesh past end" );
		Rejected( () => obj.SetSkinningPalette( 0, Array.Empty<Matrix3x4>() ), "empty" );
		Rejected( () => obj.SetSkinningPalette( 0, new[] { Matrix3x4.Identity } ), "too few bones" );
		Rejected( () => obj.SetSkinningPalette( 0, new[] { Matrix3x4.Identity, Matrix3x4.Identity, Matrix3x4.Identity } ), "too many bones" );
		// Include an invalid later bone, so preflight must finish before any
		// matrix in the previous valid palette can change.
		for ( var bone = 0; bone < 2; bone++ )
			for ( var component = 0; component < 12; component++ )
			{
				var invalid = Palette( 5 );
				var rows = new[] { invalid[bone].Row0, invalid[bone].Row1, invalid[bone].Row2 };
				var row = component / 4; var column = component % 4;
				var v = rows[row];
				if ( column == 0 ) v.x = float.NaN; else if ( column == 1 ) v.y = float.PositiveInfinity; else if ( column == 2 ) v.z = float.NegativeInfinity; else v.w = float.NaN;
				rows[row] = v; invalid[bone] = new Matrix3x4 { Row0 = rows[0], Row1 = rows[1], Row2 = rows[2] };
				Rejected( () => obj.SetSkinningPalette( 0, invalid ), $"non-finite bone {bone}, component {component}" );
				AssertPalette( obj, 0, expected ); Assert.AreEqual( 123, obj.SkinGeneration );
			}
		AssertPalette( obj, 0, expected ); Assert.AreEqual( 123, obj.SkinGeneration );
		var rigid = new MeshObject( TestMesh( false ), Transform.Zero );
		Rejected( () => rigid.SetSkinningPalette( 0, expected ), "rigid mesh" );
		var unsupported = new MeshObject( TestSkinnedMesh( 0 ), Transform.Zero );
		unsupported.SkinGeneration = 456;
		Rejected( () => unsupported.SetSkinningPalette( 0, expected ), "zero blend weights" );
		Assert.AreEqual( 0, unsupported.SkinningPaletteFor( 0 ).Length );
		Assert.AreEqual( 456, unsupported.SkinGeneration );
	}

	[TestMethod]
	public void DirectPaletteIsPerMeshAndOtherMeshUsesLegacyPose()
	{
		var basis = TestSkinnedMesh( 4 );
		var draws = new[] { new RenderMesh.Draw( 0, 0, 0, 36, 24, 0, null ), new RenderMesh.Draw( 1, 0, 0, 36, 24, 0, null ) };
		var mesh = new RenderMesh( UnitBox, [draws] ); mesh.SetSkinning( [basis.SkinFor( 0 ), basis.SkinFor( 0 )], basis.BindPose );
		var obj = new MeshObject( mesh, new Transform( new Vector3( 20, 0, 0 ) ) );
		obj.SetBones( [new Transform( new Vector3( 20, 1, 0 ) ), new Transform( new Vector3( 20, 0, 3 ) )] );
		var expected = Palette( 6 ); obj.SetSkinningPalette( 0, expected );
		var world = new RenderWorld(); world.Add( obj ); using var system = new RenderSystem();
		Prepare( system, world );
		AssertEntries( system, obj, 0, expected ); AssertEntries( system, obj, 1, LegacyBones( obj, 1 ) );
	}

	[TestMethod]
	public void PaletteChangesInvalidateSameGenerationAndAreUsedInNextGeneration()
	{
		var obj = new MeshObject( TestSkinnedMesh( 4 ), new Transform( new Vector3( 20, 0, 0 ) ) );
		var first = Palette( 0 ); var second = Palette( 7 ); obj.SetSkinningPalette( 0, first );
		var world = new RenderWorld(); world.Add( obj ); using var system = new RenderSystem();
		Prepare( system, world ); AssertEntries( system, obj, 0, first );
		var generation = system.Transforms.Generation; var oldSlot = obj.SkinSlots[0];
		var feature = system.GetFeature<MeshRenderFeature>();
		var shadowRuns = system.ShadowMaps.Passes.ToArray().SelectMany( p => feature.RunsFor( p ).Opaque ).Where( r => r.Skinned ).ToArray();
		// Optional consistency check only, not shadow coverage: if the editor's
		// shadow cache prepared a view, it must reuse the same palette slot.
		foreach ( var run in shadowRuns ) Assert.AreEqual( oldSlot, system.Transforms.InstanceSlot( run.FirstInstance ) );
		obj.SetSkinningPalette( 0, second ); Assert.AreNotEqual( generation, obj.SkinGeneration );
		// Reprepare the already collected main view against the same transform
		// buffer generation; a new slot must be written, without changing the old.
		system.Prepare( world, system.MainPass, system.Transforms );
		Assert.AreEqual( generation, system.Transforms.Generation );
		Assert.AreNotEqual( oldSlot, obj.SkinSlots[0] ); AssertEntries( system, obj, 0, second );
		AssertEntriesAtSlot( system, oldSlot, first ); // CPU transform buffer only; not GPU timing proof.
		Prepare( system, world ); AssertEntries( system, obj, 0, second );
		obj.ClearSkinningPalette( 0 ); Assert.AreNotEqual( system.Transforms.Generation, obj.SkinGeneration );
	}
}
