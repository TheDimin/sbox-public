using Microsoft.CodeAnalysis.CSharp;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;

namespace CompilingTests;

[TestClass]
[DoNotParallelize]
public class CompilerPublicationTests
{
	[TestMethod]
	[Timeout( 5000 )]
	public void ArchivesPublishOnCallingThreadAndContext()
	{
		var caller = Environment.CurrentManagedThreadId;
		var published = 0;
		AsyncPump.RunSynchronously( async () =>
		{
			var context = SynchronizationContext.Current;
			await CompilerOutput.PublishArchivesAsync( Enumerable.Range( 0, 5 ).Select( CreateOutput ).ToArray(), ( output, bytes ) =>
			{
				Assert.AreEqual( caller, Environment.CurrentManagedThreadId );
				Assert.AreSame( context, SynchronizationContext.Current );
				published++;
			} );
		} );
		Assert.AreEqual( 5, published );
	}

	[TestMethod]
	public async Task RazorUsesBuiltDependencyAndReusesUnchangedTree()
	{
		using var group = new CompileGroup( "RazorDependency" );
		var dependency = group.GetOrCreateCompiler( "dependency" );
		var dependencyFiles = new MemoryFileSystem();
		dependencyFiles.WriteAllText( "/Dependency.cs", "public static class Dependency { public static string Value => \"Hello\"; }" );
		dependency.AddSourceLocation( dependencyFiles );
		dependency.MarkForRecompile();

		var compiler = group.GetOrCreateCompiler( "panel" );
		var files = new MemoryFileSystem();
		files.WriteAllText( "/TestPanel.razor", "@inherits Sandbox.UI.Panel\n<div>@Dependency.Value</div>" );
		compiler.AddSourceLocation( files );
		compiler.AddReference( dependency );
		compiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components;" );
		compiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components.Rendering;" );
		compiler.MarkForRecompile();

		Assert.IsTrue( await group.BuildAsync(), group.BuildResult.BuildDiagnosticsString() );
		Assert.AreSame( dependency, group.BuildResult.Output.First().Compiler );
		var razorTree = compiler.incrementalState.SyntaxTrees.Single( x => x.FilePath.StartsWith( "_gen_TestPanel.razor_" ) );
		Assert.IsTrue( razorTree.ToString().Contains( "Dependency.Value" ) );

		compiler.MarkForRecompile();
		Assert.IsTrue( await group.BuildAsync(), group.BuildResult.BuildDiagnosticsString() );
		Assert.AreSame( razorTree, compiler.incrementalState.SyntaxTrees.Single( x => x.FilePath == razorTree.FilePath ) );
	}

	[TestMethod]
	public async Task PublicationCompletesBeforeSuccessAndKeepsNewChangesQueued()
	{
		using var group = new CompileGroup( "Publication" );
		var compiler = group.GetOrCreateCompiler( "test" );
		var files = new MemoryFileSystem();
		files.WriteAllText( "/Test.cs", "public class Test {}" );
		compiler.AddSourceLocation( files );
		compiler.MarkForRecompile();

		var entered = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
		var release = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
		var notified = false;
		group.OnCompileSuccessAsync = async () =>
		{
			entered.SetResult();
			await release.Task;
		};
		group.OnCompileSuccess = () => notified = true;

		var build = group.BuildAsync();
		try
		{
			await entered.Task.WaitAsync( TimeSpan.FromSeconds( 30 ) );
			Assert.IsTrue( group.IsBuilding );
			Assert.IsFalse( build.IsCompleted );
			Assert.IsFalse( notified );
			compiler.MarkForRecompile();
		}
		finally
		{
			release.TrySetResult();
		}

		Assert.IsTrue( await build );
		Assert.IsTrue( notified );
		Assert.IsFalse( group.IsBuilding );
		Assert.IsTrue( group.NeedsBuild, "A change during publication must remain queued." );
	}

	[TestMethod]
	public async Task FailedBuildDoesNotPublish()
	{
		using var group = new CompileGroup( "PublicationFailure" );
		var compiler = group.GetOrCreateCompiler( "test" );
		var files = new MemoryFileSystem();
		files.WriteAllText( "/Test.cs", "public class Test { this is invalid; }" );
		compiler.AddSourceLocation( files );
		compiler.MarkForRecompile();

		var published = false;
		group.OnCompileSuccessAsync = () =>
		{
			published = true;
			return Task.CompletedTask;
		};

		Assert.IsFalse( await group.BuildAsync() );
		Assert.IsFalse( published );
		Assert.IsFalse( group.IsBuilding );
	}

	[TestMethod]
	public async Task ArchivesRetainOrderAndContentsAcrossBatches()
	{
		var outputs = Enumerable.Range( 0, 5 ).Select( CreateOutput ).ToArray();
		var published = new List<CompilerOutput>();
		await CompilerOutput.PublishArchivesAsync( outputs, ( output, bytes ) =>
		{
			published.Add( output );
			var restored = new CodeArchive( bytes );
			Assert.AreEqual( output.Archive.CompilerName, restored.CompilerName );
			Assert.AreEqual( output.Archive.SyntaxTrees.Single().ToString(), restored.SyntaxTrees.Single().ToString() );
			Assert.AreEqual( output.Archive.AdditionalFiles.Single(), restored.AdditionalFiles.Single() );
			CollectionAssert.AreEquivalent( output.Archive.References.ToArray(), restored.References.ToArray() );
			Assert.AreEqual( output.Archive.FileMap["test.cs"], restored.FileMap["test.cs"] );
		} );
		CollectionAssert.AreEqual( outputs, published );
	}

	[TestMethod]
	public async Task PublicationFailureStopsLaterOutputs()
	{
		var outputs = Enumerable.Range( 0, 5 ).Select( CreateOutput ).ToArray();
		var count = 0;
		await Assert.ThrowsExceptionAsync<InvalidOperationException>( () => CompilerOutput.PublishArchivesAsync( outputs, ( output, bytes ) =>
		{
			count++;
			throw new InvalidOperationException( "Publication failed" );
		} ) );
		Assert.AreEqual( 1, count );
	}

	private static CompilerOutput CreateOutput( int index )
	{
		var archive = new CodeArchive { CompilerName = $"test{index}", Configuration = new Compiler.Configuration() };
		archive.SyntaxTrees.Add( CSharpSyntaxTree.ParseText( $"public class Test{index} {{}}", path: "test.cs" ) );
		archive.AdditionalFiles.Add( new CodeArchive.AdditionalFile( "<div>Hello</div>", "test.razor" ) );
		archive.References.Add( "Sandbox.Engine" );
		archive.FileMap["test.cs"] = $"code/test{index}.cs";
		return new CompilerOutput( null ) { Successful = true, Archive = archive };
	}
}
