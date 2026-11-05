using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.Internal;
using System;
using System.IO;
using System.Reflection;
using Project = Sandbox.Project;

namespace PackageTests;

[TestClass]
public class PackageEditorToolbaseReloadTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

	private static byte[] Compile( string name, string source, params byte[][] references )
	{
		var runtime = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var refs = new[] { MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
			MetadataReference.CreateFromFile( Path.Combine( runtime, "System.Runtime.dll" ) ),
			MetadataReference.CreateFromFile( typeof( SupportsILHotloadAttribute ).Assembly.Location ) }
			.Concat( references.Select( bytes => MetadataReference.CreateFromImage( bytes ) ) );
		var compilation = CSharpCompilation.Create( name, [CSharpSyntaxTree.ParseText( source )], refs,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
		using var output = new MemoryStream();
		var result = compilation.Emit( output );
		Assert.IsTrue( result.Success, string.Join( "\n", result.Diagnostics ) );
		return output.ToArray();
	}

	[TestMethod]
	[DataRow( true, true, false, false, false )]
	[DataRow( false, true, false, false, false )]
	[DataRow( false, false, false, false, false )]
	[DataRow( true, false, true, false, false )]
	[DataRow( false, true, true, false, false )]
	[DataRow( false, true, true, true, false )]
	[DataRow( false, true, false, false, true )]
	public void GameEditorAndToolbaseFullSwapUsesEnrolledPreviewBase( bool editorChanged, bool toolChanged, bool fastEnabled, bool explicitDependency, bool transitiveConsumer )
	{
		var expectedEngineHash = Environment.GetEnvironmentVariable( "VALARK_EXPECT_ENGINE_SHA256" );
		if ( !string.IsNullOrEmpty( expectedEngineHash ) )
		{
			var actualEngineHash = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( typeof( Sandbox.PackageLoader ).Assembly.Location ) ) );
			Assert.AreEqual( expectedEngineHash, actualEngineHash, true, "The fixture must execute against the staged engine." );
		}
		var expectedReflectionHash = Environment.GetEnvironmentVariable( "VALARK_EXPECT_REFLECTION_SHA256" );
		if ( !string.IsNullOrEmpty( expectedReflectionHash ) )
		{
			var actualReflectionHash = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( typeof( TypeDescription ).Assembly.Location ) ) );
			Assert.AreEqual( expectedReflectionHash, actualReflectionHash, true, "The fixture must execute against the staged reflection assembly." );
		}
		var root = Path.Combine( Path.GetTempPath(), "sbox-toolbase-reload-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var toolProject = new Project( new DirectoryInfo( root ) ) { Config = new Sandbox.DataModel.ProjectConfig { Org = "local", Ident = "toolbase", Title = "Toolbase", Type = "tool" } };
		var gameProject = new Project( new DirectoryInfo( root ) ) { Config = new Sandbox.DataModel.ProjectConfig { Org = "local", Ident = "reloadtest", Title = "Reload test", Type = "game" } };
		if ( explicitDependency ) gameProject.Config.PackageReferences = ["local.toolbase"];
		var chainProject = transitiveConsumer
			? new Project( new DirectoryInfo( root ) ) { Config = new Sandbox.DataModel.ProjectConfig { Org = "local", Ident = "chain", Title = "Chain test", Type = "game" } }
			: null;
		var loader = new Sandbox.PackageLoader( "toolbase-regression", GetType().Assembly, disableAccessControl: true ) { ToolsMode = true };
		var library = new TypeLibrary();
		var enroller = loader.CreateEnroller( "toolbase-regression" );
		enroller.OnAssemblyAdded = a => library.AddAssembly( a.Assembly, true );
		enroller.OnAssemblyRemoved = a => library.RemoveAssembly( a.Assembly );
		try
		{
			// Exercise full swaps both with and without the fast-hotload machinery present.
			if ( !fastEnabled )
			{
				var ilProperty = typeof( Sandbox.PackageLoader ).GetProperty( "ILHotload", PrivateInstance );
				(ilProperty.GetValue( loader ) as IDisposable)?.Dispose();
				ilProperty.SetValue( loader, null );
			}
			var tool = new PackageManager.ActivePackage();
			var game = new PackageManager.ActivePackage();
			void SetPackage( PackageManager.ActivePackage active, Project project )
			{
				typeof( PackageManager.ActivePackage ).GetProperty( "Package" ).SetValue( active, project.Package );
				typeof( PackageManager.ActivePackage ).GetProperty( "AssemblyFileSystem" ).SetValue( active, project.AssemblyFileSystem );
			}
			SetPackage( tool, toolProject ); SetPackage( game, gameProject );
			const string toolPath = "/.bin/package.toolbase.dll";
			const string editorPath = "/.bin/package.local.reloadtest.editor.dll";
			var baseOld = Compile( "package.toolbase", "public class PreviewBase {} public class Preview : PreviewBase {}" );
			var baseNew = Compile( "package.toolbase", "public class PreviewBase { public int Added; } public class Preview : PreviewBase {}" );
			byte[] Editor( byte[] basis, int value )
			{
				// Match the compiler's explicit fast-hotload metadata for a method-only edit.
				var fastEdit = fastEnabled && !toolChanged && value == 2;
				var assemblyAttributes = fastEdit ? "[assembly: System.Reflection.AssemblyVersion(\"0.0.0.2\")] [assembly: Sandbox.SupportsILHotloadAttribute(\"0.0.0.0\")] " : "";
				var methodAttribute = fastEdit ? "[Sandbox.MethodBodyChangeAttribute(\"0.0.0.2\")] " : "";
				return Compile( "package.local.reloadtest.editor", $"{assemblyAttributes}public class Factory : PreviewBase {{ public static System.Type Base() => typeof(PreviewBase); public static PreviewBase Create() => new Preview(); {methodAttribute}public static int Version() => {value}; }}", basis );
			}
			toolProject.AssemblyFileSystem.WriteAllBytes( toolPath, baseOld );
			gameProject.AssemblyFileSystem.WriteAllBytes( editorPath, Editor( baseOld, 1 ) );
			var load = typeof( Sandbox.PackageLoader ).GetMethod( "LoadAssemblyFromPackage", PrivateInstance );
			var add = enroller.GetType().GetMethod( "Add", PrivateInstance );
			var oldTool = (LoadedAssembly)load.Invoke( loader, [tool, toolPath, null, false, false] );
			var oldEditor = (LoadedAssembly)load.Invoke( loader, [game, editorPath, null, false, false] );
			add.Invoke( enroller, [oldTool] ); add.Invoke( enroller, [oldEditor] );
			PackageManager.ActivePackage chain = null;
			LoadedAssembly oldChain = null;
			if ( transitiveConsumer )
			{
				chain = new PackageManager.ActivePackage();
				SetPackage( chain, chainProject );
				const string chainPath = "/.bin/package.local.chain.editor.dll";
				var chainBytes = Compile( "package.local.chain.editor", "public class ChildFactory : Factory { public static object Make() => new ChildFactory(); }", baseOld, Editor( baseOld, 1 ) );
				chainProject.AssemblyFileSystem.WriteAllBytes( chainPath, chainBytes );
				oldChain = (LoadedAssembly)load.Invoke( loader, [chain, chainPath, null, false, false] );
				var chainReferences = oldChain.Assembly.GetReferencedAssemblies().Select( reference => reference.Name ).ToArray();
				Assert.IsTrue( chainReferences.Contains( "package.local.reloadtest.editor" ), "The chain must reference the intermediate editor assembly." );
				Assert.IsFalse( chainReferences.Contains( "package.toolbase" ), "The chain must not directly reference toolbase." );
				add.Invoke( enroller, [oldChain] );
			}
			var active = (System.Collections.Generic.HashSet<PackageManager.ActivePackage>)typeof( Sandbox.PackageLoader ).GetField( "loadedPackages", PrivateInstance ).GetValue( loader );
			active.Add( tool ); active.Add( game );
			if ( chain is not null ) active.Add( chain );
			var pending = (System.Collections.Generic.HashSet<(PackageManager.ActivePackage ap, string filename)>)typeof( Sandbox.PackageLoader ).GetField( "changedPackageDlls", PrivateInstance ).GetValue( loader );
			if ( editorChanged ) gameProject.AssemblyFileSystem.WriteAllBytes( editorPath, Editor( baseNew, 2 ) );
			if ( toolChanged ) toolProject.AssemblyFileSystem.WriteAllBytes( toolPath, baseNew );
			// No manifest edge connects these packages: force the adverse editor-first order.
			if ( editorChanged ) pending.Add( (game, editorPath) );
			if ( toolChanged ) pending.Add( (tool, toolPath) );
			loader.Tick();
			var enrolled = enroller.GetLoadedAssemblies();
			var currentTool = enrolled.Single( a => a.Name == "package.toolbase" ).Assembly;
			var currentEditor = enrolled.Single( a => a.Name == "package.local.reloadtest.editor" ).Assembly;
			if ( toolChanged )
			{
				Assert.AreNotSame( oldTool.Assembly, currentTool, "The fixture must produce a full toolbase swap." );
				Assert.AreNotSame( oldEditor.Assembly, currentEditor, "The editor consumer must fully rebind after the dependency swap." );
			}
			if ( !toolChanged )
			{
				Assert.AreSame( oldTool.Assembly, currentTool );
				Assert.AreSame( oldEditor.Assembly, currentEditor );
			}
			var expected = currentTool.GetType( "PreviewBase", true );
			var factory = currentEditor.GetType( "Factory", true );
			var actual = (Type)factory.GetMethod( "Base" ).Invoke( null, null );
			var preview = factory.GetMethod( "Create" ).Invoke( null, null );
			Assert.AreEqual( editorChanged ? 2 : 1, (int)factory.GetMethod( "Version" ).Invoke( null, null ) );
			Assert.AreSame( expected, actual, "The game editor factory must bind to the enrolled toolbase after the full swap." );
			Assert.IsTrue( expected.IsInstanceOfType( preview ), "The factory result must be assignable to the enrolled preview base." );
			var registeredPreview = library.GetType( currentTool.GetType( "Preview", true ) );
			var registeredFactory = library.GetType( factory );
			Assert.IsNotNull( registeredPreview, "The current preview must be registered." );
			Assert.IsNotNull( registeredFactory, "The current factory must be registered." );
			Assert.IsTrue( registeredPreview.IsValid, "The registered preview description must remain valid after replacement." );
			Assert.IsTrue( registeredFactory.IsValid, "The registered factory description must remain valid after replacement." );
			Assert.IsTrue( registeredPreview.TargetType.IsInstanceOfType( preview ), "The factory must create the registered preview type." );
			if ( transitiveConsumer )
			{
				var currentChain = enrolled.Single( a => a.Name == "package.local.chain.editor" ).Assembly;
				Assert.AreNotSame( oldChain.Assembly, currentChain, "The transitive editor consumer must fully rebind." );
				var childFactory = currentChain.GetType( "ChildFactory", true );
				Assert.AreSame( factory, childFactory.BaseType, "The transitive consumer must reference the current editor factory." );
				var chainPreview = childFactory.GetMethod( "Make" ).Invoke( null, null );
				Assert.IsTrue( expected.IsInstanceOfType( chainPreview ), "The transitive factory must create a current preview." );
				Assert.IsTrue( library.GetType( childFactory ).IsValid, "The transitive factory description must be valid." );
			}
		}
		finally
		{
			enroller.Dispose(); loader.Dispose(); toolProject.Dispose(); gameProject.Dispose(); chainProject?.Dispose();
			Directory.Delete( root, true );
		}
	}
}
