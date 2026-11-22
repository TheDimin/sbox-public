namespace CompilingTests.CodeAccess;

[TestClass]
public class AdaptiveTriggerAccessTest
{
	[TestMethod]
	public void AdaptiveResistanceIsAvailableToGameAssemblies()
	{
		var result = CodeAccessGate.Verify( """
			using Sandbox;
			public static class TriggerExample
			{
				public static bool Enabled => Preferences.AdaptiveTriggers;
				public static bool Apply()
				{
					using ( Input.PlayerScope( Input.PlayerIndex.Controller1 ) )
						return Input.TrySetAdaptiveTriggerResistance( 0.25f, 0.5f, 1000 );
				}
				public static void Stop() => Input.StopAllHaptics();
			}
			""" );
		Assert.IsFalse( result.EmitFailed, result.ToString() );
		Assert.AreEqual( 0, result.Denied.Count, result.ToString() );
	}
}
