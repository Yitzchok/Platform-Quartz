using System;
using MassTransit;
using MassTransit.Context;
using MassTransit.ExtensionsDependencyInjectionIntegration;
using MassTransit.Platform.Abstractions;
using MassTransit.QuartzIntegration;
using MassTransit.QuartzIntegration.Configuration;
using MassTransit.Util;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Quartz;
using Quartz.Impl;

namespace Platform.Quartz
{
    public class QuartzPlatformStartup :
        IPlatformStartup
    {
        readonly IConfiguration _configuration;

        public QuartzPlatformStartup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureMassTransit(IServiceCollectionConfigurator configurator, IServiceCollection services)
        {
            LogContext.Info?.Log("Configuring Quartz");

            services.Configure<QuartzSettings>(_configuration.GetSection("Quartz"));

            var options = new QuartzOptions();

            services.AddSingleton(options);
            services.AddSingleton(x =>
            {
                var quartzConfig = x.GetRequiredService<QuartzOptions>();

                var schedulerFactory = new StdSchedulerFactory(quartzConfig.Configuration);

                var scheduler = TaskUtil.Await(() => schedulerFactory.GetScheduler());

                return scheduler;
            });

            services.AddSingleton<QuartzEndpointDefinition>();

            services.AddScoped<ScheduleMessageConsumer>();
            services.AddScoped<CancelScheduledMessageConsumer>();

            services.AddHealthChecks()
                .AddSqlServer(options.ConnectionString,
                    "SELECT 1;",
                    "sql",
                    HealthStatus.Degraded,
                    new[] {"db", "sql", "sqlserver"});
        }

        public void ConfigureBus<TEndpointConfigurator>(IBusFactoryConfigurator<TEndpointConfigurator> configurator, IServiceProvider provider)
            where TEndpointConfigurator : IReceiveEndpointConfigurator
        {
            var scheduler = provider.GetRequiredService<IScheduler>();
            var options = provider.GetRequiredService<QuartzOptions>();

            var schedulerAddress = new Uri($"queue:{options.QueueName}");

            configurator.ConnectBusObserver(new SchedulerBusObserver(scheduler, schedulerAddress));

            configurator.ReceiveEndpoint(options.QueueName, e =>
            {
                e.Consumer<ScheduleMessageConsumer>(provider);
                e.Consumer<CancelScheduledMessageConsumer>(provider);
            });
        }
    }
}