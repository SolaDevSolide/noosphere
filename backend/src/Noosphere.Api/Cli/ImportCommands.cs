using System.Text.Json;
using Noosphere.Api.Contracts;
using Noosphere.Api.Data;
using Noosphere.Api.Importing;
using Noosphere.Api.Services;

namespace Noosphere.Api.Cli;

/// <summary>
/// <c>noosphere imports ...</c>: imports from Backstage, OpenAPI, AsyncAPI, ADR, Mermaid and Excel files, with the same
/// dry run and plan as the API (the plans of the command line belong to "system").
/// </summary>
public static partial class CommandLine
{
    private static async Task<int> Imports(Options o, IServiceProvider sp, Output output)
    {
        var p = o.Positionals;
        var db = sp.GetRequiredService<NoosphereDbContext>();
        var now = sp.GetRequiredService<TimeProvider>().GetUtcNow();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Noosphere.Imports");
        try
        {
            switch (p.Count >= 2 ? p[1] : "")
            {
                case "plan":
                {
                    if (p.Count < 3) throw new UsageException("Missing file: 'imports plan <file>...'.");
                    var format = o["--format"]?.ToLowerInvariant();
                    if (format is not null && !ImportFormats.All.Contains(format)) throw new UsageException($"--format expects one of: {string.Join(", ", ImportFormats.All)}.");
                    var files = new List<ImportFile>();
                    foreach (var path in p.Skip(2))
                    {
                        if (!File.Exists(path)) throw new CommandException(ExitCodes.NotFound, $"File not found: {path}");
                        if (new FileInfo(path).Length > ImportLimits.MaxUploadBytes) throw new CommandException(ExitCodes.Failure, $"{path} is larger than {ImportLimits.MaxUploadBytes / (1024 * 1024)} MB.");
                        files.Add(new ImportFile(Path.GetFileName(path), await File.ReadAllBytesAsync(path)));
                    }
                    var options = new ImportOptions(format, o["--domain"], o["--team"], o["--application"], o["--parent-page"]);
                    var plan = await ImportService.Plan(db, await Access.System(db), files, options, now, logger);
                    await PrintPlan(output, plan);
                    return plan.Counts.Errors > 0 ? ExitCodes.Failure : ExitCodes.Success;
                }
                case "apply":
                {
                    Expect(p, 3);
                    if (!Guid.TryParse(p[2], out var id)) throw new UsageException("Expected the id of an import plan.");
                    var skip = new List<int>();
                    foreach (var part in (o["--skip"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        skip.Add(int.TryParse(part, out var index) ? index : throw new UsageException("--skip expects entry indexes separated by commas: --skip 3,7."));
                    var plan = await ImportService.Apply(db, await Access.System(db), id, now, logger, skip);
                    int Written(string action) => plan.Entries.Count(e => e.Action == action && !plan.Skipped.Contains(e.Index));
                    var left = plan.Skipped.Count > 0 ? $", {plan.Skipped.Count} left out" : "";
                    await Done(output, $"Import applied: {Written("create")} created, {Written("update")} updated, {plan.Counts.Unchanged} unchanged{left}.", plan);
                    return ExitCodes.Success;
                }
                case "template":
                {
                    Expect(p, 2);
                    var file = o["--output"] ?? Endpoints.ImportEndpoints.TemplateFile;
                    await File.WriteAllBytesAsync(file, ExcelReader.Template());
                    await Done(output, $"Template written to {file}.", new { file });
                    return ExitCodes.Success;
                }
                default:
                    throw new UsageException("Expected 'imports plan <file>...', 'imports apply <plan-id> [--skip 3,7]' or 'imports template'.");
            }
        }
        catch (ApiProblem e)
        {
            throw new CommandException(e.Status == 404 ? ExitCodes.NotFound : ExitCodes.Failure, e.Message);
        }
    }

    private static string Where(ImportSourceDto s) => s.File + (s.Line is { } line ? $":{line}" : "") + (s.Place is { } place ? $" ({place})" : "");

    private static async Task PrintPlan(Output output, ImportPlanDto plan)
    {
        if (output.Json)
        {
            await output.Out.WriteLineAsync(JsonSerializer.Serialize(new { code = plan.Counts.Errors > 0 ? ExitCodes.Failure : ExitCodes.Success, result = plan }, JsonOutput));
            return;
        }
        var c = plan.Counts;
        await output.Out.WriteLineAsync($"Dry run, nothing written: {c.Create} to create, {c.Update} to update, {c.Unchanged} unchanged, {c.Warnings} warning(s), {c.Errors} error(s).");
        foreach (var e in plan.Entries.Where(e => e.Action != "unchanged"))
        {
            var fields = e.Action == "update" ? ": " + string.Join(", ", e.Changes.Select(d => d.Field)) : "";
            var requires = e.Requires.Count > 0 ? $"  (requires {string.Join(", ", e.Requires.Select(r => "#" + r))})" : "";
            await output.Out.WriteLineAsync($"  #{e.Index,-3} {e.Action,-7} {Noosphere.Domain.Rules.KindName(e.Kind),-11} {e.Id} \"{e.Name}\"{fields}  [{Where(e.Source)}]{requires}");
            foreach (var m in e.PossibleMatches) await output.Out.WriteLineAsync($"               possible match: {m.Id} \"{m.Name}\" (not merged)");
        }
        foreach (var w in plan.Warnings) await output.Out.WriteLineAsync($"Warning: {Where(w.Source)}: {w.Message}");
        foreach (var e in plan.Errors) await output.Error.WriteLineAsync($"Error: {Where(e.Source)}: {e.Message}");
        await output.Out.WriteLineAsync(plan.CanApply
            ? $"Apply this plan within {(int)ImportLimits.PlanLifetime.TotalMinutes} minutes: noosphere imports apply {plan.Id}"
            : "Fix the errors and run the dry run again.");
    }
}
