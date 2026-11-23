using Microsoft.Data.SqlClient;

namespace PawCare.Desktop.Services;

public sealed record LoginRouteRecord(string Username, string ServerName, string TenantId);

public sealed class LoginRouteDirectory(DatabasePaths paths)
{
    private readonly string _connectionString =
        paths.DatabaseConnectionString(DatabasePaths.ConfiguredDirectoryDatabase());

    public LoginRouteRecord? Resolve(string username)
    {
        var normalized = username.Trim().ToUpperInvariant();
        if (normalized.Length == 0) return null;
        using var connection = new SqlConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (2) route.Username, databaseRegistration.ServerName, route.TenantId
            FROM dbo.LoginRoutes route
            JOIN dbo.Companies company ON company.CompanyId=route.CompanyId
            JOIN dbo.CompanyDatabases databaseRegistration ON databaseRegistration.CompanyId=company.CompanyId
            WHERE route.NormalizedUsername=@username AND route.IsActive=1
              AND company.IsActive=1 AND databaseRegistration.IsActive=1
            ORDER BY databaseRegistration.CompanyDatabaseId;
            """;
        command.Parameters.AddWithValue("@username", normalized);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        var result = new LoginRouteRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2));
        if (reader.Read())
            throw new InvalidOperationException("This account has multiple active database routes. Ask the Super Admin to correct its company registration.");
        return result;
    }
}
