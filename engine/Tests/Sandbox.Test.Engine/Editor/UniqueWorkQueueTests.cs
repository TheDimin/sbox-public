using System;

namespace EditorTests;

[TestClass]
public class UniqueWorkQueueTests
{
	[TestMethod]
	public void DuplicateEnqueueKeepsOnePendingItem()
	{
		var queue = new Editor.UniqueWorkQueue<int>();

		for ( var i = 0; i < 100_000; i++ )
		{
			queue.Enqueue( 42 );
		}

		Assert.AreEqual( 1, queue.Count );
		Assert.IsTrue( queue.TryDequeue( out var value ) );
		Assert.AreEqual( 42, value );
		Assert.AreEqual( 0, queue.Count );
	}

	[TestMethod]
	public void ExistingItemsCanBeReprioritized()
	{
		var queue = new Editor.UniqueWorkQueue<int>();
		queue.Enqueue( 1 );
		queue.Enqueue( 2 );
		queue.Enqueue( 3 );

		queue.Enqueue( 1 );
		queue.EnqueueFirst( 3 );

		Assert.IsTrue( queue.TryDequeue( out var first ) );
		Assert.IsTrue( queue.TryDequeue( out var second ) );
		Assert.IsTrue( queue.TryDequeue( out var third ) );
		Assert.AreEqual( 3, first );
		Assert.AreEqual( 2, second );
		Assert.AreEqual( 1, third );
	}

	[TestMethod]
	public void RemoveAndConditionalEnqueuePreserveMembership()
	{
		var queue = new Editor.UniqueWorkQueue<string>( StringComparer.OrdinalIgnoreCase );
		queue.Enqueue( "model.vmdl" );

		Assert.IsTrue( queue.Contains( "MODEL.VMDL" ) );
		Assert.IsTrue( queue.Remove( "MODEL.VMDL" ) );
		Assert.IsFalse( queue.Enqueue( "missing.vmdl", addIfMissing: false ) );
		Assert.AreEqual( 0, queue.Count );
	}
}
