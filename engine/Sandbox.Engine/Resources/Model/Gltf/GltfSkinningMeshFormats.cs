using NativeEngine;
using Sandbox.Gltf;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>Matches the stock compute shader's F_SKIN_WEIGHT_FORMAT feature. Zero preserves the existing vmdl path.</summary>
internal enum GltfSkinWeightFormat
{
	Unorm8 = 0,
	Float32 = 1,
	Unorm16 = 2
}

/// <summary>The exact declaration and byte range supplied to CreateRawVertexBuffer, before native model construction.</summary>
internal readonly record struct GltfSkinningVertexLayout( int Stride, int VertexCount, int ByteCount, RawVertexField[] Fields );

internal static class GltfSkinningMeshFormats
{
	private sealed class Registration( IntPtr nativeIdentity, string[] names, GltfSkinWeightFormat[] formats )
	{
		internal readonly IntPtr NativeIdentity = nativeIdentity;
		internal readonly string[] Names = names;
		internal readonly GltfSkinWeightFormat[] Formats = formats;
		internal volatile bool Invalidated;
	}

	// No native allocation or source upload storage is retained. SceneModel owns
	// the registered glTF Model strongly while in use; this table does not extend
	// resource lifetime or accidentally associate a reused native pointer with it.
	private static readonly ConditionalWeakTable<Model, Registration> registrations = new();

	internal static unsafe void Register( Model model, IReadOnlyDictionary<string, GltfSkinningVertexLayout> uploadedLayouts )
	{
		ThreadSafe.AssertIsMainThread();
		ArgumentNullException.ThrowIfNull( model );
		ArgumentNullException.ThrowIfNull( uploadedLayouts );
		if ( !model.IsValid || model.IsError ) throw new GltfException( "Cannot register vertex formats for an invalid model." );
		var prepared = uploadedLayouts.ToDictionary( entry => entry.Key, entry => FromLayout( entry.Value ), StringComparer.Ordinal );
		var names = model.MeshInfo.Meshes.Select( mesh => mesh.Name ).ToArray();
		var formats = MatchNativeMeshes( names, prepared );
		for ( var mesh = 0; mesh < names.Length; mesh++ )
		{
			int bones = 0, weights = 0;
			var vertices = RenderTools.GetModelMeshSkinning( model.native, mesh, (IntPtr)(&bones), (IntPtr)(&weights) );
			if ( vertices != uploadedLayouts[names[mesh]].VertexCount || bones <= 0 || weights != 4 ) throw new GltfException( "Native compilation changed the uploaded skinning vertex count or weight declaration." );
		}
		if ( registrations.TryGetValue( model, out var old ) )
		{
			if ( !old.Invalidated ) throw new InvalidOperationException( "The model's native skinning declaration has already been registered." );
			registrations.Remove( model );
		}
		registrations.Add( model, new( model.native.GetBindingPtr(), names, formats ) );
	}

	internal static GltfSkinWeightFormat FromLayout( GltfSkinningVertexLayout uploaded )
	{
		try { RawVertexLayout.Validate( uploaded.Stride, uploaded.VertexCount, uploaded.ByteCount, uploaded.Fields ); }
		catch ( ArgumentException e ) { throw new GltfException( "The uploaded skinning vertex range or declaration is invalid.", e ); }
		if ( uploaded.Stride % 4 != 0 ) throw new GltfException( "The stock skinning byte-address fetch requires a four-byte-aligned vertex stride." );
		var foundIndices = false;
		foreach ( var field in uploaded.Fields )
		{
			if ( field.Semantic is not ("blendindices" or "blendweight") ) continue;
			if ( field.SemanticIndex != 0 ) throw new GltfException( "Additional joint influence sets require an extended deformation path." );
			if ( field.Offset % 4 != 0 ) throw new GltfException( "The stock skinning byte-address fetch requires four-byte-aligned joint and weight offsets." );
			if ( field.Semantic == "blendindices" )
			{
				foundIndices = true;
				if ( field.Format is not (ColorFormat.COLOR_FORMAT_R8G8B8A8_UINT or ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT) ) throw new GltfException( "The registered stock skinning path requires unsigned 8-bit or 16-bit joint indices." );
			}
		}
		if ( !foundIndices ) throw new GltfException( "The uploaded skinning declaration has no joint indices." );
		return FromDeclaration( uploaded.Fields );
	}

	internal static GltfSkinWeightFormat ForModel( Model model, int nativeMesh )
	{
		if ( model is null ) return GltfSkinWeightFormat.Unorm8;
		if ( !registrations.TryGetValue( model, out var registered ) )
		{
			if ( model.GltfAnimation?.Rig is not null ) throw new GltfException( "The glTF model's skinning declaration registration is missing." );
			return GltfSkinWeightFormat.Unorm8;
		}
		if ( registered.Invalidated || !model.IsValid || model.native.GetBindingPtr() != registered.NativeIdentity ) throw new GltfException( "The model's native skinning declaration was invalidated; rebuild and register it before rendering." );
		if ( (uint)nativeMesh >= (uint)registered.Formats.Length || model.MeshCount != registered.Names.Length || model.native.GetMeshName( nativeMesh ) != registered.Names[nativeMesh] ) throw new GltfException( "The registered native skinning mesh no longer matches its declaration." );
		return registered.Formats[nativeMesh];
	}

	/// <summary>Keep an invalid marker so reload/disposal can never fall back to a different decoder.</summary>
	internal static void Invalidate( Model model )
	{
		if ( model is not null && registrations.TryGetValue( model, out var registered ) ) registered.Invalidated = true;
	}

	internal static GltfSkinWeightFormat FromDeclaration( ReadOnlySpan<RawVertexField> declaration )
	{
		ColorFormat? format = null;
		foreach ( var field in declaration )
		{
			if ( field.Semantic != "blendweight" || field.SemanticIndex != 0 ) continue;
			if ( format.HasValue ) throw new GltfException( "The native skinning declaration has duplicate weight fields." );
			format = field.Format;
		}
		return format switch
		{
			ColorFormat.COLOR_FORMAT_R8G8B8A8_UNORM => GltfSkinWeightFormat.Unorm8,
			ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT => GltfSkinWeightFormat.Float32,
			ColorFormat.COLOR_FORMAT_R16G16B16A16_UNORM => GltfSkinWeightFormat.Unorm16,
			_ => throw new GltfException( "The native skinning declaration has no supported four-component weight field." )
		};
	}

	/// <summary>
	/// Native compilation may reorder meshes. Names are explicit source identities;
	/// reject missing, merged or ambiguous meshes instead of assigning the wrong decoder.
	/// </summary>
	internal static GltfSkinWeightFormat[] MatchNativeMeshes( IReadOnlyList<string> nativeNames, IReadOnlyDictionary<string, GltfSkinWeightFormat> prepared )
	{
		if ( nativeNames.Count != prepared.Count ) throw new GltfException( "Native compilation changed the number of glTF skinning meshes." );
		var seen = new HashSet<string>( StringComparer.Ordinal );
		var formats = new GltfSkinWeightFormat[nativeNames.Count];
		for ( var mesh = 0; mesh < formats.Length; mesh++ )
		{
			var name = nativeNames[mesh];
			if ( name is null || !seen.Add( name ) || !prepared.TryGetValue( name, out formats[mesh] ) ) throw new GltfException( "Native compilation renamed or duplicated a glTF skinning mesh; its vertex format cannot be identified safely." );
		}
		return formats;
	}
}
