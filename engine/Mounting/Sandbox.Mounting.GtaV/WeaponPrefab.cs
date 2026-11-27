using System.Linq;
using System.Text.Json.Nodes;
using Sandbox;
using Sandbox.Mounting;

namespace GtaV;

// The Sandbox prefab owns all gameplay, including ammo, animations, sounds and scope settings.
internal sealed class WeaponPrefab( string template, string title, string model, string heldPath ) : ResourceLoader<GtaMount>
{
	PrefabFile prefab;

	readonly record struct Weapon( string Model, string Id, string Title, string Template );

	// English labels and model IDs from the GTA weapon definitions (DurtyFree/gta-v-data-dumps, weapons.json).
	// A model can represent several distinct weapons, for example Tear Gas and BZ Gas.
	static readonly System.Linq.ILookup<string, Weapon> Weapons = new Weapon[]
	{
		new( "w_me_knife_01", "knife", "Knife", null ),
		new( "w_me_nightstick", "nightstick", "Nightstick", "crowbar" ),
		new( "w_me_hammer", "hammer", "Hammer", "crowbar" ),
		new( "w_me_bat", "baseball_bat", "Baseball Bat", "crowbar" ),
		new( "w_me_gclub", "golfclub", "Golf Club", "crowbar" ),
		new( "w_me_crowbar", "crowbar", "Crowbar", "crowbar" ),
		new( "w_pi_pistol", "pistol", "Pistol", "usp" ),
		new( "w_pi_combatpistol", "combat_pistol", "Combat Pistol", "usp" ),
		new( "w_pi_appistol", "appistol", "AP Pistol", null ),
		new( "w_pi_pistol50", "pistol50", "Pistol .50", "colt1911" ),
		new( "w_sb_microsmg", "microsmg", "Micro SMG", "mp5" ),
		new( "w_sb_smg", "smg", "SMG", "mp5" ),
		new( "w_sb_assaultsmg", "assaultsmg", "Assault SMG", "mp5" ),
		new( "w_ar_assaultrifle", "assault_rifle", "Assault Rifle", "m4a1" ),
		new( "w_ar_carbinerifle", "carbine_rifle", "Carbine Rifle", "m4a1" ),
		new( "w_ar_advancedrifle", "advancedrifle", "Advanced Rifle", "m4a1" ),
		new( "w_mg_mg", "mg", "MG", null ),
		new( "w_mg_combatmg", "combatmg", "Combat MG", null ),
		new( "w_sg_pumpshotgun", "pump_shotgun", "Pump Shotgun", "shotgun" ),
		new( "w_sg_sawnoff", "sawnoffshotgun", "Sawed-Off Shotgun", null ),
		new( "w_sg_assaultshotgun", "assaultshotgun", "Assault Shotgun", null ),
		new( "w_sg_bullpupshotgun", "bullpupshotgun", "Bullpup Shotgun", "shotgun" ),
		new( "w_pi_stungun", "stungun", "Stun Gun", null ),
		new( "w_sr_sniperrifle", "sniper_rifle", "Sniper Rifle", "sniper" ),
		new( "w_sr_heavysniper", "heavysniper", "Heavy Sniper", "sniper" ),
		new( "w_lr_grenadelauncher", "grenadelauncher", "Grenade Launcher", null ),
		new( "w_lr_grenadelauncher", "grenadelauncher_smoke", "Tear Gas Launcher", null ),
		new( "w_lr_rpg", "rpg", "RPG", "rpg" ),
		new( "w_mg_minigun", "minigun", "Minigun", null ),
		new( "w_ex_grenadefrag", "grenade", "Grenade", "grenade" ),
		new( "w_ex_pe", "stickybomb", "Sticky Bomb", null ),
		new( "w_ex_grenadesmoke", "smokegrenade", "Tear Gas", null ),
		new( "w_ex_grenadesmoke", "bzgas", "BZ Gas", null ),
		new( "w_ex_molotov", "molotov", "Molotov", null ),
		new( "w_am_fire_exting", "fireextinguisher", "Fire Extinguisher", null ),
		new( "w_am_jerrycan", "petrolcan", "Jerry Can", null ),
		new( "w_am_digiscanner", "digiscanner", "Digiscanner", null ),
		new( "w_am_baseball", "ball", "Ball", null ),
		new( "w_am_flare", "flare", "Flare", null ),
		new( "w_me_bottle", "bottle", "Bottle", null ),
		new( "w_pi_sns_pistol", "sns_pistol", "SNS Pistol", "usp" ),
		new( "w_pi_heavypistol", "heavy_pistol", "Heavy Pistol", "colt1911" ),
		new( "w_ar_bullpuprifle", "bullpuprifle", "Bullpup Rifle", "m4a1" ),
		new( "w_ar_specialcarbine", "specialcarbine", "Special Carbine", "m4a1" ),
		new( "w_pi_sns_pistolmk2", "snspistol_mk2", "SNS Pistol Mk II", "usp" ),
		new( "w_sr_marksmanriflemk2", "marksmanrifle_mk2", "Marksman Rifle Mk II", null ),
		new( "w_sg_pumpshotgunmk2", "pumpshotgun_mk2", "Pump Shotgun Mk II", "shotgun" ),
		new( "w_ar_bullpupriflemk2", "bullpup_rifle_mk2", "Bullpup Rifle Mk II", "m4a1" ),
		new( "w_ar_specialcarbinemk2", "special_carbine_mk2", "Special Carbine Mk II", "m4a1" ),
		new( "w_am_papers_xm3", "acidpackage", "Acid Package", null ),
		new( "w_me_candy_xm3", "candycane", "Candy Cane", null ),
		new( "w_ar_railgun_xm3", "railgunxm3", "Railgun", null ),
		new( "w_pi_pistol_xm3", "pistolxm3", "WM 29 Pistol", "usp" ),
		new( "w_pi_raygun", "raypistol", "Up-n-Atomizer", null ),
		new( "w_ar_srifle", "raycarbine", "Unholy Hellbringer", null ),
		new( "w_mg_sminigun", "rayminigun", "Widowmaker", null ),
		new( "w_pi_wep1_gun", "doubleaction", "Double-Action Revolver", "revolver" ),
		new( "w_ex_snowball", "snowball", "Snowball", null ),
		new( "w_ex_apmine", "proxmine", "Proximity Mine", null ),
		new( "w_lr_homing", "hominglauncher", "Homing Launcher", null ),
		new( "w_pi_revolvermk2", "revolver_mk2", "Heavy Revolver Mk II", "revolver" ),
		new( "w_sb_gusenberg", "gusenberg", "Gusenberg Sweeper", null ),
		new( "w_me_dagger", "dagger", "Antique Cavalry Dagger", null ),
		new( "w_pi_vintage_pistol", "vintagepistol", "Vintage Pistol", "colt1911" ),
		new( "w_ar_musket", "musket", "Musket", null ),
		new( "w_lr_firework", "firework", "Firework Launcher", null ),
		new( "w_me_hatchet", "hatchet", "Hatchet", null ),
		new( "w_ar_railgun", "railgun", "Railgun", null ),
		new( "w_sg_heavyshotgun", "heavyshotgun", "Heavy Shotgun", null ),
		new( "w_sr_marksmanrifle", "marksmanrifle", "Marksman Rifle", null ),
		new( "w_pi_ceramic_pistol", "ceramicpistol", "Ceramic Pistol", "usp" ),
		new( "w_ar_bullpuprifleh4", "militaryrifle", "Military Rifle", "m4a1" ),
		new( "w_sg_pumpshotgunh4", "combatshotgun", "Combat Shotgun", "shotgun" ),
		new( "w_ch_jerrycan", "hazardcan", "Hazardous Jerry Can", null ),
		new( "w_pi_flaregun", "flaregun", "Flare Gun", null ),
		new( "w_pi_singleshoth4", "gadgetpistol", "Perico Pistol", null ),
		new( "w_pi_wep2_gun", "navyrevolver", "Navy Revolver", "revolver" ),
		new( "w_me_knuckle", "knuckle", "Knuckle Duster", null ),
		new( "w_sb_pdw", "combatpdw", "Combat PDW", "mp5" ),
		new( "w_pi_singleshot", "marksmanpistol", "Marksman Pistol", null ),
		new( "w_sg_doublebarrel", "dbshotgun", "Double Barrel Shotgun", null ),
		new( "w_me_machette_lr", "machete", "Machete", null ),
		new( "w_sb_compactsmg", "machinepistol", "Machine Pistol", "mp5" ),
		new( "w_ar_assaultrifle_smg", "compactrifle", "Compact Rifle", "m4a1" ),
		new( "w_me_flashlight", "flashlight", "Flashlight", null ),
		new( "w_me_switchblade", "switchblade", "Switchblade", null ),
		new( "w_pi_revolver", "heavy_revolver", "Heavy Revolver", "revolver" ),
		new( "w_lr_compactgl", "compactlauncher", "Compact Grenade Launcher", null ),
		new( "w_sb_minismg", "minismg", "Mini SMG", "mp5" ),
		new( "w_me_wrench", "wrench", "Pipe Wrench", "crowbar" ),
		new( "w_sg_sweeper", "autoshotgun", "Sweeper Shotgun", null ),
		new( "w_me_poolcue", "poolcue", "Pool Cue", "crowbar" ),
		new( "w_me_battleaxe", "battleaxe", "Battle Axe", null ),
		new( "w_ex_pipebomb", "pipebomb", "Pipe Bomb", null ),
		new( "w_ar_carbineriflemk2", "carbine_rifle_mk2", "Carbine Rifle Mk II", "m4a1" ),
		new( "w_mg_combatmgmk2", "combatmg_mk2", "Combat MG Mk II", null ),
		new( "w_sb_smgmk2", "smg_mk2", "SMG Mk II", "mp5" ),
		new( "w_ar_assaultriflemk2", "assault_rifle_mk2", "Assault Rifle Mk II", "m4a1" ),
		new( "w_sr_heavysnipermk2", "heavy_sniper_mk2", "Heavy Sniper Mk II", "sniper" ),
		new( "w_pi_pistolmk2", "pistol_mk2", "Pistol Mk II", "usp" ),
		new( "w_me_stonehatchet", "stone_hatchet", "Stone Hatchet", null ),
		new( "w_ar_carbinerifle_reh", "tacticalrifle", "Service Carbine", "m4a1" ),
		new( "w_am_digiscanner_reh", "metaldetector", "Metal Detector", null ),
		new( "w_sr_precisionrifle_reh", "precisionrifle", "Precision Rifle", null ),
		new( "w_ar_heavyrifleh", "heavyrifle", "Heavy Rifle", "m4a1" ),
		new( "w_am_jerrycan_sf", "fertilizercan", "Fertilizer Can", null ),
		new( "w_lr_compactml", "emplauncher", "Compact EMP Launcher", null ),
		new( "w_pi_pistolsmg_m31", "tecpistol", "Tactical SMG", null ),
		new( "w_lr_compactsl_m32", "snowlauncher", "Snowball Launcher", null ),
		new( "w_sl_battlerifle_m32", "battlerifle", "Battle Rifle", "m4a1" ),
		new( "w_am_hackdevice_m32", "hackingdevice", "Hacking Device", null ),
		new( "w_me_rod_m41", "stunrod", "The Shocker", null ),
		new( "w_ar_bullpupriflem42", "strickler", "El Strickler", null ),
		new( "w_am_papers_xm3", "newspaper", "Newspaper", null ),
	}.ToLookup( x => x.Model, System.StringComparer.OrdinalIgnoreCase );

