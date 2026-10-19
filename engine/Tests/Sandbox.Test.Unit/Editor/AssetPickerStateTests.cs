using Editor.AssetPickers;
using System;
using System.Text.Json;

namespace EditorTests;

[TestClass]
public class AssetPickerStateTests
{
	[TestMethod]
	public void NewPickerDefaultsToAssetBrowser()
	{
		var state = new AssetPickerState();
		Assert.AreEqual( "Asset Browser", state.GetTab( true, true ) );
		Assert.IsFalse( state.IsRecent( DateTime.UtcNow ) );
	}

	[TestMethod]
	public void RecentStateExpiresAfterTenMinutes()
	{
		var closedAt = new DateTime( 2026, 1, 1, 0, 0, 0, DateTimeKind.Utc );
		var state = new AssetPickerState { ClosedAt = closedAt };
		Assert.IsTrue( state.IsRecent( closedAt ) );
		Assert.IsTrue( state.IsRecent( closedAt.AddMinutes( 10 ).AddTicks( -1 ) ) );
		Assert.IsFalse( state.IsRecent( closedAt.AddMinutes( 10 ) ) );
		Assert.IsFalse( state.IsRecent( closedAt.AddTicks( -1 ) ) );
	}

	[TestMethod]
	[DataRow( "Mounts", true, true, "Mounts" )]
	[DataRow( "Mounts", true, false, "Asset Browser" )]
	[DataRow( "Cloud Browser", true, true, "Cloud Browser" )]
	[DataRow( "Cloud Browser", false, true, "Asset Browser" )]
	[DataRow( "Unknown", true, true, "Asset Browser" )]
	public void RestoreOnlyAvailableTabs( string tab, bool cloud, bool mounts, string expected )
	{
		var state = new AssetPickerState { Tab = tab };
		Assert.AreEqual( expected, state.GetTab( cloud, mounts ) );
	}

	[TestMethod]
	public void CookieSerializationPreservesNavigation()
	{
		var state = new AssetPickerState
		{
			ClosedAt = DateTime.UtcNow,
			Tab = "Mounts",
			Mounts = new() { Path = "mount://game/models", Search = "chair", Asset = "mount://game/models/chair.vmdl" },
			Local = new() { Path = "c:/project/materials", Search = "wood" },
			CloudSearch = "table",
			CloudFilter = "type:model",
			Package = "facepunch.table"
		};
		var restored = JsonSerializer.Deserialize<AssetPickerState>( JsonSerializer.Serialize( state ) );
		Assert.AreEqual( state.ClosedAt, restored.ClosedAt );
		Assert.AreEqual( state.Tab, restored.Tab );
		Assert.AreEqual( state.Mounts.Path, restored.Mounts.Path );
		Assert.AreEqual( state.Mounts.Search, restored.Mounts.Search );
		Assert.AreEqual( state.Mounts.Asset, restored.Mounts.Asset );
		Assert.AreEqual( state.Local.Path, restored.Local.Path );
		Assert.AreEqual( state.Local.Search, restored.Local.Search );
		Assert.AreEqual( state.CloudSearch, restored.CloudSearch );
		Assert.AreEqual( state.CloudFilter, restored.CloudFilter );
		Assert.AreEqual( state.Package, restored.Package );
	}
}
