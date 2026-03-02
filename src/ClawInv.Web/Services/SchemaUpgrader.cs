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

        EnsureColumn(conn,
            table: "Portfolios",
            column: "LastRebalanceDate",
            columnSql: "TEXT NULL");

        EnsureTable(conn, createSql: """
CREATE TABLE IF NOT EXISTS "RecommendationRuns" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RecommendationRuns" PRIMARY KEY AUTOINCREMENT,
    "StrategyConfigId" INTEGER NOT NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "AsOfDate" TEXT NOT NULL,
    "Notes" TEXT NOT NULL,
    CONSTRAINT "FK_RecommendationRuns_StrategyConfigs_StrategyConfigId" FOREIGN KEY ("StrategyConfigId") REFERENCES "StrategyConfigs" ("Id") ON DELETE CASCADE
);
""");

        EnsureTable(conn, createSql: """
CREATE TABLE IF NOT EXISTS "TradeRecommendations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TradeRecommendations" PRIMARY KEY AUTOINCREMENT,
    "RecommendationRunId" INTEGER NOT NULL,
    "Action" INTEGER NOT NULL,
    "FundId" TEXT NOT NULL,
    "FundName" TEXT NOT NULL,
    "Reason" TEXT NOT NULL,
    CONSTRAINT "FK_TradeRecommendations_RecommendationRuns_RecommendationRunId" FOREIGN KEY ("RecommendationRunId") REFERENCES "RecommendationRuns" ("Id") ON DELETE CASCADE
);
""");
    }

    private static void EnsureTable(SqliteConnection conn, string createSql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = createSql;
        cmd.ExecuteNonQuery();
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
