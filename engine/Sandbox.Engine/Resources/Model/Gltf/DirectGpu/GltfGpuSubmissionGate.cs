namespace Sandbox.Gltf.DirectGpu;

/// <summary>
/// Native views can invoke one custom object's callback on different render
/// workers. Protect its mutable draw attributes and retirement with one local
/// CPU submission boundary. This is not a GPU completion fence.
/// </summary>
internal sealed class GltfGpuSubmissionGate
{
	private readonly object sync = new();

	internal void Run( Action body )
	{
		ArgumentNullException.ThrowIfNull( body );
		lock ( sync ) body();
	}
}
