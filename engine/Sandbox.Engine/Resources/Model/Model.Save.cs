using NativeEngine;
using Sandbox.Resources;

namespace Sandbox;

partial class Model
{
	public byte[] SaveToVmdl()
	{
		var writer = new VmdlWriter( this );
		return writer.Write();
	}

	/// <summary>Saves this model while serializing the complete native physics of another model.</summary>
	/// <remarks>Only static models are accepted. When originalPhysicsBlock is supplied,
	/// its exact captured PHYS bytes are retained; native serialization may normalize
	/// those bytes. The caller must bind this block to the same source model.</remarks>
	public byte[] SaveToVmdlWithPhysicsFrom( Model physicsSource, byte[] originalPhysicsBlock = null )
	{
		if ( physicsSource == null || !physicsSource.IsValid || physicsSource.IsError ) throw new ArgumentException( "A valid physics source is required.", nameof( physicsSource ) );
		if ( BoneCount != 0 || physicsSource.BoneCount != 0 ) throw new InvalidOperationException( "Preserved physics requires static source and destination skeletons." );
		if ( originalPhysicsBlock != null && (originalPhysicsBlock.Length < 8 || originalPhysicsBlock.Length > 128 * 1024 * 1024) ) throw new ArgumentOutOfRangeException( nameof( originalPhysicsBlock ) );
		return new VmdlWriter( this, physicsSource, originalPhysicsBlock ).Write();
	}

	public async Task<byte[]> SaveToVmdlAsync()
	{
		return await Task.Run( SaveToVmdl );
	}
}

sealed class VmdlWriter
{
	const ushort RESOURCE_VERSION = 1;

	const uint RESOURCE_BLOCK_ID_MDAT = 0x5441444D; // 'MDAT'
	const uint RESOURCE_BLOCK_ID_MBUF = 0x4655424D; // 'MBUF'
	const uint RESOURCE_BLOCK_ID_MRPH = 0x4850524D; // 'MRPH'
	const uint RESOURCE_BLOCK_ID_CTRL = 0x4C525443; // 'CTRL'
	const uint RESOURCE_BLOCK_ID_ASEQ = 0x51455341; // 'ASEQ'
	const uint RESOURCE_BLOCK_ID_AGRP = 0x50524741; // 'AGRP'
	const uint RESOURCE_BLOCK_ID_ANIM = 0x4D494E41; // 'ANIM'
	const uint RESOURCE_BLOCK_ID_PHYS = 0x53594850; // 'PHYS'

	readonly IModel _native;
	readonly Model _model;
	readonly IModel _physicsNative;
	readonly Model _physicsModel;
	readonly byte[] _originalPhysicsBlock;
	readonly ResourceWriter _resource;

	public VmdlWriter( Model model, Model physicsSource = null, byte[] originalPhysicsBlock = null )
	{
		_native = model.native;
		_originalPhysicsBlock = originalPhysicsBlock == null ? null : (byte[])originalPhysicsBlock.Clone();
		_physicsModel = physicsSource ?? model;
		_physicsNative = _physicsModel.native;
		_model = model;

		_resource = new ResourceWriter
		{
			ResourceVersion = RESOURCE_VERSION
		};
	}

	public byte[] Write()
	{
		WriteExternalReferences();

		var physBlock = WritePHYSBlock();

		WriteCTRLBlock( physBlock );
		WriteDATABlock();
		return _resource.ToArray();
	}

	void WriteExternalReferences()
	{
		if ( _physicsModel.Physics is { } physics )
		{
			foreach ( var surface in physics.Surfaces )
				if ( surface != null && !string.IsNullOrWhiteSpace( surface.ResourcePath ) ) _resource.AddExternalReference( surface.ResourcePath );
		}
		if ( _model.AuthoringMaterialGroups != null )
			foreach ( var group in _model.AuthoringMaterialGroups )
				foreach ( var path in group.Materials ) _resource.AddExternalReference( path );
		foreach ( var material in _model.Materials.Concat( Enumerable.Range( 0, _model.MaterialGroupCount ).SelectMany( _model.GetMaterials ) ) )
		{
			if ( material.IsValid() )
				_resource.AddExternalReference( material.ResourcePath );
		}

		if ( _model.TryGetData<Sandbox.ModelEditor.Nodes.ModelBreakPiece[]>( out var pieces ) && pieces != null )
		{
			foreach ( var piece in pieces )
				_resource.AddExternalReference( piece.Model );
		}
	}

	void WriteCTRLBlock( int physBlock )
	{
		var ctrl = KeyValues3.Create();

		try
		{
			var embeddedMeshes = ctrl.FindOrCreateMember( "embedded_meshes" );
			embeddedMeshes.SetToEmptyArray();

			var meshCount = _native.GetNumMeshes();
			for ( int i = 0; i < meshCount; i++ ) WriteEmbeddedMesh( embeddedMeshes, i );

			if ( physBlock >= 0 )
			{
				var embeddedPhysics = ctrl.FindOrCreateMember( "embedded_physics" );
				embeddedPhysics.SetToEmptyTable();
				embeddedPhysics.SetMemberInt( "phys_data_block", physBlock );
			}

			using var buffer = ctrl.Save();
			_resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_CTRL, buffer.ToArray() );
		}
		finally
		{
			ctrl.DeleteThis();
		}
	}

	void WriteEmbeddedMesh( KeyValues3 array, int meshIndex )
	{
		var vbibBlock = WriteVBIBBlock( meshIndex );
		var mdatBlock = WriteMDATBlock( meshIndex );

		var mesh = array.ArrayAddToTail();
		mesh.SetToEmptyTable();
		var meshName = _native.GetMeshName( meshIndex );
		mesh.SetMemberString( "name", string.IsNullOrWhiteSpace( meshName ) ? $"mesh#{meshIndex}" : meshName );
		mesh.SetMemberInt( "mesh_index", meshIndex );
		mesh.SetMemberInt( "data_block", mdatBlock );
		mesh.SetMemberInt( "vbib_block", vbibBlock );
	}

	int WriteVBIBBlock( int meshIndex )
	{
		using var buffer = MeshGlue.SerializeMeshBuffers( _native, meshIndex );
		return buffer.IsNull || buffer.TellMaxPut() <= 0 ? -1 : _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_MBUF, buffer.ToArray() );
	}

	int WriteMDATBlock( int meshIndex )
	{
		using var buffer = MeshGlue.SerializeMeshData( _native, meshIndex );
		return buffer.IsNull || buffer.TellMaxPut() <= 0 ? -1 : _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_MDAT, buffer.ToArray() );
	}

	void WriteDATABlock()
	{
		using var buffer = MeshGlue.SerializeModelData( _native );
		if ( buffer.IsNull || buffer.TellMaxPut() <= 0 ) return;
		var data = buffer.ToArray();
		data = VmdlMeshGroupDefaults.Preserve( data, _native.GetDefaultMeshGroupMask(), _model.AuthoringMeshGroupNames, _model.AuthoringMaterialGroups );
		_resource.SetDataBlock( data );
	}

	int WritePHYSBlock()
	{
		if ( _originalPhysicsBlock != null ) return _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_PHYS, _originalPhysicsBlock );
		using var buffer = MeshGlue.SerializePhysicsData( _physicsNative );
		if ( buffer.IsNull || buffer.TellMaxPut() <= 0 ) return -1;
		return _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_PHYS, buffer.ToArray() );
	}
}
