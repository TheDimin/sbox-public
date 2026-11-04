using System;

namespace EngineTests;

[TestClass]
public class GltfMaterialCacheNativeTests
{
	[TestMethod]
	public void SharedMaterialCanOutliveSceneAndReplacementDoesNotMutateOtherLease()
	{
		var oldLibrary = Game.TypeLibrary;
		Game.TypeLibrary = SharedTypeLibrary.EngineAndTests;
		Scene scene = null;
		Scene otherScene = null;
		GltfMaterialLease<Material> assetLease = null;
		GltfMaterialLease<Material> replacement = null;
		try
		{
			var template = Material.Load( "materials/default/white.vmat" );
			Assert.IsTrue( template is { IsValid: true } );
			var templateTint = template.GetVector4( "g_vColorTint" );
			GltfMaterialKey Key( float value ) => new( template, 0, [], [new( "g_vColorTint", new Vector4( value, 0, 0, 1 ) )], [], scalars: [new( "g_flRoughness", value )] );
			assetLease = GltfMaterialCache.Shared.Acquire( Key( .25f ) );
			scene = new Scene();
			var system = scene.GetSystem<GltfMaterialSceneSystem>();
			Assert.IsNotNull( system );
			var sceneLease = system.Acquire( Key( .25f ) );
			Assert.AreSame( assetLease.Value, sceneLease.Value );
			otherScene = new Scene();
			var otherSystem = otherScene.GetSystem<GltfMaterialSceneSystem>();
			Assert.IsNotNull( otherSystem );
			var otherLease = otherSystem.Acquire( Key( .25f ) );
			Assert.AreSame( assetLease.Value, otherLease.Value, "Different scenes and an asset owner share the same process cache." );
			replacement = system.Acquire( Key( .75f ) );
			Assert.AreNotSame( assetLease.Value, replacement.Value );
			Assert.AreEqual( .25f, assetLease.Value.GetVector4( "g_vColorTint" ).x, .0001f );
			Assert.AreEqual( .75f, replacement.Value.GetVector4( "g_vColorTint" ).x, .0001f );
			Assert.AreEqual( .25f, assetLease.Value.GetVector4( "g_flRoughness" ).x, .0001f );
			Assert.AreEqual( .75f, replacement.Value.GetVector4( "g_flRoughness" ).x, .0001f );
			Assert.AreEqual( templateTint, template.GetVector4( "g_vColorTint" ), "Copy edits must not mutate the source template." );
			var old = assetLease.Value;
			scene.Destroy(); scene = null;
			Assert.IsTrue( old.IsValid, "Asset owner must keep the shared material alive after scene destruction." );
			GltfMaterialLease<Material> unexpected = null;
			try { Assert.ThrowsException<ObjectDisposedException>( () => unexpected = system.Acquire( Key( .25f ) ), "A destroyed scene system must not create untracked leases." ); }
			finally { unexpected?.Dispose(); }
			system.Dispose(); // Repeated disposal must be harmless.
			Assert.AreSame( old, otherLease.Value );
			otherScene.Destroy(); otherScene = null;
			Assert.IsTrue( old.IsValid, "Asset lease must survive both scene lifetimes." );
			assetLease.Dispose(); assetLease = null;
			Assert.IsFalse( old.IsValid, "The final lease must invalidate the procedural material wrapper; native handle release is queued." );
		}
		finally
		{
			replacement?.Dispose(); assetLease?.Dispose(); scene?.Destroy(); otherScene?.Destroy();
			GltfMaterialCache.Shared.Clear();
			Game.TypeLibrary = oldLibrary;
		}
	}
}
