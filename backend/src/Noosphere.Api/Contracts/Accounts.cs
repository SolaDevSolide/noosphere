using Noosphere.Domain;

namespace Noosphere.Api.Contracts;

public sealed record LoginInput(string? Username, string? Password);

/// <summary>The signed-in user.</summary>
/// <param name="Language"><c>en</c> or <c>fr</c>.</param>
public sealed record MeDto(string Username, string DisplayName, Role Role, string Language);

public sealed record LanguageInput(string? Language);

public sealed record PasswordChangeInput(string? CurrentPassword, string? NewPassword);

public sealed record UserDto(string Username, string DisplayName, Role Role, string Language, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record UserCreateInput(string? Username, string? DisplayName, Role? Role, string? Language, string? Password);

/// <param name="Password">New password; leave empty to keep the current one. Changing it closes the user's sessions.</param>
public sealed record UserUpdateInput(string? DisplayName, Role? Role, string? Language, string? Password, int? Version);

/// <param name="Status"><c>live</c> or <c>ready</c> when healthy, <c>unavailable</c> otherwise.</param>
/// <param name="Database"><c>ok</c>, <c>unreachable</c> or <c>migrations-pending</c>.</param>
public sealed record HealthDto(string Status, string? Database = null);
