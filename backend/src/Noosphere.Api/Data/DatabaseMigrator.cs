using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Noosphere.Api.Data;

/// <summary>
/// Applies pending EF Core migrations. Before migrating a database that already holds data, and when a backups directory
/// is set (BACKUPS_DIR), every table is copied to a JSON file (<c>before-migration-YYYYMMDD-HHMMSS.json</c>: one array of
/// rows per table), whatever the schema version.
/// </summary>
public static class DatabaseMigrator
{
    /// <summary>Returns the number of migrations applied.</summary>
    public static async Task<int> Migrate(NoosphereDbContext db, string? backupsDirectory, ILogger logger, CancellationToken cancel = default)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancel)).ToList();
        if (pending.Count == 0) return 0;
        if ((await db.Database.GetAppliedMigrationsAsync(cancel)).Any())
        {
            if (backupsDirectory is null)
                logger.LogWarning("Migrating the database ({Count} migration(s)) without a backup: BACKUPS_DIR is not set.", pending.Count);
            else
            {
                var file = await Backup(db, backupsDirectory, cancel);
                logger.LogInformation("Database backed up to {File} before {Count} migration(s).", file, pending.Count);
            }
        }
        await db.Database.MigrateAsync(cancel);
        logger.LogInformation("Database migrated: {Migrations}.", string.Join(", ", pending));
        return pending.Count;
    }

    /// <summary>Writes every table of the public schema to one JSON file and returns its path.</summary>
    public static async Task<string> Backup(NoosphereDbContext db, string directory, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, $"before-migration-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json");
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(cancel);
        try
        {
            var tables = new List<string>();
            await using (var list = new NpgsqlCommand("select table_name from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1", connection))
            await using (var reader = await list.ExecuteReaderAsync(cancel))
                while (await reader.ReadAsync(cancel)) tables.Add(reader.GetString(0));

            var json = new StringBuilder("{\n");
            for (var i = 0; i < tables.Count; i++)
            {
                var quoted = "\"" + tables[i].Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
#pragma warning disable CA2100 // Table names come from information_schema and are quoted.
                await using var dump = new NpgsqlCommand($"select coalesce(json_agg(t), '[]'::json)::text from {quoted} t", connection);
#pragma warning restore CA2100
                var rows = (string?)await dump.ExecuteScalarAsync(cancel) ?? "[]";
                json.Append(System.Text.Json.JsonSerializer.Serialize(tables[i])).Append(": ").Append(rows).Append(i < tables.Count - 1 ? ",\n" : "\n");
            }
            json.Append("}\n");
            await File.WriteAllTextAsync(file, json.ToString(), cancel);
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
        return file;
    }
}
