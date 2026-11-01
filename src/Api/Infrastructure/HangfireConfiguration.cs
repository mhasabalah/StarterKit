using Hangfire;
using Hangfire.PostgreSql;

namespace Api.Infrastructure;

public static class HangfireConfiguration
{
    public static IServiceCollection AddHangfireServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("starterkit")
            ?? throw new InvalidOperationException("Database connection string 'starterkit' not found");

        services.Configure<HangfireDashboardAuthOptions>(
            configuration.GetSection(HangfireDashboardAuthOptions.SectionName));

        services.AddHangfire(config =>
        {
            config.UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(connectionString));
            config.UseRecommendedSerializerSettings();
        });

        services.AddHangfireServer(options =>
        {
            options.Queues = ["default", "critical"];
            options.WorkerCount = 5;
            options.ServerName = "main-server";
        });

        services.AddHangfireServer(options =>
        {
            options.Queues = ["email"];
            options.WorkerCount = 2;
            options.ServerName = "email-server";
        });

        services.AddHangfireServer(options =>
        {
            options.Queues = ["notification"];
            options.WorkerCount = 2;
            options.ServerName = "notification-server";
        });

        return services;
    }
}
