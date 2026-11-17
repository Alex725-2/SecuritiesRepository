using Npgsql;

namespace Securities.Infrastructure;

/// <summary>Safely applies a password containing connection-string delimiters.</summary>
public static class DatabaseConnection
{
    /// <summary>Builds a connection string without interpolating secret values into its syntax.</summary>
    public static string? Resolve(string? connection, string? password)
    {
        if (connection is null) return null;
        var builder = new NpgsqlConnectionStringBuilder(connection);
        if (password is not null) builder.Password = password;
        return builder.ConnectionString;
    }
}
