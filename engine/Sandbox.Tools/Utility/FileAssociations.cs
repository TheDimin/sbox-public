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

	/// <summary>
	/// Writes the shared-mime-info type and desktop entry that make .sbproj files open in the editor.
	/// Returns the desktop entry file name. Does not run any external tool.
	/// </summary>
	internal static string WriteLinuxFiles( string dataHome, string editorPath, string iconSourcePath )
	{
		var mimeDirectory = Path.Combine( dataHome, "mime", "packages" );
		var applicationsDirectory = Path.Combine( dataHome, "applications" );
		Directory.CreateDirectory( mimeDirectory );
		Directory.CreateDirectory( applicationsDirectory );

		// The file manager looks up the MIME type icon by its generic name in the hicolor theme.
		var iconDirectory = Path.Combine( dataHome, "icons", "hicolor", "128x128", "mimetypes" );
		Directory.CreateDirectory( iconDirectory );
		File.Copy( iconSourcePath, Path.Combine( iconDirectory, "application-x-sbproj.png" ), true );

		File.WriteAllText( Path.Combine( mimeDirectory, "sandbox-project.xml" ), string.Join( "\n",
			"<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
			"<mime-info xmlns=\"http://www.freedesktop.org/standards/shared-mime-info\">",
			"  <mime-type type=\"application/x-sbproj\">",
			"    <comment>Sandbox Project File</comment>",
			"    <icon name=\"application-x-sbproj\"/>",
			"    <glob pattern=\"*.sbproj\"/>",
			"  </mime-type>",
			"</mime-info>",
			"" ) );

		// Desktop entry values are quoted with double quotes, and \ " ` $ must be escaped inside them.
		var quotedPath = editorPath.Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" ).Replace( "`", "\\`" ).Replace( "$", "\\$" );

		var desktopName = "sandbox-project.desktop";
		File.WriteAllText( Path.Combine( applicationsDirectory, desktopName ), string.Join( "\n",
			"[Desktop Entry]",
			"Type=Application",
			"Name=s&box",
			"Comment=Open a Sandbox project",
			"Icon=application-x-sbproj",
			$"Exec=\"{quotedPath}\" -project %f",
			"MimeType=application/x-sbproj;",
			"NoDisplay=true",
			"Terminal=false",
			"Categories=Development;Game;",
			"" ) );

		return desktopName;
	}

	private static void CreateLinux()
	{
		try
		{
			var dataHome = Environment.GetEnvironmentVariable( "XDG_DATA_HOME" );
			if ( string.IsNullOrEmpty( dataHome ) )
				dataHome = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.UserProfile ), ".local", "share" );

			var desktopName = WriteLinuxFiles( dataHome, FileSystem.Root.GetFullPath( "sbox-dev" ), FileSystem.Root.GetFullPath( "core/tools/images/logo_rounded.png" ) );

			// Both tools are optional. A desktop without them just keeps the files we wrote.
			RunTool( "update-mime-database", Path.Combine( dataHome, "mime" ) );
			RunTool( "gtk-update-icon-cache", "-f", "-t", Path.Combine( dataHome, "icons", "hicolor" ) );
			RunTool( "xdg-mime", "default", desktopName, "application/x-sbproj" );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "Failed to associate project file extension" );
		}
	}

	private static void RunTool( string tool, params string[] args )
	{
		try
		{
			var info = new ProcessStartInfo( tool ) { UseShellExecute = false, RedirectStandardError = true };
			foreach ( var arg in args ) info.ArgumentList.Add( arg );

			using var process = Process.Start( info );
			if ( process is null ) return;
			if ( !process.WaitForExit( 5000 ) ) process.Kill();
		}
		catch ( Exception ex )
		{
			Log.Info( $"Could not run {tool}: {ex.Message}" );
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
