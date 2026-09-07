using Microsoft.OpenApi;

namespace Ustin.Work.LessMess.UmbarcoWrapper.Web.Swagger;

/// <summary>Bound from the <c>Swagger</c> configuration section.</summary>
public sealed class SwaggerOptions
{
    public const string SectionName = "Swagger";

    public string Title { get; set; } = "Ustin Provider Wrapper";

    public string Version { get; set; } = "1.1.0";
}

public static class SwaggerConfig
{
    public static IServiceCollection AddWrapperSwagger(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection(SwaggerOptions.SectionName).Get<SwaggerOptions>() ?? new SwaggerOptions();
        services.AddSingleton(options);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(gen =>
        {
            gen.SwaggerDoc("v1", new OpenApiInfo { Title = options.Title, Version = options.Version });

            // "Authorize" button: send `Authorization: Bearer <jwt>` from Swagger UI.
            gen.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the raw JWT (without the \"Bearer \" prefix).",
            });
            gen.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer")] = new List<string>(),
            });
        });

        return services;
    }

    public static WebApplication UseWrapperSwagger(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<SwaggerOptions>();
        app.UseSwagger();
        app.UseSwaggerUI(ui =>
        {
            ui.SwaggerEndpoint("/swagger/v1/swagger.json", $"{options.Title} {options.Version}");
            ui.DocumentTitle = options.Title;
        });
        return app;
    }
}
