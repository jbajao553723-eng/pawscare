using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Data.SqlClient;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;

namespace PawCare.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly UserSession _session;
    private readonly ClinicDataStore _store = App.DataStore;
    private MasterDatabase _master = App.MasterDatabase;
    private readonly CloudSyncService _sync = App.SyncService;
    private string _activePage = "dashboard";
    private string? _syncTenantId;
    private string? _syncBranchId;
    private SubscriptionFeatures _enabledFeatures;
    private ServerProfile _administrationServer = App.SelectedServer;
    private readonly Dictionary<string, Button> _navigationButtons = [];

    private ClinicData Data => _store.Data;

    internal void SetModalState(bool isOpen)
    {
        ModalScrim.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
        ModalScrim.IsHitTestVisible = isOpen;
        UpdateLayout();
    }

    private sealed record NavEntry(string Page, string Label, string Icon, string? Section = null);

    public MainWindow(UserSession session)
    {
        _session = session;
        _enabledFeatures = session.Role == UserRole.SuperAdmin ? SubscriptionFeatures.All : _master.GetTenant(session.TenantId).SubscriptionFeatures;
        InitializeComponent();
        SidebarInitials.Text = session.Initials;
        HeaderInitials.Text = session.Initials;
        SidebarName.Text = session.Name;
        SidebarRole.Text = $"{session.Title} · {session.BranchName} · {App.SelectedServer.Name}";
        BuildNavigation();
        Navigate("dashboard");
    }

    private void BuildNavigation()
    {
        NavigationStack.Children.Clear();
        _navigationButtons.Clear();
        IReadOnlyList<NavEntry> entries = _session.Role switch
        {
            UserRole.Receptionist =>
            [
                new NavEntry("dashboard", "Dashboard", "dashboard", "OVERVIEW"),
                new NavEntry("clients", "Client profiles", "clients", "CLIENT CARE"),
                new NavEntry("appointments", "Appointments", "calendar"),
                new NavEntry("inquiries", "Inquiries", "message", "COMMUNICATIONS"),
                new NavEntry("feedback-intake", "Complaints & feedback", "feedback"),
                new NavEntry("reminders", "Reminders & follow-ups", "reminder", "ENGAGEMENT"),
                new NavEntry("promotions", "Promotional offers", "promotion")
            ],
            UserRole.Manager =>
            [
                new NavEntry("dashboard", "Dashboard", "dashboard", "WORKSPACE"),
                new NavEntry("clients", "Client & patient directory", "clients", "OPERATIONS"),
                new NavEntry("appointments", "Customer appointments", "calendar"),
                new NavEntry("inquiries", "Inquiry follow-ups", "message", "CLIENT EXPERIENCE"),
                new NavEntry("feedback", "Feedback & escalations", "feedback"),
                new NavEntry("satisfaction", "Client satisfaction", "heart"),
                new NavEntry("reports", "Business intelligence", "reports", "INSIGHTS")
            ],
            UserRole.Veterinarian =>
            [
                new NavEntry("dashboard", "Clinical dashboard", "dashboard", "WORKSPACE"),
                new NavEntry("clients", "Patient directory", "clients", "PATIENT CARE"),
                new NavEntry("appointments", "Assigned visits", "calendar", "CLINICAL CARE"),
                new NavEntry("consultations", "Consultations & treatment", "medical"),
                new NavEntry("medical-records", "Medical records", "records"),
                new NavEntry("inventory", "Stocks management", "inventory", "RESOURCES")
            ],
            UserRole.Admin =>
            [
                new NavEntry("dashboard", "Branch dashboard", "dashboard", "OVERVIEW"),
                new NavEntry("staff", "Staff & roles", "staff", "ACCESS CONTROL"),
                new NavEntry("branches", "Branches", "organization"),
                new NavEntry("clients", "Client profiles", "clients", "OPERATIONS"),
                new NavEntry("appointments", "Appointments", "calendar"),
                new NavEntry("inventory", "Inventory", "inventory"),
                new NavEntry("promotions", "Promotions", "promotion", "COMMUNICATIONS"),
                new NavEntry("inquiries", "Inquiries", "message"),
                new NavEntry("feedback", "Complaints & feedback", "feedback"),
                new NavEntry("satisfaction", "Client satisfaction", "heart"),
                new NavEntry("reports", "Business intelligence", "reports", "GOVERNANCE"),
                new NavEntry("audit", "Audit logs", "audit"),
                new NavEntry("sync", "Sync & backup", "sync")
            ],
            _ =>
            [
                new NavEntry("dashboard", "Platform dashboard", "dashboard", "OVERVIEW"),
                new NavEntry("organizations", "Organizations", "organization", "TENANT CONTROL"),
                new NavEntry("branches", "Tenant branches", "organization"),
                new NavEntry("subscriptions", "Subscriptions", "reports"),
                new NavEntry("servers", "Server settings", "sync"),
                new NavEntry("branch-admins", "Branch administrators", "staff"),
                new NavEntry("audit", "Master audit logs", "audit", "GOVERNANCE"),
                new NavEntry("sync", "Sync & backup", "sync")
            ]
        };

        foreach (var entry in entries.Where(x => PageIncluded(x.Page)))
        {
            if (!string.IsNullOrWhiteSpace(entry.Section))
                NavigationStack.Children.Add(new TextBlock
                {
                    Text = entry.Section,
                    Foreground = new SolidColorBrush(Color.FromRgb(135, 152, 142)),
                    FontSize = 8,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(12, NavigationStack.Children.Count == 0 ? 0 : 17, 0, 7)
                });

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(CreateIcon(entry.Icon, new SolidColorBrush(Color.FromRgb(193, 203, 197)), 17));
            var label = new TextBlock { Text = entry.Label, FontSize = 11, FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(label, 1);
            content.Children.Add(label);

            var button = new Button
            {
                Content = content,
                Tag = entry.Page,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(193, 203, 197)),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 11, 10, 11),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                FocusVisualStyle = (Style)Application.Current.FindResource("PawFocusVisual"),
                Template = RoundedButtonTemplate(9)
            };
            button.MouseEnter += (_, _) =>
            {
                if (_activePage != entry.Page)
                    button.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
            };
            button.MouseLeave += (_, _) =>
            {
                if (_activePage != entry.Page)
                    button.Background = Brushes.Transparent;
            };
            button.Click += NavigationButton_Click;
            NavigationStack.Children.Add(button);
            _navigationButtons[entry.Page] = button;
        }
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) Navigate(page);
    }

    private void Navigate(string page)
    {
        try
        {
            NavigateCore(page);
        }
        catch (SqlException exception) when (exception.Number == -2)
        {
            MessageBox.Show(this,
                "PawCare could not read SQL Server before the request timed out. Your current screen and unsaved form values were not changed. Try the action again; if it continues, restart the SQLEXPRESS service from Administrator PowerShell.",
                "Database response delayed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NavigateCore(string page)
    {
        // Sync services are created for the server selected during authentication.
        // A Super Admin may browse other masters for subscription administration,
        // but must return to that authenticated server before sync or backup work.
        if (page == "sync" && _session.Role == UserRole.SuperAdmin &&
            !string.Equals(_administrationServer.Instance, App.SelectedServer.Instance, StringComparison.OrdinalIgnoreCase))
        {
            _master = App.MasterDatabase;
            _administrationServer = App.SelectedServer;
            SidebarRole.Text = $"{_session.Title} · {_administrationServer.Name}";
            _syncTenantId = null;
            _syncBranchId = null;
        }
        if (page != "dashboard" && !_navigationButtons.ContainsKey(page))
        {
            MessageBox.Show(this, "This page is not available to your role.", "Access denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _activePage = page;
        foreach (var pair in _navigationButtons)
        {
            var active = pair.Key == page;
            pair.Value.Background = active ? new SolidColorBrush(Color.FromArgb(42, 212, 184, 122)) : Brushes.Transparent;
            pair.Value.Foreground = active ? Brushes.White : new SolidColorBrush(Color.FromRgb(193, 203, 197));
            if (pair.Value.Content is Grid navContent && navContent.Children[0] is Viewbox { Child: Path icon })
                icon.Stroke = active ? Brush("GoldBrush") : new SolidColorBrush(Color.FromRgb(193, 203, 197));
        }

        var metadata = page switch
        {
            "dashboard" => ("OVERVIEW", $"{Greeting()}, {_session.Name.Split(' ')[0]}"),
            "clients" => ("CLIENT CARE", "Client profiles"),
            "appointments" => ("OPERATIONS", _session.Role == UserRole.Manager ? "Customer appointments" : "Appointments"),
            "inquiries" => ("CLIENT CARE", "Inquiries"),
            "feedback-intake" => ("CLIENT CARE", "Complaints & feedback"),
            "reminders" => ("CLIENT CARE", "Reminders & follow-ups"),
            "promotions" => ("ENGAGEMENT", "Promotional offers"),
            "satisfaction" => ("EXPERIENCE", "Client satisfaction"),
            "feedback" => ("EXPERIENCE", "Feedback & escalations"),
            "reports" => ("INSIGHTS", "Reports"),
            "consultations" => ("CLINICAL CARE", "Consultations & treatment"),
            "medical-records" => ("CLINICAL CARE", "Medical records"),
            "inventory" => ("RESOURCES", "Inventory"),
            "staff" => ("ACCESS CONTROL", "Staff & roles"),
            "branches" => ("ACCESS CONTROL", "Branches"),
            "organizations" => ("TENANT CONTROL", "Organizations"),
            "subscriptions" => ("TENANT CONTROL", "Subscriptions"),
            "branch-admins" => ("TENANT CONTROL", "Branch administrators"),
            "servers" => ("TENANT CONTROL", "Server settings"),
            "audit" => ("GOVERNANCE", "Audit logs"),
            "sync" => ("GOVERNANCE", "Sync & backup"),
            _ => ("PAWCARE", "Dashboard")
        };
        var organization = _session.Role == UserRole.SuperAdmin ? "ALL ORGANIZATIONS" : _session.TenantName.ToUpperInvariant();
        BreadcrumbText.Text = $"{organization} / {metadata.Item1}";
        PageTitleText.Text = metadata.Item2;
        ContentHost.Content = page switch
        {
            "dashboard" => BuildDashboardPage(),
            "clients" => BuildClientsPage(),
            "appointments" => BuildAppointmentsPage(),
            "inquiries" => BuildInquiriesPage(),
            "feedback-intake" => BuildFeedbackIntakePage(),
            "reminders" => BuildRemindersPage(),
            "promotions" => BuildPromotionsPage(),
            "satisfaction" => BuildSatisfactionPage(),
            "feedback" => BuildFeedbackPage(),
            "reports" => BuildReportsPage(),
            "consultations" => BuildConsultationsPage(),
            "medical-records" => BuildMedicalRecordsPage(),
            "inventory" => BuildInventoryPage(),
            "staff" => BuildStaffPage(),
            "branches" => BuildBranchesPage(),
            "organizations" => BuildOrganizationsPage(),
            "subscriptions" => BuildSubscriptionsPage(),
            "branch-admins" => BuildBranchAdminsPage(),
            "servers" => BuildServerSettingsPage(),
            "audit" => BuildAuditPage(),
            "sync" => BuildSyncPage(),
            _ => BuildDashboardPage()
        };
    }

    private bool PageIncluded(string page)
    {
        if (page == "reports" && !RoleAccessPolicy.CanViewBusinessIntelligence(_session.Role)) return false;
        var feature = _session.Role == UserRole.SuperAdmin ? SubscriptionFeatures.None : PageFeature(page);
        return feature == SubscriptionFeatures.None || _enabledFeatures.HasFlag(feature);
    }

    private bool RequireSubscribed(string page)
    {
        if (page == "reports" && !RoleAccessPolicy.CanViewBusinessIntelligence(_session.Role))
        {
            MessageBox.Show(this, "Business intelligence is available to Manager and Admin roles.", "Access denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        if (_session.Role == UserRole.SuperAdmin || _enabledFeatures.HasFlag(PageFeature(page))) return true;
        MessageBox.Show(this, "This module is not included in the company's subscription.", "Subscription", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private static SubscriptionFeatures PageFeature(string page) => page switch
    {
        "clients" => SubscriptionFeatures.Clients,
        "appointments" => SubscriptionFeatures.Appointments,
        "inquiries" or "feedback-intake" or "feedback" => SubscriptionFeatures.Collection,
        "satisfaction" => SubscriptionFeatures.Satisfaction,
        "reminders" => SubscriptionFeatures.Reminders,
        "promotions" => SubscriptionFeatures.Promotions,
        "consultations" or "medical-records" => SubscriptionFeatures.Clinical,
        "inventory" => SubscriptionFeatures.Inventory,
        "reports" => SubscriptionFeatures.Reports,
        "branches" => SubscriptionFeatures.Branching,
        "sync" => SubscriptionFeatures.Backup,
        "incremental-sync" => SubscriptionFeatures.IncrementalSync,
        _ => SubscriptionFeatures.None
    };

    private static string Greeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening";
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        try { _store.Save(); }
        catch (Exception exception) when (exception is DataConcurrencyException or UnauthorizedAccessException) { }
        new LoginWindow().Show();
        Close();
    }

    private void GlobalSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(GlobalSearchTextBox.Text)) return;
        var term = GlobalSearchTextBox.Text.Trim();
        if (_navigationButtons.ContainsKey("clients") && Data.Clients.Any(x => Contains(x.ClientName, term) || Contains(x.PetName, term)))
            Navigate("clients");
        else if (_navigationButtons.ContainsKey("appointments") && Data.Appointments.Any(x => Contains(x.ClientName, term) || Contains(x.PetName, term) || Contains(x.Service, term)))
            Navigate("appointments");
        else if (_navigationButtons.ContainsKey("inquiries") && Data.Inquiries.Any(x => Contains(x.Subject, term) || Contains(x.ClientName, term) || Contains(x.Contact, term)))
            Navigate("inquiries");
        else if ((_navigationButtons.ContainsKey("feedback-intake") || _navigationButtons.ContainsKey("feedback")) && Data.Feedback.Any(x => Contains(x.ClientName, term) || Contains(x.PetName, term) || Contains(x.Comment, term)))
            Navigate(_navigationButtons.ContainsKey("feedback") ? "feedback" : "feedback-intake");
        else if (_session.Role == UserRole.Receptionist && Data.Reminders.Any(x => Contains(x.ClientName, term) || Contains(x.PetName, term) || Contains(x.Type, term)))
            Navigate("reminders");
        else if (_session.Role == UserRole.Receptionist && Data.Promotions.Any(x => Contains(x.Name, term) || Contains(x.Code, term) || Contains(x.Audience, term)))
            Navigate("promotions");
        else
            MessageBox.Show(this, $"No records matched “{term}”. Try a client, patient, service, inquiry, reminder, or offer name.", "Search PawCare", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void GlobalSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (GlobalSearchHint is not null)
            GlobalSearchHint.Visibility = string.IsNullOrEmpty(GlobalSearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PersistAndRefresh(string page, string activityIcon, string activityTitle, string activityDetail)
    {
        _store.AddActivity(activityIcon, activityTitle, activityDetail);
        try
        {
            _store.Save();
            Navigate(page);
        }
        catch (DataConcurrencyException exception)
        {
            _store.Reload();
            MessageBox.Show(this, exception.Message, "Concurrent update prevented", MessageBoxButton.OK, MessageBoxImage.Warning);
            Navigate(page);
        }
        catch (UnauthorizedAccessException exception)
        {
            _store.Reload();
            MessageBox.Show(this, exception.Message, "Access denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            Navigate(page);
        }
        catch (InvalidOperationException exception)
        {
            _store.Reload();
            MessageBox.Show(this, exception.Message, "Changes not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            Navigate(page);
        }
    }

    private static bool Contains(string? source, string term) => source?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    private static FontFamily DisplayFont => (FontFamily)Application.Current.FindResource("DisplayFont");

    private static TextBlock Text(string value, double size = 12, Brush? foreground = null, FontWeight? weight = null) => new()
    {
        Text = value,
        FontSize = size,
        Foreground = foreground ?? Brush("InkBrush"),
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap
    };

    private static Viewbox CreateIcon(string key, Brush? stroke = null, double size = 18)
    {
        var data = key switch
        {
            "dashboard" => "M3,3 H10 V10 H3 Z M14,3 H21 V8 H14 Z M14,12 H21 V21 H14 Z M3,14 H10 V21 H3 Z",
            "clients" => "M9,11 A4,4 0 1 1 9,3 A4,4 0 0 1 9,11 M3,21 C3,16 6,14 9,14 C12,14 15,16 15,21 M16,5 C19,5 21,7 21,10 C21,13 19,15 16,15 M17,17 C20,17 22,19 22,21",
            "calendar" => "M4,6 H20 V21 H4 Z M4,10 H20 M8,3 V7 M16,3 V7 M8,14 H11 M14,14 H17 M8,18 H11",
            "message" => "M4,4 H20 V16 H10 L5,20 V16 H4 Z M8,9 H16 M8,12 H14",
            "feedback" => "M12,3 L22,21 H2 Z M12,9 V14 M12,18 L12,18.1",
            "reminder" => "M6,17 H18 M8,17 V10 C8,5 16,5 16,10 V17 M10,20 C11,21 13,21 14,20",
            "promotion" => "M3,12 L12,3 H20 V11 L11,20 Z M16,7 L16.1,7",
            "heart" => "M12,21 C9,18 3,14 3,9 C3,5 8,3 12,7 C16,3 21,5 21,9 C21,14 15,18 12,21 Z",
            "reports" => "M4,21 V13 H8 V21 M10,21 V8 H14 V21 M16,21 V3 H20 V21 M3,21 H21",
            "staff" => "M8,11 A4,4 0 1 1 8,3 A4,4 0 0 1 8,11 M2,21 C2,16 5,14 8,14 C11,14 14,16 14,21 M17,8 V16 M13,12 H21",
            "medical" => "M4,4 H20 V20 H4 Z M12,8 V16 M8,12 H16",
            "records" => "M6,3 H18 V21 H6 Z M9,8 H15 M9,12 H15 M9,16 H13",
            "inventory" => "M4,7 L12,3 L20,7 V18 L12,22 L4,18 Z M4,7 L12,11 L20,7 M12,11 V22",
            "organization" => "M3,21 H21 M5,21 V8 H19 V21 M8,8 V4 H16 V8 M8,12 H10 M14,12 H16 M8,16 H10 M14,16 H16",
            "audit" => "M5,3 H19 V21 H5 Z M8,8 H16 M8,12 H16 M8,16 H13 M17,17 L19,19 L22,15",
            "sync" => "M7,7 C9,4 14,3 17,6 L20,9 M20,5 V9 H16 M17,17 C15,20 10,21 7,18 L4,15 M4,19 V15 H8",
            "search" => "M10.5,18 A7.5,7.5 0 1 1 10.5,3 A7.5,7.5 0 0 1 10.5,18 M16,16 L21,21",
            "activity" => "M12,3 A9,9 0 1 1 12,21 A9,9 0 0 1 12,3 M12,7 V12 L16,14",
            "check" => "M4,12 L9,17 L20,6",
            _ => "M4,4 H20 V20 H4 Z"
        };
        var path = new Path
        {
            Data = Geometry.Parse(data),
            Stroke = stroke ?? Brush("ForestBrush"),
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent
        };
        return new Viewbox { Width = size, Height = size, Child = path, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    private static Button ActionButton(string label, RoutedEventHandler handler, bool primary = true)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)Application.Current.FindResource(primary ? "PrimaryButton" : "OutlineButton"),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 102
        };
        button.Click += handler;
        return button;
    }

    private static Border Panel(UIElement child, Thickness? padding = null) => new()
    {
        Background = Brushes.White,
        BorderBrush = Brush("LineBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = padding ?? new Thickness(18),
        Child = child
    };

    private static ControlTemplate RoundedButtonTemplate(double radius)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static Grid PageHeader(string title, string subtitle, params UIElement[] actions)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock { Text = title, FontFamily = DisplayFont, FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brush("ForestDarkBrush") });
        var description = Text(subtitle, 11, Brush("MutedBrush"));
        description.TextWrapping = TextWrapping.Wrap;
        description.Margin = new Thickness(0, 4, 0, 0);
        copy.Children.Add(description);
        grid.Children.Add(copy);
        var stack = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var action in actions) stack.Children.Add(action);
        Grid.SetRow(stack, 1);
        grid.Children.Add(stack);
        return grid;
    }

    private static Border MetricCard(string iconKey, string value, string label, string trend, Brush accent)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(10), Background = accent, Child = CreateIcon(iconKey, Brush("ForestBrush"), 17) };
        top.Children.Add(icon);
        var trendText = Text(trend, 9, Brush("SageBrush"), FontWeights.SemiBold);
        trendText.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(trendText, 1);
        top.Children.Add(trendText);
        grid.Children.Add(top);
        var number = new TextBlock { Text = value, FontFamily = DisplayFont, FontSize = 27, FontWeight = FontWeights.SemiBold, Foreground = Brush("ForestDarkBrush"), Margin = new Thickness(0, 14, 0, 2) };
        Grid.SetRow(number, 1);
        grid.Children.Add(number);
        var caption = Text(label, 10, Brush("MutedBrush"));
        Grid.SetRow(caption, 2);
        grid.Children.Add(caption);
        return Panel(grid, new Thickness(17));
    }

    private static UniformGrid Metrics(params Border[] cards)
    {
        var grid = new UniformGrid { Columns = cards.Length, Margin = new Thickness(0, 0, 0, 18) };
        for (var index = 0; index < cards.Length; index++)
        {
            cards[index].Margin = new Thickness(0, 0, index == cards.Length - 1 ? 0 : 12, 0);
            grid.Children.Add(cards[index]);
        }
        return grid;
    }

    private static DataGrid Table(IEnumerable source, params DataGridColumn[] columns)
    {
        var table = new DataGrid { ItemsSource = source, MinHeight = 280 };
        foreach (var column in columns) table.Columns.Add(column);
        return table;
    }

    private static DataGridTextColumn Column(string header, string property, double width = 150) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = width
    };

    private static Border Toolbar(TextBox search, ComboBox? filter = null, FrameworkElement? extra = null)
    {
        var toolbar = new Grid();
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };

        search.Style = (Style)Application.Current.FindResource("FieldTextBox");
        search.Width = 320;
        search.Height = 38;
        search.Padding = new Thickness(36, 8, 12, 8);
        var searchHint = search.ToolTip?.ToString() ?? "Search records";
        var searchHost = new Grid();
        searchHost.Children.Add(search);
        var searchIcon = CreateIcon("search", Brush("MutedBrush"), 15);
        searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
        searchIcon.Margin = new Thickness(12, 0, 0, 0);
        searchIcon.IsHitTestVisible = false;
        searchHost.Children.Add(searchIcon);
        var watermark = Text(searchHint, 10, Brush("MutedBrush"));
        watermark.VerticalAlignment = VerticalAlignment.Center;
        watermark.Margin = new Thickness(37, 0, 10, 0);
        watermark.IsHitTestVisible = false;
        searchHost.Children.Add(watermark);
        search.TextChanged += (_, _) => watermark.Visibility = string.IsNullOrWhiteSpace(search.Text) ? Visibility.Visible : Visibility.Collapsed;

        var searchGroup = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        searchGroup.Children.Add(Text("SEARCH RECORDS", 8, Brush("MutedBrush"), FontWeights.SemiBold));
        searchHost.Margin = new Thickness(0, 5, 0, 0);
        searchGroup.Children.Add(searchHost);
        controls.Children.Add(searchGroup);
        if (filter is not null)
        {
            filter.Style = (Style)Application.Current.FindResource("FieldComboBox");
            filter.Width = 155;
            filter.Height = 38;
            var filterGroup = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
            filterGroup.Children.Add(Text("FILTER", 8, Brush("MutedBrush"), FontWeights.SemiBold));
            filter.Margin = new Thickness(0, 5, 0, 0);
            filterGroup.Children.Add(filter);
            controls.Children.Add(filterGroup);
        }
        if (extra is not null)
        {
            if (extra is ComboBox secondaryFilter)
            {
                secondaryFilter.Style = (Style)Application.Current.FindResource("FieldComboBox");
                secondaryFilter.Width = 145;
                secondaryFilter.Height = 38;
                secondaryFilter.Margin = new Thickness(0, 5, 0, 0);
                var secondaryGroup = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                secondaryGroup.Children.Add(Text("PRIORITY", 8, Brush("MutedBrush"), FontWeights.SemiBold));
                secondaryGroup.Children.Add(secondaryFilter);
                controls.Children.Add(secondaryGroup);
            }
            else
            {
                extra.VerticalAlignment = VerticalAlignment.Bottom;
                controls.Children.Add(extra);
            }
        }
        toolbar.Children.Add(controls);

        var guidance = Text("Select a row to use the actions above. Double-click to open it.", 9, Brush("MutedBrush"));
        guidance.VerticalAlignment = VerticalAlignment.Bottom;
        guidance.Margin = new Thickness(18, 0, 0, 9);
        Grid.SetColumn(guidance, 1);
        toolbar.Children.Add(guidance);

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Brush("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 11, 14, 11),
            Margin = new Thickness(0, 0, 0, 12),
            Child = toolbar
        };
    }
}
