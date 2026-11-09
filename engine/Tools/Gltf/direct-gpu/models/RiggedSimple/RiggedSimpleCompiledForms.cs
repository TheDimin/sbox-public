using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.RiggedSimpleValidation;

/// <summary>Package each pinned source using the loaded producer for isolated public-load capture.</summary>
public static class RiggedSimpleCompiledForms
{
	public sealed record Result( string Name, bool Passed, string Error, string SourceSha256, string OutputPath, string OutputSha256 );
	public static Result[] RunAll( string checkout, string destination )
	{
		var root = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets" );
		var forms = new[] { ("glTF", "RiggedSimple.gltf"), ("glTF-Embedded", "RiggedSimple.gltf"), ("glTF-Binary", "RiggedSimple.glb"), ("glTF-Draco", "RiggedSimple.gltf") };
		var rows = new List<Result>();
		foreach ( var (variant, filename) in forms )
		{
			var source = Path.Combine( root, "Models/RiggedSimple", variant, filename );
			var output = Path.Combine( destination, "Models/RiggedSimple", variant, "RiggedSimple.glb_c" );
			string sourceHash = null;
			try
			{
				var bytes = File.ReadAllBytes( source ); sourceHash = Hash( bytes );
				byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( source ), Uri.UnescapeDataString( uri ) ) );
				var compiled = GltfCompiler.Compile( bytes, Resolve, new HashSet<string>( StringComparer.Ordinal ) { "KHR_draco_mesh_compression" } );
				Directory.CreateDirectory( Path.GetDirectoryName( output ) );
				using ( var file = new FileStream( output, FileMode.CreateNew, FileAccess.Write, FileShare.None ) ) file.Write( compiled );
				rows.Add( new( variant, true, null, sourceHash, output, Hash( compiled ) ) );
			}
			catch ( Exception e ) { rows.Add( new( variant, false, e.ToString(), sourceHash, output, null ) ); }
		}
		return rows.ToArray();
	}
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
