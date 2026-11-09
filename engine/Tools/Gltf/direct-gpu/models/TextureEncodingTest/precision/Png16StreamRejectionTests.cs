using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

internal static class Png16StreamRejectionTests
{
	public sealed record Result( string Name, bool Passed, string Error );
	public static List<Result> Run( string checkout )
	{
		using var doc = JsonDocument.Parse( File.ReadAllText( Path.Combine( checkout, "research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision/invalid-stream-fixtures.json" ) ) );
		var result = new List<Result>();
		foreach ( var property in doc.RootElement.EnumerateObject() )
		{
			try { global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = Convert.FromBase64String( property.Value.GetString() ) }, false ); result.Add( new( property.Name, false, "Invalid stream was accepted." ) ); }
			catch ( GltfException ) { result.Add( new( property.Name, true, null ) ); }
			catch ( Exception e ) { result.Add( new( property.Name, false, e.ToString() ) ); }
		}
		return result;
	}
}
