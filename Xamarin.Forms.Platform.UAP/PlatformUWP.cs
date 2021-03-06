﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Xamarin.Forms.Internals;

namespace Xamarin.Forms.Platform.UWP
{
	public abstract partial class Platform
	{
		internal static StatusBar MobileStatusBar => ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar") ? StatusBar.GetForCurrentView() : null;

		IToolbarProvider _toolbarProvider;

		void InitializeStatusBar()
		{
			StatusBar statusBar = MobileStatusBar;
			if (statusBar != null)
			{
				statusBar.Showing += (sender, args) => UpdateBounds();
				statusBar.Hiding += (sender, args) => UpdateBounds();

				// UWP 14393 Bug: If RequestedTheme is Light (which it is by default), then the 
				// status bar uses White Foreground with White Background. 
				// UWP 10586 Bug: If RequestedTheme is Light (which it is by default), then the 
				// status bar uses Black Foreground with Black Background. 
				// Since the Light theme should have a Black on White status bar, we will set it explicitly. 
				// This can be overriden by setting the status bar colors in App.xaml.cs OnLaunched.

				if (statusBar.BackgroundColor == null && statusBar.ForegroundColor == null && Windows.UI.Xaml.Application.Current.RequestedTheme == ApplicationTheme.Light)
				{
					statusBar.BackgroundColor = Colors.White;
					statusBar.ForegroundColor = Colors.Black;
					statusBar.BackgroundOpacity = 1;
				}
			}
		}

		void UpdateToolbarTitle(Page page)
		{
			if (_toolbarProvider == null)
				return;

			((ToolbarProvider)_toolbarProvider).CommandBar.Content = page.Title;
		}

		async void OnPageActionSheet(Page sender, ActionSheetArguments options)
		{
			List<string> buttons = options.Buttons.ToList();

			var list = new Windows.UI.Xaml.Controls.ListView
			{
				Style = (Windows.UI.Xaml.Style)Windows.UI.Xaml.Application.Current.Resources["ActionSheetList"],
				ItemsSource = buttons,
				IsItemClickEnabled = true
			};

			var dialog = new ContentDialog
			{
				Template = (Windows.UI.Xaml.Controls.ControlTemplate)Windows.UI.Xaml.Application.Current.Resources["MyContentDialogControlTemplate"],
				Content = list,
				Style = (Windows.UI.Xaml.Style)Windows.UI.Xaml.Application.Current.Resources["ActionSheetStyle"]
			};

			if (options.Title != null)
				dialog.Title = options.Title;

			list.ItemClick += (s, e) =>
			{
				dialog.Hide();
				options.SetResult((string)e.ClickedItem);
			};

			TypedEventHandler<CoreWindow, CharacterReceivedEventArgs> onEscapeButtonPressed = delegate (CoreWindow window, CharacterReceivedEventArgs args)
			{
				if (args.KeyCode == 27)
				{
					dialog.Hide();
					options.SetResult(ContentDialogResult.None.ToString());
				}
			};

			Window.Current.CoreWindow.CharacterReceived += onEscapeButtonPressed;

			_actionSheetOptions = options;

			if (options.Cancel != null)
				dialog.SecondaryButtonText = options.Cancel;

			if (options.Destruction != null)
				dialog.PrimaryButtonText = options.Destruction;

			ContentDialogResult result = await dialog.ShowAsync();
			if (result == ContentDialogResult.Secondary)
				options.SetResult(options.Cancel);
			else if (result == ContentDialogResult.Primary)
				options.SetResult(options.Destruction);

			Window.Current.CoreWindow.CharacterReceived -= onEscapeButtonPressed;
		}

		void ClearCommandBar()
		{
			if (_toolbarProvider != null)
			{
				_toolbarProvider = null;
				if (Device.Idiom == TargetIdiom.Phone)
					_page.BottomAppBar = null;
				else
					_page.TopAppBar = null;
			}
		}

