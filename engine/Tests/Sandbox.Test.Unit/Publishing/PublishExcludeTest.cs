using Editor;
using Sandbox.DataModel;
using System;
using System.IO;
using System.Threading;
using System.Text.Json;

namespace PublishingTests;

[TestClass]
public class PublishExcludeTest
{
	[TestMethod]
	[DataRow( null, "Editor/bin/Debug/net10.0/Engine.xml", true )]
	[DataRow( "", "Tests/Test.cs", true )]
	[DataRow( "Tests/*", "Tests/Test.cs", false )]
	[DataRow( "Tests/*", "Tests/Editor/Check.cs", false )]
	[DataRow( "tests/*", "TESTS/Test.cs", false )]
	[DataRow( "*/bin/*", "Editor/bin/Debug/net10.0/Engine.xml", false )]
	[DataRow( "*/bin/*", "Tests/bin/Debug/net10.0/Test.deps.json", false )]
	[DataRow( "bin/*", "bin/Engine.xml", false )]
	[DataRow( "Editor\\bin\\*", "Editor/bin/Engine.xml", false )]
	[DataRow( "Editor/bin/*", "Editor\\bin\\Engine.xml", false )]
	[DataRow( " /Tests/* \r\n\n // comment\n */bin/* ", "Tests/Test.cs", false )]
	[DataRow( "Tests/*\n*/bin/*", "Editor/Chat.cs", true )]
	[DataRow( "Tests/*", "Editor/View.razor", true )]
	[DataRow( "Tests/*", "THIRD_PARTY_NOTICES.md", true )]
	[DataRow( "Tests/*", "Contest/Test.cs", true )]
	[DataRow( "Tests/*.cs", "Tests/readme.md", true )]
	public async Task ExclusionsApplyToGeneratedFiles( string patterns, string path, bool included )
	{
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.SetPublishExclusions( patterns );
		await manifest.AddTextFile( "package content", path );
		Assert.AreEqual( included ? 1 : 0, manifest.Assets.Count );
	}

	[TestMethod]
	public async Task ChangingExclusionsReplacesPreviousPatterns()
	{
		var manifest = new ProjectPublisher.PackageManifest();
		manifest.SetPublishExclusions( "Tests/*" );
		manifest.SetPublishExclusions( "*/bin/*" );
		await manifest.AddTextFile( "test source", "Tests/Test.cs" );
		Assert.AreEqual( 1, manifest.Assets.Count );
	}

	[TestMethod]
	public async Task SourceCollectionKeepsChatAndLicenseWithoutBuildOutputOrTests()
	{
		var root = Path.Combine( Path.GetTempPath(), $"sbox-publish-exclude-{Guid.NewGuid():N}" );
		string[] included = ["globalchat.sbproj", "Editor/Chat.cs", "Editor/View.razor", "README.md", "THIRD_PARTY_NOTICES.md"];
		string[] excluded = ["Editor/bin/Debug/net10.0/Engine.xml", "Tests/bin/Debug/net10.0/Test.deps.json", "Tests/Test.cs", "Tests/Editor/Check.cs", "Editor/Validation/Check.cs", "Editor/Properties/AssemblyInfo.cs", ".git/config"];
		try
		{
			foreach ( var relative in included.Concat( excluded ) )
			{
				var path = Path.Combine( root, relative );
				Directory.CreateDirectory( Path.GetDirectoryName( path ) );
				File.WriteAllText( path, "fixture" );
			}
			var manifest = new ProjectPublisher.PackageManifest();
			manifest.SetPublishExclusions( "Tests/*\n*/bin/*\nEditor/Validation/*\nEditor/Properties/*\n.git/*" );
			await manifest.IncludeFiles( root, "*", CancellationToken.None, allowSourceFiles: true );
			CollectionAssert.AreEquivalent( included, manifest.Assets.Select( x => x.Name ).ToArray() );
		}
		finally
		{
			Directory.Delete( root, recursive: true );
		}
	}

	[TestMethod]
	public void ExclusionsSurviveProjectConfigSerialization()
	{
		var config = new ProjectConfig { PublishExclude = "Tests/*\n*/bin/*" };
		var restored = JsonSerializer.Deserialize<ProjectConfig>( JsonSerializer.Serialize( config ) );
		Assert.AreEqual( config.PublishExclude, restored.PublishExclude );
	}
}
