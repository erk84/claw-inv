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
            table: "StrategyConfigs",
            column: "Regime",
            columnSql: "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(conn,
            table: "StrategyConfigs",
            column: "RegimeMaMonths",
            columnSql: "INTEGER NOT NULL DEFAULT 10");

        EnsureColumn(conn,
            table: "StrategyConfigs",
            column: "RegimeThreshold",
            columnSql: "REAL NOT NULL DEFAULT 0.0");

        EnsureColumn(conn,
            table: "StrategyConfigs",
            column: "RiskOffMode",
            columnSql: "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(conn,
            table: "StrategyConfigs",
            column: "DefensiveVolLookbackMonths",
            columnSql: "INTEGER NOT NULL DEFAULT 12");

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

        EnsureTable(conn, createSql: """
CREATE TABLE IF NOT EXISTS "TradeEvents" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TradeEvents" PRIMARY KEY AUTOINCREMENT,
    "PortfolioId" INTEGER NOT NULL,
    "Date" TEXT NOT NULL,
    "FundId" TEXT NOT NULL,
    "FundName" TEXT NOT NULL,
    "Side" INTEGER NOT NULL,
    "Nav" TEXT NOT NULL,
    CONSTRAINT "FK_TradeEvents_Portfolios_PortfolioId" FOREIGN KEY ("PortfolioId") REFERENCES "Portfolios" ("Id") ON DELETE CASCADE
);
""");

        EnsureTable(conn, createSql: """
CREATE TABLE IF NOT EXISTS "PortfolioDailySnapshots" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PortfolioDailySnapshots" PRIMARY KEY AUTOINCREMENT,
    "PortfolioId" INTEGER NOT NULL,
    "Date" TEXT NOT NULL,
    "EquityIndex" REAL NOT NULL,
    CONSTRAINT "FK_PortfolioDailySnapshots_Portfolios_PortfolioId" FOREIGN KEY ("PortfolioId") REFERENCES "Portfolios" ("Id") ON DELETE CASCADE
);
""");

        // Indices (IF NOT EXISTS supported by SQLite)
        EnsureTable(conn, createSql: """
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PortfolioDailySnapshots_PortfolioId_Date" ON "PortfolioDailySnapshots" ("PortfolioId", "Date");
""");

        EnsureTable(conn, createSql: """
CREATE INDEX IF NOT EXISTS "IX_TradeEvents_PortfolioId_Date" ON "TradeEvents" ("PortfolioId", "Date");
""");

        EnsureTable(conn, createSql: """
CREATE TABLE IF NOT EXISTS "BackgroundTasks" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_BackgroundTasks" PRIMARY KEY AUTOINCREMENT,
    "Type" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "StrategyConfigId" INTEGER NULL,
    "CreatedAtUtc" TEXT NOT NULL,
    "StartedAtUtc" TEXT NULL,
    "FinishedAtUtc" TEXT NULL,
    "Message" TEXT NOT NULL,
    "Error" TEXT NULL
);
""");

        EnsureTable(conn, createSql: """
CREATE INDEX IF NOT EXISTS "IX_BackgroundTasks_Type_Status_CreatedAtUtc" ON "BackgroundTasks" ("Type", "Status", "CreatedAtUtc");
""");

        EnsureTable(conn, createSql: """
CREATE TABLE IF NOT EXISTS "JobStates" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_JobStates" PRIMARY KEY AUTOINCREMENT,
    "Key" TEXT NOT NULL,
    "LastRunAtUtc" TEXT NULL,
    "LastError" TEXT NULL
);
""");

        EnsureTable(conn, createSql: """
CREATE UNIQUE INDEX IF NOT EXISTS "IX_JobStates_Key" ON "JobStates" ("Key");
""");

        EnsureColumn(conn,
            table: "UniverseSettings",
            column: "UniverseFundCount",
            columnSql: "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(conn,
            table: "UniverseSettings",
            column: "LastRegeneratedAtUtc",
            columnSql: "TEXT NULL");

        EnsureColumn(conn,
            table: "UniverseSettings",
            column: "RatingLimit",
            columnSql: "INTEGER NOT NULL DEFAULT 3");

        EnsureColumn(conn,
            table: "UniverseSettings",
            column: "RiskLimit",
            columnSql: "INTEGER NOT NULL DEFAULT 0");

        EnsureColumn(conn,
            table: "UniverseSettings",
            column: "TotalFeeLimit",
            columnSql: "REAL NOT NULL DEFAULT 2.0");
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
