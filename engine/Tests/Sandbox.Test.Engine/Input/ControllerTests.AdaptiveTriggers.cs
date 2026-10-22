using NativeEngine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Sandbox.Engine;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Input = Sandbox.Input;

namespace EngineTests;

public unsafe partial class ControllerTests
{
	static ushort vendor, product;
	static int effectWrites;
	static byte[] effectPacket;
	static bool effectSucceeds;
	static IntPtr effectController;
	static ulong steamHandle;

	[TestMethod]
	public void SteamResistanceUsesTheSdkLayoutAndStrengthRange()
	{
		var effect = Steamworks.DualSenseTriggerEffect.Resistance( 70, 255 );
		var expected = new byte[120];
		expected[0] = 3;
		expected[8] = expected[64] = 1;
		expected[17] = 2;
		expected[73] = 8;
		CollectionAssert.AreEqual( expected, MemoryMarshal.AsBytes( new[] { effect }.AsSpan() ).ToArray() );
		Assert.AreEqual( (byte)1, Steamworks.DualSenseTriggerEffect.Resistance( 1, 0 ).LeftStrength );
		effect = Steamworks.DualSenseTriggerEffect.Resistance( 0, 0 );
		Array.Clear( expected );
		expected[0] = 3;
		CollectionAssert.AreEqual( expected, MemoryMarshal.AsBytes( new[] { effect }.AsSpan() ).ToArray() );
	}

