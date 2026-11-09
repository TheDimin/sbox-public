using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Tests;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

[TestClass]
public sealed class GltfSourceTexturePresenceKeyTests
{
	[TestMethod]
	[DataRow( "g_flGltfHasBaseColorTexture" )]
	[DataRow( "g_flGltfHasMetallicRoughnessTexture" )]
	[DataRow( "g_flGltfHasOcclusionTexture" )]
	[DataRow( "g_flGltfHasEmissiveTexture" )]
	public void ExactRolePresenceValuesAreFrozenAndSeparateCachedMaterials( string changedRole )
	{
		var template = new object(); var creations = 0;
		var values = new List<KeyValuePair<string, float>>
		{
			new( "g_flGltfHasBaseColorTexture", 0 ), new( "g_flGltfHasMetallicRoughnessTexture", 0 ),
			new( "g_flGltfHasOcclusionTexture", 0 ), new( "g_flGltfHasEmissiveTexture", 0 )
		};
		var absent = new GltfMaterialKey( template, 0, [], [], [], values );
		var sameAbsent = new GltfMaterialKey( template, 0, [], [], [], values );
		values[values.FindIndex( value => value.Key == changedRole )] = new( changedRole, 1 );
		var present = new GltfMaterialKey( template, 0, [], [], [], values );
		Assert.IsTrue( absent.Scalars.ToArray().All( value => value.Value == 0 ), "Caller mutation must not change an existing snapshot." );
		Assert.AreEqual( 1f, present.Scalars.ToArray().Single( value => value.Key == changedRole ).Value );
		Assert.AreNotEqual( absent, present, "Every exact role flag changes material identity independently of texture/uniform peers." );
		using var cache = new GltfMaterialCacheCore<object>( _ => { creations++; return new object(); }, _ => { }, _ => true );
		using var first = cache.Acquire( absent ); using var second = cache.Acquire( sameAbsent ); using var changed = cache.Acquire( present );
		Assert.AreSame( first.Value, second.Value ); Assert.AreNotSame( first.Value, changed.Value ); Assert.AreEqual( 2, creations );
		var retained = second.Value; first.Dispose(); Assert.AreSame( retained, second.Value, "A live equal-key peer survives another lease's release." );
	}
}
