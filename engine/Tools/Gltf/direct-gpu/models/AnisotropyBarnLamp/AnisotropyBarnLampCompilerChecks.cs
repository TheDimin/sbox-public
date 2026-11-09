using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Sandbox.Gltf.DirectGpu.AnisotropyBarnLampValidation;

/// <summary>Actual compiler routes; resource metadata and bounded rejection, not image parity.</summary>
public static class AnisotropyBarnLampCompilerChecks
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );

	public static Result[] RunAll( string checkoutRoot )
	{
		var results = new List<Result>();
		var directory = Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/AnisotropyBarnLamp/glTF-KTX-BasisU" );
		var source = File.ReadAllBytes( Path.Combine( directory, "AnisotropyBarnLamp.gltf" ) );
		var extensions = new HashSet<string>( StringComparer.Ordinal ) { "KHR_texture_basisu" };
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( directory, uri ) );
		foreach ( var compiled in new[] { false, true } )
		{
			var bytes = compiled ? GltfCompiler.Compile( source, Resolve, extensions ) : source;
			var form = compiled ? "glTF-KTX-BasisU/compiled-glb_c" : "glTF-KTX-BasisU/source";
			for ( var image = 0; image < 4; image++ )
			{
				var index = image;
				Run( "compiler_ktx_rgba8_role_" + index, form, () =>
				{
					using var document = GltfReader.Read( bytes, Resolve, extensions );
					var resource = global::Sandbox.GltfTextureCompiler.CompileWithMipmaps( document.ReadImage( index ), index == 2 );
					// Literal Source 2 framing: 16-byte resource header, one 12-byte
					// DATA entry, 32-byte VTEX header, 8-byte empty extra-data array.
					Require( resource.Length == 22369620 + 68 && BinaryPrimitives.ReadUInt32LittleEndian( resource ) == 68, "Unexpected native RGBA8 chain/framing size." );
					Require( BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( 48 ) ) == 2048 && BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( 50 ) ) == 2048, "Compiler changed source dimensions." );
					Require( resource[54] == 4 && resource[55] == 12, "Compiler did not emit RGBA8 full mip chain." );
					Require( (BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( 30 ) ) & 0x0208) == (index == 2 ? 0 : 0x0200), "Compiler changed linear/sRGB storage or suppressed mip sampling." );
					return 4;
				} );
			}
			Run( "legacy_float_orm_keeps_configured_256mib_rejection", form, () =>
			{
				using var document = GltfReader.Read( bytes, Resolve, extensions );
				try { _ = global::Sandbox.GltfTextureCompiler.CompileMetallicRoughness( document.ReadImage( 1 ), 1, 1, true ); }
				catch ( GltfException error ) when ( error.Message.Contains( "working set", StringComparison.Ordinal ) && error.Message.Contains( "budget", StringComparison.Ordinal ) ) { return 1; }
				throw new InvalidOperationException( "Oversized legacy ORM float adapter was admitted or failed for a different reason." );
			} );
		}
		return results.ToArray();
		void Run( string name, string form, Func<int> body )
		{
			try { results.Add( new( name, form, body(), true, null ) ); }
			catch ( Exception error ) { results.Add( new( name, form, 0, false, error.ToString() ) ); }
		}
	}

	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
