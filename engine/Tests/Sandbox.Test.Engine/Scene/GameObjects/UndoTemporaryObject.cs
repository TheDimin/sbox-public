using Editor;

namespace SceneTests.GameObjects;

[TestClass]
[DoNotParallelize]
public class UndoTemporaryObjectTest : SceneTest
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MoveUndoRestoresTemporaryObjectWithoutSavingIt( bool temporary )
	{
		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		var target = new GameObject( true, "Move target" )
		{
			Flags = temporary ? GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked : 0,
			LocalPosition = new Vector3( 10, 20, 30 )
		};
		var child = new GameObject( true, "Child" ) { Parent = target, LocalPosition = new Vector3( 2, 3, 4 ) };
		var original = target.WorldTransform;
		var originalFlags = target.Flags;
		var originalChild = child.WorldTransform;
		var session = new GameEditorSession( null, scene );
		try
		{
			using ( session.UndoScope( "Move target" ).WithGameObjectChanges( target, GameObjectUndoFlags.Properties ).Push() )
				target.LocalPosition += new Vector3( 100, 200, 300 );
			var moved = target.WorldTransform;
			for ( var repeat = 0; repeat < 2; repeat++ )
			{
				Assert.IsTrue( session.UndoSystem.Undo() );
				Assert.AreEqual( original, target.WorldTransform );
				Assert.AreEqual( originalFlags, target.Flags );
				Assert.AreEqual( originalChild, child.WorldTransform );
				Assert.IsTrue( session.UndoSystem.Redo() );
				Assert.AreEqual( moved, target.WorldTransform );
				Assert.AreEqual( temporary, target.Serialize() is null );
			}
		}
		finally { session.Destroy(); scene.Destroy(); }
	}
}
