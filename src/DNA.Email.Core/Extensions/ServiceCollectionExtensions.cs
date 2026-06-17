using DNA.Email.Core.Interfaces;
using DNA.Email.Core.Options;
using DNA.Email.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DNA.Email.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDnaEmailCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName));

        services.AddOptions<DkimOptions>()
            .Bind(configuration.GetSection(DkimOptions.SectionName));

        services.AddOptions<SmimeOptions>()
            .Bind(configuration.GetSection(SmimeOptions.SectionName));

        services.AddOptions<ApiKeyOptions>()
            .Bind(configuration.GetSection(ApiKeyOptions.SectionName));

        services.AddScoped<SmtpEmailService>();
        services.AddScoped<IEmailService>(sp => sp.GetRequiredService<SmtpEmailService>());
        services.AddScoped<ISmtpConnectionTester>(sp => sp.GetRequiredService<SmtpEmailService>());

        return services;
    }
}
