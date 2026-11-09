using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.AnisotropyBarnLampValidation;

/// <summary>
/// Pinned independent logical-stream hashes and image/container metadata. Native
/// codec/resource probes are CPU integration evidence; none establish GPU pixels.
/// </summary>
public static class AnisotropyBarnLampTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );
	private static readonly HashSet<string> Extensions = new( StringComparer.Ordinal ) { "KHR_texture_basisu" };
	private static readonly string[] Optional = ["KHR_materials_anisotropy", "KHR_materials_clearcoat", "KHR_materials_emissive_strength", "KHR_materials_transmission", "KHR_materials_volume"];

	public static Result[] RunAll( string checkoutRoot )
	{
		var results = new List<Result>();
		using var audit = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( checkoutRoot, "research/gltf-validation/direct-gpu/models/AnisotropyBarnLamp/source-audit.json" ) ) );
		foreach ( var variant in audit.RootElement.GetProperty( "variants" ).EnumerateArray() )
		{
			var name = variant.GetProperty( "variant" ).GetString();
			var directory = Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/AnisotropyBarnLamp", name );
			var source = File.ReadAllBytes( Path.Combine( directory, variant.GetProperty( "file" ).GetString() ) );
			Run( "source_identity", name, () => { Require( Hash( source ) == variant.GetProperty( "sha256" ).GetString(), "Pinned source changed." ); return 1; } );
			foreach ( var compiled in new[] { false, true } )
			{
				var form = name + (compiled ? "/compiled-glb_c" : "/source");
				var resolved = new List<string>();
				byte[] Resolve( string uri ) { resolved.Add( uri ); Require( Path.GetFileName( uri ) == uri && !uri.Contains( ':' ), "Unexpected nonportable dependency." ); return File.ReadAllBytes( Path.Combine( directory, uri ) ); }
				byte[] bytes = null;
				Run( "portable_packaging", form, () =>
				{
					bytes = compiled ? GltfCompiler.Compile( source, Resolve, Extensions ) : source;
					if ( compiled ) Require( BinaryPrimitives.ReadUInt32LittleEndian( bytes ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( 8 ) ) == bytes.Length, "Compiled framing changed." );
					return 1;
				} );
				if ( bytes is null )
				{
					foreach ( var row in new[] { "logical_streams", "core_material_fallbacks", "image_bytes_and_dependencies" } ) results.Add( new( row, form, 0, false, "Packaging prerequisite failed." ) );
					if ( name == "glTF-KTX-BasisU" ) for ( var index = 0; index < 4; index++ ) foreach ( var row in new[] { "basis_metadata_", "basis_native_resource_" } ) results.Add( new( row + index, form, 0, false, "Packaging prerequisite failed." ) );
					continue;
				}
				GltfDocument Open() => GltfReader.Read( bytes, Resolve, Extensions );
				Run( "logical_streams", form, () => CheckGeometry( Open, variant ) );
				Run( "core_material_fallbacks", form, () => CheckMaterials( Open ) );
				Run( "image_bytes_and_dependencies", form, () =>
				{
					resolved.Clear(); using var document = Open(); var index = 0;
					foreach ( var image in variant.GetProperty( "images" ).EnumerateArray() )
					{
						var actual = document.ReadImage( index++ );
						Require( actual.MimeType == image.GetProperty( "mimeType" ).GetString() && Hash( actual.Bytes ) == image.GetProperty( "sha256" ).GetString(), "Image source bytes or MIME were changed." );
					}
					if ( compiled ) Require( !resolved.Any( x => x.EndsWith( ".bin", StringComparison.Ordinal ) ), "Compiled geometry still resolves source .bin." );
					Require( name == "glTF-Binary" ? resolved.Count == 0 : resolved.Count( x => !x.EndsWith( ".bin", StringComparison.Ordinal ) ) == 4, "Embedded images or explicit relative image dependencies changed." );
					return 6;
				} );
				if ( name != "glTF-KTX-BasisU" ) continue;
				for ( var image = 0; image < 4; image++ )
				{
					var index = image;
					Run( "basis_metadata_" + index, form, () =>
					{
						using var document = Open(); var encoded = document.ReadImage( index ); var expected = variant.GetProperty( "images" )[index].GetProperty( "ktx" );
						var plan = global::Sandbox.GltfBasisTexturePlan.Create( encoded, index == 2, true );
						Require( plan.Width == 2048 && plan.Height == 2048 && plan.Levels.Count == 1 && plan.OutputLevelCount == 12, "Authored base mip or generated tail count changed." );
						Require( plan.SourceCodec.ToString() == "Uastc" && plan.TargetCodec.ToString() == "Rgba32" && plan.SupercompressionScheme == 2, "Codec selection changed." );
						Require( plan.ColorPrimaries == 1 && plan.IsSrgb == (index == 2) && plan.HasNonColorPrimaries == (index != 2), "Source DFD role/primaries diagnostic changed." );
						Require( Hash( plan.DataFormatDescriptor.Span ) == expected.GetProperty( "dfdSha256" ).GetString() && Hash( plan.KeyValueData.Span ) == expected.GetProperty( "kvdSha256" ).GetString(), "DFD or orientation/writer metadata changed." );
						Require( plan.Orientation == "rd" && plan.Swizzle == "rgba" && Hash( plan.EncodedSource.Span ) == Hash( encoded.Bytes ), "Source payload or origin changed." );
						return 5;
					} );
					Run( "basis_native_resource_" + index, form, () =>
					{
						using var document = Open(); var plan = global::Sandbox.GltfBasisTexturePlan.Create( document.ReadImage( index ), index == 2, true );
						var resource = plan.Compile(); ValidateNativeResource( resource, index == 2 ); return 5;
					} );
				}
			}
		}
		return results.ToArray();
		void Run( string name, string form, Func<int> body )
		{
			try { results.Add( new( name, form, body(), true, null ) ); }
			catch ( Exception error ) { results.Add( new( name, form, 0, false, error.ToString() ) ); }
		}
	}

	private static int CheckGeometry( Func<GltfDocument> open, JsonElement expected )
	{
		using var document = open(); var assertions = 0; var mesh = 0;
		foreach ( var primitive in expected.GetProperty( "primitives" ).EnumerateArray() )
		{
			var upload = document.MaterializePrimitiveUpload( mesh++, 0 );
			foreach ( var stream in primitive.GetProperty( "streams" ).EnumerateObject() )
			{
				var actual = stream.Name == "INDICES" ? upload.Indices : upload.Attributes[stream.Name];
				Require( actual.Count == stream.Value.GetProperty( "count" ).GetInt32() && actual.Components == stream.Value.GetProperty( "components" ).GetInt32() && actual.ComponentType == stream.Value.GetProperty( "componentType" ).GetInt32() && !actual.Normalized, "Source stream shape/type changed." );
				var size = actual.Components * (actual.ComponentType == 5123 ? 2 : 4); var logical = new byte[actual.Count * size];
				for ( var vertex = 0; vertex < actual.Count; vertex++ ) actual.Storage.Bytes.Span.Slice( actual.ByteOffset + vertex * actual.ByteStride, size ).CopyTo( logical.AsSpan( vertex * size ) );
				Require( Hash( logical ) == stream.Value.GetProperty( "sha256" ).GetString(), "Logical stream differs from independent source byte oracle: " + stream.Name ); assertions += 2;
			}
		}
		return assertions;
	}

	private static int CheckMaterials( Func<GltfDocument> open )
	{
		using var document = open();
		Require( document.UnsupportedOptionalExtensions.OrderBy( x => x, StringComparer.Ordinal ).SequenceEqual( Optional.OrderBy( x => x, StringComparer.Ordinal ) ), "Optional effects must remain reported as standard core fallbacks." );
		var metal = document.ReadMaterial( 0 ); var filament = document.ReadMaterial( 1 ); var glass = document.ReadMaterial( 2 );
		Require( metal.BaseColorTexture.Index == 2 && metal.MetallicRoughnessTexture.Index == 1 && metal.NormalTexture.Index == 0 && metal.OcclusionTexture.Index == 1, "Core metal material links changed." );
		Require( metal.BaseColorTexture.IsSrgb && !metal.MetallicRoughnessTexture.IsSrgb && !metal.NormalTexture.IsSrgb && metal.MetallicFactor == 1 && metal.RoughnessFactor == 1, "Core texture transfer/factors changed." );
		Require( filament.EmissiveFactor == new System.Numerics.Vector3( 1, .5f, .25f ) && filament.MetallicFactor == 0 && filament.RoughnessFactor == .7f, "Optional emissive strength must fall back to core emission." );
		Require( glass.MetallicFactor == 0 && glass.RoughnessFactor == 0 && glass.AlphaMode == "OPAQUE", "Optional transmission/volume must fall back to opaque core material." );
		for ( var i = 0; i < 4; i++ ) { var texture = document.ReadTexture( i ); Require( texture.Image == i && texture.Sampler == 0, "Resolved Basis/core image selection changed." ); }
		var sampler = document.ReadSampler( 0 ); Require( sampler.MinFilter == 9987 && sampler.MagFilter == 9729 && sampler.WrapS == 10497 && sampler.WrapT == 10497, "Trilinear repeat sampler changed." );
		return 10;
	}

	private static void ValidateNativeResource( byte[] resource, bool srgb )
	{
		Require( resource.Length > 28 && BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( 4 ) ) == 12, "Invalid native resource framing." );
		var payload = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( resource )); var array = checked(8 + BinaryPrimitives.ReadInt32LittleEndian( resource.AsSpan( 8 ) )); var count = BinaryPrimitives.ReadUInt32LittleEndian( resource.AsSpan( 12 ) );
		Require( array >= 16 && count is > 0 and <= 32 && (long)array + 12L * count <= payload && payload <= resource.Length, "Invalid native resource table." );
		for ( var i = 0; i < count; i++ )
		{
			var at = checked(array + (int)i * 12); if ( BinaryPrimitives.ReadUInt32LittleEndian( resource.AsSpan( at ) ) != 0x41544144 ) continue;
			var data = checked(at + 4 + BinaryPrimitives.ReadInt32LittleEndian( resource.AsSpan( at + 4 ) ));
			Require( data >= 0 && data + 32 <= payload && BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( data + 20 ) ) == 2048 && BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( data + 22 ) ) == 2048, "Native texture dimensions changed." );
			Require( resource[data + 26] == 4 && resource[data + 27] == 12 && (BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( data + 2 ) ) & 0x0208) == (srgb ? 0 : 0x0200), "Native RGBA/mip/transfer flags changed." );
			Require( resource.Length - payload == 22369620, "Native complete mip payload length changed." ); return;
		}
		throw new InvalidOperationException( "Native texture has no DATA block." );
	}

	private static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
