using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace PawCare.Desktop.Services;

public sealed class ServerProfile
{
    public string Name { get; set; } = string.Empty;
    public string Instance { get; set; } = string.Empty;
    public string DatabasePrefix { get; set; } = "PawCare";
    public string? TenantId { get; set; }
    public string? MirrorDirectory { get; set; }
    public override string ToString() => Name;
}

public sealed class DatabasePaths
{
    private sealed class DatabaseSettings
    {
        public string Server { get; set; } = @".\SQLEXPRESS";
        public string DatabasePrefix { get; set; } = "PawCare";
        public bool TrustServerCertificate { get; set; } = true;
        public string DirectoryDatabase { get; set; } = "MasterCRMJ";
        public List<ServerProfile> Servers { get; set; } = [];
    }

    public string Root { get; }
    public string LocalBackupDirectory { get; }
    public string CloudMirrorDirectory { get; }
    public string ServerName { get; }
    public string DatabasePrefix { get; }
    public string? AssignedTenantId { get; }
    public bool TrustServerCertificate { get; }
    public string MasterDatabaseName => $"{DatabasePrefix}Master";
    public string MasterDatabasePath => MasterDatabaseName;

    public DatabasePaths(string? root = null, string? serverName = null, string? databasePrefix = null,
        string? mirrorDirectory = null, string? tenantId = null)
    {
        var settings = LoadSettings();
        ServerName = serverName ?? Environment.GetEnvironmentVariable("PAWCARE_SQL_SERVER") ?? settings.Server;
        DatabasePrefix = databasePrefix ?? Environment.GetEnvironmentVariable("PAWCARE_DATABASE_PREFIX") ?? settings.DatabasePrefix;
        AssignedTenantId = tenantId ?? Environment.GetEnvironmentVariable("PAWCARE_TENANT_ID") ??
            settings.Servers.SingleOrDefault(x =>
                string.Equals(x.Instance, ServerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.DatabasePrefix, DatabasePrefix, StringComparison.OrdinalIgnoreCase))?.TenantId;
        if (!string.IsNullOrWhiteSpace(AssignedTenantId) && AssignedTenantId is not ("tenant-a" or "tenant-b" or "tenant-c"))
            throw new InvalidOperationException("A server tenant assignment must be tenant-a, tenant-b, or tenant-c.");
        Root = root ?? LocalRoot(ServerName, DatabasePrefix, settings);
        TrustServerCertificate = settings.TrustServerCertificate;
        CloudMirrorDirectory = !string.IsNullOrWhiteSpace(mirrorDirectory) ? Environment.ExpandEnvironmentVariables(mirrorDirectory)
            : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PAWCARE_MIRROR_DIRECTORY"))
                ? Environment.ExpandEnvironmentVariables(Environment.GetEnvironmentVariable("PAWCARE_MIRROR_DIRECTORY")!)
                : Path.Combine(Root, "cloud-sync-simulator");
        LocalBackupDirectory = Path.Combine(Root, "local-backups");
        Directory.CreateDirectory(Root);
    }

    private static string LocalRoot(string serverName, string databasePrefix, DatabaseSettings settings)
    {
        var baseRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawCare", "data");
        if (string.Equals(serverName, settings.Server, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(databasePrefix, settings.DatabasePrefix, StringComparison.OrdinalIgnoreCase))
            return baseRoot;

        var identity = $"{serverName}|{databasePrefix}";
        var label = new string(serverName.Where(char.IsAsciiLetterOrDigit).Take(32).ToArray());
        if (label.Length == 0) label = "sql";
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
        return Path.Combine(baseRoot, "servers", $"{label}-{fingerprint}");
    }

    public static IReadOnlyList<ServerProfile> ConfiguredServers()
    {
        var settings = LoadSettings();
        var overrideServer = Environment.GetEnvironmentVariable("PAWCARE_SQL_SERVER");
        var overridePrefix = Environment.GetEnvironmentVariable("PAWCARE_DATABASE_PREFIX");
        if (!string.IsNullOrWhiteSpace(overrideServer) || !string.IsNullOrWhiteSpace(overridePrefix))
            return [new ServerProfile { Name = "Environment server", Instance = overrideServer ?? settings.Server, DatabasePrefix = overridePrefix ?? settings.DatabasePrefix,
                TenantId = Environment.GetEnvironmentVariable("PAWCARE_TENANT_ID"),
                MirrorDirectory = Environment.GetEnvironmentVariable("PAWCARE_MIRROR_DIRECTORY") }];
        if (settings.Servers.Count == 0)
            return [new ServerProfile { Name = "PawCare Server 1", Instance = settings.Server, DatabasePrefix = settings.DatabasePrefix }];
        if (settings.Servers.Any(x => string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Instance) ||
            string.IsNullOrWhiteSpace(x.DatabasePrefix) || !x.DatabasePrefix.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_') ||
            (!string.IsNullOrWhiteSpace(x.TenantId) && x.TenantId is not ("tenant-a" or "tenant-b" or "tenant-c"))))
            throw new InvalidOperationException("Each server profile needs a name, SQL instance, and safe database prefix.");
        if (settings.Servers.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1) ||
            settings.Servers.GroupBy(x => (x.Instance.ToUpperInvariant(), x.DatabasePrefix.ToUpperInvariant())).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Server profiles must have distinct names and database locations.");
        return settings.Servers;
    }

    public static string ConfiguredDirectoryDatabase()
    {
        var name = LoadSettings().DirectoryDatabase;
        if (string.IsNullOrWhiteSpace(name) || !name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            throw new InvalidOperationException("The directory database name is invalid.");
        return name;
    }

    public string TenantDatabaseName(string tenantId) => tenantId switch
    {
        "tenant-a" => $"{DatabasePrefix}TenantA",
        "tenant-b" => $"{DatabasePrefix}TenantB",
        "tenant-c" => $"{DatabasePrefix}TenantC",
        _ => throw new ArgumentOutOfRangeException(nameof(tenantId), "Unknown PawCare tenant identifier.")
    };

    public string TenantDatabasePath(string tenantId) => TenantDatabaseName(tenantId);
    public bool ManagesTenant(string tenantId) => string.IsNullOrWhiteSpace(AssignedTenantId) ||
        string.Equals(AssignedTenantId, tenantId, StringComparison.OrdinalIgnoreCase);
    public string ServerConnectionString => BuildConnectionString("master");
    public string MasterConnectionString => BuildConnectionString(MasterDatabaseName);
    public string TenantConnectionString(string tenantId) => BuildConnectionString(TenantDatabaseName(tenantId));
    public string DatabaseConnectionString(string databaseName) => BuildConnectionString(databaseName);

    private string BuildConnectionString(string databaseName) => new SqlConnectionStringBuilder
    {
        DataSource = ServerName,
        InitialCatalog = databaseName,
        IntegratedSecurity = true,
        Encrypt = true,
        TrustServerCertificate = TrustServerCertificate,
        ConnectTimeout = 60,
        ApplicationName = "PawCare Desktop",
        MultipleActiveResultSets = false,
        Pooling = true
    }.ConnectionString;

    private static DatabaseSettings LoadSettings()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path)) return new DatabaseSettings();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("Database", out var database)) return new DatabaseSettings();
            return JsonSerializer.Deserialize<DatabaseSettings>(database.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new DatabaseSettings();
        }
        catch
        {
            return new DatabaseSettings();
        }
    }
}
