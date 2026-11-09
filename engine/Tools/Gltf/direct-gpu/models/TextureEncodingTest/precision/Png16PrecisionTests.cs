using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using NativeTextureReference = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.BoxTexturedReference;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

/// <summary>Independent literal source samples: adjacent 16-bit values share their high byte.</summary>
internal static class Png16PrecisionTests
{
	private static readonly (string Name, string Png, ushort[] Samples)[] Fixtures =
	[
		("rgba", "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABEAYAAACksqPJAAAAGUlEQVR4nGNgZGRkamA0sGRkYmRsYLpiCAAORAJ8hmWiZwAAAABJRU5ErkJggg==", [257,258,32769,12345, 258,257,32770,54321]),
		("rgb", "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABEAIAAAAr0DSeAAAAFUlEQVR4nGNgZGRkamBkZGJkbGACAAVgAQ7lpF0YAAAAAElFTkSuQmCC", [257,258,32769,65535, 258,257,32770,65535]),
		("gray", "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABEAAAAACB2fwVAAAADUlEQVR4nGNgZGRkAgAAEAAGQV5rlgAAAABJRU5ErkJggg==", [257,257,257,65535, 258,258,258,65535]),
		("gray-alpha", "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABEAQAAAAOu2tCAAAAEUlEQVR4nGNgZDSwZGS6YggABDgBdDI++mgAAAAASUVORK5CYII=", [257,257,257,12345, 258,258,258,54321])
	];
	public sealed record Result( string Name, bool Passed, string Error );
	public static List<Result> Run()
	{
		var results = new List<Result>();
		foreach ( var fixture in Fixtures )
		{
			RunCase( fixture.Name + "_core_linear_data", () => CheckBase( fixture.Png, fixture.Samples, false ) );
		}
		RunCase( "rgba_metallic_roughness_preserves_subbyte_values", CheckMetallicRoughness );
		return results;
		void RunCase( string name, Action test ) { try { test(); results.Add( new( name, true, null ) ); } catch ( Exception e ) { results.Add( new( name, false, e.ToString() ) ); } }
	}
	private static GltfImage Image( string png ) => new() { MimeType = "image/png", Bytes = Convert.FromBase64String( png ) };
	public static List<Result> RunPrototype()
	{
		var results = new List<Result>();
		foreach ( var fixture in Fixtures )
		{
			var name = fixture.Name + "_linear";
			try { CheckBase( fixture.Png, fixture.Samples, true ); results.Add( new( name, true, null ) ); }
			catch ( Exception e ) { results.Add( new( name, false, e.ToString() ) ); }
		}
		return results;
	}
	public static List<Result> RunPackedPrototype()
	{
		var rows = new List<Result>();
		RunCase( "linear_base_and_mip", () =>
		{
			var texture = NativeTextureReference.ParseNativeTextureResource( Png16Prototype.Compile( Convert.FromBase64String( Fixtures[0].Png ), true ) );
			Require( texture.Format == 14 && texture.MipCount == 2 && texture.Payload.Length == 48, "linear float mip layout" );
			for ( var c = 0; c < 4; c++ )
			{
				var expected = (Fixtures[0].Samples[c] + Fixtures[0].Samples[c + 4]) / 131070.0;
				Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( texture.Payload.AsSpan( c * 4 ) ) - expected ) < .0000001, "linear mip mean" );
			}
		} );
		RunCase( "packed_mr_preserves_subbyte_and_factor_mip", () =>
		{
			var packed = Png16Prototype.CompileMetallicRoughness( Convert.FromBase64String( Fixtures[0].Png ), .75f, .5f, true );
			var rough = NativeTextureReference.ParseNativeTextureResource( packed.NormalRoughness );
			var metal = NativeTextureReference.ParseNativeTextureResource( packed.Metalness );
			Require( rough.Format == 14 && metal.Format == 14 && rough.MipCount == 2 && metal.MipCount == 2, "packed format/mips" );
			for ( int p = 0; p < 2; p++ )
			{
				Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( rough.Payload.AsSpan( 16 + p * 16 + 8 ) ) - Fixtures[0].Samples[p * 4 + 1] / 65535.0 * .5 ) < .0000001, "roughness base" );
				Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( metal.Payload.AsSpan( 16 + p * 16 + 4 ) ) - Fixtures[0].Samples[p * 4 + 2] / 65535.0 * .75 ) < .0000001, "metallic base" );
			}
			Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( rough.Payload.AsSpan( 8 ) ) - (258 + 257) / 131070.0 * .5 ) < .0000001, "roughness mip" );
			Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( metal.Payload.AsSpan( 4 ) ) - (32769 + 32770) / 131070.0 * .75 ) < .0000001, "metallic mip" );
		} );
		return rows;
		void RunCase( string name, Action test ) { try { test(); rows.Add( new( name, true, null ) ); } catch ( Exception e ) { rows.Add( new( name, false, e.ToString() ) ); } }
	}
	public static List<Result> RunProducerMip()
	{
		var rows = new List<Result>();
		RunCase( "producer_linear_mip_precision", () =>
		{
			var texture = NativeTextureReference.ParseNativeTextureResource( global::Sandbox.GltfTextureCompiler.CompileWithMipmaps( Image( Fixtures[0].Png ), false ) );
			Require( texture.Format == 14 && texture.MipCount == 2 && texture.Payload.Length == 48, "linear mip layout" );
			for ( var c = 0; c < 4; c++ ) Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( texture.Payload.AsSpan( c * 4 ) ) - (Fixtures[0].Samples[c] + Fixtures[0].Samples[c + 4]) / 131070.0 ) < .0000001, "linear mip mean" );
		} );
		RunCase( "producer_mr_mips_and_factors", () =>
		{
			var packed = global::Sandbox.GltfTextureCompiler.CompileMetallicRoughness( Image( Fixtures[0].Png ), .75f, .5f, true );
			var rough = NativeTextureReference.ParseNativeTextureResource( packed.NormalRoughness ); var metal = NativeTextureReference.ParseNativeTextureResource( packed.Metalness );
			Require( rough.Format == 14 && metal.Format == 14 && rough.MipCount == 2 && metal.MipCount == 2, "packed format/mips" );
			for ( int p = 0; p < 2; p++ )
			{
				Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( rough.Payload.AsSpan( 16 + p * 16 + 8 ) ) - Fixtures[0].Samples[p * 4 + 1] / 65535.0 * .5 ) < .0000001, "roughness base" );
				Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( metal.Payload.AsSpan( 16 + p * 16 + 4 ) ) - Fixtures[0].Samples[p * 4 + 2] / 65535.0 * .75 ) < .0000001, "metallic base" );
			}
			Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( rough.Payload.AsSpan( 8 ) ) - (258 + 257) / 131070.0 * .5 ) < .0000001, "roughness mip" );
			Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( metal.Payload.AsSpan( 4 ) ) - (32769 + 32770) / 131070.0 * .75 ) < .0000001, "metallic mip" );
		} );
		return rows;
		void RunCase( string name, Action test ) { try { test(); rows.Add( new( name, true, null ) ); } catch ( Exception e ) { rows.Add( new( name, false, e.ToString() ) ); } }
	}
	private static void CheckBase( string png, ushort[] expected, bool prototype )
	{
		var texture = NativeTextureReference.ParseNativeTextureResource( prototype ? Png16Prototype.Compile( Convert.FromBase64String( png ) ) : global::Sandbox.GltfTextureCompiler.Compile( Image( png ), false ) );
		Require( texture.Width == 2 && texture.Height == 1 && texture.MipCount == 1, "dimensions/mips" );
		// Both representations are acceptable only with linear sampling.
		if ( texture.Format == 7 )
		{
			Require( texture.Payload.Length == 16, "RGBA16 payload length" );
			Require( (texture.Flags & 0x0200) != 0, "16-bit resource transfer flag" );
			for ( var i = 0; i < 8; i++ ) Require( BinaryPrimitives.ReadUInt16LittleEndian( texture.Payload.AsSpan( i * 2 ) ) == expected[i], $"channel {i} loses authored bits" );
		}
		else if ( texture.Format == 14 )
		{
			Require( (texture.Flags & 0x0200) != 0 && texture.Payload.Length == 32, "float storage must be linear" );
			for ( var i = 0; i < 8; i++ )
			{
				var v = expected[i] / 65535.0;
				Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( texture.Payload.AsSpan( i * 4 ) ) - v ) < 0.0000001, $"channel {i} transfer or precision" );
			}
		}
		else throw new Exception( $"Unexpected texture format {texture.Format}; RGBA8 quantizes the source." );
	}
	private static void CheckMetallicRoughness()
	{
		var resource = global::Sandbox.GltfTextureCompiler.CompileMetallicRoughness( Image( Fixtures[0].Png ), .75f, .5f, false );
		var rough = NativeTextureReference.ParseNativeTextureResource( resource.NormalRoughness );
		var metal = NativeTextureReference.ParseNativeTextureResource( resource.Metalness );
		Require( rough.Format == 14 && metal.Format == 14, "material payload format" );
		for ( var p = 0; p < 2; p++ )
		{
			var g = Fixtures[0].Samples[p * 4 + 1] / 65535.0 * .5;
			var b = Fixtures[0].Samples[p * 4 + 2] / 65535.0 * .75;
			Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( rough.Payload.AsSpan( p * 16 + 8 ) ) - g ) < 0.0000001, "roughness precision" );
			Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( metal.Payload.AsSpan( p * 16 + 4 ) ) - b ) < 0.0000001, "metallic precision" );
		}
	}
	private static void Require( bool condition, string message ) { if ( !condition ) throw new Exception( message ); }
}
