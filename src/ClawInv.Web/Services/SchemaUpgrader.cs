using Microsoft.Data.Sqlite;

namespace ClawInv.Web.Services;

/// <summary>
/// Minimal schema upgrader for SQLite when using EnsureCreated() (no migrations).
/// Keeps existing local DBs working after small model changes.
/// </summary>
public static class SchemaUpgrader
{
    public static void Upgrade(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        EnsureColumn(conn,
            table: "StrategyConfigs",
            column: "DefaultSource",
            columnSql: "TEXT NOT NULL DEFAULT ''");
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string columnSql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {columnSql};";
        alter.ExecuteNonQuery();
    }
}
