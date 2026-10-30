using Noosphere.Domain;

namespace Noosphere.Api.Contracts;

public sealed record LoginInput(string? Username, string? Password);

/// <summary>The signed-in user.</summary>
/// <param name="Language"><c>en</c> or <c>fr</c>.</param>
/// <param name="Teams">Teams the user belongs to.</param>
public sealed record MeDto(string Username, string DisplayName, Role Role, string Language, IReadOnlyList<TeamRefDto> Teams);

public sealed record LanguageInput(string? Language);

public sealed record PasswordChangeInput(string? CurrentPassword, string? NewPassword);

/// <param name="TeamIds">Teams the user belongs to.</param>
public sealed record UserDto(string Username, string DisplayName, Role Role, string Language, IReadOnlyList<string> TeamIds, int Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record UserCreateInput(string? Username, string? DisplayName, Role? Role, string? Language, string? Password, List<string>? TeamIds = null);

/// <param name="Password">New password; leave empty to keep the current one. Changing it closes the user's sessions.</param>
/// <param name="TeamIds">Teams of the user; null keeps them unchanged.</param>
public sealed record UserUpdateInput(string? DisplayName, Role? Role, string? Language, string? Password, int? Version, List<string>? TeamIds = null);

/// <param name="Status"><c>live</c> or <c>ready</c> when healthy, <c>unavailable</c> otherwise.</param>
/// <param name="Database"><c>ok</c>, <c>unreachable</c> or <c>migrations-pending</c>.</param>
public sealed record HealthDto(string Status, string? Database = null);
