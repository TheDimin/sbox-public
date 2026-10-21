using System.Reflection;
using System.Reflection.Emit;

namespace PackageTests;

[TestClass]
public sealed class GameDataPackageTests
{
	[TestMethod]
	public async Task LocalAssemblyUsesItsProjectPackageWithoutRemoteLookup()
	{
		Project.Clear();
		try
		{
			var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );
			var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "package.local.testmap" ), AssemblyBuilderAccess.Run );
			var result = await Editor.GameData.GetAssemblyPackage( assembly );
			Assert.AreSame( project.Package, result );
		}
		finally
		{
			Project.Clear();
		}
	}

	[TestMethod]
	public async Task EngineAssemblyHasNoProjectPackage()
	{
		Assert.IsNull( await Editor.GameData.GetAssemblyPackage( typeof( Scene ).Assembly ) );
	}
}
