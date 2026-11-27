using Sandbox.UI;
using Sandbox.UI.Construct;

namespace Sandbox;

/// <summary>
/// The list under an open <see cref="UIDropdown"/>. It's a popup, so it lives above the menu and closes by itself when something else is pressed.
/// </summary>
public class UIDropdownList : Popup
{
	/// <summary>
	/// Opens the list beneath its source and highlights the selected item.
	/// </summary>
	public UIDropdownList( Panel source, List<UIDropdown.Item> items, object selected, bool tokenize, Action<UIDropdown.Item> onPick )
		: base( source, PositionMode.BelowStretch, 0f )
	{
		AddClass( "dropdown-popup" );
		CloseWhenParentIsHidden = true;
		SetClass( "with-swatches", items.Any( x => x.Swatch is not null ) );

		var categories = items.GroupBy( item => item.Category, StringComparer.OrdinalIgnoreCase ).ToArray();
		var showCategories = categories.Count( category => category.Key is not null ) > 1;

		foreach ( var category in categories )
		{
			if ( showCategories && category.Key is { } name )
			{
				var heading = Add.Label( "", "category" );
				heading.Tokenize = tokenize;
				heading.Text = name;
			}

			foreach ( var item in category )
				AddOption( item, selected, tokenize, onPick );
		}
	}

	void AddOption( UIDropdown.Item item, object selected, bool tokenize, Action<UIDropdown.Item> onPick )
	{
		var row = Add.Panel( "option" );
		row.SetClass( "active", Equals( item.Value, selected ) );

		if ( item.Swatch is { } swatch )
			row.Add.Panel( "swatch" ).Style.BackgroundColor = swatch;

		var label = row.Add.Label( "", "label" );
		label.Tokenize = tokenize;
		label.Text = item.Label;

		if ( item.Font is not null )
			label.Style.FontFamily = item.Font;

		row.AddEventListener( "onclick", () =>
		{
			onPick?.Invoke( item );
			Delete();
		} );
	}

	// keeps a popup the dropdown sits in, like the resource picker, open while the list is used
	protected override BasePopup ParentPopup => PopupSource?.AncestorsAndSelf.OfType<BasePopup>().FirstOrDefault();
}
