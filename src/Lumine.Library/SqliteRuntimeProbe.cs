using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Lumine.Library;

public readonly record struct SqliteProbeResult(
    string Version,
    long Scalar,
    bool Fts5TrigramContentlessDelete);

public static class SqliteRuntimeProbe
{
    public static SqliteProbeResult Probe()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT sqlite_version();";
        var version = Convert.ToString(
            versionCommand.ExecuteScalar(),
            CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException(
                "SQLite did not return a version.");

        using var scalarCommand = connection.CreateCommand();
        scalarCommand.CommandText = "SELECT 1;";
        var scalar = Convert.ToInt64(
            scalarCommand.ExecuteScalar(),
            CultureInfo.InvariantCulture);

        var searchRuntime = ProbeSearchRuntime(connection);

        return new SqliteProbeResult(
            version,
            scalar,
            searchRuntime);
    }

    private static bool ProbeSearchRuntime(
        SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE VIRTUAL TABLE search_probe USING fts5(
                    value,
                    content='',
                    contentless_delete=1,
                    tokenize='trigram',
                    detail='none',
                    columnsize=0
                );

                INSERT INTO search_probe(rowid, value)
                VALUES(1, 'abcdef');

                DELETE FROM search_probe
                WHERE rowid = 1;
                """;
            command.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }
}
