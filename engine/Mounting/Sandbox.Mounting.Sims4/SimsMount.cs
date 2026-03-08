using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Mounting.Sims4;
using NLog;
using Sandbox.Mounting;
using Sandbox.Mounting.Sims4;

public readonly record struct ObjectDefinitionSet(
	string Label,
	DbpfRecord Record,
	string ResolvedName,
	IReadOnlyList<ulong> ModelInstances,
	IReadOnlyList<ulong> GeomInstances,
	IReadOnlyList<ulong> MaterialInstances,
	IReadOnlyList<ulong> TextureInstances )
{
	public override string ToString()
	{
		var sb = new StringBuilder();
		sb.AppendLine( $"[{Label}] type_id=0x{(uint)Record.ResourceType:X8}" );
		sb.AppendLine( $"[{Label}] group_id=0x{Record.ResourceGroup:X8}" );
		sb.AppendLine( $"[{Label}] instance_id=0x{Record.InstanceId:X16}" );
		sb.AppendLine( $"[{Label}] resolved_name={(string.IsNullOrWhiteSpace( ResolvedName ) ? "<unresolved>" : ResolvedName)}" );
		sb.AppendLine( $"[{Label}] file_offset=0x{Record.FileOffset:X}" );
		sb.AppendLine( $"[{Label}] compression_type=0x{(ushort)Record.CompressionType:X4}" );
		sb.AppendLine( $"[{Label}] compressed_size={Record.CompressedSize}" );
		sb.AppendLine( $"[{Label}] decompressed_size={Record.DecompressedSize}" );
		sb.AppendLine( $"[{Label}] model_instances={FormatInstances( ModelInstances )}" );
		sb.AppendLine( $"[{Label}] geom_instances={FormatInstances( GeomInstances )}" );
		sb.AppendLine( $"[{Label}] material_instances={FormatInstances( MaterialInstances )}" );
		sb.AppendLine( $"[{Label}] texture_instances={FormatInstances( TextureInstances )}" );

		return sb.ToString().TrimEnd();
	}

	private static string FormatInstances( IReadOnlyList<ulong> instances ) =>
		instances.Count == 0
			? "[]"
			: $"[{string.Join( ", ", instances.Select( x => $"0x{x:X16}" ) )}]";
}

/// <summary>
/// Mounts The Sims 4 .package files into S&amp;box, exposing their textures
/// through the standard mounting API.
///
/// Supported resource types
/// ────────────────────────
///   Texture  (TypeID 0x00B2D882 — DDS image)
///
/// Discovery order
/// ───────────────
///   1. Steam (App ID 1222670)
///   2. EA App default install path
///   3. The path set in SIMS4_DIR environment variable (for custom installs)
///
/// Sims 4 resources do not have human-readable file names — each resource is
/// identified by a 64-bit instance hash. Textures are mounted at:
///   mount://sims4/textures/&lt;groupId&gt;/&lt;instanceId&gt;
/// </summary>
public class SimsMount : BaseGameMount
{
	new internal static Sandbox.Diagnostics.Logger Log = new Sandbox.Diagnostics.Logger( "Sims4-Mount" );
	public override string Ident => "sims4";
	public override string Title => "The Sims 4";

	private List<DbpfPackage> packages = new List<DbpfPackage>();

	// TS4 on Steam
	const long AppId = 1222670;

	string _gameDir;

	protected override void Initialize( InitializeContext context )
	{
		// 1. Steam
		if ( context.IsAppInstalled( AppId ) )
		{
			Log.Info( "Sims 4 Steam install detected" );
			var dir = context.GetAppDirectory( AppId );
			if ( System.IO.Directory.Exists( dir ) )
			{
				Log.Info( $"Sims 4 Steam directory: {dir}" );
				_gameDir = dir;
				IsInstalled = true;
				return;
			}
		}

		//Detecting it trough EA app requires us to know if the user owns the game... 
		return;
		// 2. EA App common paths
		//foreach ( var path in EaAppPaths )
		//{
		//	if ( System.IO.Directory.Exists( path ) )
		//	{
		//		_gameDir = path;
		//		IsInstalled = true;
		//		return;
		//	}
		//}
	}

	protected override Task Mount( MountContext context )
	{
		if ( string.IsNullOrWhiteSpace( _gameDir ) || !System.IO.Directory.Exists( _gameDir ) )
			return Task.CompletedTask;

		var dataDir = System.IO.Path.Combine( _gameDir, "Data" );
		if ( !System.IO.Directory.Exists( dataDir ) )
			return Task.CompletedTask;

		foreach ( var file in System.IO.Directory.EnumerateFiles( dataDir, "*.package", SearchOption.AllDirectories ) )
		{
			MountPackage( context, file );
		}

		IsMounted = true;
		return Task.CompletedTask;
	}


	private void MountPackage( MountContext context, string file )
	{
		var package = DbpfPackage.Open( file );

		{
			int count = 0;
			foreach ( var item in package.GetGeomRecords() )
			{
				Log.Info( $"Found GEOM record: type=0x{(uint)item.ResourceType:X8} group=0x{item.ResourceGroup:X8} instance=0x{item.InstanceId:X16}" );
				string DisplayName = "";
				if ( !package.TryResolveName( item.InstanceId, out DisplayName ) )
				{
					DisplayName = $"{item.InstanceId:X16}";
				}

				var mountPath = $"Models/{DisplayName}";

				context.Add( Sandbox.Mounting.ResourceType.Model, mountPath, new ModelLoader( package, item ) );
				count++;
				if ( count > 5 )
					break;
			}
			//foreach ( var record in package.GetTextureRecords() )
			//{
			//	var mountPath = $"mount://{Ident}/textures/{record.ResourceGroup:X8}/{record.InstanceId:X16}";
			//	context.Add( ResourceType.Texture, mountPath, new Sims4TextureLoader( package, record ) );
			//	Log.Info( $"Mounted texture: {mountPath}" );
			//}
		}
		packages.Add( package );
	}


	protected override void Shutdown()
	{
		foreach ( var item in packages )
		{
			item.Dispose();
		}
	}
}
