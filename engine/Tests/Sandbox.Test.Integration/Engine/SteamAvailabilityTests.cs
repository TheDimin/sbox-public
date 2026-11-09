namespace Engine;

[TestClass]
public class SteamAvailabilityTests
{
	[TestMethod]
	public void UnavailableSteamDoesNotExposeOptionalServices()
	{
		if ( NativeEngine.Steam.SteamUser().IsValid )
			Assert.Inconclusive( "This regression requires the native Steam interface to be unavailable." );

		Assert.IsFalse( Steamworks.SteamClient.IsValid, "Managed initialization alone must not report a valid Steam client." );
		Assert.IsFalse( Game.IsRunningOnHandheld, "Handheld detection must not call an absent Steam Utils interface." );

		IRichPresenceSystem presence = new SteamRichPresenceSystem();
		presence.Poll();
	}
}
