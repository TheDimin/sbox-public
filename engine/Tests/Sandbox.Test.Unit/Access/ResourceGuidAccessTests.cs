using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;
using System.Linq;

namespace Sandbox.Test;

[TestClass]
public class ResourceGuidAccessTests
{
	private static CSharpCompilation Compile( string source )
	{
		var corePath = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		return CSharpCompilation.Create( "package.resourceguid",
			[CSharpSyntaxTree.ParseText( source )],
			[
				MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
				MetadataReference.CreateFromFile( Path.Combine( corePath, "System.Runtime.dll" ) ),
				MetadataReference.CreateFromFile( typeof( Resource ).Assembly.Location ),
				MetadataReference.CreateFromFile( typeof( IValid ).Assembly.Location )
			], new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
	}

	[TestMethod]
	public void GameAssemblyCanReadAndLookUpResourceGuids()
	{
		var compilation = Compile( """
			using System;
			using Sandbox;
			public static class Example
			{
			    public static Guid Identity(Resource resource) => resource.Guid;
			    public static Resource Find(Guid id) => ResourceLibrary.Get<Resource>(id);
			    public static bool TryFind(Guid id, out Resource resource) => ResourceLibrary.TryGet(id, out resource);
			}
			""" );
		using var stream = new MemoryStream();
		var emitted = compilation.Emit( stream );
		Assert.IsTrue( emitted.Success, string.Join( "\n", emitted.Diagnostics ) );
		stream.Position = 0;
		using var access = new AccessControl();
		var result = access.VerifyAssembly( stream, out var trusted );
		trusted?.Dispose();
		Assert.IsTrue( result.Success, string.Join( "\n", result.Errors ) );
	}

	[TestMethod]
	public void GameAssemblyCannotSetResourceGuid()
	{
		var compilation = Compile( "using System; using Sandbox; public static class Example { public static void Set(Resource resource, Guid id) => resource.Guid = id; }" );
		using var stream = new MemoryStream();
		var emitted = compilation.Emit( stream );
		Assert.IsFalse( emitted.Success );
		Assert.IsTrue( emitted.Diagnostics.Any( diagnostic => diagnostic.Id is "CS0200" or "CS0272" ) );
	}
}
