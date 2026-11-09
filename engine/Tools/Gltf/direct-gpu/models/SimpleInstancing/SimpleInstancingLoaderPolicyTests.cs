using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using NVector = System.Numerics.Vector3;
using static SimpleInstancingReference;

/// <summary>
/// Fourteen bounded CPU policy cases against the actual Engine loader/backend.
/// Skin plus selected instancing cases prove clear rejection of valid input only.
/// That combined rendering feature remains FAILED/PENDING, never a rendering pass.
/// No native Model/Mesh, graphics resources or renderer instances are constructed.
/// </summary>
internal static class SimpleInstancingLoaderPolicyTests
{
	public const int ExpectedCases = 14;
	public const string CombinedRenderingStatus = "FAILED/PENDING: valid selected skin plus EXT_mesh_gpu_instancing rendering is unimplemented; negative error-policy success is not rendering acceptance.";
	private const string ShaderPath = "shaders/gltf_deform_cs.shader";
	private const string SkinUri = "loader-policy-skin.bin";

	public static SimpleInstancingCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var reference = new SimpleInstancingReference( checkoutRoot ); var rows = new List<SimpleInstancingCoreTests.Result>();
		foreach ( var form in reference.Forms )
			Run( "public_loader_selects_optional_EXT_and_prepares125_source_identities", form.Name, () => PublicSelection( form ) );

		var source = reference.Forms.Single( form => form.Name == "glTF/source" );
		var required = RequiredForms( source );
		foreach ( var form in required )
			Run( "public_loader_selects_required_EXT_and_prepares125_source_identities", form.Name, () => PublicSelection( form ) );

		foreach ( var form in reference.Forms.Where( form => form.Variant == "glTF" ) )
			Run( "explicitly_unselected_optional_EXT_prepares_one_ordinary_fallback_only", form.Name, () =>
			{
				using var document = GltfReader.Read( form.Bytes, form.Resolve );
				Require( document.UnsupportedOptionalExtensions.SequenceEqual( new[] { Extension } ) && document.ReadInstances( 0 ).Length == 0, "Explicitly unsupported optional extension must be reported separately." );
				var expansion = GltfGpuInstancingPlan.Create( document, 0 );
				Require( expansion.HasExtension && !expansion.AppliesInstancing && expansion.Attributes.Count == 0 && expansion.Instances.Count == 1 && expansion.Instances[0].InstanceIndex is null, "Fallback plan must remain distinguishable from selected instancing." );
				using var asset = GltfGpuAsset.Prepare( document, ShaderPath );
				Require( asset.MeshCount == 1 && asset.Nodes.Count == 1 && asset.Nodes[0].Index == 0 && asset.Nodes[0].InstanceIndex is null && asset.Nodes[0].LocalTransform == Matrix4x4.Identity && asset.Primitives.Count == 1 && !asset.HasGpuResources, "Optional unsupported capability must produce one ordinary CPU-prepared cube, never a selected-instancing pass." );
			} );

		foreach ( var form in required )
			Run( "explicitly_unselected_required_EXT_rejected_with_capability_reason", form.Name, () =>
			{
				ExpectGltf( () => { using var document = GltfReader.Read( form.Bytes, form.Resolve ); }, "unsupported", "required", Extension );
			} );

		foreach ( var form in SkinForms( source ) )
			Run( "negative_error_policy_only_valid_skin_plus_selected_EXT_rejected_RENDERING_PENDING", form.Name + " [error policy only]", () =>
			{
				using var document = GltfModelLoader.ReadDocument( form.Bytes, form.Resolve );
				// Preconditions deliberately precede the expected-rejection boundary.
				// A malformed skin/accessor/fixture must fail this case rather than
				// masquerading as the backend's unimplemented-feature policy.
				ValidateSkinFixture( document, reference );
				var released = 0;
				ExpectGltf( () => { using var asset = GltfGpuAsset.Prepare( document, ShaderPath, finalRelease: () => released++ ); }, Extension, "skin", "not implemented" );
				Require( released == 0 && document.ReadSkin( 0 ).Joints.SequenceEqual( new[] { 1 } ), "Rejected preparation must not consume the caller's document/final-release ownership." );
			} );

