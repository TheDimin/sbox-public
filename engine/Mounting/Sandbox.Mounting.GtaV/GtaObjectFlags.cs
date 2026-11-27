using Sandbox;

namespace Sandbox.Mounting;

/// <summary>Raw GTA flags on a placed object. Entity and archetype masks use separate namespaces.</summary>
public sealed class GtaObjectFlags : Component
{
	[Property] public uint Placement { get; set; }
	[Property] public uint Archetype { get; set; }
}
