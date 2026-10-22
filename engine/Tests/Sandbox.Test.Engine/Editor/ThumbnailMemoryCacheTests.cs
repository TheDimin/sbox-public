using Editor;

namespace EditorTests;

[TestClass]
public class ThumbnailMemoryCacheTests
{
	[TestMethod]
	public void BrowsingKeepsRecentlyUsedImagesWithinTheBudget()
	{
		var cache = new ThumbnailMemoryCache<int, object>( 64 );
		var first = new object();
		var second = new object();
		var third = new object();
		cache.Store( 1, first, 32 );
		cache.Store( 2, second, 32 );
		Assert.IsTrue( cache.TryGet( 1, out var imageInWidget ) );

		cache.Store( 3, third, 32 );

		Assert.AreEqual( 64L, cache.RetainedBytes );
		Assert.AreEqual( 2, cache.Count );
		Assert.IsFalse( cache.TryGet( 2, out _ ), "The least recently used image should leave the resident set." );
		Assert.IsTrue( cache.TryGet( 1, out var cachedFirst ) );
		Assert.AreSame( first, cachedFirst );
		Assert.IsTrue( cache.TryGet( 3, out var cachedThird ) );
		Assert.AreSame( third, cachedThird );

		cache.Store( 4, new object(), 64 );
		Assert.IsFalse( cache.TryGet( 1, out _ ) );
		Assert.AreSame( first, imageInWidget, "Eviction must leave the image owned by a widget usable." );
	}

	[TestMethod]
	public void ReplacementAndRemovalReleaseTheOldBudget()
	{
		var cache = new ThumbnailMemoryCache<int, object>( 64 );
		var replacement = new object();
		cache.Store( 1, new object(), 48 );
		cache.Store( 1, replacement, 16 );
		cache.Store( 2, new object(), 48 );

		Assert.AreEqual( 64L, cache.RetainedBytes );
		Assert.AreEqual( 2, cache.Count );
		Assert.IsTrue( cache.TryGet( 1, out var image ) );
		Assert.AreSame( replacement, image );

		cache.Remove( 1 );
		cache.Remove( 1 );
		Assert.AreEqual( 48L, cache.RetainedBytes );
		Assert.AreEqual( 1, cache.Count );
		Assert.IsFalse( cache.TryGet( 1, out _ ) );
	}

	[TestMethod]
	public void OversizedImagesRemainWithTheCallerWithoutDisplacingResidentImages()
	{
		var cache = new ThumbnailMemoryCache<int, object>( 64 );
		var resident = new object();
		var oversized = new object();
		cache.Store( 1, resident, 64 );
		cache.Store( 2, oversized, 65 );

		Assert.AreEqual( 64L, cache.RetainedBytes );
		Assert.AreEqual( 1, cache.Count );
		Assert.IsTrue( cache.TryGet( 1, out var image ) );
		Assert.AreSame( resident, image );
		Assert.IsFalse( cache.TryGet( 2, out _ ) );
		cache.Store( 1, null, 0 );
		Assert.AreEqual( 0L, cache.RetainedBytes );
		Assert.AreEqual( 0, cache.Count );
	}

	[TestMethod]
	public void ScrollingThroughAWholeCatalogDoesNotGrowTheResidentSet()
	{
		const int imageBytes = 256 * 256 * 4;
		const long budget = 64 * 1024 * 1024;
		var cache = new ThumbnailMemoryCache<int, object>( budget );
		for ( var i = 0; i < 20_000; i++ )
			cache.Store( i, new object(), imageBytes );

		Assert.AreEqual( budget, cache.RetainedBytes );
		Assert.AreEqual( 256, cache.Count );
		Assert.IsFalse( cache.TryGet( 0, out _ ) );
		Assert.IsTrue( cache.TryGet( 19_999, out _ ) );

		var reloaded = new object();
		cache.Store( 0, reloaded, imageBytes );
		Assert.IsTrue( cache.TryGet( 0, out var image ) );
		Assert.AreSame( reloaded, image );
		Assert.AreEqual( budget, cache.RetainedBytes );
		Assert.AreEqual( 256, cache.Count );
	}
}