	internal static void Register( MountContext context, string modelName, AssetLoader asset )
	{
		foreach ( var weapon in Weapons[modelName] )
		{
			string model = $"mount://gtav/models/{modelName}.vmdl";
			string path = $"weapons/{weapon.Id}";
			if ( asset.ThumbnailTextures is null )
			{
				asset.ThumbnailTextures = new();
				var preview = new WeaponPreview( asset );
				preview.Flags |= ResourceFlags.DeveloperOnly;
				context.Add( ResourceType.Model, $"previews/{modelName}", preview );
			}
			string title = weapon.Template is null ? $"WIP {weapon.Title}" : weapon.Title;
			string template = null, heldPath = null;
			if ( weapon.Template is { } source )
			{
				string authored = $"weapons/{source}/{source}";
				context.Add( ResourceType.PrefabFile, $"{path}/world", new WeaponPrefab( authored + "_wm.prefab", title, model, null ) );
				context.Add( ResourceType.PrefabFile, $"{path}/view", new WeaponPrefab( authored + "_vm.prefab", title, model, null ) );
				template = authored + ".prefab";
				heldPath = $"mount://gtav/{path}/world.prefab";
			}
			var loader = new WeaponPrefab( template, title, model, heldPath );
			loader.Tags.Add( $"title:{title}" );
			loader.Tags.Add( "weapon" );
			context.Add( ResourceType.PrefabFile, path, loader );
		}
	}

