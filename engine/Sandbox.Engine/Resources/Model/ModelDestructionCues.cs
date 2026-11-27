namespace Sandbox.ModelEditor.Nodes;

/// <summary>Fallback effect paths selected by a model importer.</summary>
[Hide]
public sealed class ModelDestructionCues
{
	public string DamageEffect { get; set; }
	public string BreakEffect { get; set; }
	public string GibEffect { get; set; }
	public string BreakSound { get; set; }
	public string GibSound { get; set; }
}
