using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using CarinaStudio.AppSuite.Controls;
using CarinaStudio.Collections;
using CarinaStudio.Configuration;
using CarinaStudio.Threading;
using CarinaStudio.ULogViewer.Logs.DataSources;
using CarinaStudio.Windows.Input;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace CarinaStudio.ULogViewer.Controls;

/// <summary>
/// Dialog to edit <see cref="ScriptLogDataSourceProvider"/>s.
/// </summary>
class ScriptLogDataSourceProviderEditorDialog : CarinaStudio.Controls.InputDialog<IULogViewerApplication>
{
	// Supported source option.
	public class SupportedSourceOption
	{
		// Fields.
		bool? isRequired;

		// Constructor.
		public SupportedSourceOption(string name, bool isRequired)
		{
			this.CanBeRequired = LogDataSourceOptions.IsValueTypeOption(name)
				? name switch
				{
					nameof(LogDataSourceOptions.ProcessId) => true,
					_ => false,
				}
				: name switch
				{
					nameof(LogDataSourceOptions.Encoding)
						or nameof(LogDataSourceOptions.SetupCommands)
						or nameof(LogDataSourceOptions.TeardownCommands) => false,
					_ => true,
				};
			this.isRequired = this.CanBeRequired ? isRequired : null;
			this.Name = name;
		}

		// Whether option can be required or not.
		public bool CanBeRequired { get; }

		// Whether option is required or not.
		public bool? IsRequired
		{
			get => this.isRequired;
			set
			{
				if (this.CanBeRequired)
					this.isRequired = value;
			}
		}

		// Option name.
		public string Name { get; }
	}


	// Static fields.
	static readonly StyledProperty<bool> IsEmbeddedProviderProperty = AvaloniaProperty.Register<ScriptLogDataSourceProviderEditorDialog, bool>(nameof(IsEmbeddedProvider));


	// Fields.
	readonly ToggleButton addSupportedSourceOptionButton;
	readonly ContextMenu addSupportedSourceOptionMenu;
	Uri? closingReaderScriptDocumentUri;
	readonly TextBox displayNameTextBox;
	bool isProviderShown;
	Uri? openingReaderScriptDocumentUri;
	Uri? readingLineScriptDocumentUri;
	readonly Avalonia.Controls.ListBox supportedSourceOptionListBox;
	readonly SortedObservableList<SupportedSourceOption> supportedSourceOptions = new((lhs, rhs) => string.Compare(lhs.Name, rhs.Name, true, CultureInfo.InvariantCulture));
	readonly SortedObservableList<MenuItem> unsupportedSourceOptionMenuItems = new((lhs, rhs) => string.Compare(lhs.DataContext as string, rhs.DataContext as string, true, CultureInfo.InvariantCulture));
	readonly SortedObservableList<string> unsupportedSourceOptions = new((lhs, rhs) => string.Compare(lhs, rhs, true, CultureInfo.InvariantCulture), LogDataSourceOptions.OptionNames);


	/// <summary>
	/// Initialize new <see cref="ScriptLogDataSourceProviderEditorDialog"/> instance.
	/// </summary>
	public ScriptLogDataSourceProviderEditorDialog()
	{
		this.RemoveSupportedSourceOptionCommand = new Command<SupportedSourceOption>(this.RemoveSupportedSourceOption);
		this.SupportedSourceOptions = ListExtensions.AsReadOnly(this.supportedSourceOptions);
		this.UnsupportedSourceOptions = ListExtensions.AsReadOnly(this.unsupportedSourceOptions);
		AvaloniaXamlLoader.Load(this);
		if (Platform.IsLinux)
			this.WindowStartupLocation = WindowStartupLocation.Manual;
		this.addSupportedSourceOptionButton = this.Get<ToggleButton>(nameof(addSupportedSourceOptionButton));
		this.addSupportedSourceOptionMenu = ((ContextMenu)this.Resources[nameof(addSupportedSourceOptionMenu)].AsNonNull()).Also(it =>
		{
			it.ItemsSource = this.unsupportedSourceOptionMenuItems;
			it.Closed += (_, _) => this.SynchronizationContext.Post(() => this.addSupportedSourceOptionButton.IsChecked = false);
			it.Opened += (_, _) =>
			{
				ToolTip.SetIsOpen(this.addSupportedSourceOptionButton, false);
				this.SynchronizationContext.Post(() => this.addSupportedSourceOptionButton.IsChecked = true);
			};
		});
		this.displayNameTextBox = this.Get<TextBox>(nameof(displayNameTextBox)).Also(it =>
		{
			it.GetObservable(TextBox.TextProperty).Subscribe(_ => this.InvalidateInput());
		});
		this.supportedSourceOptionListBox = this.Get<Avalonia.Controls.ListBox>(nameof(supportedSourceOptionListBox));
	}


	// Add supported source option.
	void AddSupportedSourceOption(MenuItem menuItem)
	{
		var option = (string)menuItem.DataContext.AsNonNull();
		this.unsupportedSourceOptions.Remove(option);
		this.unsupportedSourceOptionMenuItems.Remove(menuItem);
		this.supportedSourceOptions.Add(new(option, false));
	}


	// Create menu item for unsupported log data source option.
	MenuItem CreateUnsupportedSourceOptionMenuItem(string option) => new MenuItem().Also(menuItem =>
	{
		menuItem.Click += (_, _) =>
		{
			this.addSupportedSourceOptionMenu.Close();
			this.AddSupportedSourceOption(menuItem);
		};
		menuItem.DataContext = option;
		menuItem.Header = new TextBlock().Also(it =>
		{
			it.Bind(TextBlock.TextProperty, new Binding()
			{
				Converter = Converters.LogDataSourceOptionConverter.Default,
				Source = option,
			});
		});
	});


