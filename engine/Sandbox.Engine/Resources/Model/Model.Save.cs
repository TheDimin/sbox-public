using NativeEngine;
using Sandbox.Resources;
using Borealis.Source2.Models;

namespace Sandbox;

partial class Model
{
	public byte[] SaveToVmdl()
	{
		var writer = new VmdlWriter( this );
		return writer.Write();
	}

	internal ModelFile ReadForRendering( Func<string, byte[]> external )
	{
		(MeshVertexBuffer Vertices, MeshIndexBuffer Indices)[] stored = null;
		if ( BuilderMeshes is { } meshes )
		{
			for ( int i = 0; i < meshes.Length; i++ )
			{
				var mesh = meshes[i];
				if ( !mesh.UsesStoredVertexLayout || mesh.VertexData is not { } vertices || mesh.StoredIndexBuffer is not { } indices ) continue;
				// The raw vertex buffer is write-once and writing indices replaces the 16-bit buffer.
				stored ??= new (MeshVertexBuffer, MeshIndexBuffer)[meshes.Length];
				stored[i] = (vertices, indices);
			}
		}

		var metadata = new VmdlWriter( this ) { WritePhysics = false }.Write( stored );
		return ModelFile.Read( metadata, external, stored );
	}

	/// <summary>Saves this model while serializing the complete native physics of another model.</summary>
	/// <remarks>Only static models are accepted. When originalPhysicsBlock is supplied,
	/// its exact captured PHYS bytes are retained; native serialization may normalize
	/// those bytes. The caller must bind this block to the same source model.</remarks>
	public byte[] SaveToVmdlWithPhysicsFrom( Model physicsSource, byte[] originalPhysicsBlock = null )
	{
		if ( physicsSource == null || !physicsSource.IsValid || physicsSource.IsError ) throw new ArgumentException( "A valid physics source is required.", nameof( physicsSource ) );
		if ( BoneCount != 0 || physicsSource.BoneCount != 0 ) throw new InvalidOperationException( "Preserved physics requires static source and destination skeletons." );
		if ( originalPhysicsBlock != null && (originalPhysicsBlock.Length < 8 || originalPhysicsBlock.Length > 128 * 1024 * 1024) ) throw new ArgumentOutOfRangeException( nameof(originalPhysicsBlock) );
		return new VmdlWriter( this, physicsSource, originalPhysicsBlock ).Write();
	}

	public async Task<byte[]> SaveToVmdlAsync()
	{
		return await Task.Run( SaveToVmdl );
	}

