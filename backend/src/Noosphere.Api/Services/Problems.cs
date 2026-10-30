using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Noosphere.Api.Services;

/// <summary>
/// An error returned to the client as <c>application/problem+json</c> (RFC 9457). Status codes: 400 invalid input,
/// 401 not signed in, 403 not allowed, 404 not found, 409 conflict (stale version, record in use), 422 rule violated.
/// </summary>
public sealed class ApiProblem(int status, string detail, IReadOnlyDictionary<string, string[]>? errors = null, bool outdated = false) : Exception(detail)
{
    public int Status { get; } = status;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
    /// <summary>A proposal was written against an older version of a record (409 on accept).</summary>
    public bool Outdated { get; } = outdated;

    public static ApiProblem BadRequest(string detail) => new(400, detail);
    public static ApiProblem Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(400, "Some fields are invalid.", errors);
    public static ApiProblem Forbidden(string detail) => new(403, detail);
    public static ApiProblem NotFound(string detail) => new(404, detail);
    public static ApiProblem Conflict(string detail) => new(409, detail);
    public static ApiProblem Stale() => new(409, "Someone changed this record. Reload it and try again.");
    public static ApiProblem Unprocessable(string detail) => new(422, detail);
}

/// <summary>Body of every error response.</summary>
/// <param name="Errors">Field errors (field name in camelCase, then messages), on 400 only.</param>
/// <param name="Outdated">true on the 409 answered when accepting a proposal whose records changed since it was written.</param>
public sealed record ProblemDto(string Type, string Title, int Status, string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string[]>? Errors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Outdated = null);

public static class Problems
{
    public const string ContentType = "application/problem+json";

    public static string Title(int status) => status switch
    {
        400 => "Bad request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not found",
        409 => "Conflict",
        413 => "Payload too large",
        422 => "Unprocessable content",
        429 => "Too many requests",
        503 => "Service unavailable",
        >= 500 => "Internal server error",
        _ => "Error",
    };

    public static ProblemDto Body(int status, string detail, IReadOnlyDictionary<string, string[]>? errors = null, bool outdated = false) =>
        new("about:blank", Title(status), status, detail, errors, outdated ? true : null);

    public static IResult Result(int status, string detail) =>
        Results.Json(Body(status, detail), contentType: ContentType, statusCode: status);

    public static Task Write(HttpContext ctx, int status, string detail, IReadOnlyDictionary<string, string[]>? errors = null, bool outdated = false, CancellationToken cancel = default)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(Body(status, detail, errors, outdated), (System.Text.Json.JsonSerializerOptions?)null, ContentType, cancel);
    }

    /// <summary>Turns exceptions into problem responses.</summary>
    public static void UseProblems(this WebApplication app) =>
        app.Use(async (ctx, next) =>
        {
            try
            {
                await next();
                if (ctx.Response.StatusCode == StatusCodes.Status413PayloadTooLarge && !ctx.Response.HasStarted)
                    await Write(ctx, 413, "The request body is too large.");
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
            {
                // Client gone: nothing to answer.
            }
            catch (Exception ex)
            {
                var (status, detail, errors) = Translate(ex, ctx);
                if (ctx.Response.HasStarted) return;
                ctx.Response.Clear();
                await Write(ctx, status, detail, errors, outdated: ex is ApiProblem { Outdated: true });
            }
        });

    private static (int, string, IReadOnlyDictionary<string, string[]>?) Translate(Exception ex, HttpContext ctx)
    {
        switch (ex)
        {
            case ApiProblem p: return (p.Status, p.Message, p.Errors);
            case BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }:
                return (413, "The request body is too large.", null);
            case BadHttpRequestException:
                return (400, "Invalid request: the JSON body is missing, malformed or holds an unknown value.", null);
            case DbUpdateConcurrencyException:
                return (409, "Someone changed this record. Reload it and try again.", null);
            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }:
                return (409, "A record with this id already exists.", null);
            case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } }:
                return (409, "This record is referenced by other records, or refers to a record that no longer exists.", null);
            default:
                ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Errors")
                    .LogError(ex, "Unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
                return (500, "Internal server error.", null);
        }
    }
}

/// <summary>Collects field errors, then throws one 400 with all of them.</summary>
public sealed class FieldErrors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public bool Any => _errors.Count > 0;

    public IEnumerable<(string Field, string Message)> All => _errors.SelectMany(e => e.Value.Select(m => (e.Key, m)));

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
        list.Add(message);
    }

    /// <summary>Required text, trimmed, at most <paramref name="max"/> characters.</summary>
    public string Required(string field, string? value, int max = Domain.Rules.MaxNameLength)
    {
        var text = value?.Trim() ?? "";
        if (text.Length == 0) Add(field, "This field is required.");
        else if (text.Length > max) Add(field, $"At most {max} characters.");
        return text;
    }

    /// <summary>Optional text: trimmed, null when empty, at most <paramref name="max"/> characters.</summary>
    public string? Optional(string field, string? value, int max = Domain.Rules.MaxShortTextLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > max) Add(field, $"At most {max} characters.");
        return text;
    }

    public void ThrowIfAny()
    {
        if (Any) throw ApiProblem.Invalid(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
