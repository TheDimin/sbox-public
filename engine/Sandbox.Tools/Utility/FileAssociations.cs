using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Editor;

internal class FileAssociations
{
	/// <summary>
	/// Creates/updates file associations between .sbproj files and the editor
	/// </summary>
	internal static void Create()
	{
		if ( OperatingSystem.IsWindows() )
		{
			CreateWindows();
		}
		else if ( OperatingSystem.IsLinux() )
		{
			CreateLinux();
		}
	}

	private static void CreateLinux()
	{
		try
		{
			var data = Environment.GetEnvironmentVariable( "XDG_DATA_HOME" );
			if ( string.IsNullOrEmpty( data ) )
				data = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".local", "share" );

			// Raw strings keep the source file's line endings, and desktop files need LF.
			void Write( string path, string text )
			{
				path = Path.Combine( data, path );
				Directory.CreateDirectory( Path.GetDirectoryName( path ) );
				File.WriteAllText( path, text.ReplaceLineEndings( "\n" ) );
			}

			// The file manager finds the MIME icon by name in the hicolor theme.
			var icon = Path.Combine( data, "icons/hicolor/128x128/mimetypes/application-x-sbproj.png" );
			Directory.CreateDirectory( Path.GetDirectoryName( icon ) );
			File.Copy( FileSystem.Root.GetFullPath( "core/tools/images/logo_rounded.png" ), icon, true );

			Write( "mime/packages/sandbox-project.xml", """
				<?xml version="1.0" encoding="UTF-8"?>
				<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
				  <mime-type type="application/x-sbproj">
				    <comment>Sandbox Project File</comment>
				    <icon name="application-x-sbproj"/>
				    <glob pattern="*.sbproj"/>
				  </mime-type>
				</mime-info>
				""" );

			// Exec values are double quoted, and \ " ` $ must be escaped inside the quotes.
			var editor = FileSystem.Root.GetFullPath( "sbox-dev" ).Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" ).Replace( "`", "\\`" ).Replace( "$", "\\$" );

			Write( "applications/sandbox-project.desktop", $"""
				[Desktop Entry]
				Type=Application
				Name=s&box
				Comment=Open a Sandbox project
				Icon=application-x-sbproj
				Exec="{editor}" -project %f
				MimeType=application/x-sbproj;
				NoDisplay=true
				""" );

			// Refresh the desktop caches. Each tool is optional, so one shell runs them all and ignores failures.
			Process.Start( "sh", ["-c", "update-mime-database \"$0/mime\"; gtk-update-icon-cache -f -t \"$0/icons/hicolor\"; xdg-mime default sandbox-project.desktop application/x-sbproj", data] );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "Failed to associate project file extension" );
		}
	}

	private static void CreateWindows()
	{
		var sboxExeFilePath = $"\"{FileSystem.Root.GetFullPath( "sbox-dev.exe" )}\"";

		try
		{
			RegistryKey fileTypeKey = Registry.CurrentUser.CreateSubKey( @"SOFTWARE\Classes\Sandbox.ProjectFile" );
			fileTypeKey.SetValue( "", "Sandbox Project File" );
			fileTypeKey.CreateSubKey( "DefaultIcon" ).SetValue( "", sboxExeFilePath );

			RegistryKey shellKey = fileTypeKey.CreateSubKey( "shell" );
			RegistryKey shellOpenKey = shellKey.CreateSubKey( "open" );
			shellOpenKey.SetValue( "", "Open" );
			shellOpenKey.CreateSubKey( "command" ).SetValue( "", sboxExeFilePath + " -project \"%1\"" );

			RegistryKey fileExtensionKey = Registry.CurrentUser.CreateSubKey( @"SOFTWARE\Classes\.sbproj" );
			fileExtensionKey.SetValue( "", "Sandbox.ProjectFile" );

			// Tell explorer the file association has been changed
			SHChangeNotify( 0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "Failed to associate project file extension" );
		}
	}

	[DllImport( "shell32.dll", CharSet = CharSet.Auto, SetLastError = true )]
	public static extern void SHChangeNotify( uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2 );
}
