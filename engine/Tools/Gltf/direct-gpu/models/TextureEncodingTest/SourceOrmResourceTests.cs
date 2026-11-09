using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

using Native = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.BoxTexturedReference;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using static TextureEncodingReference;

/// <summary>
/// Independent direct-source expectations written before adapter integration.
/// Invokes only the actual loaded Engine's candidate preparation boundary. A
/// missing boundary remains a failed case; replacement producer code is forbidden.
/// </summary>
public static class SourceOrmResourceTests
{
	private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
	public sealed record Result( string Name, bool Passed, string Error );
	public static Result[] RunAll()
	{
		var rows = new List<Result>();
		Run( "one_image_has_distinct_color_data_transfer_and_role_deduplication", CheckSharedImage );
		Run( "independent_role_samplers_do_not_duplicate_matching_encoded_resources", CheckRoleSamplers );
		Run( "mip_demand_is_part_of_resource_identity", CheckMipDemand );
		Run( "authored_linear_factors_are_not_baked_into_source_pixels", CheckFactors );
		return rows.ToArray();
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, false, error.ToString() ) ); } }
	}

	private static void CheckSharedImage()
	{
		using var doc = Fixture(); var prepared = Prepare( doc );
		var color = Resource( prepared, "BaseColor" ); var emissive = Resource( prepared, "Emissive" ); var mr = Resource( prepared, "MetallicRoughness" ); var ao = Resource( prepared, "Occlusion" );
		Require( ReferenceEquals( color, emissive ) && ReferenceEquals( mr, ao ) && !ReferenceEquals( color, mr ), "CPU preparation must deduplicate matching image/transfer/mip requests while preserving color/data distinction." );
		var colorPayload = Native.ParseNativeTextureResource( color ); var dataPayload = Native.ParseNativeTextureResource( mr );
		_ = StraightAlphaFixture( 6, out var literal );
		Require( colorPayload.Format == 4 && dataPayload.Format == 4 && colorPayload.Payload.AsSpan().SequenceEqual( literal ) && dataPayload.Payload.AsSpan().SequenceEqual( literal ), "Direct source textures must keep original RGBA8 components; RGBA32F packed normal/metalness is not source ORM." );
		Require( (colorPayload.Flags & 0x0200) == 0 && (dataPayload.Flags & 0x0200) != 0, "One image's color and data resource flags must differ independently of its gamma metadata." );
	}

	private static void CheckRoleSamplers()
	{
		using var doc = Fixture( aoSampler: 1 ); var prepared = Prepare( doc );
		var mr = Slot( prepared, "MetallicRoughness" ); var ao = Slot( prepared, "Occlusion" );
		Require( ReferenceEquals( Property<byte[]>( mr, "Resource" ), Property<byte[]>( ao, "Resource" ) ), "Sampler addressing alone must not duplicate identical image/transfer/mip payloads." );
		var mrSampling = Property<global::Sandbox.GltfSamplerMapping.Result>( mr, "Sampling" ); var aoSampling = Property<global::Sandbox.GltfSamplerMapping.Result>( ao, "Sampling" );
		Require( mrSampling.State.AddressModeU == global::Sandbox.Rendering.TextureAddressMode.Wrap && aoSampling.State.AddressModeU == global::Sandbox.Rendering.TextureAddressMode.Clamp && mrSampling.State != aoSampling.State, "Independent authored role samplers were silently merged." );
	}

	private static void CheckMipDemand()
	{
		using var doc = Fixture( aoSampler: 2 ); var prepared = Prepare( doc ); var mr = Resource( prepared, "MetallicRoughness" ); var ao = Resource( prepared, "Occlusion" );
		Require( !ReferenceEquals( mr, ao ), "Base-only and complete-mip resources must not be conflated by the preparation cache." );
		var mrPayload = Native.ParseNativeTextureResource( mr ); var aoPayload = Native.ParseNativeTextureResource( ao );
		_ = StraightAlphaFixture( 6, out var literal );
		Require( mrPayload.MipCount == 1 && mrPayload.Payload.AsSpan().SequenceEqual( literal ), "Authored nonmip minification must remain base-only." );
		byte[] mip = [90, 52, 168, 100]; // Literal linear means (89.5,52,168.5,100), nearest-even RGBA8 quantization.
		Require( aoPayload.Format == 4 && aoPayload.MipCount == 2 && aoPayload.Payload.AsSpan( 0, 4 ).SequenceEqual( mip ) && aoPayload.Payload.AsSpan( 4 ).SequenceEqual( literal ), "Linear data mip values/ordering differ from independent literal means." );
	}

	private static void CheckFactors()
	{
		using var first = Fixture( factor: .001f ); using var second = Fixture( factor: .7f ); var a = Prepare( first ); var b = Prepare( second );
		Require( Property<NVector4>( a, "LinearBaseColorFactor" ) == new NVector4( .2f, .3f, .4f, .5f ) && Property<NVector3>( a, "EmissiveFactor" ) == new NVector3( .25f, .5f, .75f ), "Source authored color/emissive factors were encoded or replaced." );
		Require( Property<float>( a, "MetallicFactor" ) == .001f && Property<float>( a, "RoughnessFactor" ) == .001f && Property<float>( a, "OcclusionStrength" ) == .6f, "Sub-byte source scalar factors must remain exact shader inputs." );
		foreach ( var role in new[] { "BaseColor", "MetallicRoughness", "Occlusion", "Emissive" } ) Require( Resource( a, role ).AsSpan().SequenceEqual( Resource( b, role ) ), "Changing factors must not rebake the source texture or multiply channels twice: " + role );
	}

	private static GltfDocument Fixture( int aoSampler = 0, float factor = 1 )
	{
		var png = StraightAlphaFixture( 6, out _ );
		var root = JsonNode.Parse( """
		{"asset":{"version":"2.0"},"images":[{"uri":"literal.png"}],"samplers":[{"minFilter":9729,"magFilter":9729},{"minFilter":9729,"magFilter":9729,"wrapS":33071},{"minFilter":9987,"magFilter":9729}],"textures":[{"source":0,"sampler":0},{"source":0,"sampler":0}],"materials":[{"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.3,0.4,0.5],"baseColorTexture":{"index":0},"metallicRoughnessTexture":{"index":0}},"occlusionTexture":{"index":1,"strength":0.6},"emissiveTexture":{"index":0},"emissiveFactor":[0.25,0.5,0.75]}]}
		""" )!.AsObject();
		root["textures"]![1]!["sampler"] = aoSampler; root["materials"]![0]!["pbrMetallicRoughness"]!["metallicFactor"] = factor; root["materials"]![0]!["pbrMetallicRoughness"]!["roughnessFactor"] = factor;
		return GltfReader.Read( Encoding.UTF8.GetBytes( root.ToJsonString() ), uri => uri == "literal.png" ? png : throw new InvalidOperationException( "Unexpected source-ORM fixture dependency: " + uri ) );
	}
	private static object Prepare( GltfDocument document )
	{
		var method = typeof( global::Sandbox.GltfStockMaterialAdapter ).GetMethod( "PrepareSource", Flags, null, [typeof( GltfDocument ), typeof( GltfMaterial )], null );
		Require( method is not null, "Actually loaded Engine has no integrated GltfStockMaterialAdapter.PrepareSource candidate route; do not compile replacement producer code into this helper." );
		try { return method.Invoke( null, [document, document.ReadMaterial( 0 )] )!; } catch ( TargetInvocationException error ) when ( error.InnerException is not null ) { throw error.InnerException; }
	}
	private static object Slot( object prepared, string role )
	{
		var pair = ((IEnumerable)Property<object>( prepared, "Resources" )).Cast<object>().Single( value => Property<object>( value, "Key" ).ToString() == role );
		return Property<object>( pair, "Value" );
	}
	private static byte[] Resource( object prepared, string role ) => Property<byte[]>( Slot( prepared, role ), "Resource" );
	private static T Property<T>( object value, string name ) => (T)(value.GetType().GetProperty( name, Flags ) ?? throw new InvalidOperationException( "Actual source-ORM adapter contract is missing " + name )).GetValue( value )!;
}
