using Sandbox;
using System;
using System.Reflection;

namespace EngineTests;

/// <summary>Native material ownership and reload-dispatch tests; shader bytecode and posed GPU output have separate gates.</summary>
[TestClass]
public sealed class GltfSkinningMaterialCacheTests
{
	[TestMethod]
	public void FormatVariantsRetainExactFeaturesAndInvalidateOnMaterialAndShaderReload()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires native graphics materials." );
		Assert.IsTrue( ThreadSafe.IsMainThread );
		var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		var renderer = Assembly.Load( "Sandbox.SceneRenderer" );
		var featureType = renderer.GetType( "Sandbox.SceneRenderer.Features.MeshRenderFeature", throwOnError: true );
		var feature = Activator.CreateInstance( featureType, nonPublic: true );
		var select = featureType.GetMethod( "SkinningShaderFor", flags );
		var dispose = featureType.GetMethod( "Dispose", flags );
		var template = Material.Load( "materials/dev/skinning_cs.vmat" ).CreateCopy( "gltf_cache_template_" + Guid.NewGuid().ToString( "N" ) );
		Assert.IsTrue( template is { IsValid: true, IsError: false } );
		featureType.GetField( "skinningShader", flags ).SetValue( feature, template );
		Material Select( GltfSkinWeightFormat format ) => (Material)select.Invoke( feature, [format] );
		try
		{
			Assert.AreSame( template, Select( GltfSkinWeightFormat.Unorm8 ) );
			var first = Select( GltfSkinWeightFormat.Float32 );
			var ushortVariant = Select( GltfSkinWeightFormat.Unorm16 );
			Assert.AreSame( first, Select( GltfSkinWeightFormat.Float32 ) );
			Assert.AreEqual( 1, first.GetFeature( "F_SKIN_WEIGHT_FORMAT" ) );
			Assert.AreEqual( 2, ushortVariant.GetFeature( "F_SKIN_WEIGHT_FORMAT" ) );
			Assert.AreEqual( 0, template.GetFeature( "F_SKIN_WEIGHT_FORMAT" ), "The borrowed default must not be changed." );

			var materialRevision = template.ReloadRevision;
			Resource.OnResourceReloaded( template.ResourcePath, template.native.GetBindingPtr() );
			Assert.AreEqual( materialRevision + 1, template.ReloadRevision, "Central dispatch must increment before Material.OnReloaded overrides the virtual callback." );
			var afterMaterial = Select( GltfSkinWeightFormat.Float32 );
			Assert.AreNotSame( first, afterMaterial );
			Assert.IsFalse( first.IsValid );
			Assert.IsFalse( ushortVariant.IsValid );
			Assert.IsTrue( template.IsValid );

			var shader = template.Shader;
			var shaderRevision = shader.ReloadRevision;
			Resource.OnResourceReloaded( shader.ResourcePath, shader.native.GetBindingPtr() );
			Assert.AreEqual( shaderRevision + 1, shader.ReloadRevision );
			var afterShader = Select( GltfSkinWeightFormat.Float32 );
			Assert.AreNotSame( afterMaterial, afterShader );
			Assert.IsFalse( afterMaterial.IsValid );
			Assert.IsTrue( template.IsValid );
			Assert.AreEqual( 1, afterShader.GetFeature( "F_SKIN_WEIGHT_FORMAT" ) );

			afterShader.Destroy();
			var afterInvalidHandle = Select( GltfSkinWeightFormat.Float32 );
			Assert.AreNotSame( afterShader, afterInvalidHandle );
			Assert.IsTrue( afterInvalidHandle.IsValid );
			dispose.Invoke( feature, null );
			Assert.IsFalse( afterInvalidHandle.IsValid );
			Assert.IsTrue( template.IsValid, "Disposing the feature must not destroy the borrowed default." );
		}
		finally
		{
			dispose.Invoke( feature, null );
			template.Destroy();
		}
	}
}
