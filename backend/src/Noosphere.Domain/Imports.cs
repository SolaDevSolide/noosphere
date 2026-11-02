namespace Noosphere.Domain;

/// <summary>
/// <c>planned</c> after the dry run; <c>applied</c> once written; <c>rejected</c> when applying failed (the catalogue
/// changed since the dry run, or the plan had errors); <c>expired</c> when it was not applied in time.
/// </summary>
public enum ImportStatus { Planned, Applied, Rejected, Expired }

/// <summary>
/// One import of files from another format (Backstage, OpenAPI, AsyncAPI, ADR, Mermaid, Excel): the dry run stores its
/// plan here, and the same plan is applied by id, by the same person, before <see cref="ExpiresAt"/>, and only while the
/// catalogue has not changed (<see cref="CatalogueStamp"/>). Rows are kept as the record of every import.
/// </summary>
public sealed class ImportPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Null for the command line.</summary>
    public Guid? UserId { get; set; }
    /// <summary>Username when the plan was made (kept when the account is deleted); "system" for the command line.</summary>
    public string Username { get; set; } = "";
    public ImportStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>When it was applied, rejected or found expired.</summary>
    public DateTimeOffset? ClosedAt { get; set; }
    /// <summary>Why applying was refused.</summary>
    public string? Outcome { get; set; }
    /// <summary>Formats read, comma-separated: <c>backstage,openapi</c>.</summary>
    public string Formats { get; set; } = "";
    /// <summary>The id of the last revision when the plan was made: any write to the catalogue since makes the plan stale.</summary>
    public long CatalogueStamp { get; set; }
    public int Creates { get; set; }
    public int Updates { get; set; }
    public int Unchanged { get; set; }
    public int Warnings { get; set; }
    public int Errors { get; set; }
    /// <summary>JSON: the preview returned by the dry run (files, entries with their field diff, warnings, errors).</summary>
    public string Preview { get; set; } = "{}";
    /// <summary>JSON: the writes to make, in order. Cleared once the plan is applied, rejected or expired.</summary>
    public string? Operations { get; set; }
}
