using EnUcuzTelefon.Infrastructure;
using EnUcuzTelefon.Scheduler.Jobs;
using Quartz;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services.AddSerilog();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddQuartz(q =>
{
    q.UseSimpleTypeLoader();
    q.UseInMemoryStore();

    var jobKey = new JobKey("TwoHourStoreSyncJob", "DataCollectionGroup");
    q.AddJob<TwoHourStoreSyncJob>(opts => opts.WithIdentity(jobKey));

    // Schedule: Runs every 2 hours (using Europe/Istanbul timezone)
    q.AddTrigger(opts => opts
        .ForJob(jobKey)
        .WithIdentity("TwoHourStoreSyncTrigger", "DataCollectionGroup")
        .WithSimpleSchedule(x => x
            .WithIntervalInHours(2)
            .RepeatForever())
        .StartNow());
});

builder.Services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});

var host = builder.Build();
host.Run();
