namespace Sandbox;

/// <summary>
/// Presentation for damage, breakage and the pieces created by a prop.
/// Empty fields use the model's surface effects when those are enabled.
/// </summary>
[AssetType( Name = "Prop Destruction Profile", Extension = "propfx", Category = "Effects" )]
public sealed class PropDestructionProfile : GameResource
{
	public PrefabFile DamageEffect { get; set; }
	public PrefabFile BreakEffect { get; set; }
	public PrefabFile GibEffect { get; set; }
	public SoundEvent DamageSound { get; set; }
	public SoundEvent BreakSound { get; set; }
	public SoundEvent GibSound { get; set; }
	public bool ShakeOnExplosion { get; set; } = true;
}
