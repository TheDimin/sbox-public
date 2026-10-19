namespace Editor.AssetPickers;

public partial class GenericPicker
{
	string RecentStateCookie;
	string ActiveTab = "Asset Browser";
	AssetPickerState RecentState;
	bool HasRecentState => RecentState is not null;

	void InitializeRecentState( List<AssetType> assetTypes )
	{
		// Match the browser's per-project, per-filter preference scope.
		RecentStateCookie = $"AssetPicker.Recent.{string.Join( ",", (assetTypes ?? []).Select( x => x.FileExtension ).OrderBy( x => x ) )}";
		var saved = ProjectCookie.Get<AssetPickerState>( RecentStateCookie, null );
		if ( saved?.IsRecent( DateTime.UtcNow ) == true )
			RecentState = saved;

		AssetBrowser.VisibilityChanged += visible => { if ( visible ) ActiveTab = "Asset Browser"; };
		if ( MountsBrowser is not null )
			MountsBrowser.VisibilityChanged += visible => { if ( visible ) ActiveTab = "Mounts"; };
		if ( CloudBrowser is not null )
			CloudBrowser.VisibilityChanged += visible => { if ( visible ) ActiveTab = "Cloud Browser"; };

		// The window owns the dialog. Save on hide, before its children are destroyed,
		// for Select, Cancel, Escape and the window close button alike.
		Window.VisibilityChanged += visible => { if ( !visible ) SaveRecentState(); };
		DockManager.RaiseDock( RecentState?.GetTab( Options.EnableCloud, Options.EnableMounts ) ?? "Asset Browser" );
	}

	public override async void Show()
	{
		base.Show();

		if ( RecentState is null )
			return;

		await RestoreBrowser( AssetBrowser, RecentState.Local, ActiveTab == "Asset Browser" );
		if ( !IsValid ) return;
		await RestoreBrowser( MountsBrowser, RecentState.Mounts, ActiveTab == "Mounts" );
		if ( !IsValid ) return;
		if ( CloudBrowser is not null )
		{
			CloudBrowser.NavigateTo( RecentState.CloudFilter );
			CloudBrowser.Search.Value = RecentState.CloudSearch ?? "";
			if ( ActiveTab == "Cloud Browser" && !string.IsNullOrEmpty( RecentState.Package ) && Package.TryGetCached( RecentState.Package, out var package ) )
				await CloudBrowser.RestoreSelection( package, skipEvents: true );
		}
		if ( !IsValid ) return;

		AssetBrowser browser = ActiveTab == "Mounts" ? MountsBrowser : ActiveTab == "Asset Browser" ? AssetBrowser : null;
		ConfirmButton.Enabled = browser is not null
			? browser.SelectedCount > 0 && IsSelectionValid( browser.GetSelected<AssetEntry>().Select( x => x.Asset ).ToList() )
			: IsSelectionValid( CloudBrowser?.GetSelected<PackageEntry>().FirstOrDefault()?.Package );
	}

	static async Task RestoreBrowser( AssetBrowser browser, AssetPickerState.BrowserState state, bool select )
	{
		if ( browser is null || state is null )
			return;

		browser.Search.Value = state.Search ?? "";
		browser.NavigateTo( state.Path );
		var asset = string.IsNullOrEmpty( state.Asset ) ? null : AssetSystem.FindByPath( state.Asset );
		if ( select && asset is not null && (browser.FilterAssetTypes is null || browser.FilterAssetTypes.Count == 0 || browser.FilterAssetTypes.Contains( asset.AssetType )) )
			await browser.RestoreSelection( asset, skipEvents: true );
	}

	static AssetPickerState.BrowserState CaptureBrowser( AssetBrowser browser ) => browser is null ? null : new()
	{
		Path = browser.CurrentLocation?.Path,
		Search = browser.Search.Value,
		Asset = browser.GetSelected<AssetEntry>().FirstOrDefault()?.Asset?.Path
	};

	void SaveRecentState()
	{
		ProjectCookie.Set( RecentStateCookie, new AssetPickerState
		{
			ClosedAt = DateTime.UtcNow,
			Tab = ActiveTab,
			Local = CaptureBrowser( AssetBrowser ),
			Mounts = CaptureBrowser( MountsBrowser ),
			CloudSearch = CloudBrowser?.Search.Value,
			CloudFilter = CloudBrowser?.BaseQuery,
			Package = CloudBrowser?.GetSelected<PackageEntry>().FirstOrDefault()?.Package?.FullIdent
		} );
	}

	public override void SetSearchText( string value )
	{
		if ( string.IsNullOrEmpty( value ) )
			return;

		if ( ActiveTab == "Cloud Browser" && CloudBrowser is not null )
			CloudBrowser.Search.Value = value;
		else
		{
			AssetBrowser browser = ActiveTab == "Mounts" ? MountsBrowser : AssetBrowser;
			browser.Search.Value = value;
		}
	}
}
