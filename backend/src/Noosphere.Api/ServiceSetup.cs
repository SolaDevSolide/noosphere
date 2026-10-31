using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Noosphere.Api.Data;
using Noosphere.Api.Links;
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
        // Saved changes of shared records become change notices for linked gestion-mep instances.
        services.AddSingleton<LinkChangeFeed>();
        services.AddSingleton<LinkChangeInterceptor>();
        services.AddDbContext<NoosphereDbContext>((sp, o) => o.UseNpgsql(settings.Database.Length > 0 ? settings.Database
            : throw new MissingConfigurationException("No database configured: set NOOSPHERE_DATABASE (a PostgreSQL connection string)."))
            .AddInterceptors(sp.GetRequiredService<LinkChangeInterceptor>()));
        // Session cookies and link private keys: the key ring survives restarts when KEYS_DIR is set (Docker volume);
        // otherwise ASP.NET Core's default location. The command line uses the same ring.
        var protection = services.AddDataProtection().SetApplicationName("noosphere");
        if (settings.KeysDirectory is { } keys) protection.PersistKeysToFileSystem(new DirectoryInfo(keys));
        services.AddNoosphereLinks();
        return services;
    }
}

/// <summary>A required setting is missing.</summary>
public sealed class MissingConfigurationException(string message) : Exception(message);