	/// <summary>
	/// The model as a compiled model without its physics block: what drawing it needs. Safe on a worker, unlike the
	/// physics block, whose native serialisation goes through the resource system.
	/// </summary>
	internal byte[] SaveMeshesToVmdl()
	{
		var writer = new VmdlWriter( this ) { WritePhysics = false };
		return writer.Write();
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

	/// <summary>Whether the physics block is written (<see cref="Model.SaveMeshesToVmdl"/> leaves it out).</summary>
	public bool WritePhysics { get; init; } = true;

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

	public byte[] Write( (MeshVertexBuffer Vertices, MeshIndexBuffer Indices)[] stored = null )
	{
		WriteExternalReferences();

		var physBlock = WritePhysics ? WritePHYSBlock() : -1;

		WriteCTRLBlock( physBlock, stored );
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

	void WriteCTRLBlock( int physBlock, (MeshVertexBuffer Vertices, MeshIndexBuffer Indices)[] stored )
	{
		var ctrl = KeyValues3.Create();

		try
		{
			var embeddedMeshes = ctrl.FindOrCreateMember( "embedded_meshes" );
			embeddedMeshes.SetToEmptyArray();

			var meshCount = _native.GetNumMeshes();
			for ( int i = 0; i < meshCount; i++ )
			{
				var hasStoredBuffers = stored is not null && i < stored.Length && stored[i].Vertices is not null;
				WriteEmbeddedMesh( embeddedMeshes, i, hasStoredBuffers );
			}

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

	void WriteEmbeddedMesh( KeyValues3 array, int meshIndex, bool hasStoredBuffers )
	{
		var vbibBlock = hasStoredBuffers ? -1 : WriteVBIBBlock( meshIndex );
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
		// A model made in code keeps its buffers in its meshes
		if ( _model.BuilderMeshes is { } built )
		{
			if ( (uint)meshIndex >= (uint)built.Length ) return -1;
			return _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_MBUF, MeshBuffersBlock( built[meshIndex] ) );
		}

		using var buffer = MeshGlue.SerializeMeshBuffers( _native, meshIndex );
		return buffer.IsNull || buffer.TellMaxPut() <= 0 ? -1 : _resource.RegisterAdditionalBlock( RESOURCE_BLOCK_ID_MBUF, buffer.ToArray() );
	}

	const int BufferDataSize = 24; // RenderMeshBufferData_t
	const int LayoutFieldSize = 56; // RenderMeshInputLayoutField_t
	const int SemanticNameSize = 32;

	/// <summary>
	/// A mesh's buffers as a <c>VBIBBlockHeader_t</c> laid out the way native's <c>SerializeMeshBuffers</c> streams it
	/// (<c>CResourceStream</c>: each array aligned to its element, offsets relative to where they're stored, empty arrays
	/// at offset 0): the header, the vertex then index buffers' descriptions, then each one's layout fields and bytes.
	/// </summary>
	internal static byte[] MeshBuffersBlock( Mesh mesh )
	{
		var vb = mesh.VertexData;
		var ib = mesh.IndexBufferBytes;
		var vbCount = vb is null ? 0 : 1;
		var ibCount = ib.IsEmpty ? 0 : 1;

		static int Align4( int at ) => (at + 3) & ~3;

		var at = 16;
		var vbArray = at;
		at += vbCount * BufferDataSize;
		var ibArray = at;
		at += ibCount * BufferDataSize;

		var vbFields = 0;
		var vbData = 0;
		if ( vb is not null )
		{
			vbFields = at = Align4( at );
			at += vb.Fields.Length * LayoutFieldSize;
			vbData = at;
			at += vb.Data.Length;
		}

		var ibData = 0;
		if ( !ib.IsEmpty )
		{
			at = Align4( at ); // its empty layout array is still aligned
			ibData = at;
			at += ib.Length;
		}

		var block = new byte[at];
		var span = block.AsSpan();

		static void Array( Span<byte> block, int at, int target, int count )
		{
			System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( block[at..], count > 0 ? target - at : 0 );
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( block[(at + 4)..], (uint)count );
		}

		static void Buffer( Span<byte> block, int at, int count, int size, int fieldsAt, int fieldCount, int dataAt, int dataLength )
		{
			System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( block[at..], count );
			System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( block[(at + 4)..], size );
			Array( block, at + 8, fieldsAt, fieldCount );
			Array( block, at + 16, dataAt, dataLength );
		}

		Array( span, 0, vbArray, vbCount );
		Array( span, 8, ibArray, ibCount );

		if ( vb is not null )
		{
			Buffer( span, vbArray, vb.Count, vb.Stride, vbFields, vb.Fields.Length, vbData, vb.Data.Length );
			for ( int f = 0; f < vb.Fields.Length; f++ )
			{
				var field = vb.Fields[f];
				var to = span.Slice( vbFields + f * LayoutFieldSize, LayoutFieldSize );
				var name = System.Text.Encoding.ASCII.GetBytes( field.Semantic );
				name.AsSpan( 0, Math.Min( name.Length, SemanticNameSize - 1 ) ).CopyTo( to );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[32..], field.SemanticIndex );
				System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian( to[36..], (uint)field.Format );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[40..], field.Offset );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[44..], field.Slot );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[48..], field.PerInstance ? 1 : 0 );
				System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian( to[52..], field.InstanceStepRate );
			}

			vb.Data.CopyTo( span[vbData..] );
		}

		if ( !ib.IsEmpty )
		{
			Buffer( span, ibArray, mesh.IndexCount, mesh.IndexElementSize, 0, 0, ibData, ib.Length );
			ib.CopyTo( span[ibData..] );
		}

		return block;
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
