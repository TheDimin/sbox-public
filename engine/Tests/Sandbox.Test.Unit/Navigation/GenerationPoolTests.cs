using Sandbox.Navigation.Generation;

namespace NavigationTests;

[TestClass]
public class GenerationPoolTests
{
	private sealed class Scratch
	{
		internal long Bytes;
		internal bool Released;
	}

	private static GenerationPool<Scratch> CreatePool( long budget ) => new(
		() => new Scratch(), item => item.Released = true, item => item.Bytes, budget );

	[TestMethod]
	public void ActiveBuildReusesBuffersAboveIdleBudget()
	{
		using var pool = CreatePool( 16 );
		var item = pool.Get();
		item.Bytes = 32;
		pool.Return( item, 100 );
		pool.Trim( 100 + GenerationPool<Scratch>.IdleMilliseconds - 1 );

		Assert.IsFalse( item.Released );
		Assert.AreSame( item, pool.Get() );
		Assert.AreEqual( 0L, pool.RetainedBytes );
		pool.Return( item, 200 );
	}

	[TestMethod]
	public void IdleTrimMeetsBudgetWithoutReleasingCheckedOutState()
	{
		using var pool = CreatePool( 16 );
		var checkedOut = pool.Get();
		checkedOut.Bytes = 64;
		var oldest = pool.Get();
		oldest.Bytes = 24;
		var retained = pool.Get();
		retained.Bytes = 8;
		pool.Return( oldest, 100 );
		pool.Return( retained, 100 );
		pool.Trim( 100 + GenerationPool<Scratch>.IdleMilliseconds );

		Assert.IsTrue( oldest.Released );
		Assert.IsFalse( retained.Released );
		Assert.IsFalse( checkedOut.Released );
		Assert.AreEqual( 8L, pool.RetainedBytes );
		Assert.AreSame( retained, pool.Get() );
		pool.Return( checkedOut, 200 );
		pool.Return( retained, 200 );
	}

	[TestMethod]
	public void TrimmedPoolRefillsAndAccountsForChangedCapacity()
	{
		using var pool = CreatePool( 16 );
		var original = pool.Get();
		original.Bytes = 32;
		pool.Return( original, 100 );
		pool.Trim( 100 + GenerationPool<Scratch>.IdleMilliseconds );
		Assert.IsTrue( original.Released );

		var next = pool.Get();
		Assert.AreNotSame( original, next );
		next.Bytes = 8;
		pool.Return( next, 200 );
		Assert.AreSame( next, pool.Get() );
		next.Bytes = 12;
		pool.Return( next, 300 );
		Assert.AreEqual( 12L, pool.RetainedBytes );
		pool.Trim( 300 + GenerationPool<Scratch>.IdleMilliseconds );
		Assert.AreSame( next, pool.Get() );
		pool.Return( next, 400 );
	}

	[TestMethod]
	public void HeightfieldCanBuildFreshSpansAfterBuffersAreReleased()
	{
		var min = Vector3.Zero;
		var max = Vector3.One * 64;
		using var field = new Heightfield( 2, 2, min, max, 1, 1 );
		field.AddOrMergeSpan( 0, 0, 0, 4, Constants.WALKABLE_AREA, 0 );
		field.EnsureCompressed();
		Assert.IsTrue( field.RetainedBytes > 0 );
		field.ReleaseMemory();
		Assert.AreEqual( 0L, field.RetainedBytes );

		field.Init( 2, 2, min, max, 1, 1 );
		field.AddOrMergeSpan( 1, 1, 8, 12, Constants.WALKABLE_AREA, 0 );
		using var compact = field.BuildCompactHeightfield( 1, 1 );
		Assert.AreEqual( 1, compact.SpanCount );
		Assert.AreEqual( 0, compact.Cells[0].Count );
		Assert.AreEqual( 1, compact.Cells[3].Count );
		Assert.AreEqual( (ushort)12, compact.Spans[0].StartY );
		Assert.AreEqual( Constants.WALKABLE_AREA, compact.Areas[0] );
	}
}
