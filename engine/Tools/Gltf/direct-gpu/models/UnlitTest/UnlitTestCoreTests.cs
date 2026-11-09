using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

using Vector3 = System.Numerics.Vector3;

/// <summary>Independent pinned-model assertions. No device allocation or editor mutation.</summary>
public static class UnlitTestCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );
	public const int ExpectedCases = 39;

	public static Result[] RunAll( string checkoutRoot )
	{
		var rows = new List<Result>();
		Run( "literal_oracle_detects_wrong_view_origin", "reference", CheckOffsetSensitivity );
		Run( "linear_base_color_is_not_authored_srgb_byte_fraction", "reference", CheckColorSensitivity );
		Run( "optional_unlit_fallback_is_explicit_and_supported_unlit_wins", "derived_optional_fixture", () => CheckOptionalFallback( checkoutRoot ) );
		foreach ( var form in UnlitTestReference.OpenForms( checkoutRoot ) )
		{
			Run( "required_unlit_is_rejected_without_capability", form.Name, () => CheckRequired( form ) );
			Run( "source_views_offsets_and_component_formats", form.Name, () => CheckDescriptors( form ) );
			Run( "all_literal_positions_normals_and_u16_indices", form.Name, () => CheckRawValues( form ) );
			Run( "two_authored_node_translations_and_world_bounds", form.Name, () => CheckWorld( form ) );
			Run( "both_authored_materials_are_unlit_with_linear_factors", form.Name, () => CheckMaterials( form ) );
			Run( "gpu_plan_preserves_source_descriptors_and_material_bindings", form.Name, () => CheckPlan( form ) );
			Run( "portable_packaging_retains_required_unlit_and_both_materials", form.Name, () => CheckPackaging( form ) );
			Run( "owned_upload_survives_document_disposal", form.Name, () => CheckLifetime( form ) );
			Run( "default_stock_material_preparation_accepts_required_unlit", form.Name, () => CheckStockMaterialAcceptance( form ) );
		}
		Require( rows.Count == ExpectedCases, "Frozen case inventory changed." );
		return rows.ToArray();

		void Run( string name, string form, Func<int> body )
		{
			try { rows.Add( new( name, form, body(), true, null ) ); }
			catch ( Exception error ) { rows.Add( new( name, form, 0, false, error.ToString() ) ); }
		}
	}

	private static int CheckOffsetSensitivity()
	{
		Require( UnlitTestReference.Positions.Length == 96 && UnlitTestReference.Normals.Length == 96 && UnlitTestReference.Indices.Length == 132, "Literal sample inventory changed." );
		Require( Vector3.Distance( UnlitTestReference.Positions[0], UnlitTestReference.Normals[0] ) > .9f, "Confusing the position and normal views must be detected." );
		Require( UnlitTestReference.Normals.Distinct().Count() > 20, "An all-identical normal oracle would miss accidental lit shading on this sample." );
		Require( UnlitTestReference.Indices.All( index => index < 96 ), "Literal topology is out of range." );
		return 4;
	}

	private static int CheckColorSensitivity()
	{
		var linearGreen = UnlitTestReference.BaseColors[0].Y;
		Require( Math.Abs( linearGreen - 127f / 255f ) > .25f, "The authored green factor is linear, not an encoded byte fraction." );
		// The sample's hand-authored .5^2.2 factor is approximate; do not change it
		// to the exact sRGB decode of #7f merely because its README names #FF7F00.
		var srgb = linearGreen <= .0031308 ? 12.92 * linearGreen : 1.055 * Math.Pow( linearGreen, 1 / 2.4 ) - .055;
		Require( srgb > .503 && srgb < .505, "Independent linear-to-sRGB sanity range changed." );
		Require( UnlitTestReference.BaseColors[0] != UnlitTestReference.BaseColors[1], "Orange/Blue material swap must remain detectable." );
		return 3;
	}

	private static int CheckOptionalFallback( string checkoutRoot )
	{
		var source = UnlitTestReference.SourceJson( checkoutRoot );
		source.Remove( "extensionsRequired" );
		var bytes = Encoding.UTF8.GetBytes( source.ToJsonString() );
		using var fallback = GltfReader.Read( bytes, uri => UnlitTestReference.ResolvePinnedBinary( checkoutRoot, uri ) );
		using var supported = GltfReader.Read( bytes, uri => UnlitTestReference.ResolvePinnedBinary( checkoutRoot, uri ), UnlitTestReference.Selected );
		Require( fallback.UnsupportedOptionalExtensions.Contains( UnlitTestReference.Extension ), "Unsupported optional unlit must be reported." );
		for ( var material = 0; material < 2; material++ )
		{
			Require( !fallback.ReadMaterial( material ).Unlit && supported.ReadMaterial( material ).Unlit, "Only an unsupported optional extension may use the lit core fallback." );
			Require( fallback.ReadMaterial( material ).BaseColorFactor == UnlitTestReference.BaseColors[material], "Fallback changed core base color." );
		}
		return 5;
	}

	private static int CheckRequired( UnlitTestReference.Form form )
	{
		try { using var rejected = GltfReader.Read( form.Bytes, form.Resolve ); }
		catch ( GltfException error )
		{
			Require( error.Message.Contains( UnlitTestReference.Extension, StringComparison.Ordinal ) && error.Message.Contains( "required", StringComparison.OrdinalIgnoreCase ), "Required-extension rejection must identify the unsupported feature." );
			return 1;
		}
		throw new InvalidOperationException( "Required unlit was silently loaded as lit without extension capability." );
	}

	private static int CheckDescriptors( UnlitTestReference.Form form )
	{
		using var document = form.Read();
		for ( var mesh = 0; mesh < 2; mesh++ )
		{
			var upload = document.MaterializePrimitiveUpload( mesh, 0 );
			Require( upload.Layout.VertexCount == 96 && upload.Layout.SourceMode == 4 && upload.Attributes.Count == 2, "Pinned primitive shape changed." );
			var position = upload.Attributes["POSITION"]; var normal = upload.Attributes["NORMAL"]; var index = upload.Indices;
			foreach ( var stream in new[] { position, normal } )
				Require( stream.ComponentType == 5126 && stream.Components == 3 && stream.Count == 96 && stream.ByteStride == 12 && stream.ByteOffset == 0 && !stream.Normalized && !stream.IsMaterialized && stream.Storage.Bytes.Length == 1152, "FLOAT3 view representation changed." );
			Require( position.Binding.ViewByteOffset == 0 && normal.Binding.ViewByteOffset == 1152 && !ReferenceEquals( position.Storage, normal.Storage ), "Source view origins were confused with offsets into owned view storage." );
			Require( index.ComponentType == 5123 && index.Components == 1 && index.Count == 132 && index.ByteStride == 2 && index.ByteOffset == 0 && index.Binding.ViewByteOffset == 2304 && index.Storage.Bytes.Length == 264, "Authored U16 triangle topology changed." );
			Require( upload.OwnedBytes == 2568, "Owned view bytes changed." );
		}
		return 12;
	}

	private static int CheckRawValues( UnlitTestReference.Form form )
	{
		using var document = form.Read();
		for ( var mesh = 0; mesh < 2; mesh++ )
		{
			var upload = document.MaterializePrimitiveUpload( mesh, 0 );
			for ( var vertex = 0; vertex < 96; vertex++ )
			{
				Equal( UnlitTestReference.Positions[vertex], Read3( upload.Attributes["POSITION"], vertex ), 0, "raw position" );
				Equal( UnlitTestReference.Normals[vertex], Read3( upload.Attributes["NORMAL"], vertex ), 0, "raw normal" );
			}
			for ( var index = 0; index < 132; index++ )
				Require( BinaryPrimitives.ReadUInt16LittleEndian( upload.Indices.Storage.Bytes.Span.Slice( upload.Indices.ByteOffset + index * upload.Indices.ByteStride, 2 ) ) == UnlitTestReference.Indices[index], "Triangle order/winding differs from independent literal source." );
			Require( document.ReadPrimitive( mesh, 0 ).Material == mesh, "Orange/Blue material binding was swapped." );
		}
		return 650;
	}

	private static int CheckWorld( UnlitTestReference.Form form )
	{
		using var document = form.Read(); var pose = document.EvaluatePose();
		Require( document.GetDefaultScene() == 0, "Pinned default scene changed." );
		var minimum = new Vector3( float.PositiveInfinity ); var maximum = new Vector3( float.NegativeInfinity );
		for ( var node = 0; node < 2; node++ )
			for ( var vertex = 0; vertex < 96; vertex++ )
			{
				Equal( UnlitTestReference.WorldPosition( node, vertex ), document.EvaluateVertexPosition( node, 0, vertex, pose ), .000001f, "independent authored translation" );
				var engine = UnlitTestReference.EnginePosition( node, vertex ); minimum = Vector3.Min( minimum, engine ); maximum = Vector3.Max( maximum, engine );
			}
		Equal( new( -1f / .0254f, -2.2f / .0254f, -1f / .0254f ), minimum, .00002f, "independent engine minimum" );
		Equal( new( 1f / .0254f, 2.2f / .0254f, 1f / .0254f ), maximum, .00002f, "independent engine maximum" );
		return 195;
	}

	private static int CheckMaterials( UnlitTestReference.Form form )
	{
		using var document = form.Read();
		for ( var index = 0; index < 2; index++ )
		{
			var material = document.ReadMaterial( index );
			Require( material.Unlit && material.BaseColorFactor == UnlitTestReference.BaseColors[index], "Authored unlit state/linear factors changed." );
			Require( material.AlphaMode == "OPAQUE" && !material.DoubleSided && material.MetallicFactor == 1 && material.RoughnessFactor == 1, "Unlit core defaults changed. These PBR factors must not introduce lighting." );
			Require( material.BaseColorTexture is null && material.NormalTexture is null && material.OcclusionTexture is null && material.EmissiveTexture is null && material.EmissiveFactor == Vector3.Zero, "Untextured authored material changed." );
		}
		return 6;
	}

	private static int CheckPlan( UnlitTestReference.Form form )
	{
		using var document = form.Read();
		for ( var mesh = 0; mesh < 2; mesh++ )
		{
			var plan = GltfGpuPrimitivePlan.Create( document, mesh, 0 );
			Require( plan.Material == mesh && plan.Mode == 4 && !plan.GenerateFlatNormals && plan.SourceVertices.SequenceEqual( Enumerable.Range( 0, 96 ).Select( value => (uint)value ) ) && plan.Indices.SequenceEqual( UnlitTestReference.Indices ), "GPU plan changed material/topology identity." );
			Require( plan.Sources.Count == 2 && plan.InputBytes.Length == 2304, "Only the two authored FLOAT3 views belong in the raw input arena." );
			foreach ( var binding in plan.Sources.Values )
			{
				var start = binding.ArenaByteOffset - binding.Stream.ByteOffset;
				Require( start >= 0 && start % 4 == 0 && binding.Stream.ByteStride == 12 && binding.Stream.ComponentType == 5126 && binding.Stream.Components == 3 && !binding.Stream.Normalized, "GPU source descriptor changed." );
				Require( plan.InputBytes.AsSpan( start, 1152 ).SequenceEqual( binding.Stream.Storage.Bytes.Span ), "Raw source view bytes were repacked by assumption." );
			}
			var fields = plan.Fields.OrderBy( field => field.Offset ).ToArray();
			Require( fields.Select( field => (field.Semantic, field.SemanticIndex) ).Distinct().Count() == fields.Length && fields.All( field => field.ComponentType == 5126 && field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset + field.Components * 4 <= plan.VertexStride ), "Runtime output declarations are invalid." );
			for ( var index = 1; index < fields.Length; index++ ) Require( fields[index - 1].Offset + fields[index - 1].Components * 4 <= fields[index].Offset, "Runtime output fields overlap." );
		}
		return 22;
	}

	private static int CheckPackaging( UnlitTestReference.Form form )
	{
		using var document = form.Read();
		Require( document.Root.GetProperty( "extensionsRequired" ).EnumerateArray().Single().GetString() == UnlitTestReference.Extension, "Packaging dropped required extension." );
		Require( document.Root.GetProperty( "materials" ).GetArrayLength() == 2 && document.Root.GetProperty( "nodes" ).GetArrayLength() == 2, "Packaging merged the differently colored node/material instances." );
		if ( form.Compiled )
		{
			Require( BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 8 ) ) == form.Bytes.Length, "Compiled form is not valid GLB framing." );
			using var standalone = GltfReader.Read( form.Bytes, _ => throw new InvalidOperationException( "Portable compiled form requested an external file." ), UnlitTestReference.Selected );
			Require( standalone.MaterializePrimitiveUpload( 1, 0 ).Layout.VertexCount == 96 && standalone.ReadMaterial( 0 ).Unlit && standalone.ReadMaterial( 1 ).Unlit, "Standalone compiled form lost geometry/materials." );
		}
		return form.Compiled ? 4 : 2;
	}

	private static int CheckLifetime( UnlitTestReference.Form form )
	{
		GltfPrimitiveUploadData upload;
		using ( var document = form.Read() ) upload = document.MaterializePrimitiveUpload( 0, 0 );
		for ( var vertex = 0; vertex < 96; vertex++ )
		{
			Equal( UnlitTestReference.Positions[vertex], Read3( upload.Attributes["POSITION"], vertex ), 0, "detached position" );
			Equal( UnlitTestReference.Normals[vertex], Read3( upload.Attributes["NORMAL"], vertex ), 0, "detached normal" );
		}
		return 192;
	}

	private static int CheckStockMaterialAcceptance( UnlitTestReference.Form form )
	{
		using var document = form.Read();
		// Forward acceptance requirement, deliberately red while the shared stock
		// adapter rejects unlit. A diagnostic resolver cannot satisfy this row.
		for ( var material = 0; material < 2; material++ )
			Require( global::Sandbox.GltfStockMaterialAdapter.Prepare( document, document.ReadMaterial( material ) ) is not null, "Required unlit material was not prepared." );
		return 2;
	}

	internal static Vector3 Read3( GltfUploadStream stream, int vertex )
	{
		var offset = checked(stream.ByteOffset + vertex * stream.ByteStride); var bytes = stream.Storage.Bytes.Span;
		return new( BinaryPrimitives.ReadSingleLittleEndian( bytes[offset..] ), BinaryPrimitives.ReadSingleLittleEndian( bytes[(offset + 4)..] ), BinaryPrimitives.ReadSingleLittleEndian( bytes[(offset + 8)..] ) );
	}
	internal static void Equal( Vector3 expected, Vector3 actual, float tolerance, string label )
	{
		Require( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && float.IsFinite( actual.Z ) && Vector3.Abs( expected - actual ).X <= tolerance && Vector3.Abs( expected - actual ).Y <= tolerance && Vector3.Abs( expected - actual ).Z <= tolerance,
			$"{label}: expected {expected}, actual {actual}, tolerance {tolerance:R}." );
	}
	internal static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
