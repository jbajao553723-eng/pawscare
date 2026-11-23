using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Data.SqlClient;
using PawCare.Desktop.Controls;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;

namespace PawCare.Desktop.Views;

public partial class MainWindow
{
    private UIElement BuildVeterinarianDashboard()
    {
        var assigned = Data.Appointments.Where(x => Same(x.Veterinarian, _session.Name) && x.Status != "Cancelled").ToArray();
        var consultations = Data.Consultations.Where(x => Same(x.Veterinarian, _session.Name)).ToArray();
        var undocumented = assigned.Count(x => x.Date.Date <= DateTime.Today && !consultations.Any(c => c.AppointmentId == x.Id));
        var followUps = consultations.Count(x => !string.IsNullOrWhiteSpace(x.FollowUpPlan) && x.Status != "Completed");
        var root = VerticalPage();
        root.Children.Add(PageHeader("Clinical workspace", $"Review assigned visits and complete records for {_session.TenantName}.",
            ActionButton("Open consultations", (_, _) => Navigate("consultations")),
            ActionButton("Review inventory", (_, _) => Navigate("inventory"), false)));
        root.Children.Add(Metrics(
            MetricCard("calendar", assigned.Count(x => x.Date.Date == DateTime.Today).ToString(), "Assigned today", "Visit schedule", Brush("SageSoftBrush")),
            MetricCard("medical", consultations.Count(x => x.Status != "Completed").ToString(), "Open consultations", "Clinical work", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("records", undocumented.ToString(), "Visits needing notes", undocumented > 0 ? "Document today" : "Records current", Brush("GoldSoftBrush")),
            MetricCard("reminder", followUps.ToString(), "Clinical follow-ups", "Plans still in progress", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("inventory", Data.Inventory.Count(x => x.Quantity <= x.ReorderLevel).ToString(), "Low-stock items", "Review supplies", new SolidColorBrush(Color.FromRgb(248, 233, 231)))));

        var grid = TwoColumnGrid(1.35, 0.9);
        var visitPanel = new StackPanel();
        visitPanel.Children.Add(SectionHeading("Assigned visits", "Appointments routed to your clinical account"));
        foreach (var visit in assigned.OrderBy(x => x.Date).ThenBy(x => x.Time).Take(6))
            visitPanel.Children.Add(InfoRow(visit.Visit, visit.ClientPatient, visit.Service, visit.Status));
        if (assigned.Length == 0) visitPanel.Children.Add(EmptyState("calendar", "No assigned visits", "The branch schedule has no appointments assigned to this account."));
        grid.Children.Add(Panel(visitPanel));

        var right = new StackPanel();
        var actionPanel = new StackPanel();
        actionPanel.Children.Add(SectionHeading("Clinical actions", "Move directly into the record that needs work"));
        actionPanel.Children.Add(ActionQueueButton("medical", "Consultations", "Assessment and treatment planning", consultations.Count(x => x.Status != "Completed"), (_, _) => Navigate("consultations")));
        actionPanel.Children.Add(ActionQueueButton("clients", "Patient directory", "Owner contacts and recent care context", Data.Clients.Count, (_, _) => Navigate("clients")));
        actionPanel.Children.Add(ActionQueueButton("records", "Medical records", "Notes and vaccination history", consultations.Length, (_, _) => Navigate("medical-records")));
        actionPanel.Children.Add(ActionQueueButton("inventory", "Stocks management", "Medication and vaccine quantities", Data.Inventory.Count(x => x.Quantity <= x.ReorderLevel), (_, _) => Navigate("inventory"), Data.Inventory.Any(x => x.Quantity <= x.ReorderLevel)));
        right.Children.Add(Panel(actionPanel, new Thickness(18, 18, 18, 12)));
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        root.Children.Add(grid);
        return root;
    }

    private UIElement BuildAdminDashboard()
    {
        var staff = _master.GetStaff(_session.TenantId).Where(x => x.Role != UserRole.Admin).ToArray();
        var sync = _sync.GetStatus();
        var root = VerticalPage();
        root.Children.Add(PageHeader("Branch administration", $"Access control, branch operations, and data protection for {_session.TenantName}.",
            ActionButton("Manage staff", (_, _) => Navigate("staff")),
            PageIncluded("sync") ? ActionButton("Backup & sync", (_, _) => Navigate("sync"), false) : new StackPanel()));
        root.Children.Add(Metrics(
            MetricCard("staff", staff.Length.ToString(), "Staff accounts", "Branch access", Brush("SageSoftBrush")),
            MetricCard("calendar", Data.Appointments.Count(x => x.Date.Date >= DateTime.Today).ToString(), "Upcoming visits", "Operations", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("feedback", Data.Feedback.Count(x => x.Status is "Open" or "Escalated").ToString(), "Open client cases", "Follow-through", Brush("GoldSoftBrush")),
            PageIncluded("incremental-sync")
                ? MetricCard("sync", sync.PendingEvents.ToString(), "Pending sync events", "Local outbox", new SolidColorBrush(Color.FromRgb(248, 233, 231)))
                : MetricCard("records", sync.LastFullBackupUtc is null ? "Never" : "Ready", "Full backup", sync.LastBackup, new SolidColorBrush(Color.FromRgb(248, 233, 231)))));

        var grid = TwoColumnGrid(1.2, 1);
        var roles = new StackPanel();
        roles.Children.Add(SectionHeading("Branch role coverage", "Accounts are assigned through the master database"));
        foreach (var group in staff.GroupBy(x => x.Role).OrderBy(x => x.Key.ToString()))
            roles.Children.Add(InfoRow(group.Count().ToString(), group.Key.ToString(), $"{group.Count(x => x.IsActive)} active accounts", "Branch role"));
        grid.Children.Add(Panel(roles));

        var protection = new StackPanel();
        protection.Children.Add(SectionHeading("Data protection", "Operations remain local; cloud targets receive sync and backup packages only"));
        protection.Children.Add(InfoRow(sync.PendingEvents.ToString(), "Transactional outbox", "Idempotent events waiting for sync", "Protected"));
        protection.Children.Add(InfoRow("SQL", "Concurrent writes", "Serializable transactions, update locks, and optimistic revision checks", "Enabled"));
        protection.Children.Add(InfoRow("Local", "Operational database", _store.StoragePath, "Primary"));
        var protectionPanel = Panel(protection);
        Grid.SetColumn(protectionPanel, 2);
        grid.Children.Add(protectionPanel);
        root.Children.Add(grid);
        return root;
    }

    private UIElement BuildSuperAdminDashboard()
    {
        var tenants = _master.GetTenants();
        var admins = _master.GetStaff(role: UserRole.Admin);
        var root = VerticalPage();
        root.Children.Add(PageHeader("Platform administration", "Manage organization isolation and appoint one or more administrators for each branch.",
            ActionButton("Assign branch admin", (_, _) => OpenBranchAdminDialog()),
            ActionButton("Manage subscriptions", (_, _) => Navigate("subscriptions"), false),
            ActionButton("View organizations", (_, _) => Navigate("organizations"), false)));
        root.Children.Add(Metrics(
            MetricCard("organization", tenants.Count.ToString(), "Organizations", "Isolated tenant databases", Brush("SageSoftBrush")),
            MetricCard("staff", admins.Count.ToString(), "Branch administrators", "Master assignments", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("audit", _master.GetAuditLogs(limit: 1000).Count.ToString(), "Master audit events", "Access governance", Brush("GoldSoftBrush")),
            MetricCard("sync", tenants.Count(x => x.IsActive).ToString(), "Active sync targets", "Branch endpoints", Brush("SageSoftBrush"))));

        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Organization directory", "The master database resolves usernames to these isolated local databases"));
        foreach (var tenant in tenants)
        {
            var count = admins.Count(x => x.TenantId == tenant.Id);
            panel.Children.Add(InfoRow(tenant.Code, tenant.Name, tenant.DatabasePath, count == 0 ? "No admin" : $"{count} admin{(count == 1 ? "" : "s")}"));
        }
        root.Children.Add(Panel(panel));
        return root;
    }

    private UIElement BuildServerSettingsPage()
    {
        var servers = App.Servers.ToArray();
        var table = Table(servers,
            Column("PROFILE", nameof(ServerProfile.Name), 190),
            Column("SQL INSTANCE", nameof(ServerProfile.Instance), 270),
            Column("DATABASE PREFIX", nameof(ServerProfile.DatabasePrefix), 170));
        void SwitchServer()
        {
            if (table.SelectedItem is not ServerProfile selected)
            {
                ShowSelectionRequired("server profile");
                return;
            }
            try
            {
                _master = App.OpenMaster(selected);
                _administrationServer = selected;
                SidebarRole.Text = $"{_session.Title} · {_administrationServer.Name}";
                Navigate("dashboard");
            }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException or UnauthorizedAccessException or IOException)
            {
                ShowAdministrationError(exception);
            }
        }
        table.MouseDoubleClick += (_, _) => SwitchServer();
        var root = VerticalPage();
        root.Children.Add(PageHeader("Server settings",
            $"Subscription and organization administration currently targets {_administrationServer.Name}.",
            ActionButton("Open selected server", (_, _) => SwitchServer())));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildStaffPage()
    {
        var accounts = _master.GetStaff(_session.TenantId).Where(x => x.Role != UserRole.Admin && x.Role != UserRole.SuperAdmin).ToArray();
        var table = Table(accounts,
            Column("USERNAME", nameof(StaffAccountRecord.Username), 150), Column("STAFF MEMBER", nameof(StaffAccountRecord.FullName), 210),
            Column("ROLE", nameof(StaffAccountRecord.RoleLabel), 150), Column("ORGANIZATION", nameof(StaffAccountRecord.TenantName), 220),
            Column("STATUS", nameof(StaffAccountRecord.Status), 100));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is StaffAccountRecord record) OpenStaffDialog(record); };
        var root = VerticalPage();
        root.Children.Add(PageHeader("Staff & roles", "Create staff accounts and assign operational roles within this branch only.",
            ActionButton("Edit selected", (_, _) => { if (table.SelectedItem is StaffAccountRecord record) OpenStaffDialog(record); else ShowSelectionRequired("staff account"); }, false),
            ActionButton("Toggle access", (_, _) => { if (table.SelectedItem is StaffAccountRecord record) ToggleAccount(record); else ShowSelectionRequired("staff account"); }, false),
            ActionButton("＋ Add staff", (_, _) => OpenStaffDialog())));
        root.Children.Add(Metrics(
            MetricCard("staff", accounts.Length.ToString(), "Branch staff", "All roles", Brush("SageSoftBrush")),
            MetricCard("clients", accounts.Count(x => x.Role == UserRole.Receptionist).ToString(), "Receptionists", "Front desk", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("reports", accounts.Count(x => x.Role == UserRole.Manager).ToString(), "Managers", "Operations", Brush("GoldSoftBrush")),
            MetricCard("medical", accounts.Count(x => x.Role == UserRole.Veterinarian).ToString(), "Veterinarians", "Clinical care", Brush("SageSoftBrush"))));
        var search = new TextBox { ToolTip = "Search username, staff member, role or organization" };
        var view = ResetView(accounts);
        search.TextChanged += (_, _) => { var term = search.Text.Trim(); view.Filter = item => item is StaffAccountRecord record && (string.IsNullOrEmpty(term) || Contains(record.Username, term) || Contains(record.FullName, term) || Contains(record.RoleLabel, term)); };
        root.Children.Add(Toolbar(search));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildBranchesPage()
    {
        if (_session.Role == UserRole.SuperAdmin) return BuildPlatformBranchesPage();

        var tenant = _master.GetTenant(_session.TenantId);
        var branches = _master.GetBranches(_session.TenantId).ToArray();
        var table = Table(branches,
            Column("CODE", nameof(BranchRecord.Code), 120), Column("BRANCH", nameof(BranchRecord.Name), 220),
            Column("DATABASE", nameof(BranchRecord.DatabasePath), 360), Column("STATUS", nameof(BranchRecord.Status), 110));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is BranchRecord branch) OpenBranchDialog(branch); };
        var root = VerticalPage();
        root.Children.Add(PageHeader("Branches", $"{_session.TenantName} uses {branches.Count(x => x.IsActive)} of {tenant.MaxBranches} branches on the {tenant.SubscriptionPlanName} plan.",
            ActionButton("Switch selected", (_, _) => { if (table.SelectedItem is BranchRecord branch) SwitchBranch(branch); else ShowSelectionRequired("branch"); }, false),
            ActionButton("Edit selected", (_, _) => { if (table.SelectedItem is BranchRecord branch) OpenBranchDialog(branch); else ShowSelectionRequired("branch"); }, false),
            ActionButton("＋ Add branch", (_, _) => OpenBranchDialog())));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildPlatformBranchesPage()
    {
        var tenants = _master.GetTenants().ToArray();
        var branches = tenants
            .SelectMany(tenant => _master.GetBranches(tenant.Id)
                .Select(branch => new BranchDirectoryRecord { Tenant = tenant, Branch = branch }))
            .ToArray();
        var branchingTenants = tenants
            .Where(x => x.SubscriptionFeatures.HasFlag(SubscriptionFeatures.Branching))
            .ToArray();
        var remainingCapacity = branchingTenants.Sum(tenant =>
            Math.Max(0, tenant.MaxBranches - branches.Count(x => x.TenantId == tenant.Id && x.Branch.IsActive)));
        var table = Table(branches,
            Column("ORGANIZATION", nameof(BranchDirectoryRecord.Organization), 230),
            Column("PLAN", nameof(BranchDirectoryRecord.Plan), 150),
            Column("CODE", nameof(BranchDirectoryRecord.Code), 90),
            Column("BRANCH", nameof(BranchDirectoryRecord.Name), 190),
            Column("DATABASE", nameof(BranchDirectoryRecord.DatabasePath), 250),
            Column("TYPE", nameof(BranchDirectoryRecord.Type), 95),
            Column("STATUS", nameof(BranchDirectoryRecord.Status), 95));
        table.MouseDoubleClick += (_, _) =>
        {
            if (table.SelectedItem is BranchDirectoryRecord record)
                OpenBranchDialog(record.Branch, record.TenantId);
        };

        var root = VerticalPage();
        root.Children.Add(PageHeader("Tenant branches",
            $"Manage isolated branch databases on {_administrationServer.Name}. Only a plan with Branching can add branches.",
            ActionButton("Edit selected", (_, _) =>
            {
                if (table.SelectedItem is BranchDirectoryRecord record) OpenBranchDialog(record.Branch, record.TenantId);
                else ShowSelectionRequired("branch");
            }, false),
            ActionButton("＋ Add branch", (_, _) => OpenBranchDialog())));
        root.Children.Add(Metrics(
            MetricCard("organization", tenants.Length.ToString(), "Organizations", "On this SQL server", Brush("SageSoftBrush")),
            MetricCard("reports", branchingTenants.Length.ToString(), "Branch-enabled", "Full Clinic or equivalent", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("organization", branches.Count(x => x.Branch.IsActive).ToString(), "Active branches", "Isolated databases", Brush("GoldSoftBrush")),
            MetricCard("calendar", remainingCapacity.ToString(), "Available branch slots", "Across enabled plans", new SolidColorBrush(Color.FromRgb(248, 233, 231)))));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private void OpenBranchDialog(BranchRecord? existing = null, string? targetTenantId = null)
    {
        if (!RequireSubscribed("branches")) return;

        var tenantId = targetTenantId ?? _session.TenantId;
        Dictionary<string, TenantRecord>? tenantChoices = null;
        var fields = new List<FieldDefinition>();
        if (_session.Role == UserRole.SuperAdmin)
        {
            if (existing is null)
            {
                tenantChoices = _master.GetTenants()
                    .Where(x => x.IsActive && x.SubscriptionFeatures.HasFlag(SubscriptionFeatures.Branching))
                    .ToDictionary(x => $"{x.Code} · {x.Name} ({x.SubscriptionPlanName})", x => x);
                if (tenantChoices.Count == 0)
                {
                    MessageBox.Show(this,
                        "No active organization on this server has a branching-enabled subscription. Tenant A and Tenant B remain single-branch on their current plans; select Tenant C's server or assign Full Clinic first.",
                        "No branch-enabled organization", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var selectedTenant = tenantChoices.First();
                fields.Add(new FieldDefinition("tenant", "Organization", FieldKind.Choice, selectedTenant.Key, tenantChoices.Keys.ToArray(),
                    Help: "Only organizations whose subscription includes Branching are listed.", SpanColumns: true));
            }
            else
            {
                var tenant = _master.GetTenant(tenantId);
                fields.Add(new FieldDefinition("tenant", "Organization", FieldKind.ReadOnly,
                    $"{tenant.Code} · {tenant.Name} ({tenant.SubscriptionPlanName})", SpanColumns: true));
            }
        }
        fields.AddRange([
            new FieldDefinition("code", "Branch code", Value: existing?.Code,
                Help: "Used in the readable database name, for example PawCareTenantC_NORTH."),
            new FieldDefinition("name", "Branch name", Value: existing?.Name),
            new FieldDefinition("active", "Active branch", FieldKind.CheckBox, existing?.IsActive ?? true, Required: false)
        ]);

        var dialog = new RecordDialog(this, existing is null ? "Add branch" : "Edit branch",
            "Each branch has its own operational database. Staff choose an active branch when signing in.", fields);
        if (dialog.ShowModal() != true) return;
        try
        {
            if (tenantChoices is not null) tenantId = tenantChoices[dialog.Text("tenant")].Id;
            _master.SaveBranch(_session, existing, tenantId, dialog.Text("code"), dialog.Text("name"), dialog.Bool("active"));
            Navigate("branches");
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or SqlException)
        { ShowAdministrationError(exception); }
    }

    private void SwitchBranch(BranchRecord branch)
    {
        if (!branch.IsActive) { MessageBox.Show(this, "Select an active branch.", "Branches"); return; }
        if (branch.Id == _session.BranchId) return;
        try
        {
            _store.Save();
            var session = App.ActivateSession(_session, branch);
            new MainWindow(session).Show();
            Close();
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or DataConcurrencyException or SqlException or IOException)
        { ShowAdministrationError(exception); }
    }

    private UIElement BuildBranchAdminsPage()
    {
        var accounts = _master.GetStaff(role: UserRole.Admin).ToArray();
        var table = Table(accounts,
            Column("USERNAME", nameof(StaffAccountRecord.Username), 150), Column("ADMINISTRATOR", nameof(StaffAccountRecord.FullName), 220),
            Column("ORGANIZATION", nameof(StaffAccountRecord.TenantName), 230), Column("STATUS", nameof(StaffAccountRecord.Status), 100));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is StaffAccountRecord record) OpenBranchAdminDialog(record); };
        var root = VerticalPage();
        root.Children.Add(PageHeader("Branch administrators", "Super Admin assigns administrator access to a specific organization; branch admins assign only operational staff.",
            ActionButton("Edit selected", (_, _) => { if (table.SelectedItem is StaffAccountRecord record) OpenBranchAdminDialog(record); else ShowSelectionRequired("branch administrator"); }, false),
            ActionButton("Toggle access", (_, _) => { if (table.SelectedItem is StaffAccountRecord record) ToggleAccount(record); else ShowSelectionRequired("branch administrator"); }, false),
            ActionButton("＋ Assign admin", (_, _) => OpenBranchAdminDialog())));
        var search = new TextBox { ToolTip = "Search administrator, username or organization" };
        var view = ResetView(accounts);
        search.TextChanged += (_, _) => { var term = search.Text.Trim(); view.Filter = item => item is StaffAccountRecord record && (string.IsNullOrEmpty(term) || Contains(record.Username, term) || Contains(record.FullName, term) || Contains(record.TenantName, term)); };
        root.Children.Add(Toolbar(search));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildOrganizationsPage()
    {
        var tenants = _master.GetTenants().ToArray();
        var table = Table(tenants,
            Column("CODE", nameof(TenantRecord.Code), 100), Column("ORGANIZATION", nameof(TenantRecord.Name), 220),
            Column("DATABASE", nameof(TenantRecord.DatabasePath), 300), Column("SUBSCRIPTION", nameof(TenantRecord.SubscriptionPlanName), 150),
            Column("SYNC ENDPOINT", nameof(TenantRecord.SyncEndpoint), 180),
            Column("STATUS", nameof(TenantRecord.Status), 90));
        var root = VerticalPage();
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is TenantRecord record) OpenTenantDialog(record); };
        root.Children.Add(PageHeader("Organizations", "Manage each organization's isolated tenant database and access status.",
            ActionButton("Edit selected", (_, _) => { if (table.SelectedItem is TenantRecord record) OpenTenantDialog(record); else ShowSelectionRequired("organization"); }, false),
            ActionButton("＋ Add organization", (_, _) => OpenTenantDialog())));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildSubscriptionsPage()
    {
        var plans = _master.GetSubscriptionPlans().ToArray();
        var table = Table(plans,
            Column("CODE", nameof(SubscriptionPlanRecord.Code), 110), Column("PLAN", nameof(SubscriptionPlanRecord.Name), 180),
            Column("MODULES", nameof(SubscriptionPlanRecord.Modules), 600), Column("BRANCH LIMIT", nameof(SubscriptionPlanRecord.MaxBranches), 120),
            Column("STATUS", nameof(SubscriptionPlanRecord.Status), 100));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is SubscriptionPlanRecord plan) OpenSubscriptionDialog(plan); };
        var root = VerticalPage();
        root.Children.Add(PageHeader("Subscription settings", "Super Admin defines plan modules and branch limits. Assign a plan on each organization record.",
            ActionButton("Edit selected", (_, _) => { if (table.SelectedItem is SubscriptionPlanRecord plan) OpenSubscriptionDialog(plan); else ShowSelectionRequired("subscription plan"); }, false),
            ActionButton("＋ Add plan", (_, _) => OpenSubscriptionDialog())));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildAuditPage()
    {
        var records = _session.Role == UserRole.SuperAdmin
            ? _master.GetAuditLogs(limit: 500).ToArray()
            : _store.GetAuditLogs(500).Concat(_master.GetAuditLogs(_session.TenantId, 200)).OrderByDescending(x => x.OccurredUtc).ToArray();
        var table = Table(records,
            Column("TIME", nameof(AuditLogRecord.Occurred), 165), Column("ACTOR", nameof(AuditLogRecord.ActorUsername), 160),
            Column("ACTION", nameof(AuditLogRecord.Action), 120), Column("ENTITY", nameof(AuditLogRecord.EntityType), 120),
            Column("RECORD", nameof(AuditLogRecord.EntityId), 150), Column("DETAILS", nameof(AuditLogRecord.Details), 360));
        var root = VerticalPage();
        root.Children.Add(PageHeader("Audit logs", "Review access-control and local transaction events without changing operational data."));
        var search = new TextBox { ToolTip = "Search actor, action, entity or details" };
        var view = ResetView(records);
        search.TextChanged += (_, _) => { var term = search.Text.Trim(); view.Filter = item => item is AuditLogRecord record && (string.IsNullOrEmpty(term) || Contains(record.ActorUsername, term) || Contains(record.Action, term) || Contains(record.EntityType, term) || Contains(record.Details, term)); };
        root.Children.Add(Toolbar(search));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildSyncPage()
    {
        var selectedTenant = _session.Role == UserRole.SuperAdmin && _syncTenantId is not null
            ? _master.GetTenants().FirstOrDefault(x => x.Id == _syncTenantId && x.IsActive) : null;
        var selectedBranch = selectedTenant is null ? null : _master.GetBranches(selectedTenant.Id).FirstOrDefault(x => x.Id == _syncBranchId && x.IsActive);
        var displayedService = _session.Role == UserRole.SuperAdmin
            ? selectedTenant is null || selectedBranch is null ? null : CreateSyncService(selectedTenant, selectedBranch)
            : _sync;
        var status = displayedService?.GetStatus() ?? new SyncStatus(0, null, null, "Select an organization");
        var context = _session.Role == UserRole.SuperAdmin
            ? selectedBranch is null ? "Select an organization and branch to see its sync status." : $"Status for {selectedTenant!.Name} / {selectedBranch.Name}."
            : $"Status for {_session.TenantName} / {_session.BranchName}.";
        async void RunCloudBackupClick(object sender, RoutedEventArgs _) => await RunCloudBackupAsync((Button)sender);
        var root = VerticalPage();
        root.Children.Add(PageHeader("Sync & backup", $"{context} The local tenant database is authoritative; backups are available on every plan.",
            _session.Role == UserRole.SuperAdmin ? ActionButton("Select organization", (_, _) => ChooseSyncTenant(), false) : new StackPanel(),
            PageIncluded("incremental-sync") ? ActionButton("Incremental sync", (_, _) => RunIncrementalSync()) : new StackPanel(),
            ActionButton("Cloud backup", RunCloudBackupClick)));
        root.Children.Add(Metrics(
            MetricCard("sync", status.PendingEvents.ToString(), "Pending events", "Transactional outbox", Brush("GoldSoftBrush")),
            MetricCard("activity", status.LastIncrementalSyncUtc is null ? "Never" : "Synced", "Last incremental sync", status.LastIncremental, Brush("SageSoftBrush")),
            MetricCard("records", status.LastFullBackupUtc is null ? "Never" : "Protected", "Last cloud backup", status.LastBackup, new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("check", "Local", "Operational source", "No cloud writes in request path", Brush("SageSoftBrush"))));
        var destinations = new StackPanel();
        destinations.Children.Add(SectionHeading("Backup destinations", "Cloud backup creates a local recovery copy first, then copies it atomically to the configured cloud mirror."));
        destinations.Children.Add(InfoRow("LOCAL", "Local recovery copy", displayedService?.LocalBackupTarget ?? "Select an organization and branch", displayedService is null ? "Waiting" : "Ready"));
        destinations.Children.Add(InfoRow("CLOUD", "Configured cloud mirror", displayedService?.CloudBackupTarget ?? "Select an organization and branch", displayedService is null ? "Waiting" : "Ready"));
        var destinationPanel = Panel(destinations);
        destinationPanel.Margin = new Thickness(0, 0, 0, 18);
        root.Children.Add(destinationPanel);
        var panel = new StackPanel();
        panel.Children.Add(SectionHeading("Race-condition protection", "Every write and sync path has an explicit concurrency boundary"));
        panel.Children.Add(InfoRow("TX", "Serializable SQL transaction", "Update locks serialize writers while committed reads remain available", "Enabled"));
        panel.Children.Add(InfoRow("REV", "Optimistic data revision", "Stale sessions reload instead of overwriting newer branch data", "Enabled"));
        panel.Children.Add(InfoRow("OUT", "Transactional outbox", "Local data and incremental sync events commit together", "Enabled"));
        panel.Children.Add(InfoRow("LOCK", "Expiring sync lease", "Only one incremental sync or cloud backup runs per tenant", "Enabled"));
        root.Children.Add(Panel(panel));
        return root;
    }

    private UIElement BuildConsultationsPage()
    {
        var records = Data.Consultations.Where(x => Same(x.Veterinarian, _session.Name)).ToArray();
        var table = Table(records,
            Column("DATE", nameof(ConsultationRecord.Date), 110), Column("CLIENT & PATIENT", nameof(ConsultationRecord.PatientVisit), 190),
            Column("CHIEF COMPLAINT", nameof(ConsultationRecord.ChiefComplaint), 190), Column("ASSESSMENT", nameof(ConsultationRecord.Summary), 220),
            Column("TREATMENT PLAN", nameof(ConsultationRecord.TreatmentPlan), 260), Column("FOLLOW-UP", nameof(ConsultationRecord.FollowUpPlan), 200),
            Column("STATUS", nameof(ConsultationRecord.Status), 110));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is ConsultationRecord record) OpenConsultationDialog(record); };
        var root = VerticalPage();
        root.Children.Add(PageHeader("Consultations & treatment", "Document assessments, treatment plans, medical notes, and vaccinations for assigned visits.",
            ActionButton("Open selected", (_, _) => { if (table.SelectedItem is ConsultationRecord record) OpenConsultationDialog(record); else ShowSelectionRequired("consultation"); }, false),
            ActionButton("Archive selected", (_, _) => ArchiveSelected(Data.Consultations, table.SelectedItem as ConsultationRecord, "consultations", "consultation"), false),
            ActionButton("Start consultation", (_, _) => OpenConsultationDialog())));
        root.Children.Add(Metrics(
            MetricCard("medical", records.Count(x => x.Status == "In progress").ToString(), "In progress", "Active clinical work", Brush("SageSoftBrush")),
            MetricCard("records", records.Count(x => x.Status == "Draft").ToString(), "Draft records", "Needs completion", new SolidColorBrush(Color.FromRgb(248, 233, 231))),
            MetricCard("check", records.Count(x => x.Status == "Completed").ToString(), "Completed", "Signed clinical records", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("reminder", records.Count(x => !string.IsNullOrWhiteSpace(x.FollowUpPlan)).ToString(), "Follow-up plans", "Continuity of care", Brush("GoldSoftBrush"))));
        var search = new TextBox { ToolTip = "Search patient, owner, complaint, assessment or treatment" };
        var filter = ChoiceFilter(["All statuses", "Draft", "In progress", "Completed"]);
        var view = CollectionViewSource.GetDefaultView(records);
        void Apply()
        {
            var term = search.Text.Trim();
            var status = filter.SelectedItem?.ToString();
            view.Filter = item => item is ConsultationRecord record
                && (string.IsNullOrEmpty(term) || Contains(record.ClientName, term) || Contains(record.PetName, term) || Contains(record.ChiefComplaint, term) || Contains(record.Assessment, term) || Contains(record.TreatmentPlan, term))
                && (status == "All statuses" || record.Status == status);
        }
        search.TextChanged += (_, _) => Apply(); filter.SelectionChanged += (_, _) => Apply();
        root.Children.Add(Toolbar(search, filter));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildMedicalRecordsPage()
    {
        var records = Data.Consultations.Where(x => Same(x.Veterinarian, _session.Name)).ToArray();
        var table = Table(records,
            Column("DATE", nameof(ConsultationRecord.Date), 110), Column("CLIENT & PATIENT", nameof(ConsultationRecord.PatientVisit), 190),
            Column("CHIEF COMPLAINT", nameof(ConsultationRecord.ChiefComplaint), 190), Column("ASSESSMENT", nameof(ConsultationRecord.Assessment), 220),
            Column("PRESCRIPTIONS", nameof(ConsultationRecord.Prescriptions), 210), Column("VACCINATIONS", nameof(ConsultationRecord.Vaccinations), 180),
            Column("FOLLOW-UP PLAN", nameof(ConsultationRecord.FollowUpPlan), 220), Column("STATUS", nameof(ConsultationRecord.Status), 100));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is ConsultationRecord record) OpenConsultationDialog(record); };
        var root = VerticalPage();
        root.Children.Add(PageHeader("Medical records", "Review visit reasons, assessments, prescriptions, vaccinations, and continuity-of-care plans for assigned patients.",
            ActionButton("Review selected", (_, _) => { if (table.SelectedItem is ConsultationRecord record) OpenConsultationDialog(record); else ShowSelectionRequired("medical record"); }, false)));
        root.Children.Add(Metrics(
            MetricCard("records", records.Length.ToString(), "Clinical records", "Assigned patients", Brush("SageSoftBrush")),
            MetricCard("medical", records.Count(x => !string.IsNullOrWhiteSpace(x.Prescriptions)).ToString(), "With prescriptions", "Medication plans", new SolidColorBrush(Color.FromRgb(232, 242, 244))),
            MetricCard("check", records.Count(x => !string.IsNullOrWhiteSpace(x.ClientInstructions)).ToString(), "Owner instructions", "Documented home care", Brush("GoldSoftBrush")),
            MetricCard("reminder", records.Count(x => !string.IsNullOrWhiteSpace(x.FollowUpPlan)).ToString(), "Follow-up plans", "Continuity recorded", new SolidColorBrush(Color.FromRgb(248, 233, 231)))));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private UIElement BuildInventoryPage()
    {
        var table = Table(Data.Inventory,
            Column("ITEM", nameof(InventoryRecord.ItemName), 220), Column("CATEGORY", nameof(InventoryRecord.Category), 140),
            Column("QUANTITY", nameof(InventoryRecord.Quantity), 100), Column("UNIT", nameof(InventoryRecord.Unit), 100),
            Column("REORDER LEVEL", nameof(InventoryRecord.ReorderLevel), 120), Column("STATUS", nameof(InventoryRecord.StockStatus), 110),
            Column("UPDATED", nameof(InventoryRecord.Updated), 150));
        table.MouseDoubleClick += (_, _) => { if (table.SelectedItem is InventoryRecord record) OpenInventoryDialog(record); };
        var root = VerticalPage();
        root.Children.Add(PageHeader("Inventory", "Maintain local stock quantities for treatment supplies, vaccines, and clinical resources.",
            ActionButton("Update selected", (_, _) => { if (table.SelectedItem is InventoryRecord record) OpenInventoryDialog(record); else ShowSelectionRequired("inventory item"); }, false),
            ActionButton("Archive selected", (_, _) => ArchiveSelected(Data.Inventory, table.SelectedItem as InventoryRecord, "inventory", "inventory item"), false),
            ActionButton("＋ Add item", (_, _) => OpenInventoryDialog())));
        root.Children.Add(Metrics(
            MetricCard("inventory", Data.Inventory.Count.ToString(), "Tracked items", "Local inventory", Brush("SageSoftBrush")),
            MetricCard("feedback", Data.Inventory.Count(x => x.Quantity <= x.ReorderLevel).ToString(), "Reorder needed", "Stock alert", new SolidColorBrush(Color.FromRgb(248, 233, 231))),
            MetricCard("medical", Data.Inventory.Count(x => x.Category == "Vaccines").ToString(), "Vaccines", "Clinical stock", Brush("GoldSoftBrush")),
            MetricCard("check", Data.Inventory.Count(x => x.Quantity > x.ReorderLevel).ToString(), "In stock", "Above threshold", new SolidColorBrush(Color.FromRgb(232, 242, 244)))));
        root.Children.Add(Panel(table, new Thickness(0)));
        return root;
    }

    private void OpenStaffDialog(StaffAccountRecord? existing = null)
    {
        var dialog = new RecordDialog(this, existing is null ? "Add branch staff" : "Update staff role",
            "The account is stored in the master database and can access only this organization.",
            [
                new("username", "Username", Value: existing?.Username),
                new("name", "Full name", Value: existing?.FullName),
                new("role", "Branch role", FieldKind.Choice, existing?.Role.ToString() ?? UserRole.Receptionist.ToString(), [UserRole.Receptionist.ToString(), UserRole.Manager.ToString(), UserRole.Veterinarian.ToString()]),
                new("password", existing is null ? "Temporary password" : "New password", Value: "", Required: existing is null, Help: existing is null ? "Required for a new account." : "Leave blank to keep the current password.", SpanColumns: true)
            ]);
        if (dialog.ShowModal() != true) return;
        try
        {
            _master.SaveBranchStaff(_session, existing, dialog.Text("username"), dialog.Text("name"), Enum.Parse<UserRole>(dialog.Text("role")), dialog.Text("password"));
            Navigate("staff");
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or DataConcurrencyException or SqlException)
        {
            ShowAdministrationError(exception);
        }
    }

    private void ToggleAccount(StaffAccountRecord account)
    {
        var action = account.IsActive ? "deactivate" : "reactivate";
        if (MessageBox.Show(this, $"{action} {account.Username}?", "Account access", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            _master.SetAccountActive(_session, account, !account.IsActive);
            Navigate(account.Role == UserRole.Admin ? "branch-admins" : "staff");
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or DataConcurrencyException or SqlException)
        { ShowAdministrationError(exception); }
    }

    private void OpenTenantDialog(TenantRecord? existing = null)
    {
        var plans = _master.GetSubscriptionPlans().Where(x => x.IsActive || x.Code == existing?.SubscriptionPlanCode).ToArray();
        var labels = plans.ToDictionary(x => $"{x.Name} ({x.Code})", x => x);
        if (labels.Count == 0) { MessageBox.Show(this, "Create an active subscription plan first.", "Organizations"); return; }
        var selectedPlan = existing is null ? labels.Keys.First() : labels.First(x => x.Value.Code == existing.SubscriptionPlanCode).Key;
        var dialog = new RecordDialog(this, existing is null ? "Add organization" : "Edit organization",
            "Each organization has a separate tenant database and one subscription. Deactivating prevents sign-in.",
            [
                new("code", "Organization code", Value: existing?.Code),
                new("name", "Organization name", Value: existing?.Name),
                new("plan", "Subscription plan", FieldKind.Choice, selectedPlan, labels.Keys.ToArray()),
                new("active", "Active", FieldKind.CheckBox, existing?.IsActive ?? true, Required: false)
            ]);
        if (dialog.ShowModal() != true) return;
        try
        {
            _master.SaveTenant(_session, existing, dialog.Text("code"), dialog.Text("name"), dialog.Bool("active"), labels[dialog.Text("plan")].Code);
            Navigate("organizations");
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or DataConcurrencyException or SqlException)
        { ShowAdministrationError(exception); }
    }

    private void OpenSubscriptionDialog(SubscriptionPlanRecord? existing = null)
    {
        var fields = new List<FieldDefinition>
        {
            new("code", "Plan code", existing is null ? FieldKind.Text : FieldKind.ReadOnly, existing?.Code),
            new("name", "Plan name", Value: existing?.Name),
            new("branches", "Maximum branches", FieldKind.Number, existing?.MaxBranches ?? 1),
            new("active", "Active plan", FieldKind.CheckBox, existing?.IsActive ?? true, Required: false)
        };
        foreach (var feature in Enum.GetValues<SubscriptionFeatures>().Where(x => x is not SubscriptionFeatures.None and not SubscriptionFeatures.All))
            fields.Add(new(feature.ToString(), feature.ToString(), FieldKind.CheckBox, existing?.Features.HasFlag(feature) ?? false, Required: false));
        var dialog = new RecordDialog(this, existing is null ? "Add subscription plan" : "Edit subscription plan",
            "Choose the modules available to companies on this plan. Existing records remain stored if a module is removed.", fields);
        if (dialog.ShowModal() != true) return;
        var features = SubscriptionFeatures.None;
        foreach (var feature in Enum.GetValues<SubscriptionFeatures>().Where(x => x is not SubscriptionFeatures.None and not SubscriptionFeatures.All))
            if (dialog.Bool(feature.ToString())) features |= feature;
        try
        {
            _master.SaveSubscriptionPlan(_session, existing, dialog.Text("code"), dialog.Text("name"), features, dialog.Number("branches"), dialog.Bool("active"));
            Navigate("subscriptions");
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or SqlException)
        { ShowAdministrationError(exception); }
    }

    private void OpenBranchAdminDialog(StaffAccountRecord? existing = null)
    {
        var tenants = _master.GetTenants().Where(x => x.IsActive || x.Id == existing?.TenantId).ToArray();
        if (tenants.Length == 0) { MessageBox.Show(this, "Activate an organization before assigning an administrator.", "Branch administrators"); return; }
        var labels = tenants.ToDictionary(x => $"{x.Code} · {x.Name}", x => x);
        var selected = existing is null ? labels.Keys.First() : labels.First(x => x.Value.Id == existing.TenantId).Key;
        var dialog = new RecordDialog(this, existing is null ? "Assign branch administrator" : "Update branch administrator",
            "Super Admin selects the organization; the account cannot administer other tenant databases.",
            [
                new("tenant", "Organization", FieldKind.Choice, selected, labels.Keys.ToArray()),
                new("username", "Username", Value: existing?.Username),
                new("name", "Full name", Value: existing?.FullName),
                new("password", existing is null ? "Temporary password" : "New password", Value: "", Required: existing is null, Help: existing is null ? "Required for a new administrator." : "Leave blank to keep the current password.", SpanColumns: true)
            ]);
        if (dialog.ShowModal() != true) return;
        try
        {
            _master.SaveBranchAdmin(_session, existing, labels[dialog.Text("tenant")].Id, dialog.Text("username"), dialog.Text("name"), dialog.Text("password"));
            Navigate("branch-admins");
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or DataConcurrencyException or SqlException)
        {
            ShowAdministrationError(exception);
        }
    }

    private void OpenConsultationDialog(ConsultationRecord? existing = null)
    {
        if (!RequireSubscribed("consultations")) return;
        var visits = Data.Appointments.Where(x => Same(x.Veterinarian, _session.Name) && x.Status != "Cancelled")
            .OrderByDescending(x => x.Date.Date == DateTime.Today).ThenBy(x => x.Date).ThenBy(x => x.Time).ToArray();
        if (visits.Length == 0) { ShowWorkflowNotice("No assigned appointments", "There are no active appointments assigned to this veterinarian. Ask the receptionist or administrator to assign a visit first."); return; }
        var labels = visits.ToDictionary(x => $"{x.Id} · {x.PetName} · {x.ClientName}", x => x);
        var selected = existing is null ? labels.Keys.First() : labels.FirstOrDefault(x => x.Value.Id == existing.AppointmentId).Key ?? labels.Keys.First();
        var initialVisit = labels[selected];
        var dialog = new RecordDialog(this, existing is null ? "Start consultation" : "Update consultation",
            "Clinical documentation is stored locally and queued for incremental sync after commit.",
            [
                new("visit", "Assigned visit", FieldKind.Choice, selected, labels.Keys.ToArray()),
                new("date", "Consultation date", FieldKind.Date, existing?.Date ?? DateTime.Today),
                new("complaint", "Chief complaint / visit reason", FieldKind.Multiline, existing?.ChiefComplaint ?? initialVisit.Notes, Required: false, SpanColumns: true),
                new("vitals", "Vitals and observations", FieldKind.Multiline, existing?.Vitals, Required: false, Help: "Weight, temperature, pulse, respiration, hydration, and behavior when clinically relevant.", SpanColumns: true),
                new("assessment", "Assessment", FieldKind.Multiline, existing?.Assessment, SpanColumns: true),
                new("plan", "Treatment plan", FieldKind.Multiline, existing?.TreatmentPlan, SpanColumns: true),
                new("prescriptions", "Medications and prescriptions", FieldKind.Multiline, existing?.Prescriptions, Required: false, SpanColumns: true),
                new("vaccinations", "Vaccinations", FieldKind.Multiline, existing?.Vaccinations, Required: false, SpanColumns: true),
                new("notes", "Medical notes", FieldKind.Multiline, existing?.MedicalNotes, Required: false, SpanColumns: true),
                new("followUp", "Follow-up plan", FieldKind.Multiline, existing?.FollowUpPlan, Required: false, Help: "Record the recommended recheck timing, monitoring, or diagnostic next step.", SpanColumns: true),
                new("instructions", "Client care instructions", FieldKind.Multiline, existing?.ClientInstructions, Required: false, Help: "Plain-language home care, warning signs, and when the owner should contact PawCare.", SpanColumns: true),
                new("status", "Status", FieldKind.Choice, existing?.Status ?? "In progress", ["Draft", "In progress", "Completed"])
            ], validate: ValidateConsultation);
        if (dialog.ShowModal() != true) return;
        var visit = labels[dialog.Text("visit")];
        var record = existing ?? new ConsultationRecord { Id = _store.NextId("CON", 100, Data.Consultations.Count) };
        record.AppointmentId = visit.Id; record.Date = dialog.Date("date") ?? DateTime.Today; record.ClientName = visit.ClientName; record.PetName = visit.PetName;
        record.Veterinarian = _session.Name; record.ChiefComplaint = dialog.Text("complaint"); record.Vitals = dialog.Text("vitals");
        record.Assessment = dialog.Text("assessment"); record.TreatmentPlan = dialog.Text("plan"); record.Prescriptions = dialog.Text("prescriptions");
        record.Vaccinations = dialog.Text("vaccinations"); record.MedicalNotes = dialog.Text("notes"); record.FollowUpPlan = dialog.Text("followUp");
        record.ClientInstructions = dialog.Text("instructions"); record.Status = dialog.Text("status");
        if (existing is null) Data.Consultations.Insert(0, record);
        if (record.Status == "Completed") visit.Status = "Completed";
        PersistAndRefresh("consultations", "medical", existing is null ? "Consultation started" : "Consultation updated", $"{record.PetName} · {record.Status}");
    }

    private static string? ValidateConsultation(IReadOnlyDictionary<string, object?> values)
    {
        if (values["status"]?.ToString() != "Completed") return null;
        if (string.IsNullOrWhiteSpace(values["assessment"]?.ToString())) return "Complete the assessment before signing this consultation as completed.";
        if (string.IsNullOrWhiteSpace(values["plan"]?.ToString())) return "Complete the treatment plan before signing this consultation as completed.";
        if (string.IsNullOrWhiteSpace(values["instructions"]?.ToString())) return "Add client care instructions before signing this consultation as completed.";
        return null;
    }

    private void OpenInventoryDialog(InventoryRecord? existing = null)
    {
        if (!RequireSubscribed("inventory")) return;
        var dialog = new RecordDialog(this, existing is null ? "Add inventory item" : "Update inventory item",
            "Stock changes are committed to the local branch database before any cloud sync.",
            [
                new("item", "Item name", Value: existing?.ItemName),
                new("category", "Category", FieldKind.Choice, existing?.Category ?? "Treatment", ["Vaccines", "Medication", "Treatment", "Supplies", "Other"]),
                new("quantity", "Quantity", FieldKind.Number, existing?.Quantity ?? 0),
                new("reorder", "Reorder level", FieldKind.Number, existing?.ReorderLevel ?? 5),
                new("unit", "Unit", Value: existing?.Unit ?? "units")
            ]);
        if (dialog.ShowModal() != true) return;
        if (dialog.Number("quantity") < 0 || dialog.Number("reorder") < 0) { MessageBox.Show(this, "Quantity and reorder level cannot be negative.", "Inventory", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var record = existing ?? new InventoryRecord { Id = _store.NextId("INV", 100, Data.Inventory.Count) };
        record.ItemName = dialog.Text("item"); record.Category = dialog.Text("category"); record.Quantity = dialog.Number("quantity");
        record.ReorderLevel = dialog.Number("reorder"); record.Unit = dialog.Text("unit"); record.UpdatedUtc = DateTime.UtcNow;
        if (existing is null) Data.Inventory.Insert(0, record);
        PersistAndRefresh("inventory", "inventory", existing is null ? "Inventory item added" : "Inventory updated", $"{record.ItemName} · {record.Quantity} {record.Unit}");
    }

    private void RunIncrementalSync()
    {
        if (!RequireSubscribed("incremental-sync")) return;
        try
        {
            var service = ResolveSyncService();
            if (service is null) return;
            var result = service.SyncIncremental();
            MessageBox.Show(this, $"Incremental sync completed. {result.EventCount} idempotent event(s) were written to:\n{result.Destination}", "Sync complete", MessageBoxButton.OK, MessageBoxImage.Information);
            Navigate("sync");
        }
        catch (Exception exception) when (exception is InvalidOperationException or DataConcurrencyException or SqlException or IOException)
        {
            ShowAdministrationError(exception);
        }
    }

    private async Task RunCloudBackupAsync(Button button)
    {
        if (!RequireSubscribed("sync")) return;
        var originalContent = button.Content;
        try
        {
            var service = ResolveSyncService();
            if (service is null) return;
            button.IsEnabled = false;
            button.Content = "Backing up...";
            var result = await Task.Run(service.CreateCloudBackup);
            MessageBox.Show(this,
                $"Cloud backup completed successfully.\n\nLocal recovery copy:\n{result.LocalDestination}\n\nCloud mirror copy:\n{result.CloudDestination}",
                "Cloud backup complete", MessageBoxButton.OK, MessageBoxImage.Information);
            Navigate("sync");
        }
        catch (Exception exception) when (exception is InvalidOperationException or DataConcurrencyException or SqlException or IOException)
        {
            ShowAdministrationError(exception);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }

    private CloudSyncService? ResolveSyncService()
    {
        if (_session.Role != UserRole.SuperAdmin) return _sync;
        if ((_syncTenantId is null || _syncBranchId is null || _master.GetTenants().All(x => x.Id != _syncTenantId || !x.IsActive) ||
            _master.GetBranches(_syncTenantId).All(x => x.Id != _syncBranchId || !x.IsActive)) && !ChooseSyncTenant()) return null;
        var tenants = _master.GetTenants();
        var tenant = tenants.First(x => x.Id == _syncTenantId);
        var branch = _master.GetBranches(tenant.Id).First(x => x.Id == _syncBranchId);
        return CreateSyncService(tenant, branch);
    }

    private CloudSyncService CreateSyncService(TenantRecord tenant, BranchRecord branch)
    {
        var store = new ClinicDataStore(branch.DatabasePath, tenant.Id, _session.UserId, _session.Name, App.DatabasePaths, branch.Id);
        store.Load();
        return new CloudSyncService(App.DatabasePaths, store);
    }

    private bool ChooseSyncTenant()
    {
        var labels = _master.GetTenants().Where(x => x.IsActive).ToDictionary(x => $"{x.Code} · {x.Name}", x => x);
        if (labels.Count == 0) return false;
        var selected = labels.FirstOrDefault(x => x.Value.Id == _syncTenantId).Key ?? labels.Keys.First();
        var dialog = new RecordDialog(this, "Choose organization", "Select the branch for status, sync, and backup.",
            [new("tenant", "Organization", FieldKind.Choice, selected, labels.Keys.ToArray())], "Select");
        if (dialog.ShowModal() != true) return false;
        _syncTenantId = labels[dialog.Text("tenant")].Id;
        var branches = _master.GetBranches(_syncTenantId).Where(x => x.IsActive).ToArray();
        if (branches.Length == 0) return false;
        if (branches.Length == 1) _syncBranchId = branches[0].Id;
        else
        {
            var choices = branches.ToDictionary(x => $"{x.Code} · {x.Name}", x => x);
            var branchDialog = new RecordDialog(this, "Choose branch", "Select the branch database to sync or back up.",
                [new("branch", "Branch", FieldKind.Choice, choices.Keys.First(), choices.Keys.ToArray())], "Select");
            if (branchDialog.ShowModal() != true) return false;
            _syncBranchId = choices[branchDialog.Text("branch")].Id;
        }
        Navigate("sync");
        return true;
    }

    private static Grid TwoColumnGrid(double left, double right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(left, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(right, GridUnitType.Star) });
        return grid;
    }

    private void ShowAdministrationError(Exception exception)
    {
        var message = exception is SqlException { Number: 2601 or 2627 }
            ? "That username already exists. Choose another username or edit the existing account."
            : exception.Message;
        MessageBox.Show(this, message, "PawCare administration", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
