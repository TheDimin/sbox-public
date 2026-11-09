using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.IO;

namespace Sandbox.Test;

[TestClass]
public class GltfAnimationControllerAccessTests
{
	[TestMethod]
	public void GameAssemblyCanControlThePublicGltfAnimationController()
	{
		var core = Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var compilation = CSharpCompilation.Create( "package.gltfcontroller",
			[CSharpSyntaxTree.ParseText( """
				using Sandbox;
				public static class Example
				{
				    public static float Apply(GltfAnimationController controller, SkinnedModelRenderer renderer)
				    {
				        controller.Target = renderer;
				        controller.SetClips(new[] { "Move", "Scale" });
				        controller.Clips.Add("Extra");
				        controller.Looping = true;
				        controller.PlaybackRate = 1;
				        controller.Time = .25f;
				        return controller.IsBound && !controller.IsFinished && controller.ValidationError == null ? controller.Duration : 0;
				    }
				}
				""" )],
			[
				MetadataReference.CreateFromFile( typeof( object ).Assembly.Location ),
				MetadataReference.CreateFromFile( Path.Combine( core, "System.Runtime.dll" ) ),
				MetadataReference.CreateFromFile( Path.Combine( core, "System.Collections.dll" ) ),
				MetadataReference.CreateFromFile( typeof( GltfAnimationController ).Assembly.Location ),
				MetadataReference.CreateFromFile( typeof( IValid ).Assembly.Location )
			], new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
		using var stream = new MemoryStream();
		var emitted = compilation.Emit( stream );
		Assert.IsTrue( emitted.Success, string.Join( "\n", emitted.Diagnostics ) );
		stream.Position = 0;
		using var access = new AccessControl();
		var result = access.VerifyAssembly( stream, out var trusted );
		trusted?.Dispose();
		Assert.IsTrue( result.Success, string.Join( "\n", result.Errors ) );
	}
}
