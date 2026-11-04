namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	internal int CompilerBufferCount => buffers.Length;

	internal ReadOnlyMemory<byte> CompilerBuffer( int index )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( (uint)index >= (uint)buffers.Length ) throw new GltfException( $"Invalid buffer index {index}." );
		if ( buffers[index].IsEmpty ) throw new GltfException( "Compressed placeholder buffers require extension-aware compilation." );
		return buffers[index];
	}
}
