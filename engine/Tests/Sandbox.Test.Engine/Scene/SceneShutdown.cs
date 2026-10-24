namespace SceneTests;

[TestClass]
[DoNotParallelize]
public class SceneShutdownTest : SceneTest
{
	[TestMethod]
	public void DestroyStageReadsLatestComponentsBeforeTeardownInOrder()
	{
		var scene = new Scene();
		var target = scene.CreateObject();
		var probe = target.Components.Create<SceneShutdownProbe>();
		probe.Value = 5;
		var captured = 0;
		var ownerWasValid = false;
		using var prepare = scene.AddHook( GameObjectSystem.Stage.SceneDestroying, -10,
			() => probe.Value += 2, nameof( SceneShutdownTest ), "Prepare shutdown state" );
		using var commit = scene.AddHook( GameObjectSystem.Stage.SceneDestroying, 10, () =>
		{
			captured = scene.GetAllComponents<SceneShutdownProbe>().Single().Value;
			ownerWasValid = probe.IsValid && !probe.DestroyCalled;
		}, nameof( SceneShutdownTest ), "Capture shutdown state" );
		probe.Value = 17;

		scene.Destroy();

		Assert.AreEqual( 19, captured );
		Assert.IsTrue( ownerWasValid );
		Assert.IsTrue( probe.DestroyCalled );
		Assert.IsFalse( scene.IsValid );
	}

	[TestMethod]
	public void DestroyStageRunsOnceIncludingRecursiveAndRepeatedDestroy()
	{
		var scene = new Scene();
		var calls = 0;
		using var hook = scene.AddHook( GameObjectSystem.Stage.SceneDestroying, 0, () =>
		{
			calls++;
			scene.Destroy();
		}, nameof( SceneShutdownTest ), "Recursive destroy" );

		scene.Destroy();
		scene.Destroy();

		Assert.AreEqual( 1, calls );
		Assert.IsFalse( scene.IsValid );
	}
}

public class SceneShutdownProbe : Component
{
	public int Value { get; set; }
	public bool DestroyCalled { get; private set; }

	protected override void OnDestroy()
	{
		DestroyCalled = true;
	}
}