	[TestMethod]
	public void SteamResistanceUsesTheSdlHandleAndSharedExpiry() => WithAdaptiveController( controller =>
	{
		var steam = UseSteamInput( 1234 );
		vendor = 0x045e; // Steam's emulated controller need not expose Sony USB IDs.
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 255, 60000, default ) );
		Assert.AreEqual( (ulong)1234, steam.Writes[0].Handle );
		Assert.AreEqual( (byte)2, steam.Writes[0].Effect.LeftStrength );
		Assert.AreEqual( (byte)8, steam.Writes[0].Effect.RightStrength );
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 255, 60000, default ) );
		Assert.AreEqual( 1, steam.Writes.Count );
		controller.UpdateAdaptiveTriggerResistance( long.MaxValue );
		Assert.AreEqual( 2, steam.Writes.Count );
		Assert.AreEqual( 0, steam.Writes[1].Effect.LeftMode );
		Assert.AreEqual( 0, steam.Writes[1].Effect.RightMode );
		Assert.AreEqual( 0, effectWrites );
	} );

	[TestMethod]
	public void UnsupportedSteamDeviceDoesNotFallBackToDirectSdlEffects() => WithAdaptiveController( controller =>
	{
		var steam = UseSteamInput( 1234 );
		steam.Type = Steamworks.InputType.PS4Controller;
		Assert.IsFalse( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		Assert.AreEqual( 0, steam.Writes.Count );
		Assert.AreEqual( 0, effectWrites );
		Steamworks.SteamInput.Interface = null;
		Assert.IsFalse( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		Assert.AreEqual( 0, effectWrites );
	} );

	[TestMethod]
	public void SteamResistanceReleasesTheOriginalHandleBeforeChangingOwner() => WithAdaptiveController( controller =>
	{
		var steam = UseSteamInput( 1234 );
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		steamHandle = 5678;
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		Assert.AreEqual( 3, steam.Writes.Count );
		Assert.AreEqual( (ulong)1234, steam.Writes[1].Handle );
		Assert.AreEqual( 0, steam.Writes[1].Effect.LeftMode );
		Assert.AreEqual( (ulong)5678, steam.Writes[2].Handle );
		Assert.AreEqual( 1, steam.Writes[2].Effect.LeftMode );
		steamHandle = 0;
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		Assert.AreEqual( (ulong)5678, steam.Writes[3].Handle );
		Assert.AreEqual( 0, steam.Writes[3].Effect.LeftMode );
		Assert.AreEqual( 1, effectWrites );
	} );

	[TestMethod]
	public void SteamInputShutdownReleasesEffectsBeforeClosingTheInterface() => WithAdaptiveController( controller =>
	{
		var steam = UseSteamInput( 1234 );
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		new Steamworks.SteamInput().DestroyInterface( false );
		Assert.IsTrue( steam.ShutDown );
		Assert.AreEqual( 2, steam.Writes.Count );
		Assert.AreEqual( 0, steam.Writes[1].Effect.LeftMode );
		Assert.AreEqual( 0, steam.Writes[1].Effect.RightMode );
		Assert.IsNull( Steamworks.SteamInput.Interface );
	} );

	[TestMethod]
	public void SteamVirtualGamepadDisconnectReleasesThePhysicalController() => WithAdaptiveController( controller =>
	{
		var steam = UseSteamInput( 1234 );
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		connected = false;
		InputRouter.OnGameControllerDisconnected( controller.DeviceId );
		Assert.AreEqual( 2, steam.Writes.Count );
		Assert.AreEqual( (ulong)1234, steam.Writes[1].Handle );
		Assert.AreEqual( 0, steam.Writes[1].Effect.LeftMode );
		Assert.AreEqual( 0, steam.Writes[1].Effect.RightMode );
		Assert.AreEqual( 0, Controller.All.Count );
		controller.UpdateAdaptiveTriggerResistance( long.MaxValue );
		Assert.AreEqual( 2, steam.Writes.Count );
	} );

	static TestSteamInput UseSteamInput( ulong handle )
	{
		steamHandle = handle;
		var input = new TestSteamInput { Self = (IntPtr)1 };
		Steamworks.SteamInput.Interface = input;
		return input;
	}

	sealed class TestSteamInput : Steamworks.SteamInputInterface
	{
		internal Steamworks.InputType Type = Steamworks.InputType.PS5Controller;
		internal readonly System.Collections.Generic.List<(ulong Handle, Steamworks.DualSenseTriggerEffect Effect)> Writes = new();
		internal bool ShutDown;
		internal override Steamworks.InputType GetInputType( ulong handle ) => ShutDown ? Steamworks.InputType.Unknown : Type;
		internal override void SetTriggerEffect( ulong handle, in Steamworks.DualSenseTriggerEffect effect )
		{
			Assert.IsFalse( ShutDown );
			Writes.Add( (handle, effect) );
		}
		internal override bool Shutdown() { ShutDown = true; return true; }
	}

	[TestMethod]
	public void AdaptiveResistanceIsAvailableToGameAssemblies()
	{
		// Compile outside the test friend assembly, then apply the same access rules as a game.
		var source = """
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
			""";
		var references = ((string)AppContext.GetData( "TRUSTED_PLATFORM_ASSEMBLIES" )).Split( Path.PathSeparator )
			.Select( path => MetadataReference.CreateFromFile( path ) );
		var compilation = CSharpCompilation.Create( "package.tests.adaptivetriggers", [CSharpSyntaxTree.ParseText( source )], references,
			new CSharpCompilationOptions( OutputKind.DynamicallyLinkedLibrary ) );
		using var output = new MemoryStream();
		var emitted = compilation.Emit( output );
		Assert.IsTrue( emitted.Success, string.Join( Environment.NewLine, emitted.Diagnostics ) );
		output.Position = 0;
		using var access = new AccessControl();
		var verified = access.VerifyAssembly( output, out var trusted );
		using ( trusted )
			Assert.IsTrue( verified.Success, string.Join( Environment.NewLine, verified.Errors ) );
	}

	[TestMethod]
	public void AdaptiveResistancePacketMatchesSdlLayout()
	{
		// Two independent strengths, right first in the device packet; all unrelated controls stay zero.
		var expected = Convert.FromHexString(
			"0C000000000000000000" + "01006E0000000000000000" + "0100460000000000000000" + "000000000000000000000000000000" );
		var packet = new byte[47];
		Array.Fill( packet, (byte)0xff );
		SdlGamepadEffects.WriteResistance( packet, 70, 110 );
		CollectionAssert.AreEqual( expected, packet );
		SdlGamepadEffects.WriteResistance( packet, 0, 0 );
		CollectionAssert.AreEqual( Convert.FromHexString(
			"0C000000000000000000" + "0500000000000000000000" + "0500000000000000000000" + "000000000000000000000000000000" ), packet );
	}

	[TestMethod]
	[DataRow( float.NaN )]
	[DataRow( float.PositiveInfinity )]
	[DataRow( float.NegativeInfinity )]
	[DataRow( -0.01f )]
	[DataRow( 1.01f )]
	public void AdaptiveResistanceRejectsInvalidStrength( float value )
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Input.TrySetAdaptiveTriggerResistance( value, 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Input.TrySetAdaptiveTriggerResistance( 0, value ) );
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( -1 )]
	public void AdaptiveResistanceRejectsInvalidDuration( int duration ) =>
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => Input.TrySetAdaptiveTriggerResistance( 0, 0, duration ) );

	[TestMethod]
	public void AdaptiveResistanceRequiresAnIdentifiedDualSense() => WithAdaptiveController( controller =>
	{
		vendor = 0x045e;
		Assert.IsFalse( controller.TrySetAdaptiveTriggerResistance( 70, 0, 500, default ) );
		vendor = 0x054c;
		product = 0x05c4;
		Assert.IsFalse( controller.TrySetAdaptiveTriggerResistance( 70, 0, 500, default ) );
		product = 0x0ce6;
		connected = false;
		Assert.IsFalse( controller.TrySetAdaptiveTriggerResistance( 70, 0, 500, default ) );
		Assert.AreEqual( 0, effectWrites );
	} );

	[TestMethod]
	public void AdaptiveResistanceRefreshDoesNotResendButExpiryReleases() => WithAdaptiveController( controller =>
	{
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 0, 60000, default ) );
		Assert.AreEqual( (byte)5, effectPacket[10] );
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 0, 60000, default ) );
		Assert.AreEqual( 1, effectWrites );
		controller.UpdateAdaptiveTriggerResistance( Stopwatch.GetTimestamp() );
		Assert.AreEqual( 1, effectWrites );
		controller.UpdateAdaptiveTriggerResistance( long.MaxValue );
		AssertReleased();
		controller.UpdateAdaptiveTriggerResistance( long.MaxValue );
		Assert.AreEqual( 2, effectWrites );
	} );

	[TestMethod]
	public void AdaptiveResistanceFailedWriteIsRetriedAndFailedReleaseIsRetained() => WithAdaptiveController( controller =>
	{
		effectSucceeds = false;
		Assert.IsFalse( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		effectSucceeds = true;
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		Assert.AreEqual( 2, effectWrites );
		effectSucceeds = false;
		Assert.IsFalse( controller.StopAdaptiveTriggerResistance() );
		controller.UpdateAdaptiveTriggerResistance( 0 );
		Assert.AreEqual( 3, effectWrites );
		effectSucceeds = true;
		controller.UpdateAdaptiveTriggerResistance( long.MaxValue );
		Assert.AreEqual( 4, effectWrites );
		AssertReleased();
	} );

	[TestMethod]
	public void AdaptiveResistanceStopsForCancelledGameAndPlayerPreference() => WithAdaptiveController( controller =>
	{
		using var lifetime = new CancellationTokenSource();
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, lifetime.Token ) );
		lifetime.Cancel();
		SdlGamepads.UpdateAdaptiveTriggers();
		AssertReleased();
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		Preferences.AdaptiveTriggers = false;
		SdlGamepads.UpdateAdaptiveTriggers();
		AssertReleased();
		Assert.AreEqual( 4, effectWrites );
	} );

	[TestMethod]
	public void AdaptiveResistanceDoesNotStopWhenOrdinaryRumbleStarts() => WithAdaptiveController( controller =>
	{
		Assert.IsTrue( Input.TrySetAdaptiveTriggerResistance( 0.25f, 0.5f ) );
		Assert.AreEqual( (byte)64, effectPacket[23] );
		Assert.AreEqual( (byte)128, effectPacket[12] );
		Input.TriggerHaptics( 1, 1 );
		Assert.AreEqual( 1, effectWrites );
		Input.StopAllHaptics();
		AssertReleased();
		Assert.AreEqual( 2, effectWrites );
	} );

	[TestMethod]
	public void AdaptiveResistanceFocusLossReleasesAndBlocksNewEffects() => WithAdaptiveController( controller =>
	{
		Assert.IsTrue( Input.TrySetAdaptiveTriggerResistance( 0.25f, 0.25f ) );
		InputRouter.OnWindowActive( false );
		AssertReleased();
		SetFocused( false );
		Assert.IsFalse( Input.TrySetAdaptiveTriggerResistance( 0.25f, 0.25f ) );
		Assert.IsTrue( Input.TrySetAdaptiveTriggerResistance( 0, 0 ) );
		AssertReleased();
		Assert.AreEqual( 3, effectWrites );
	} );

	[TestMethod]
	public void AdaptiveResistanceUsesPlayerScopeAndAllowsReleaseWhenDisabled() => WithAdaptiveController( controller =>
	{
		using ( Input.PlayerScope( Input.PlayerIndex.KeyboardAndMouse ) )
			Assert.IsFalse( Input.TrySetAdaptiveTriggerResistance( 0, 0 ) );
		Preferences.AdaptiveTriggers = false;
		Assert.IsFalse( Input.TrySetAdaptiveTriggerResistance( 0.25f, 0.25f ) );
		Assert.IsTrue( Input.TrySetAdaptiveTriggerResistance( 0, 0 ) );
		AssertReleased();
	} );

	[TestMethod]
	public void AdaptiveResistanceTargetsOnlyTheSelectedPlayer() => WithAdaptiveController( controller =>
	{
		var second = new Controller( 456 );
		Controller.All.Add( second );
		using ( Input.PlayerScope( Input.PlayerIndex.Controller2 ) )
		{
			Assert.IsTrue( Input.TrySetAdaptiveTriggerResistance( 0.25f, 0 ) );
			Assert.AreEqual( (IntPtr)456, effectController );
			Input.StopAllHaptics();
			Assert.AreEqual( (IntPtr)456, effectController );
			AssertReleased();
		}
		controller.UpdateAdaptiveTriggerResistance( long.MaxValue );
		Assert.AreEqual( 2, effectWrites );
	} );

	[TestMethod]
	public void AdaptiveResistanceHeadlessAndSuppressedCallsDoNotWrite() => WithAdaptiveController( controller =>
	{
		Input.Suppressed = true;
		Assert.IsFalse( Input.TrySetAdaptiveTriggerResistance( 0.25f, 0 ) );
		Input.Suppressed = false;
		typeof( Application ).GetProperty( nameof( Application.IsHeadless ) ).SetValue( null, true );
		Assert.IsFalse( Input.TrySetAdaptiveTriggerResistance( 0.25f, 0 ) );
		Assert.AreEqual( 0, effectWrites );
	} );

	[TestMethod]
	public void AdaptiveResistanceShutdownReleasesActiveEffects() => WithAdaptiveController( controller =>
	{
		var scene = Game.ActiveScene;
		var playing = Game.IsPlaying;
		try
		{
			Game.ActiveScene = null;
			Assert.IsTrue( Input.TrySetAdaptiveTriggerResistance( 0.25f, 0.25f ) );
			Game.Shutdown();
			AssertReleased();
			Assert.AreEqual( 2, effectWrites );
		}
		finally
		{
			Game.ActiveScene = scene;
			Game.IsPlaying = playing;
		}
	} );

	[TestMethod]
	public void AdaptiveResistanceDisconnectDoesNotReuseOldState() => WithAdaptiveController( controller =>
	{
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		connected = false;
		Assert.IsFalse( controller.StopAdaptiveTriggerResistance() );
		Assert.AreEqual( 1, effectWrites );
		connected = true;
		controller.UpdateAdaptiveTriggerResistance( long.MaxValue );
		Assert.AreEqual( 1, effectWrites );
		Assert.IsTrue( controller.TrySetAdaptiveTriggerResistance( 70, 70, 60000, default ) );
		Assert.AreEqual( 2, effectWrites );
	} );

	static void AssertReleased()
	{
		Assert.AreEqual( (byte)5, effectPacket[10] );
		Assert.AreEqual( (byte)5, effectPacket[21] );
		Assert.AreEqual( (byte)0, effectPacket[12] );
		Assert.AreEqual( (byte)0, effectPacket[23] );
	}

	static void SetFocused( bool value ) => typeof( WindowInput ).GetField( "focused", BindingFlags.Static | BindingFlags.NonPublic ).SetValue( null, value );

	static void WithAdaptiveController( Action<Controller> test )
	{
		var getVendor = SdlGamepadEffects.Functions.GetVendor;
		var getProduct = SdlGamepadEffects.Functions.GetProduct;
		var send = SdlGamepadEffects.Functions.SendEffect;
		var getSteamHandle = SdlGamepadEffects.Functions.GetSteamHandle;
		var steamInterface = Steamworks.SteamInput.Interface;
		var enabled = Preferences.AdaptiveTriggers;
		var headless = Application.IsHeadless;
		var focused = WindowInput.HasMouseFocus();
		var suppressed = Input.Suppressed;
		var mainThread = ThreadSafe.IsMainThread;
		var controllers = Controller.All.ToArray();
		try
		{
			ThreadSafe.MarkMainThread();
			typeof( Application ).GetProperty( nameof( Application.IsHeadless ) ).SetValue( null, false );
			SetFocused( true );
			Input.Suppressed = false;
			Preferences.AdaptiveTriggers = true;
			Controller.All.Clear();
			SdlGamepadEffects.Functions.GetVendor = &GetVendor;
			SdlGamepadEffects.Functions.GetProduct = &GetProduct;
			SdlGamepadEffects.Functions.SendEffect = &SendEffect;
			SdlGamepadEffects.Functions.GetSteamHandle = &GetSteamHandle;
			Steamworks.SteamInput.Interface = null;
			steamHandle = 0;
			vendor = 0x054c;
			product = 0x0ce6;
			effectWrites = 0;
			effectPacket = null;
			effectSucceeds = true;
			WithController( controller =>
			{
				Controller.All.Add( controller );
				test( controller );
			} );
		}
		finally
		{
			SdlGamepadEffects.Functions.GetVendor = getVendor;
			SdlGamepadEffects.Functions.GetProduct = getProduct;
			SdlGamepadEffects.Functions.SendEffect = send;
			SdlGamepadEffects.Functions.GetSteamHandle = getSteamHandle;
			Steamworks.SteamInput.Interface = steamInterface;
			Preferences.AdaptiveTriggers = enabled;
			Controller.All.Clear();
			Controller.All.UnionWith( controllers );
			SetFocused( focused );
			Input.Suppressed = suppressed;
			typeof( Application ).GetProperty( nameof( Application.IsHeadless ) ).SetValue( null, headless );
			typeof( ThreadSafe ).GetField( "isMainThread", BindingFlags.Static | BindingFlags.NonPublic ).SetValue( null, mainThread );
		}
	}

	[UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
	static ushort GetVendor( IntPtr gamepad ) => vendor;
	[UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
	static ushort GetProduct( IntPtr gamepad ) => product;
	[UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
	static ulong GetSteamHandle( IntPtr gamepad ) => steamHandle;
	[UnmanagedCallersOnly( CallConvs = new[] { typeof( CallConvCdecl ) } )]
	static byte SendEffect( IntPtr gamepad, byte* data, int size )
	{
		effectWrites++;
		effectController = gamepad;
		effectPacket = new ReadOnlySpan<byte>( data, size ).ToArray();
		return effectSucceeds ? (byte)1 : (byte)0;
	}
}
