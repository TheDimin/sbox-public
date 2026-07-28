namespace Editor;

/// <summary>
/// Inspector for sound events, with the usual sound event controls and a focused
/// browser for adding sound files published to the s&box cloud.
/// </summary>
public sealed class SoundEventEditor : BaseResourceEditor<SoundEvent>
{
	SerializedObject _serialized;
	SerializedCollection _sounds;
	Button _addSelected;
	Label _cloudStatus;
	Label _licenseTitle;
	Label _licenseDetails;
	Package _selectedPackage;
	bool _adding;

	protected override void Initialize( Asset asset, SoundEvent resource )
	{
		Layout = Layout.Column();
		Layout.Margin = 0;
		Layout.Spacing = 0;

		_serialized = resource.GetSerialized();
		_serialized.OnPropertyChanged += NoteChanged;

		var soundsProperty = _serialized.GetProperty( nameof( SoundEvent.Sounds ) );
		if ( soundsProperty?.TryGetAsObject( out var soundsObject ) == true )
		{
			_sounds = soundsObject as SerializedCollection;
		}

		var tabs = new TabWidget( this )
		{
			VerticalSizeMode = SizeMode.CanGrow
		};

		tabs.AddPage( "Sound Files", "audio_file", CreateSoundFilesPage() );
		tabs.AddPage( "Cloud", "cloud_download", CreateCloudPage() );
		tabs.StateCookie = "SoundEventEditor.Tab";

		Layout.Add( tabs, 1 );
	}

	Widget CreateSoundFilesPage()
	{
		var page = new Widget( null );
		page.Layout = Layout.Column();
		page.VerticalSizeMode = SizeMode.CanGrow;

		var sheet = new ControlSheet
		{
			IncludePropertyNames = true
		};
		sheet.AddObject( _serialized, filter: IsSerializableProperty );

		page.Layout.Add( sheet );
		page.Layout.AddStretchCell();
		return page;
	}

	Widget CreateCloudPage()
	{
		var page = new Widget( null );
		page.Layout = Layout.Column();
		page.Layout.Spacing = 4;
		page.VerticalSizeMode = SizeMode.CanGrow;

		var help = new Label( "Browse cloud-hosted sound files. Select a sound to preview it, then add it to this event." )
		{
			WordWrap = true,
			Margin = 8
		};
		page.Layout.Add( help );

		var browser = new CloudAssetBrowser( page, new List<AssetType> { AssetType.SoundFile } )
		{
			MultiSelect = false
		};
		browser.OnPackageHighlight = HighlightPackage;
		browser.OnPackageSelected = AddPackage;
		page.Layout.Add( browser, 1 );

		var footer = page.Layout.AddRow();
		footer.Margin = 8;
		footer.Spacing = 8;

		_cloudStatus = footer.Add( new Label( "Select a cloud sound to add." ), 1 );
		_cloudStatus.WordWrap = true;

		_addSelected = footer.Add( new Button.Primary( "Add to Event" ) );
		_addSelected.Enabled = false;
		_addSelected.Clicked = () => AddPackage( _selectedPackage );

		var licensePanel = page.Layout.AddColumn();
		licensePanel.Margin = new Sandbox.UI.Margin( 8, 0, 8, 8 );
		licensePanel.Spacing = 2;
		_licenseTitle = licensePanel.Add( new Label( "License: Select a cloud sound" ) );
		_licenseDetails = licensePanel.Add( new Label( "License information will appear here." ) );
		_licenseDetails.WordWrap = true;

		return page;
	}

