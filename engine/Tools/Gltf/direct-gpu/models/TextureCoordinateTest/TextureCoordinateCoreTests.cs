using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.TextureCoordinateTestValidation;

/// <summary>Independent source/layout gates. These do not claim actual GPU or texture sampling acceptance.</summary>
public static class TextureCoordinateCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = TextureCoordinateReference.Open( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms() )
		{
			Run( "all_accessors_offsets_strides_formats_and_values", form, () => CheckStreams( form ) );
			Run( "effective_per_texture_coordinate_and_identity_transform", form, () => CheckMaterials( form, reference ) );
			Run( "upload_plan_preserves_raw_source_bytes", form, () => CheckPlan( form ) );
			Run( "authored_static_world_geometry_and_owned_lifetime", form, () => CheckWorldAndLifetime( form ) );
			Run( "portable_package_and_original_input_immutability", form, () => CheckPackaging( form, reference ) );
		}
		return results.ToArray();
		void Run( string name, TextureCoordinateReference.Form form, Func<int> body )
		{
			try { results.Add( new( name, form.Name, body(), true, null ) ); }
			catch ( Exception error ) { results.Add( new( name, form.Name, 0, false, error.ToString() ) ); }
		}
	}

	private static int CheckStreams( TextureCoordinateReference.Form form )
	{
		using var document = form.Read(); var count = 0;
		foreach ( var primitive in form.Expected )
		{
			var upload = document.MaterializePrimitiveUpload( primitive.Mesh, 0 );
			Require( upload.Attributes.Keys.Order().SequenceEqual( primitive.Streams.Keys.Order() ), "A source semantic was lost or invented." ); count++;
			Require( upload.Layout.VertexCount == 4 && upload.Layout.SourceMode == 4 && !upload.Layout.RequiresTopologyExpansion, "Pinned primitive shape changed." ); count++;
			foreach ( var (semantic, expected) in primitive.Streams ) { Check( upload.Attributes[semantic], expected, semantic ); count += 4 + expected.Count * expected.Components; }
			Check( upload.Indices, primitive.Indices, "indices" ); count += 4 + primitive.Indices.Count;
		}
		return count;
		static void Check( GltfUploadStream actual, TextureCoordinateReference.Stream expected, string label )
		{
			Require( actual.Count == expected.Count && actual.ComponentType == expected.ComponentType && actual.Components == expected.Components && actual.Normalized == expected.Normalized, label + " source component format changed." );
			Require( actual.ByteOffset == expected.AccessorOffset && actual.ByteStride == expected.Stride && !actual.IsMaterialized, label + " accessor offset/stride changed." );
			Require( actual.Binding.AccessorIndex == expected.Accessor && actual.Binding.SourceBufferView == expected.View && actual.Binding.ViewByteOffset == expected.ViewOffset && actual.Binding.ViewByteLength == expected.ViewLength, label + " source provenance changed." );
			Require( actual.Storage.Bytes.Length == expected.ViewLength, "Upload must own exactly this source view." );
			for ( var vertex = 0; vertex < expected.Count; vertex++ ) for ( var component = 0; component < expected.Components; component++ )
			{
				var value = TextureCoordinateReference.ReadComponent( actual, vertex, component );
				Require( value == (float)expected.Values[vertex][component], $"{label}[{vertex},{component}]: expected {expected.Values[vertex][component]:R}, actual {value:R}." );
			}
		}
	}

	private static int CheckMaterials( TextureCoordinateReference.Form form, TextureCoordinateReference reference )
	{
		using var document = form.Read(); var count = 0;
		foreach ( var primitive in form.Expected )
		{
			var material = document.ReadMaterial( primitive.Material );
			Require( material.DoubleSided && material.MetallicFactor == 0 && material.RoughnessFactor == 1 && material.AlphaMode == "OPAQUE", "Pinned core material factors changed." ); count++;
			var expectedTint = primitive.Material switch
			{
				0 => new System.Numerics.Vector4( .16000001668930075f, .16000001668930075f, .16000001668930075f, 1 ),
				1 => new System.Numerics.Vector4( 0, .16000000476837162f, .800000011920929f, 1 ),
				2 => new System.Numerics.Vector4( 0, .800000011920929f, 0, 1 ),
				3 => new System.Numerics.Vector4( .800000011920929f, .800000011920929f, 0, 1 ),
				4 => new System.Numerics.Vector4( .800000011920929f, .08000000238418581f, 0, 1 ),
				_ => throw new InvalidOperationException( "Unknown source material." )
			};
			Require( material.BaseColorFactor == expectedTint && !material.Unlit, "Authored quadrant color or lighting mode changed." ); count++;
			Require( material.MetallicRoughnessTexture is null && material.NormalTexture is null && material.OcclusionTexture is null && material.EmissiveTexture is null, "Source has only a base-color role." ); count++;
			if ( primitive.Mesh == 3 ) { Require( material.BaseColorTexture is null, "Untextured back plane must not demand a UV accessor." ); count++; continue; }
			var info = material.BaseColorTexture;
			Require( info is not null && info.Index == 0 && info.TexCoord == (form.Derived ? 1 : 0) && info.IsSrgb, "Effective base-color UV selection changed." ); count++;
			Require( info.Offset == System.Numerics.Vector2.Zero && info.Tiling == System.Numerics.Vector2.One && info.Rotation == 0, "Source has no authored texture transform." ); count++;
			foreach ( var uv in primitive.Streams["TEXCOORD_" + info.TexCoord].Values )
			{
				var value = new System.Numerics.Vector2( (float)uv[0], (float)uv[1] ); Require( info.TransformUv( value ) == value, "Identity texture mapping must preserve source UVs." ); count++;
			}
		}
		var texture = document.ReadTexture( 0 ); Require( texture.Image == 0 && texture.Sampler == 0, "Texture source/sampler link changed." ); count++;
		var sampler = document.ReadSampler( 0 ); Require( sampler.MinFilter is null && sampler.MagFilter is null && sampler.WrapS == 10497 && sampler.WrapT == 10497, "Undefined filters must stay undefined; repeat is the specified default." ); count++;
		Require( document.ReadImage( 0 ).Bytes.AsSpan().SequenceEqual( reference.PngBytes ), "Encoded image bytes changed." ); count++;
		return count;
	}

	private static int CheckPlan( TextureCoordinateReference.Form form )
	{
		using var document = form.Read(); var assertions = 0;
		foreach ( var expected in form.Expected )
		{
			var plan = GltfGpuPrimitivePlan.Create( document, expected.Mesh, 0 );
			Require( plan.Mode == 4 && plan.Material == expected.Material && !plan.GenerateFlatNormals && plan.SourceVertices.SequenceEqual( new uint[] { 0, 1, 2, 3 } ), "Plan changed topology or source vertex mapping." ); assertions++;
			foreach ( var (name, binding) in plan.Sources )
			{
				var stream = binding.Stream; var start = binding.ArenaByteOffset - stream.ByteOffset;
				Require( start >= 0 && plan.InputBytes.AsSpan( start, stream.Storage.Bytes.Length ).SequenceEqual( stream.Storage.Bytes.Span ), "GPU arena changed immutable source view bytes." ); assertions++;
				Require( stream.ByteStride == expected.Streams[name].Stride && stream.ByteOffset == expected.Streams[name].AccessorOffset, "Plan canonicalized source layout." ); assertions++;
			}
			foreach ( var semantic in expected.Streams.Keys.Where( value => value.StartsWith( "TEXCOORD_", StringComparison.Ordinal ) ) )
			{
				var index = int.Parse( semantic[9..] ); var field = plan.Fields.Single( value => value.Semantic == "TEXCOORD" && value.SemanticIndex == index );
				Require( field.Components == 2 && field.ComponentType == 5126 && field.Offset >= 0 && field.Offset + 8 <= plan.VertexStride, "Missing explicit runtime output declaration for source UV set." ); assertions++;
			}
		}
		return assertions;
	}

	private static int CheckWorldAndLifetime( TextureCoordinateReference.Form form )
	{
		var uploads = new List<(TextureCoordinateReference.Primitive Expected, GltfPrimitiveUploadData Upload)>(); var assertions = 0;
		using ( var document = form.Read() )
		{
			var pose = document.EvaluatePose();
			foreach ( var primitive in form.Expected )
			{
				for ( var vertex = 0; vertex < 4; vertex++ )
				{
					var raw = primitive.Streams["POSITION"].Values[vertex]; var expected = new System.Numerics.Vector3( (float)raw[0], (float)raw[1], (float)raw[2] );
					Require( document.EvaluateVertexPosition( primitive.Node, 0, vertex, pose ) == expected, "Static node transform changed authored geometry." ); assertions++;
				}
				uploads.Add( (primitive, document.MaterializePrimitiveUpload( primitive.Mesh, 0 )) );
			}
		}
		foreach ( var (expected, upload) in uploads ) foreach ( var (semantic, stream) in upload.Attributes ) for ( var vertex = 0; vertex < 4; vertex++ )
		{
			Require( TextureCoordinateReference.ReadComponent( stream, vertex, 0 ) == (float)expected.Streams[semantic].Values[vertex][0], "Owned source bytes did not survive document disposal." ); assertions++;
		}
		return assertions;
	}

	private static int CheckPackaging( TextureCoordinateReference.Form form, TextureCoordinateReference reference )
	{
		var observed = new List<string>();
		using var document = GltfReader.Read( form.Bytes, uri => { observed.Add( uri ); return reference.Resolve( uri ); } );
		foreach ( var primitive in form.Expected ) _ = document.MaterializePrimitiveUpload( primitive.Mesh, 0 );
		Require( document.ReadImage( 0 ).Bytes.AsSpan().SequenceEqual( reference.PngBytes ), "Image not preserved through portable packaging." );
		Require( observed.All( uri => uri is "TextureCoordinateTest.bin" or "TextureCoordinateTemplate.png" ), "Unexpected external dependency." );
		if ( form.Compiled ) Require( BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 8 ) ) == form.Bytes.Length && !observed.Contains( "TextureCoordinateTest.bin" ), "Compiled form must be a valid GLB with packaged geometry." );
		reference.RequireOriginalFilesUnchanged();
		return 4;
	}

	internal static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
