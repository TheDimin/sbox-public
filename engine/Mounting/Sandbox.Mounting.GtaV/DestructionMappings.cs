using System.Collections.Generic;
using Sandbox.ModelEditor.Nodes;

namespace GtaV;

internal static class DestructionMappings
{
	internal static ModelDestructionCues Resolve( IReadOnlyList<ArchetypeExtension> extensions )
	{
		if ( extensions.Count == 0 ) return null;
		var cues = new ModelDestructionCues();
		foreach ( var extension in extensions )
		{
			if ( extension is DestructionAudioExtension audio )
			{
				cues.BreakSound ??= Sound( audio.DestroySound );
				cues.GibSound ??= Sound( audio.DetachSound );
			}
			else if ( extension is DestructionParticleExtension { Bone: 0 or -1 } particle )
			{
				var prefab = Particle( particle.Name );
				switch ( particle.Trigger )
				{
					case 2: cues.DamageEffect ??= prefab; break;
					case 3: cues.GibEffect ??= prefab; break;
					case 4: cues.BreakEffect ??= prefab; break;
				}
			}
		}
		return cues;
	}

	static string Particle( string name )
	{
		// Use the effect name itself. A glass bottle on a wooden prop still emits glass.
		var effect = name switch
		{
			"dst_glass_bottles" or "dst_glass_bulb" or "sht_beer_containers" or "sht_shop_beer_cont" => "glass_bullet",
			"brk_wood_planks" or "brk_wood_splinter" or "brk_tree_trunk_bark"
				or "dst_wood_chunky" or "dst_wood_splinter" or "dst_wood_structures" => "wood_bullet",
			"brk_metal_frag" or "brk_sparking_wires" or "brk_lamppost_base" or "dst_metal_canister" or "dst_metal_frag"
				or "dst_electrical" or "dst_electric_crackle" or "dst_sparking_wires"
				or "sht_electrical_box" or "sht_electrical_fire" or "sht_sparking_wires" => "metal_bullet",
			"dst_ceramics" or "dst_ceramics_tint" => "ceramic-bullet",
			"dst_gen_cardboard" or "dst_gen_paper" or "dst_newspaper" or "dst_office_paper"
				or "sht_cardboard" or "sht_paper_bails" => "cardboard-bullet",
			"dst_gen_plastic_cont" or "dst_shop_plastic_cont" or "dst_polystyrene" => "plastic_bullet",
			"dst_upholstrey" => "fabric_bullet",
			"brk_blood" or "sht_blood" => "flesh_bullet",
			"brk_uprooted" or "brk_concrete" or "dst_concrete" or "dst_concrete_large" or "dst_dust"
				or "dst_wet_sand" or "sht_dust" => "dirt-bullet",
			_ => null
		};
		return effect is null ? null : $"prefabs/surface/{effect}.prefab";
	}

	// The keys are the sound cue hashes in the installed collision settings records.
	static string Sound( uint hash ) => hash switch
	{
		0x0648b49e or 0x3a80e3fa or 0x472f4a1c => "sounds/impacts/melee/impact-melee-wood.sound",
		0x2a14da01 => "sounds/impacts/bullets/impact-bullet-glass.sound",
		0x0ad8c43d or 0x9bb1a7b2 => "sounds/impacts/melee/impact-melee-dirt.sound",
		0x0e7a0d27 or 0xa2b82676 => "sounds/impacts/bullets/impact-bullet-plastic.sound",
		0x3d1069fe or 0x751fecf7 or 0x75771a91 or 0x905f38a5 or 0xa39547c6
			or 0xa7aa9f73 or 0xceaf6f6b or 0xdfb3b7af or 0xe0a3e008 => "sounds/impacts/melee/impact-melee-metal.sound",
		_ => null
	};
}
