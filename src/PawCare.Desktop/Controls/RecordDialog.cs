using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PawCare.Desktop.Views;

namespace PawCare.Desktop.Controls;

public enum FieldKind
{
    Text,
    Multiline,
    Choice,
    Date,
    Time12,
    CheckBox,
    Number,
    ReadOnly,
    ReadOnlyMultiline
}

public sealed record FieldDefinition(
    string Key,
    string Label,
    FieldKind Kind = FieldKind.Text,
    object? Value = null,
    IReadOnlyList<string>? Options = null,
    bool Required = true,
    string? Help = null,
    bool SpanColumns = false);

public sealed class RecordDialog : Window
{
    private readonly Dictionary<string, Control> _controls = [];
    private readonly IReadOnlyList<FieldDefinition> _fields;
    private readonly Func<IReadOnlyDictionary<string, object?>, string?>? _validate;
    private readonly TextBlock _errorText;

    public IReadOnlyDictionary<string, object?> Values { get; private set; } = new Dictionary<string, object?>();

    public RecordDialog(Window owner, string title, string subtitle, IReadOnlyList<FieldDefinition> fields, string confirmLabel = "Save changes",
        Func<IReadOnlyDictionary<string, object?>, string?>? validate = null, bool showCancel = true)
    {
        Owner = owner;
        Title = title;
        Width = 792;
        MaxHeight = 820;
        MinHeight = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ShowInTaskbar = false;
        Background = Brushes.Transparent;
        _fields = fields;
        _validate = validate;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Padding = new Thickness(24, 20, 24, 16),
            BorderBrush = ResourceBrush("LineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = title, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 21, FontWeight = FontWeights.Bold, Foreground = ResourceBrush("ForestDarkBrush") },
                    new TextBlock { Text = subtitle, FontSize = 11, Foreground = ResourceBrush("MutedBrush"), Margin = new Thickness(0, 5, 0, 0) }
                }
            }
        };
        root.Children.Add(header);

        var formGrid = new Grid { Margin = new Thickness(24, 20, 24, 8) };
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        formGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        var column = 0;
        foreach (var field in fields)
        {
            if (field.SpanColumns && column != 0)
            {
                row++;
                column = 0;
            }
            while (formGrid.RowDefinitions.Count <= row)
                formGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var container = CreateField(field);
            Grid.SetRow(container, row);
            Grid.SetColumn(container, column == 0 ? 0 : 2);
            if (field.SpanColumns)
                Grid.SetColumnSpan(container, 3);
            formGrid.Children.Add(container);

            if (field.SpanColumns || column == 1)
            {
                row++;
                column = 0;
            }
            else
            {
                column = 1;
            }
        }

        var scroll = new ScrollViewer { Content = formGrid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 610 };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footerGrid = new Grid { Margin = new Thickness(24, 8, 24, 20) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _errorText = new TextBlock { Foreground = ResourceBrush("DangerBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        footerGrid.Children.Add(_errorText);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = new Button { Content = "Cancel", Style = (Style)Application.Current.FindResource("OutlineButton"), Margin = new Thickness(0, 0, 8, 0), MinWidth = 90 };
        cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = confirmLabel, Style = (Style)Application.Current.FindResource("PrimaryButton"), MinWidth = 118, IsDefault = true };
        save.Click += Save_Click;
        if (showCancel) actions.Children.Add(cancel);
        actions.Children.Add(save);
        Grid.SetColumn(actions, 1);
        footerGrid.Children.Add(actions);
        Grid.SetRow(footerGrid, 2);
        root.Children.Add(footerGrid);

        Content = new Border
        {
            Margin = new Thickness(16),
            Background = Brushes.White,
            BorderBrush = ResourceBrush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(16, 29, 22),
                BlurRadius = 28,
                ShadowDepth = 8,
                Opacity = 0.24
            },
            Child = root
        };
        KeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Escape) return;
            DialogResult = false;
            args.Handled = true;
        };
    }

    public bool? ShowModal()
    {
        var mainWindow = Owner as MainWindow;
        mainWindow?.SetModalState(true);
        try
        {
            return ShowDialog();
        }
        finally
        {
            mainWindow?.SetModalState(false);
        }
    }

    public string Text(string key) => Values.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
    public bool Bool(string key) => Values.TryGetValue(key, out var value) && value is true;
    public DateTime? Date(string key) => Values.TryGetValue(key, out var value) ? value as DateTime? : null;
    public int Number(string key) => int.TryParse(Text(key), out var value) ? value : 0;

    private FrameworkElement CreateField(FieldDefinition field)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
        if (field.Kind == FieldKind.CheckBox)
        {
            var check = new CheckBox
            {
                Content = field.Label,
                IsChecked = field.Value as bool? ?? false,
                Foreground = ResourceBrush("InkBrush"),
                FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            _controls[field.Key] = check;
            panel.Children.Add(check);
        }
        else
        {
            panel.Children.Add(new TextBlock { Text = field.Required ? field.Label + " *" : field.Label, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = ResourceBrush("InkBrush"), Margin = new Thickness(0, 0, 0, 6) });
            Control control = field.Kind switch
            {
                FieldKind.Choice => new ComboBox { ItemsSource = field.Options, SelectedItem = Convert.ToString(field.Value) ?? field.Options?.FirstOrDefault(), Style = (Style)Application.Current.FindResource("FieldComboBox") },
                FieldKind.Date => new DatePicker { SelectedDate = field.Value as DateTime?, Padding = new Thickness(8, 7, 8, 7), BorderBrush = ResourceBrush("LineBrush") },
                FieldKind.Time12 => new Time12Picker(field.Value),
                FieldKind.Multiline => new TextBox { Text = Convert.ToString(field.Value) ?? string.Empty, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 78, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Style = (Style)Application.Current.FindResource("FieldTextBox") },
                FieldKind.Number => new TextBox { Text = Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? "0", Style = (Style)Application.Current.FindResource("FieldTextBox") },
                FieldKind.ReadOnlyMultiline => new TextBox { Text = Convert.ToString(field.Value) ?? string.Empty, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 78, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = ResourceBrush("SagePaleBrush"), Style = (Style)Application.Current.FindResource("FieldTextBox") },
                FieldKind.ReadOnly => new TextBox { Text = Convert.ToString(field.Value) ?? string.Empty, IsReadOnly = true, Background = ResourceBrush("SagePaleBrush"), Style = (Style)Application.Current.FindResource("FieldTextBox") },
                _ => new TextBox { Text = Convert.ToString(field.Value) ?? string.Empty, Style = (Style)Application.Current.FindResource("FieldTextBox") }
            };
            _controls[field.Key] = control;
            panel.Children.Add(control);
        }

        if (!string.IsNullOrWhiteSpace(field.Help))
            panel.Children.Add(new TextBlock { Text = field.Help, FontSize = 9, Foreground = ResourceBrush("MutedBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return panel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var values = new Dictionary<string, object?>();
        foreach (var field in _fields)
        {
            object? value = _controls[field.Key] switch
            {
                TextBox textBox => textBox.Text.Trim(),
                ComboBox comboBox => comboBox.SelectedItem?.ToString(),
                DatePicker datePicker => datePicker.SelectedDate,
                Time12Picker timePicker => timePicker.Value,
                CheckBox checkBox => checkBox.IsChecked == true,
                _ => null
            };
            if (field.Required && value is string text && string.IsNullOrWhiteSpace(text))
            {
                _errorText.Text = $"Please complete {field.Label.ToLowerInvariant()}.";
                return;
            }
            if (field.Required && field.Kind == FieldKind.Date && value is null)
            {
                _errorText.Text = $"Please select {field.Label.ToLowerInvariant()}.";
                return;
            }
            if (field.Kind == FieldKind.Number && !int.TryParse(Convert.ToString(value), out _))
            {
                _errorText.Text = $"{field.Label} must be a whole number.";
                return;
            }
            values[field.Key] = value;
        }
        var error = _validate?.Invoke(values);
        if (!string.IsNullOrWhiteSpace(error))
        {
            _errorText.Text = error;
            return;
        }
        Values = values;
        DialogResult = true;
    }

    private sealed class Time12Picker : UserControl
    {
        private readonly ComboBox _hour;
        private readonly ComboBox _minute;
        private readonly ComboBox _period;

        public string Value
        {
            get
            {
                var hour = int.TryParse(_hour.SelectedItem?.ToString(), out var selectedHour) ? selectedHour : 12;
                var minute = int.TryParse(_minute.SelectedItem?.ToString(), out var selectedMinute) ? selectedMinute : 0;
                var period = _period.SelectedItem?.ToString() ?? "AM";
                var hour24 = hour % 12 + (period == "PM" ? 12 : 0);
                return $"{hour24:00}:{minute:00}";
            }
        }

        public Time12Picker(object? value)
        {
            var source = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "09:00";
            if (!TimeSpan.TryParse(source, CultureInfo.InvariantCulture, out var time) &&
                DateTime.TryParse(source, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedDate))
                time = parsedDate.TimeOfDay;
            if (time < TimeSpan.Zero || time >= TimeSpan.FromDays(1)) time = TimeSpan.FromHours(9);

            var style = (Style)Application.Current.FindResource("FieldComboBox");
            _hour = new ComboBox
            {
                ItemsSource = Enumerable.Range(1, 12).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray(),
                SelectedItem = ((time.Hours + 11) % 12 + 1).ToString(CultureInfo.InvariantCulture),
                Style = style,
                MinWidth = 82
            };
            _minute = new ComboBox
            {
                ItemsSource = Enumerable.Range(0, 12).Select(x => (x * 5).ToString("00", CultureInfo.InvariantCulture)).ToArray(),
                SelectedItem = (Math.Clamp((int)Math.Round(time.Minutes / 5d) * 5, 0, 55)).ToString("00", CultureInfo.InvariantCulture),
                Style = style,
                MinWidth = 82
            };
            _period = new ComboBox
            {
                ItemsSource = new[] { "AM", "PM" },
                SelectedItem = time.Hours < 12 ? "AM" : "PM",
                Style = style,
                MinWidth = 88
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(_hour);
            var separator = new TextBlock
            {
                Text = ":",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResourceBrush("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 7, 0)
            };
            Grid.SetColumn(separator, 1);
            grid.Children.Add(separator);
            Grid.SetColumn(_minute, 2);
            grid.Children.Add(_minute);
            Grid.SetColumn(_period, 4);
            grid.Children.Add(_period);
            Content = grid;
        }
    }

    private static Brush ResourceBrush(string key) => (Brush)Application.Current.FindResource(key);
}
