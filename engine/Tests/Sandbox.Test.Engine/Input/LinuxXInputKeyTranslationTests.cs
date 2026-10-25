using Editor;
using NativeEngine;
using Sandbox.Engine;

namespace EngineTests;

[TestClass]
public class LinuxXInputKeyTranslationTests
{
	[TestMethod]
	[DataRow( 0x77UL, "W" )]
	[DataRow( 0x57UL, "W" )]
	[DataRow( 0x20UL, "SPACE" )]
	[DataRow( 0xff09UL, "TAB" )]
	[DataRow( 0xff1bUL, "ESCAPE" )]
	[DataRow( 0xffe1UL, "SHIFT" )]
	[DataRow( 0xffe2UL, "RSHIFT" )]
	[DataRow( 0xff51UL, "LEFTARROW" )]
	[DataRow( 0xff55UL, "PGUP" )]
	[DataRow( 0xffbeUL, "F1" )]
	[DataRow( 0xff8dUL, "KP_ENTER" )]
	[DataRow( 0xffb7UL, "KP_7" )]
	[DataRow( 0xff95UL, "KP_7" )]
	public void X11KeysUseExistingEngineBindings( ulong symbol, string binding )
	{
		var expected = KeyTranslation.StringToButtonCode( binding );
		Assert.AreNotEqual( ButtonCode.BUTTON_CODE_INVALID, expected );
		Assert.AreEqual( expected, LinuxXInputStream.TranslateKeySym( symbol ) );
	}

	[TestMethod]
	public void UnsupportedKeyDoesNotCreateAGameBinding()
	{
		Assert.AreEqual( ButtonCode.BUTTON_CODE_INVALID, LinuxXInputStream.TranslateKeySym( 0x10020ac ) );
	}
}
