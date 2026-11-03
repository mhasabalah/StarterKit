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

        services.AddSingleton<TenantJobFilter>();

        services.AddHangfire((sp, config) =>
        {
            config.UsePostgreSqlStorage(options =>
                options.UseNpgsqlConnection(connectionString));
            config.UseRecommendedSerializerSettings();

            // Tenant propagation: the enqueuing flow's tenant is restored while the job runs.
            config.UseFilter(sp.GetRequiredService<TenantJobFilter>());
        });

        services.AddHangfireServer(options =>
        {
            options.Queues = ["default", "critical"];
            options.WorkerCount = 5;
            options.ServerName = "main-server";
        });

        // Every notification (all email included) goes through NotificationDispatchJob on this queue.
        services.AddHangfireServer(options =>
        {
            options.Queues = ["notification"];
            options.WorkerCount = 4;
            options.ServerName = "notification-server";
        });

        return services;
    }
}