	async void HighlightPackage( Package package )
	{
		_selectedPackage = package;
		_addSelected.Enabled = package is not null && !_adding;
		_cloudStatus.Text = package is null
			? "Select a cloud sound to add."
			: $"☁ Cloud sound: {package.Title}";

		UpdateLicense( package );

		if ( package is null || !AssetSystem.CanCloudInstall( package ) )
			return;

		try
		{
			var asset = await AssetSystem.InstallAsync( package.FullIdent );
			if ( !IsValid || package != _selectedPackage || asset is null )
				return;

			EditorUtility.PlayAssetSound( asset );
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, $"Unable to preview cloud sound {package.FullIdent}" );
			if ( IsValid && package == _selectedPackage )
				_cloudStatus.Text = "Preview unavailable. Check your connection and sign-in status.";
		}
	}

	async void UpdateLicense( Package package )
	{
		if ( package is null )
		{
			_licenseTitle.Text = "License: Select a cloud sound";
			_licenseDetails.Text = "License information will appear here.";
			return;
		}

		_licenseTitle.Text = "License: Loading...";
		_licenseDetails.Text = "";

		try
		{
			var fullPackage = await Package.FetchAsync( package.FullIdent, partial: false, useCache: false );
			if ( !IsValid || package != _selectedPackage )
				return;

			if ( fullPackage is null || string.IsNullOrWhiteSpace( fullPackage.AssetLicense ) )
			{
				_licenseTitle.Text = "License: Not provided";
				_licenseDetails.Text = "The publisher did not attach license information to this cloud sound.";
				return;
			}

			var licenseTitle = fullPackage.AssetLicense;
			string licenseDescription = null;
			var licenseOptions = Sandbox.Services.PackageType.Sound?.GetAssetLicenseOptions();
			if ( licenseOptions is not null )
			{
				foreach ( var option in licenseOptions )
				{
					if ( option.Name != fullPackage.AssetLicense )
						continue;

					licenseTitle = option.Title;
					licenseDescription = option.Description;
					break;
				}
			}

			_licenseTitle.Text = $"License: {licenseTitle}";
			_licenseDetails.Text = string.IsNullOrWhiteSpace( licenseDescription )
				? "Full license details are unavailable from the current license catalog."
				: licenseDescription;
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, $"Unable to fetch license for cloud sound {package.FullIdent}" );
			if ( IsValid && package == _selectedPackage )
			{
				_licenseTitle.Text = "License: Unavailable";
				_licenseDetails.Text = "License information could not be loaded. Check your connection and sign-in status.";
			}
		}
	}

	async void AddPackage( Package package )
	{
		if ( package is null || _adding )
			return;

		if ( _sounds is null )
		{
			_cloudStatus.Text = "This sound event's sound list is unavailable.";
			return;
		}

		_adding = true;
		_addSelected.Enabled = false;
		_cloudStatus.Text = $"Adding {package.Title}...";

		try
		{
			if ( !AssetSystem.CanCloudInstall( package ) )
			{
				_cloudStatus.Text = "This cloud package cannot be installed as a sound file.";
				return;
			}

			var asset = await AssetSystem.InstallAsync( package.FullIdent );
			if ( !IsValid )
				return;

			if ( asset is null || asset.AssetType != AssetType.SoundFile )
			{
				_cloudStatus.Text = "The cloud sound could not be downloaded. Check your connection and sign-in status.";
				return;
			}

			var sound = SoundFile.Load( asset.Path );
			if ( sound is null )
			{
				_cloudStatus.Text = "The downloaded package did not contain a usable sound file.";
				return;
			}

			_sounds.Add( sound );
			_cloudStatus.Text = $"Added {package.Title} to this sound event.";
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, $"Unable to add cloud sound {package.FullIdent}" );
			if ( IsValid )
				_cloudStatus.Text = "The cloud sound is unavailable. Check your connection and sign-in status.";
		}
		finally
		{
			_adding = false;
			if ( IsValid )
				_addSelected.Enabled = _selectedPackage is not null;
		}
	}

	static bool IsSerializableProperty( SerializedProperty property )
	{
		if ( property.IsMethod )
			return true;
		if ( !property.IsProperty )
			return false;
		if ( property.HasAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() )
			return false;

		return property.IsPublic || property.HasAttribute<System.Text.Json.Serialization.JsonIncludeAttribute>();
	}
}
