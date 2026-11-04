namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	internal int CompilerBufferCount => buffers.Length;

	// Empty memory represents a validated URI-less compressed fallback placeholder;
	// glTF buffers with actual contents must have a positive byteLength.
	internal ReadOnlyMemory<byte>? CompilerBufferOrPlaceholder( int index )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		if ( (uint)index >= (uint)buffers.Length ) throw new GltfException( $"Invalid buffer index {index}." );
		return buffers[index].IsEmpty ? (ReadOnlyMemory<byte>?)null : buffers[index];
	}

	internal ReadOnlyMemory<byte> CompilerBuffer( int index )
	{
		return CompilerBufferOrPlaceholder( index ) ?? throw new GltfException( "Compressed placeholder buffer has no backing bytes." );
	}
}
