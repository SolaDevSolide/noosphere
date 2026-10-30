using Microsoft.Extensions.DependencyInjection;
using Noosphere.Api.Data;

namespace Noosphere.Tests;

internal static class ServicesExtensions
{
    public static Task<T> GetRequiredDb<T>(this IServiceProvider services, Func<NoosphereDbContext, T> read)
    {
        using var scope = services.CreateScope();
        return Task.FromResult(read(scope.ServiceProvider.GetRequiredService<NoosphereDbContext>()));
    }
}
