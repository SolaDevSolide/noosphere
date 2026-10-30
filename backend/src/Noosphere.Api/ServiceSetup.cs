using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Domain;

namespace Noosphere.Api;

/// <summary>Services shared by the web application and the command line.</summary>
public static class ServiceSetup
{
    public static IServiceCollection AddNoosphereCore(this IServiceCollection services, Settings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddDbContext<NoosphereDbContext>(o => o.UseNpgsql(settings.Database.Length > 0 ? settings.Database
            : throw new MissingConfigurationException("No database configured: set NOOSPHERE_DATABASE (a PostgreSQL connection string).")));
        return services;
    }
}

/// <summary>A required setting is missing.</summary>
public sealed class MissingConfigurationException(string message) : Exception(message);
