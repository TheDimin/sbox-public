using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CompilingTests;

[TestClass]
[DoNotParallelize]
public class ConditionalSourceTests
{
	private const string SourcePath = "Feature.Server.cs";
	private const string Source = "public class ServerOnly { public const int Value = 1; }";

	private static Compiler CreateCompiler( CompileGroup group, MemoryFileSystem files, bool server, bool strip )
	{
		files.WriteAllText( SourcePath, Source );
		var compiler = group.GetOrCreateCompiler( "conditional-source" );
		compiler.SetConfiguration( new Compiler.Configuration
		{
			DefineConstants = server ? "SERVER" : "CLIENT",
			StripDisabledTextTrivia = strip
		} );
		compiler.UseAbsoluteSourcePaths = false;
		compiler.AddSourceLocation( files );
		return compiler;
	}

	private static async Task<SyntaxTree> Build( CompileGroup group, Compiler compiler )
	{
		compiler.MarkForRecompile();
		await group.BuildAsync();
		Assert.IsTrue( group.BuildResult.Success, group.BuildResult.BuildDiagnosticsString() );
		return compiler.Output.Archive.SyntaxTrees.Single( x => x.FilePath == SourcePath );
	}

	[TestMethod]
	[DataRow( true, false )]
	[DataRow( true, true )]
	[DataRow( false, false )]
	[DataRow( false, true )]
	public async Task UnchangedConditionalSourceReusesFinalTree( bool server, bool strip )
	{
		using var group = new CompileGroup( "ConditionalSource" );
		var files = new MemoryFileSystem();
		using var compiler = CreateCompiler( group, files, server, strip );
		var first = await Build( group, compiler );
		var compiledTree = compiler.incrementalState.Compilation.SyntaxTrees.Single( x => x.FilePath == SourcePath );

		Assert.AreEqual( server, compiler.incrementalState.Compilation.GetTypeByMetadataName( "ServerOnly" ) is not null );
		Assert.AreEqual( server || !strip, first.GetText().ToString().Contains( "ServerOnly" ) );
		Assert.AreSame( first, await Build( group, compiler ), "Unchanged effective source must retain its final tree." );
		Assert.AreSame( compiledTree, compiler.incrementalState.Compilation.SyntaxTrees.Single( x => x.FilePath == SourcePath ) );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public async Task ChangedConditionalSourceUpdatesCodeAndStripsDisabledText( bool server )
	{
		using var group = new CompileGroup( "ConditionalSource" );
		var files = new MemoryFileSystem();
		using var compiler = CreateCompiler( group, files, server, strip: true );
		var first = await Build( group, compiler );

		files.WriteAllText( SourcePath, "public class ServerOnly { public const int Value = 2; }\n#if NEVER\nprivate server secret\n#endif" );
		var changed = await Build( group, compiler );
		Assert.AreNotSame( first, changed );
		Assert.IsFalse( changed.GetRoot().DescendantTrivia( descendIntoTrivia: true ).Any( x => x.IsKind( SyntaxKind.DisabledTextTrivia ) ) );
		Assert.IsFalse( changed.GetText().ToString().Contains( "private server secret" ) );

		var type = compiler.incrementalState.Compilation.GetTypeByMetadataName( "ServerOnly" );
		if ( server )
			Assert.AreEqual( 2, ((IFieldSymbol)type.GetMembers( "Value" ).Single()).ConstantValue );
		else
			Assert.IsNull( type );

		Assert.AreSame( changed, await Build( group, compiler ) );
	}

	[TestMethod]
	public async Task ReplacementDirectiveChangesInvalidateUnchangedSource()
	{
		using var group = new CompileGroup( "ConditionalSource" );
		var files = new MemoryFileSystem();
		using var compiler = CreateCompiler( group, files, server: true, strip: true );
		var first = await Build( group, compiler );

		// This dictionary is shared with the configuration. No SetConfiguration call
		// clears the incremental state, so its contents must be part of source identity.
		var directives = compiler.GetConfiguration().ReplacementDirectives;
		directives[".Server.cs"] = "CLIENT";
		var client = await Build( group, compiler );
		Assert.AreNotSame( first, client );
		Assert.IsNull( compiler.incrementalState.Compilation.GetTypeByMetadataName( "ServerOnly" ) );
		Assert.IsFalse( client.GetText().ToString().Contains( "ServerOnly" ) );

		directives.Remove( ".Server.cs" );
		var unwrapped = await Build( group, compiler );
		Assert.AreEqual( Source, unwrapped.GetText().ToString() );
		Assert.IsNotNull( compiler.incrementalState.Compilation.GetTypeByMetadataName( "ServerOnly" ) );
	}

	[TestMethod]
	public async Task ConfigurationChangesRestoreExcludedSource()
	{
		using var group = new CompileGroup( "ConditionalSource" );
		var files = new MemoryFileSystem();
		using var compiler = CreateCompiler( group, files, server: false, strip: true );
		var stripped = await Build( group, compiler );
		Assert.IsFalse( stripped.GetText().ToString().Contains( "ServerOnly" ) );

		compiler.SetConfiguration( compiler.GetConfiguration() with { StripDisabledTextTrivia = false } );
		var retained = await Build( group, compiler );
		Assert.IsTrue( retained.GetText().ToString().Contains( "ServerOnly" ) );
		Assert.IsNull( compiler.incrementalState.Compilation.GetTypeByMetadataName( "ServerOnly" ) );

		compiler.SetConfiguration( compiler.GetConfiguration() with { DefineConstants = "SERVER", StripDisabledTextTrivia = true } );
		var enabled = await Build( group, compiler );
		Assert.IsTrue( ((CSharpParseOptions)enabled.Options).PreprocessorSymbolNames.Contains( "SERVER" ) );
		Assert.IsNotNull( compiler.incrementalState.Compilation.GetTypeByMetadataName( "ServerOnly" ) );
		Assert.AreSame( enabled, await Build( group, compiler ) );
	}

	[TestMethod]
	public async Task ConditionalSourceRetainsDiagnosticPathAndWrapperLineOffset()
	{
		using var group = new CompileGroup( "ConditionalSource" );
		var files = new MemoryFileSystem();
		using var compiler = CreateCompiler( group, files, server: true, strip: false );
		files.WriteAllText( SourcePath, "#error location check" );
		compiler.MarkForRecompile();
		await group.BuildAsync();

		Assert.IsFalse( group.BuildResult.Success );
		var diagnostic = compiler.Output.Diagnostics.Single( x => x.Id == "CS1029" );
		var span = diagnostic.Location.GetLineSpan();
		Assert.AreEqual( SourcePath, span.Path );
		Assert.AreEqual( 1, span.StartLinePosition.Line, "Keep the existing one-line wrapper offset." );
	}
}
