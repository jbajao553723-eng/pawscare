using System.Windows;
using Microsoft.Data.SqlClient;
using PawCare.Desktop.Models;
using PawCare.Desktop.Services;
using PawCare.Desktop.Views;

namespace PawCare.Desktop;

public partial class App : Application
{
    public static IReadOnlyList<ServerProfile> Servers { get; } = PawCare.Desktop.Services.DatabasePaths.ConfiguredServers();
    public static ServerProfile SelectedServer { get; private set; } = Servers[0];
    public static DatabasePaths DatabasePaths { get; private set; } = PathsFor(SelectedServer);
    public static MasterDatabase MasterDatabase { get; private set; } = new(DatabasePaths);
    public static ClinicDataStore DataStore { get; private set; } = null!;
    public static CloudSyncService SyncService { get; private set; } = null!;
    private static bool _selectedServerInitialized;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!e.Args.Contains("--initialize-database", StringComparer.OrdinalIgnoreCase))
        {
            new LoginWindow().Show();
            return;
        }
        try
        {
            InitializeSelectedServer();
            Shutdown(0);
        }
        catch (Exception exception) when (exception is SqlException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(
                $"PawCare could not initialize SQL Server at {DatabasePaths.ServerName}.\n\n{exception.Message}\n\nRun scripts\\configure-sqlserver.ps1, then try again.",
                "Database connection failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static DatabasePaths PathsFor(ServerProfile server) =>
        new(serverName: server.Instance, databasePrefix: server.DatabasePrefix,
            mirrorDirectory: server.MirrorDirectory, tenantId: server.TenantId);

    public static MasterDatabase OpenMaster(ServerProfile server)
    {
        var master = new MasterDatabase(PathsFor(server));
        master.Initialize();
        return master;
    }

    public static UserSession? AuthenticateRouted(string username, string password)
    {
        var directoryPaths = PathsFor(Servers[0]);
        var route = new LoginRouteDirectory(directoryPaths).Resolve(username);
        if (route is null) return null;
        var server = Servers.SingleOrDefault(x =>
            string.Equals(x.Instance, route.ServerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("This account points to a server that is not configured on this PawCare installation.");
        SelectServer(server);
        var session = MasterDatabase.Authenticate(username, password);
        if (session is null) return null;
        if (session.Role != UserRole.SuperAdmin &&
            !string.Equals(session.TenantId, route.TenantId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The account tenant does not match its central login route.");
        return session;
    }

    public static void SelectServer(ServerProfile server)
    {
        if (ReferenceEquals(server, SelectedServer) && _selectedServerInitialized) return;
        var paths = PathsFor(server);
        var master = new MasterDatabase(paths);
        master.Initialize();
        DatabasePaths = paths;
        MasterDatabase = master;
        SelectedServer = server;
        DataStore = null!;
        SyncService = null!;
        _selectedServerInitialized = true;
    }

    private static void InitializeSelectedServer()
    {
        MasterDatabase.Initialize();
        foreach (var tenant in MasterDatabase.GetTenants())
            foreach (var branch in MasterDatabase.GetBranches(tenant.Id).Where(x => x.IsActive))
                new ClinicDataStore(branch.DatabasePath, tenant.Id, paths: DatabasePaths, branchId: branch.Id).Load();
        _selectedServerInitialized = true;
    }

    public static UserSession ActivateSession(UserSession session, BranchRecord? chosenBranch = null)
    {
        var tenant = string.IsNullOrWhiteSpace(session.TenantId)
            ? MasterDatabase.GetTenants().First()
            : MasterDatabase.GetTenant(session.TenantId);
        var branch = chosenBranch ?? MasterDatabase.GetBranches(tenant.Id).FirstOrDefault(x => x.IsActive)
            ?? throw new InvalidOperationException("This organization has no active branch.");
        if (branch.TenantId != tenant.Id || !branch.IsActive)
            throw new UnauthorizedAccessException("The selected branch is not active for this organization.");
        var resolved = string.IsNullOrWhiteSpace(session.TenantId)
            ? session with { TenantId = tenant.Id, TenantName = tenant.Name }
            : session;
        resolved = resolved with { BranchId = branch.Id, BranchName = branch.Name };
        DataStore = new ClinicDataStore(branch.DatabasePath, tenant.Id, resolved.UserId, resolved.Name, DatabasePaths, branch.Id);
        DataStore.Load();
        DataStore.SetActor(resolved);
        SyncService = new CloudSyncService(DatabasePaths, DataStore);
        return resolved;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { DataStore?.Save(); }
        catch (Exception exception) when (exception is DataConcurrencyException or UnauthorizedAccessException) { }
        base.OnExit(e);
    }
}
