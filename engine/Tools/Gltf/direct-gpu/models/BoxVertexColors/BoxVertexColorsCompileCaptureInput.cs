using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

/// <summary>Publish one task-owned compiled source input for isolated capture.</summary>
internal static class BoxVertexColorsCompileCaptureInput
{
	public static object[] Run( string checkoutRoot, string outputDirectory )
	{
		var allowed = Path.GetFullPath( Path.Combine( checkoutRoot, "Temp/gltf-validation/direct-gpu/model-workers" ) ) + Path.DirectorySeparatorChar;
		var output = Path.GetFullPath( outputDirectory );
		if ( !output.StartsWith( allowed, StringComparison.Ordinal ) || Directory.Exists( output ) ) throw new InvalidOperationException( "Compiled capture input must use a fresh model-worker directory." );
		var reference = new BoxVertexColorsReference( checkoutRoot ); var form = reference.Forms.Single( f => f.Name == "glTF/compiled_glb" );
		Directory.CreateDirectory( output ); var path = Path.Combine( output, "boxvertexcolors.glb_c" ); File.WriteAllBytes( path, form.Bytes );
		var hash = Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( path ) ) );
		return new object[] { new { Passed = true, Path = path, Sha256 = hash, Bytes = form.Bytes.Length, Form = form.Name, Scope = "Compiled current pinned source for private capture; no renderer or publication." } };
	}
}