	protected override object Load()
	{
		if ( template is null ) return BuildWorldProp();
		if ( PrefabFile.Load( template )?.RootObject?.DeepClone() is not JsonObject root ) return null;
		if ( template.EndsWith( "_vm.prefab", System.StringComparison.Ordinal ) ) SetViewPresentation( root );
		else SetPresentation( root );
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", title ).WithMetadata( "PreviewModel", model )
			.WithMetadata( "ThumbnailModel", model.Replace( "/models/", "/previews/" ) );
		using ( builder.Scope() )
		{
			SceneUtility.MakeIdGuidsUnique( root );
			new GameObject().Deserialize( root );
		}
		return prefab = builder.Create();
	}

	PrefabFile BuildWorldProp()
	{
		var mesh = Model.Load( model );
		if ( !mesh.IsValid() || mesh.IsError ) return null;
		var builder = new PrefabBuilder().WithName( Path ).WithMetadata( "Title", title ).WithMetadata( "PreviewModel", model )
			.WithMetadata( "ThumbnailModel", model.Replace( "/models/", "/previews/" ) );
		using ( builder.Scope() )
		{
			var root = new GameObject( true, title );
			root.AddComponent<ModelRenderer>().Model = mesh;
			var box = root.AddComponent<BoxCollider>();
			box.Center = mesh.Bounds.Center;
			box.Scale = mesh.Bounds.Size;
			root.AddComponent<Rigidbody>();
		}
		return prefab = builder.Create();
	}

