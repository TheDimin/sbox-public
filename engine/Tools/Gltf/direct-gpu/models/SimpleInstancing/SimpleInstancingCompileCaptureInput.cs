using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

internal static class SimpleInstancingCompileCaptureInput
{
	public static object[] Run( string checkoutRoot, string outputDirectory )
	{
		var allowed = Path.GetFullPath( Path.Combine( checkoutRoot, "Temp/gltf-validation/direct-gpu/model-workers" ) ) + Path.DirectorySeparatorChar;
		var output = Path.GetFullPath( outputDirectory );
		if ( !output.StartsWith( allowed, StringComparison.Ordinal ) || Directory.Exists( output ) ) throw new InvalidOperationException( "Compiled capture input must use a fresh model-worker directory." );
		var reference = new SimpleInstancingReference( checkoutRoot ); var form = reference.Forms.Single( f => f.Name == "glTF/compiled_glb_c" );
		var hash = Convert.ToHexStringLower( SHA256.HashData( form.Bytes ) );
		if ( hash != "847746120eac0b37aa6f9865b05206b15e97e4f9ca293f157687c31cf6128504" ) throw new InvalidOperationException( "Canonical bytes differ from previously verified public GPU input: " + hash );
		Directory.CreateDirectory( output ); var path = Path.Combine( output, "simpleinstancing.glb_c" ); File.WriteAllBytes( path, form.Bytes );
		return new object[] { new { Passed = true, Path = path, Sha256 = hash, Bytes = form.Bytes.Length, Form = form.Name, Scope = "Canonical compiled source input for isolated corrected-camera capture." } };
	}
}