	/// <inheritdoc/>
	protected override Task<object?> GenerateResultAsync(CancellationToken cancellationToken)
	{
		// check compilation error
		//
		
		// create or update provider
		var provider = this.Provider ?? new ScriptLogDataSourceProvider(this.Application);
		provider.DisplayName = this.displayNameTextBox.Text;
		provider.SetSupportedSourceOptions(
			this.supportedSourceOptions.Select(it => it.Name),
			this.supportedSourceOptions.Where(it => it.IsRequired == true).Select(it => it.Name)
		);
		
		// complete
		return Task.FromResult<object?>(provider);
	}


	/// <summary>
	/// Get or set whether the provider is embedded in another container or not.
	/// </summary>
	public bool IsEmbeddedProvider
	{
		get => this.GetValue(IsEmbeddedProviderProperty);
		set => this.SetValue(IsEmbeddedProviderProperty, value);
	}


	/// <inheritdoc/>
	protected override void OnFirstMeasurementCompleted(Size measuredSize)
	{
		// call base
		base.OnFirstMeasurementCompleted(measuredSize);
		
		// setup initial window size and position
		(this.Screens.ScreenFromWindow(this) ?? this.Screens.Primary)?.Let(screen =>
		{
			var workingArea = screen.WorkingArea;
			var widthRatio = this.Application.Configuration.GetValueOrDefault(ConfigurationKeys.LogAnalysisScriptSetEditorDialogInitWidthRatio);
			var heightRatio = this.Application.Configuration.GetValueOrDefault(ConfigurationKeys.LogAnalysisScriptSetEditorDialogInitHeightRatio);
			var scaling = screen.Scaling;
			var left = (workingArea.TopLeft.X + workingArea.Width * (1 - widthRatio) / 2); // in device pixels
			var top = (workingArea.TopLeft.Y + workingArea.Height * (1 - heightRatio) / 2); // in device pixels
			var sysDecorSize = this.GetSystemDecorationSizes();
			this.Position = new((int)(left + 0.5), (int)(top + 0.5));
			this.Width = (workingArea.Width * widthRatio) / scaling;
			this.Height = ((workingArea.Height * heightRatio) / scaling) - sysDecorSize.Top - sysDecorSize.Bottom;
		});
	}


	/// <inheritdoc/>
	protected override async void OnOpened(EventArgs e)
	{
		// call base
		base.OnOpened(e);
		
		// request running script
		await this.RequestEnablingRunningScriptAsync();

		// setup initial focus
		this.SynchronizationContext.Post(() =>
		{
			if (this.IsEmbeddedProvider)
				this.supportedSourceOptionListBox.Focus();
			else
				this.displayNameTextBox.Focus();
		});
	}


	/// <inheritdoc/>
	protected override void OnOpening(EventArgs e)
	{
		// call base
		base.OnOpening(e);
		
		// show provider
		var provider = this.Provider;
		if (provider != null)
		{
			if (!this.IsEmbeddedProvider)
				this.displayNameTextBox.Text = provider.DisplayName;
			foreach (var option in provider.SupportedSourceOptions)
			{
				this.unsupportedSourceOptions.Remove(option);
				this.supportedSourceOptions.Add(new(option, provider.RequiredSourceOptions.Contains(option)));
			}
		}
		foreach (var option in this.unsupportedSourceOptions)
			this.unsupportedSourceOptionMenuItems.Add(this.CreateUnsupportedSourceOptionMenuItem(option));
		this.isProviderShown = true;
	}


	/// <inheritdoc/>
	protected override bool OnValidateInput() =>
		base.OnValidateInput() && (this.IsEmbeddedProvider || !string.IsNullOrWhiteSpace(this.displayNameTextBox.Text));


	/// <summary>
	/// Open online documentation.
	/// </summary>
#pragma warning disable CA1822
	public void OpenDocumentation() =>
		Platform.OpenLink("https://carinastudio.azurewebsites.net/ULogViewer/ScriptLogDataSource");
#pragma warning restore CA1822


	// Remove supported source option.
	void RemoveSupportedSourceOption(SupportedSourceOption option)
	{
		if (this.supportedSourceOptions.Remove(option))
		{
			this.unsupportedSourceOptions.Add(option.Name);
			this.unsupportedSourceOptionMenuItems.Add(this.CreateUnsupportedSourceOptionMenuItem(option.Name));
		}
		this.supportedSourceOptionListBox.SelectedItem = null;
		this.supportedSourceOptionListBox.Focus();
	}


	/// <summary>
	/// Command to remove supported source option.
	/// </summary>
	public ICommand RemoveSupportedSourceOptionCommand { get; }


	// Request running script.
	async Task RequestEnablingRunningScriptAsync()
	{
		if (!this.IsOpened || this.Settings.GetValueOrDefault(AppSuite.SettingKeys.EnableRunningScript))
			return;
		if (!await new EnableRunningScriptDialog().ShowDialog(this))
		{
			this.IsEnabled = false;
			this.SynchronizationContext.PostDelayed(this.Close, 300); // [Workaround] Prevent crashing on macOS.
		}
	}


	/// <summary>
	/// Get or set script log data source provider to edit.
	/// </summary>
	public ScriptLogDataSourceProvider? Provider { get; set; }


	/// <summary>
	/// Show menu of adding supported log data source options.
	/// </summary>
	public void ShowAddSupportedSourceOptionMenu() =>
		this.addSupportedSourceOptionMenu.Open(this.addSupportedSourceOptionButton);


	/// <summary>
	/// Get supported log data source options.
	/// </summary>
	public IList<SupportedSourceOption> SupportedSourceOptions { get; }


	/// <summary>
	/// Get unsupported log data source options.
	/// </summary>
	public IList<string> UnsupportedSourceOptions { get; }
}
