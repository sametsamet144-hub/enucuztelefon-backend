using EnUcuzTelefon.Infrastructure;
using EnUcuzTelefon.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services.AddSerilog();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
