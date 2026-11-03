using Sandbox.Engine;
using System;
using System.IO;

namespace ResourceTests;

[TestClass]
[DoNotParallelize]
public class PublicResourceGuidMoveTests
{
	[TestMethod]
	public void LoadedModelKeepsItsPublicIdentityAfterAssetMove()
	{
		Project.Clear();
		var project = Project.AddFromFile( "unittest/addons/resources/.sbproj" );
		var assets = project.GetAssetsPath();
		var folder = $"public_guid_{Guid.NewGuid():N}";
		var directory = Path.Combine( assets, folder );
		Directory.CreateDirectory( directory );
		var oldPath = $"{folder}/original.vmdl";
		var newPath = $"{folder}/moved.vmdl";
		var id = Guid.NewGuid();
		try
		{
			NativeEngine.FullFileSystem.AddProjectPath( "unittest", assets );
			File.Copy( Path.Combine( assets, "model.vmdl_c" ), Path.Combine( assets, oldPath + "_c" ) );
			File.WriteAllText( Path.Combine( assets, oldPath + ".meta" ), $"{{\"guid\": \"{id}\"}}" );
			NativeEngine.g_pResourceSystem.InvalidateDatabase();

			var original = Model.Load( oldPath );
			Assert.IsNotNull( original );
			Assert.IsFalse( original.IsError );
			Assert.AreEqual( id, original.Guid );
			Assert.AreSame( original, ResourceLibrary.Get<Model>( id ) );
			var reference = Json.Serialize( original );

			File.Move( Path.Combine( assets, oldPath + "_c" ), Path.Combine( assets, newPath + "_c" ) );
			File.Move( Path.Combine( assets, oldPath + ".meta" ), Path.Combine( assets, newPath + ".meta" ) );
			NativeEngine.g_pResourceSystem.InvalidateDatabase();

			var moved = Model.Load( newPath );
			Assert.AreSame( original, moved );
			Assert.AreEqual( newPath, moved.ResourcePath );
			Assert.AreEqual( id, moved.Guid );
			Assert.AreSame( moved, ResourceLibrary.Get<Model>( id ) );
			Assert.AreSame( moved, Json.Deserialize<Model>( reference ), "A serialized GUID reference should survive the move." );
		}
		finally
		{
			Project.Clear();
			Directory.Delete( directory, recursive: true );
		}
	}
}
