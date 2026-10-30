using ModelContextProtocol.Protocol;

namespace Noosphere.Api.Agents;

/// <summary>Services of the MCP server, its OAuth authorization server and the agents' bearer tokens.</summary>
public static class AgentSetup
{
    public static IServiceCollection AddNoosphereAgents(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        AgentBearerHandler.AddAgentAuthentication(services);
        services.AddSingleton<IClientMetadataFetcher, SafeClientMetadataFetcher>();
        // Browser-based MCP clients call discovery, token and MCP endpoints from another origin. These endpoints never use
        // the session cookie (bearer tokens only), so any origin is allowed, without credentials.
        services.AddCors(o => o.AddPolicy(OAuthEndpoints.CorsPolicy, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
            .WithExposedHeaders("Mcp-Session-Id", "Mcp-Protocol-Version", "WWW-Authenticate")));
        services.AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation
                {
                    Name = "noosphere",
                    Title = "Noosphere",
                    Version = typeof(AgentSetup).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                    Description = "The application landscape: applications, flows, data objects, teams, domains and documentation pages.",
                };
                o.ServerInstructions = NoosphereMcpTools.Instructions;
            })
            .WithHttpTransport(o => o.SessionMode = ModelContextProtocol.AspNetCore.HttpServerSessionMode.Stateless)
            .WithTools<NoosphereMcpTools>();
        return services;
    }
}
