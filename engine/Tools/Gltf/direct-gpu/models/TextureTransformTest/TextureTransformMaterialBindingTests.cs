using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>
/// Actual native provided-template binding/cache checks, executed only by the
/// coordinator after compiling and identifying that template. Readable uniforms
/// and correct cache identity do not prove shader use or rendered sampling.
/// </summary>
[TestClass]
internal sealed class TextureTransformMaterialBindingTests
{
	public static string CheckoutRoot { get; set; }
	public static Material BorrowedTemplate { get; set; }
	public sealed record Result( bool Passed, string Name, string Error,
		string Scope = "Actual native material properties/cache only; shader-use, default material selection and image sampling remain separate gates." );

	/// <summary>
	/// The coordinator can supply the already reviewed anonymous compiled template
	/// from the candidate gate. This model never compiles or destroys that template.
	/// Its instance MSTest methods are the same eight assertions used here.
	/// </summary>
	public static Result[] RunAll( string checkoutRoot, Material borrowedTemplate )
	{
		ThreadSafe.AssertIsMainThread();
		var previousRoot = CheckoutRoot; var previousTemplate = BorrowedTemplate;
		CheckoutRoot = checkoutRoot; BorrowedTemplate = borrowedTemplate;
		try
		{
			var test = new TextureTransformMaterialBindingTests(); var rows = new List<Result>();
			foreach ( var compiled in new[] { false, true } )
				foreach ( var (name, run) in new (string Name, Action Run)[]
				{
					("independent_affine_uniforms", () => test.NativePerRoleAffineRowsRetainIndependentModelExpectations( compiled )),
					("identity_reference_cache_and_leases", () => test.NativeCacheSeparatesAffineMaterialsAndPreservesIndependentIdentityReference( compiled )),
					("distinct_uv1_selector", () => test.NativeUvSelectorHasIndependentIdentityAndPreservesBothSourceSets( compiled )),
					("failed_plan_preserves_live_material", () => test.WrongIdentityPlanCannotChangeAnAlreadyLiveAffineMaterial( compiled ))
				} )
				{
					try { run(); rows.Add( new( true, name + (compiled ? "/compiled_glb" : "/source"), null ) ); }
					catch ( Exception error ) { rows.Add( new( false, name + (compiled ? "/compiled_glb" : "/source"), error.ToString() ) ); }
				}
			return rows.ToArray();
		}
		finally { CheckoutRoot = previousRoot; BorrowedTemplate = previousTemplate; }
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NativePerRoleAffineRowsRetainIndependentModelExpectations( bool compiled )
	{
		var template = BorrowTemplate();
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		for ( var materialIndex = 0; materialIndex < 9; materialIndex++ )
		{
			var source = input.Document.ReadMaterial( materialIndex );
			var upload = input.Document.MaterializePrimitiveUpload( materialIndex, 0 );
			var uv = GltfMaterialUvPlan.Create( source, upload, materialIndex );
			var prepared = GltfStockMaterialAdapter.PrepareSource( input.Document, source );
			using var lease = GltfStockMaterialAdapter.AcquireSource( prepared, template, uv );
			Assert.IsTrue( lease.Value.IsValid && !lease.Value.IsError );
			Assert.AreEqual( 1, uv.Bindings.Count );
			Assert.AreEqual( 1, prepared.Resources.Count );
			Assert.AreEqual( 0f, lease.Value.GetVector4( "g_flGltfBaseColorUv1" ).x );
			var origin = TextureTransformReference.SampleUv( materialIndex, 0, 0 );
			var x = TextureTransformReference.SampleUv( materialIndex, 1, 0 );
			var y = TextureTransformReference.SampleUv( materialIndex, 0, 1 );
			var rowU = lease.Value.GetVector4( "g_vGltfBaseColorUvRowU" );
			var rowV = lease.Value.GetVector4( "g_vGltfBaseColorUvRowV" );
			Near( x[0] - origin[0], rowU.x ); Near( y[0] - origin[0], rowU.y ); Near( origin[0], rowU.z );
			Near( x[1] - origin[1], rowV.x ); Near( y[1] - origin[1], rowV.y ); Near( origin[1], rowV.z );
			var factor = lease.Value.GetVector4( "g_vGltfBaseColor" );
			Assert.AreEqual( 1f, factor.x ); Assert.AreEqual( 1f, factor.y ); Assert.AreEqual( 1f, factor.z );
			Assert.AreEqual( 0f, lease.Value.GetVector4( "g_flGltfMetallicFactor" ).x );
			Assert.AreEqual( 1f, lease.Value.GetVector4( "g_flGltfRoughnessFactor" ).x );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NativeCacheSeparatesAffineMaterialsAndPreservesIndependentIdentityReference( bool compiled )
	{
		var template = BorrowTemplate();
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		using var identity = TextureTransformIdentityMaterialReference.CreateDocument( input, compiled );
		for ( var materialIndex = 0; materialIndex < 9; materialIndex++ )
		{
			var source = input.Document.ReadMaterial( materialIndex );
			var sourceUv = GltfMaterialUvPlan.Create( source, input.Document.MaterializePrimitiveUpload( materialIndex, 0 ), materialIndex );
			var prepared = GltfStockMaterialAdapter.PrepareSource( input.Document, source );
			using var first = GltfStockMaterialAdapter.AcquireSource( prepared, template, sourceUv );
			using var repeated = GltfStockMaterialAdapter.AcquireSource( prepared, template, sourceUv );
			Assert.IsTrue( ReferenceEquals( first.Value, repeated.Value ), "Equivalent immutable role selections should share the native material." );
			var referenceSource = identity.ReadMaterial( materialIndex );
			var referenceUv = GltfMaterialUvPlan.Create( referenceSource, identity.MaterializePrimitiveUpload( materialIndex, 0 ), materialIndex );
			using var reference = GltfStockMaterialAdapter.AcquireSource( GltfStockMaterialAdapter.PrepareSource( identity, referenceSource ), template, referenceUv );
			Assert.AreEqual( materialIndex >= 6, ReferenceEquals( first.Value, reference.Value ),
				"Only the six authored affine maps may separate these otherwise equivalent source materials; source stream ownership is not a rendering-equivalence key." );
			var rowU = reference.Value.GetVector4( "g_vGltfBaseColorUvRowU" );
			var rowV = reference.Value.GetVector4( "g_vGltfBaseColorUvRowV" );
			Assert.AreEqual( 1f, rowU.x ); Assert.AreEqual( 0f, rowU.y ); Assert.AreEqual( 0f, rowU.z );
			Assert.AreEqual( 0f, rowV.x ); Assert.AreEqual( 1f, rowV.y ); Assert.AreEqual( 0f, rowV.z );
			first.Dispose();
			Assert.IsTrue( repeated.Value.IsValid && !repeated.Value.IsError, "Releasing one asset's lease must retain another live user." );
			Assert.IsTrue( reference.Value.IsValid && !reference.Value.IsError );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NativeUvSelectorHasIndependentIdentityAndPreservesBothSourceSets( bool compiled )
	{
		var template = BorrowTemplate();
		using var source = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		var fixture = JsonNode.Parse( source.Dependencies["TextureTransformTest.gltf"] );
		fixture["materials"][0]["pbrMetallicRoughness"]["baseColorTexture"]["extensions"][TextureTransformReference.Extension]["texCoord"] = 1;
		fixture["meshes"][0]["primitives"][0]["attributes"]["TEXCOORD_1"] = 1;
		var bytes = Encoding.UTF8.GetBytes( fixture.ToJsonString() );
		if ( compiled ) bytes = GltfCompiler.Compile( bytes, source.Resolve, TextureTransformReference.Selected );
		using var document = GltfReader.Read( bytes, source.Resolve, TextureTransformReference.Selected );
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var original = upload.Attributes["TEXCOORD_0"].Storage.Bytes.ToArray();
		var material = document.ReadMaterial( 0 );
		var uv = GltfMaterialUvPlan.Create( material, upload, 0 );
		Assert.IsTrue( ReferenceEquals( upload.Attributes["TEXCOORD_1"], uv.Bindings[GltfMaterialTextureRole.BaseColor].Source ) );
		Assert.AreNotEqual( upload.Attributes["TEXCOORD_0"].Binding.AccessorIndex, upload.Attributes["TEXCOORD_1"].Binding.AccessorIndex );
		using var selected = GltfStockMaterialAdapter.AcquireSource( GltfStockMaterialAdapter.PrepareSource( document, material ), template, uv );
		Assert.AreEqual( 1f, selected.Value.GetVector4( "g_flGltfBaseColorUv1" ).x );
		var originalMaterial = source.Document.ReadMaterial( 0 );
		using var originalSelection = GltfStockMaterialAdapter.AcquireSource( GltfStockMaterialAdapter.PrepareSource( source.Document, originalMaterial ),
			template, GltfMaterialUvPlan.Create( originalMaterial, source.Document.MaterializePrimitiveUpload( 0, 0 ), 0 ) );
		Assert.AreEqual( 0f, originalSelection.Value.GetVector4( "g_flGltfBaseColorUv1" ).x );
		Assert.IsFalse( ReferenceEquals( selected.Value, originalSelection.Value ), "A different selected source set must not alias a UV0 material key." );
		CollectionAssert.AreEqual( original, upload.Attributes["TEXCOORD_0"].Storage.Bytes.ToArray(), "Material selection must not bake into the shared source storage." );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void WrongIdentityPlanCannotChangeAnAlreadyLiveAffineMaterial( bool compiled )
	{
		var template = BorrowTemplate();
		using var source = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		using var identity = TextureTransformIdentityMaterialReference.CreateDocument( source, compiled );
		var material = source.Document.ReadMaterial( 5 );
		var prepared = GltfStockMaterialAdapter.PrepareSource( source.Document, material );
		using var live = GltfStockMaterialAdapter.AcquireSource( prepared, template,
			GltfMaterialUvPlan.Create( material, source.Document.MaterializePrimitiveUpload( 5, 0 ), 5 ) );
		var rowU = live.Value.GetVector4( "g_vGltfBaseColorUvRowU" );
		var rowV = live.Value.GetVector4( "g_vGltfBaseColorUvRowV" );
		var wrong = GltfMaterialUvPlan.Create( identity.ReadMaterial( 5 ), identity.MaterializePrimitiveUpload( 5, 0 ), 5 );
		Assert.ThrowsException<GltfException>( () => GltfStockMaterialAdapter.AcquireSource( prepared, template, wrong ) );
		Assert.IsTrue( live.Value.IsValid && !live.Value.IsError );
		Assert.AreEqual( rowU, live.Value.GetVector4( "g_vGltfBaseColorUvRowU" ) );
		Assert.AreEqual( rowV, live.Value.GetVector4( "g_vGltfBaseColorUvRowV" ) );
	}

	private static Material BorrowTemplate()
	{
		Assert.IsTrue( BorrowedTemplate is not null && BorrowedTemplate.IsValid && !BorrowedTemplate.IsError,
			"The coordinator must supply the identified, already compiled provided template; an anonymous compiled shader cannot be loaded by filename." );
		return BorrowedTemplate;
	}

	private static void Near( double expected, float actual ) => Assert.IsTrue( float.IsFinite( actual ) && Math.Abs( expected - actual ) <= 1e-6,
		$"Native affine uniform {actual:R} differs from independent model expectation {expected:R}." );
}
