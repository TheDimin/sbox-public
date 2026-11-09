using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>New Engine-stage identity plus the unchanged bounded default renderer smoke.</summary>
public static class StageGateDefaultRendererSmoke
{
	public static void VerifyStage()
	{
		Check( typeof( Model ).Assembly, "1464ab29-208e-4022-b94e-089cb0015f12", "30985d93ff6266240b5666ba8282dcde561ce24613fa89e0b3060db42e0e98d1" );
		Check( typeof( GltfReader ).Assembly, "c99cbed5-9108-4caa-a331-6b2a1f589bb2", "b265e0d75f9c88149266994f04768f07e845c67dabe29e8050449e9409fe82e2" );
		Check( typeof( Editor.GltfAssetImport ).Assembly, "01d79f9c-a5ad-4d9b-80f2-721dddb2c092", "539c10536c6df8a769eb4f91488ebfe9a048fdf1bd7cd589d0383479b2641b4d" );
	}
	public static DefaultRendererSmoke.Result[] Run( string checkout, string oraclePath ) => DefaultRendererSmoke.Run( checkout, oraclePath );
	private static void Check( Assembly assembly, string mvid, string hash )
	{
		if ( AssemblyLoadContext.GetLoadContext( assembly ) != AssemblyLoadContext.Default || assembly.ManifestModule.ModuleVersionId.ToString() != mvid )
			throw new InvalidOperationException( "Wrong loaded stage identity: " + assembly.GetName().Name );
		using var stream = File.OpenRead( assembly.Location );
		if ( Convert.ToHexStringLower( SHA256.HashData( stream ) ) != hash )
			throw new InvalidOperationException( "Wrong on-disk stage hash: " + assembly.GetName().Name );
	}
}
