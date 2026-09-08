namespace Ustin.Work.LessMess.UmbarcoWrapper.Api.Security;

public static class ClientGateExtensions
{
    public static IServiceCollection AddClientGate(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ClientGateOptions>().Bind(config.GetSection(ClientGateOptions.SectionName));
        services.AddSingleton<ClientRegistry>();
        return services;
    }

    public static IApplicationBuilder UseClientGate(this IApplicationBuilder app) =>
        app.UseMiddleware<ClientGateMiddleware>();
}