		Require( rows.Count == ExpectedCases, "Frozen loader-policy inventory changed." ); return rows.ToArray();
		void Run( string name, string form, Action body ) { try { body(); rows.Add( new( name, form, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form, false, error.ToString() ) ); } }
	}

	private static void PublicSelection( Form form )
	{
		using var document = GltfModelLoader.ReadDocument( form.Bytes, form.Resolve );
		Require( !document.UnsupportedOptionalExtensions.Contains( Extension ) && document.ReadInstances( 0 ).Length == InstanceCount, "The public loader policy did not select actual EXT instance transforms." );
		using var asset = GltfGpuAsset.Prepare( document, ShaderPath );
		Require( asset.MeshCount == InstanceCount && asset.Nodes.Count == InstanceCount && asset.Nodes.All( row => row.Index == 0 && row.Skin is null ) && asset.Nodes.Select( row => row.InstanceIndex ).SequenceEqual( Enumerable.Range( 0, InstanceCount ).Select( index => (int?)index ) ), "Public loader policy must preserve all125 ordered source identities and omit an ordinary base mesh." );
		Require( asset.Primitives.Count == 1 && asset.Nodes.All( row => ReferenceEquals( row.Primitive, asset.Primitives[0] ) ) && !asset.HasGpuResources, "CPU preparation must share one primitive without graphics allocation." );
		var last = NVector.Transform( NVector.UnitX, asset.Nodes[124].LocalTransform );
		Close( 10, last.X, 0, "last instance X" ); Close( 12, last.Y, 0, "last instance Y" ); Close( 10, last.Z, 0, "last instance Z" );
	}

	private static Form[] RequiredForms( Form source )
	{
		var root = JsonNode.Parse( source.Bytes ); root["extensionsRequired"] = new JsonArray( Extension ); var bytes = Encoding.UTF8.GetBytes( root.ToJsonString() );
		return new[]
		{
			new Form( "synthetic_required/source", "synthetic_required", false, bytes, source.Resolve ),
			new Form( "synthetic_required/compiled_glb_c", "synthetic_required", true, GltfCompiler.Compile( bytes, source.Resolve, Selected ), source.Resolve )
		};
	}

	private static Form[] SkinForms( Form source )
	{
		var results = new List<Form>(); var skinBytes = new byte[480];
		// Twenty-four BYTE VEC4 joint tuples are already zero-initialized.
		// The FLOAT VEC4 weights begin after those 96 bytes; only weight0 is one.
		for ( var vertex = 0; vertex < VertexCount; vertex++ ) BinaryPrimitives.WriteSingleLittleEndian( skinBytes.AsSpan( 96 + vertex * 16 ), 1 );
		byte[] Resolve( string uri ) => uri == SkinUri ? skinBytes : source.Resolve( uri );
		foreach ( var required in new[] { false, true } )
		{
			var root = JsonNode.Parse( source.Bytes );
			root["buffers"].AsArray().Add( new JsonObject { ["uri"] = SkinUri, ["byteLength"] = skinBytes.Length } );
			root["bufferViews"].AsArray().Add( new JsonObject { ["buffer"] = 1, ["byteOffset"] = 0, ["byteLength"] = 96, ["target"] = 34962 } );
			root["bufferViews"].AsArray().Add( new JsonObject { ["buffer"] = 1, ["byteOffset"] = 96, ["byteLength"] = 384, ["target"] = 34962 } );
			root["accessors"].AsArray().Add( new JsonObject { ["bufferView"] = 6, ["byteOffset"] = 0, ["componentType"] = 5121, ["count"] = VertexCount, ["type"] = "VEC4" } );
			root["accessors"].AsArray().Add( new JsonObject { ["bufferView"] = 7, ["byteOffset"] = 0, ["componentType"] = 5126, ["count"] = VertexCount, ["type"] = "VEC4" } );
			var attributes = root["meshes"][0]["primitives"][0]["attributes"].AsObject(); attributes["JOINTS_0"] = 6; attributes["WEIGHTS_0"] = 7;
			root["nodes"][0]["skin"] = 0; root["nodes"].AsArray().Add( new JsonObject { ["name"] = "IdentityJoint" } ); root["scenes"][0]["nodes"].AsArray().Add( 1 );
			root["skins"] = new JsonArray( new JsonObject { ["joints"] = new JsonArray( 1 ) } ); // Omitted inverseBindMatrices means identity.
			if ( required ) root["extensionsRequired"] = new JsonArray( Extension );
			var label = required ? "synthetic_skin_required" : "synthetic_skin_optional"; var bytes = Encoding.UTF8.GetBytes( root.ToJsonString() );
			results.Add( new( label + "/source", label, false, bytes, Resolve ) );
			results.Add( new( label + "/compiled_glb_c", label, true, GltfCompiler.Compile( bytes, Resolve, Selected ), Resolve ) );
		}
		return results.ToArray();
	}

	private static void ValidateSkinFixture( GltfDocument document, SimpleInstancingReference reference )
	{
		Require( document.GetDefaultScene() == 0 && !document.UnsupportedOptionalExtensions.Contains( Extension ) && document.ReadInstances( 0 ).Length == InstanceCount, "Valid selected combined fixture lost its scene or instance data." );
		Require( document.Root.GetProperty( "nodes" ).GetArrayLength() == 2 && document.Root.GetProperty( "scenes" )[0].GetProperty( "nodes" ).EnumerateArray().Select( value => value.GetInt32() ).SequenceEqual( new[] { 0, 1 } ), "Both the skinned mesh and identity joint must belong to the selected scene." );
		var skin = document.ReadSkin( 0 );
		Require( skin.Joints.SequenceEqual( new[] { 1 } ) && skin.Skeleton is null && skin.InverseBindMatrices.Length == 1 && skin.InverseBindMatrices[0] == Matrix4x4.Identity && document.GetWorldTransform( 1 ) == Matrix4x4.Identity, "Valid single-joint skin must use the omitted-IBM identity default." );
		var geometry = document.ReadPrimitive( 0, 0 ); Require( geometry.VertexCount == VertexCount && geometry.Attributes.Count == 4 && geometry.Attributes["JOINTS_0"] == 6 && geometry.Attributes["WEIGHTS_0"] == 7 && geometry.Indices.SequenceEqual( reference.Indices ), "Skin fixture changed base cube geometry or attribute pairing." );
		var joints = document.ReadFloatAccessor( 6 ); var weights = document.ReadFloatAccessor( 7 );
		Require( joints.Length == VertexCount * 4 && joints.All( value => value == 0 ) && weights.Length == VertexCount * 4 && weights.Select( (value, index) => value == (index % 4 == 0 ? 1 : 0) ).All( same => same ), "Skin fixture needs exactly one valid unit influence on joint palette index0." );
		var pose = document.EvaluatePose();
		for ( var vertex = 0; vertex < VertexCount; vertex++ )
		{
			// This validates skin/reader data only; EvaluateVertexPosition does not
			// implement EXT instance rendering and is never used as that oracle.
			var actual = document.EvaluateVertexPosition( 0, 0, vertex, pose ); var expected = reference.Value( 1, vertex );
			Close( expected[0], actual.X, 0, "identity-skinned fixture X" ); Close( expected[1], actual.Y, 0, "identity-skinned fixture Y" ); Close( expected[2], actual.Z, 0, "identity-skinned fixture Z" );
		}
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		Require( upload.Attributes.Count == 4 && upload.OwnedBytes == 1200 && upload.Layout.VertexCount == VertexCount, "Valid skin upload should own720 base bytes plus480 skin bytes." );
		foreach ( var (semantic, accessor, componentType, byteStride, length) in new[] { ("JOINTS_0", 6, 5121, 4, 96), ("WEIGHTS_0", 7, 5126, 16, 384) } )
		{
			var stream = upload.Attributes[semantic]; Require( stream.Binding.AccessorIndex == accessor && stream.Binding.SourceBufferView == accessor && stream.Count == VertexCount && stream.ComponentType == componentType && stream.Components == 4 && !stream.Normalized && !stream.IsMaterialized && stream.ByteOffset == 0 && stream.ByteStride == byteStride && stream.Storage.Bytes.Length == length, "Valid authored skin upload representation changed: " + semantic );
		}
		Require( upload.Attributes["JOINTS_0"].Storage.Bytes.Span.IndexOfAnyExcept( (byte)0 ) == -1, "Owned BYTE joint tuples changed." );
		var weightBytes = upload.Attributes["WEIGHTS_0"].Storage.Bytes.Span;
		for ( var vertex = 0; vertex < VertexCount; vertex++ ) for ( var component = 0; component < 4; component++ )
			Close( component == 0 ? 1 : 0, BinaryPrimitives.ReadSingleLittleEndian( weightBytes[(vertex * 16 + component * 4)..] ), 0, "owned FLOAT weights" );
	}

	private static void ExpectGltf( Action action, params string[] words )
	{
		try { action(); }
		catch ( GltfException error ) { Require( words.All( word => error.Message.Contains( word, StringComparison.OrdinalIgnoreCase ) ), "Policy rejection lacks its explicit capability/feature reason: " + error.Message ); return; }
		throw new InvalidOperationException( "Expected a clear GltfException policy rejection." );
	}
}
