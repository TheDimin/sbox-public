namespace Sandbox.Tests;

[TestClass]
public class EditorProcessNameTests
{
	[TestMethod]
	public void NamesEditorsAfterTheirProjectFiles()
	{
		Assert.AreEqual( "sbox:survive", EditorProcessName.FromProjectPath( "/projects/Survive/survive.sbproj" ) );
		Assert.AreEqual( "sbox:b2sbox", EditorProcessName.FromProjectPath( "/projects/b2sbox.sbproj" ) );
		Assert.AreEqual( "sbox:abcdefghij", EditorProcessName.FromProjectPath( "/projects/abcdefghijkl.sbproj" ) );
	}

	[TestMethod]
	public void FitsUtf8CharactersWithoutSplittingThem()
	{
		Assert.AreEqual( "sbox:ééééé", EditorProcessName.FromProjectPath( "/projects/éééééé.sbproj" ) );
		Assert.AreEqual( "sbox:abcd😀xy", EditorProcessName.FromProjectPath( "/projects/abcd😀xyz.sbproj" ) );
		Assert.IsNull( EditorProcessName.FromProjectPath( "" ) );
	}
}