	// Keep the authored skeleton driving the hands and muzzle attachment. Only its weapon mesh is hidden.
	void SetViewPresentation( JsonObject obj )
	{
		if ( obj["Name"]?.GetValue<string>() == "weapon_root" && obj["Components"] is JsonArray boneComponents )
		{
			boneComponents.Add( new JsonObject
			{
				["__type"] = "Sandbox.ModelRenderer",
				["__guid"] = System.Guid.NewGuid(),
				["Model"] = model,
				["RenderOptions"] = new JsonObject { ["GameLayer"] = false, ["OverlayLayer"] = true }
			} );
		}
		if ( string.Equals( (string)obj["Name"], "Viewmodel", System.StringComparison.OrdinalIgnoreCase ) && obj["Components"] is JsonArray components )
			foreach ( var component in components )
				if ( (string)component["__type"] == "Sandbox.SkinnedModelRenderer" && component["RenderOptions"] is JsonObject options )
				{
					options["GameLayer"] = false;
					options["OverlayLayer"] = false;
				}
		if ( obj["Children"] is JsonArray children )
			foreach ( var child in children.OfType<JsonObject>() ) SetViewPresentation( child );
	}

	sealed class WeaponPreview( AssetLoader asset ) : ResourceLoader<GtaMount>
	{
		protected override async System.Threading.Tasks.Task<object> LoadAsync()
		{
			var mesh = await Model.LoadAsync( asset.Path );
			while ( asset.ThumbnailTextures.Any( Host.IsBindingTexture ) )
				await GameTask.Yield();
			return mesh;
		}
	}

	void SetPresentation( JsonObject obj )
	{
		if ( obj["Components"] is JsonArray components )
		{
			foreach ( var component in components )
			{
				if ( component?["WorldModelPrefab"] is JsonObject reference && heldPath is not null )
				{
					reference["prefab"] = heldPath;
					if ( component["ViewModelPrefab"] is JsonObject view ) view["prefab"] = heldPath[..^"world.prefab".Length] + "view.prefab";
					component["DisplayName"] = title;
				}
				if ( (string)component?["__type"] is "Sandbox.ModelRenderer" or "Sandbox.SkinnedModelRenderer" )
					component["Model"] = model;
			}
		}
		if ( obj["Children"] is JsonArray children )
			foreach ( var child in children )
				if ( child is JsonObject gameObject ) SetPresentation( gameObject );
	}

	protected override void Shutdown() => PrefabBuilder.Destroy( prefab );
}
