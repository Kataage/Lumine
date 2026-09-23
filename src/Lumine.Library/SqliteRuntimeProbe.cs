using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public readonly record struct SqliteProbeResult(string Version, long Scalar);

public static class SqliteRuntimeProbe
{
    public static SqliteProbeResult Probe()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT sqlite_version();";
        var version = Convert.ToString(versionCommand.ExecuteScalar(), CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("SQLite did not return a version.");

        using var scalarCommand = connection.CreateCommand();
        scalarCommand.CommandText = "SELECT 1;";
        var scalar = Convert.ToInt64(scalarCommand.ExecuteScalar(), CultureInfo.InvariantCulture);

        return new SqliteProbeResult(version, scalar);
    }
}
