using System;
using System.IO;
using Editor;

namespace Sandbox.Tests;

[TestClass]
public class FileAssociationsTests
{
	[TestMethod]
	public void LinuxFilesAssociateSbprojWithTheEditor()
	{
		var dataHome = Path.Combine( Path.GetTempPath(), "sbox-fileassoc-" + Guid.NewGuid() );

		try
		{
			var logo = Path.Combine( dataHome, "logo-source.png" );
			Directory.CreateDirectory( dataHome );
			File.WriteAllBytes( logo, [1, 2, 3] );

			var desktopName = FileAssociations.WriteLinuxFiles( dataHome, "/games/my sbox/sbox-dev", logo );

			var mime = File.ReadAllText( Path.Combine( dataHome, "mime", "packages", "sandbox-project.xml" ) );
			StringAssert.Contains( mime, "application/x-sbproj" );
			StringAssert.Contains( mime, "*.sbproj" );

			var desktop = File.ReadAllText( Path.Combine( dataHome, "applications", desktopName ) );
			StringAssert.Contains( desktop, "MimeType=application/x-sbproj;" );
			StringAssert.Contains( desktop, "Exec=\"/games/my sbox/sbox-dev\" -project %f" );
			Assert.IsFalse( desktop.Contains( "\r" ) );
			StringAssert.Contains( desktop, "Icon=application-x-sbproj" );

			CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, File.ReadAllBytes( Path.Combine( dataHome, "icons", "hicolor", "128x128", "mimetypes", "application-x-sbproj.png" ) ) );
			StringAssert.Contains( mime, "<icon name=\"application-x-sbproj\"/>" );
		}
		finally
		{
			if ( Directory.Exists( dataHome ) ) Directory.Delete( dataHome, true );
		}
	}
}