		CommandBar CreateCommandBar()
		{
			var bar = new FormsCommandBar();
			if (Device.Idiom != TargetIdiom.Phone)
				bar.Style = (Windows.UI.Xaml.Style)Windows.UI.Xaml.Application.Current.Resources["TitleToolbar"];

			_toolbarProvider = new ToolbarProvider(bar);

			if (Device.Idiom == TargetIdiom.Phone)
				_page.BottomAppBar = bar;
			else
				_page.TopAppBar = bar;

			return bar;
		}

		async Task<CommandBar> GetCommandBarAsync()
		{
			IToolbarProvider provider = GetToolbarProvider();
			if (provider == null)
			{
				return null;
			}

			return await provider.GetCommandBarAsync();
		}

		void UpdateBounds()
		{
			_bounds = new Rectangle(0, 0, _page.ActualWidth, _page.ActualHeight);

			StatusBar statusBar = MobileStatusBar;
			if (statusBar != null)
			{
				bool landscape = Device.Info.CurrentOrientation.IsLandscape();
				bool titleBar = CoreApplication.GetCurrentView().TitleBar.IsVisible;
				double offset = landscape ? statusBar.OccludedRect.Width : statusBar.OccludedRect.Height;

				_bounds = new Rectangle(0, 0, _page.ActualWidth - (landscape ? offset : 0), _page.ActualHeight - (landscape ? 0 : offset));

				// Even if the MainPage is a ContentPage not inside of a NavigationPage, the calculated bounds
				// assume the TitleBar is there even if it isn't visible. When UpdatePageSizes is called,
				// _container.ActualWidth is correct because it's aware that the TitleBar isn't there, but the
				// bounds aren't, and things can subsequently run under the StatusBar.
				if (!titleBar)
				{
					_bounds.Width -= (_bounds.Width - _container.ActualWidth);
				}
			}
		}

		internal async Task UpdateToolbarItems()
		{
			CommandBar commandBar = await GetCommandBarAsync();
			if (commandBar != null)
			{
				commandBar.PrimaryCommands.Clear();
				commandBar.SecondaryCommands.Clear();

				if (_page.BottomAppBar != null || _page.TopAppBar != null)
				{
					_page.BottomAppBar = null;
					_page.TopAppBar = null;
					_page.InvalidateMeasure();
				}
			}

			var toolBarProvider = GetToolbarProvider() as IToolBarForegroundBinder;

			foreach (ToolbarItem item in _toolbarTracker.ToolbarItems.OrderBy(ti => ti.Priority))
			{
				if (commandBar == null)
					commandBar = CreateCommandBar();

				toolBarProvider?.BindForegroundColor(commandBar);

				var button = new AppBarButton();
				button.SetBinding(AppBarButton.LabelProperty, "Text");
				button.SetBinding(AppBarButton.IconProperty, "Icon", _fileImageSourcePathConverter);
				button.Command = new MenuItemCommand(item);
				button.DataContext = item;

				ToolbarItemOrder order = item.Order == ToolbarItemOrder.Default ? ToolbarItemOrder.Primary : item.Order;
				if (order == ToolbarItemOrder.Primary)
				{
					toolBarProvider?.BindForegroundColor(button);
					commandBar.PrimaryCommands.Add(button);
				}
				else
				{
					commandBar.SecondaryCommands.Add(button);
				}
			}

			if (commandBar?.PrimaryCommands.Count + commandBar?.SecondaryCommands.Count == 0)
				ClearCommandBar();
		}

		internal IToolbarProvider GetToolbarProvider()
		{
			IToolbarProvider provider = null;

			Page element = _currentPage;
			while (element != null)
			{
				provider = GetRenderer(element) as IToolbarProvider;
				if (provider != null)
					break;

				var pageContainer = element as IPageContainer<Page>;
				element = pageContainer?.CurrentPage;
			}

			if (provider != null && _toolbarProvider == null)
				ClearCommandBar();

			return provider;
		}

		class ToolbarProvider : IToolbarProvider
		{
			readonly Task<CommandBar> _commandBar;

			public ToolbarProvider(CommandBar commandBar)
			{
				_commandBar = Task.FromResult(commandBar);
			}

			public CommandBar CommandBar => _commandBar.Result;

			public Task<CommandBar> GetCommandBarAsync()
			{
				return _commandBar;
			}
		}
	}
}
