using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnUcuzTelefon.Infrastructure.Persistence;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        DotEnvLoader.Load();
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=enucuztelefon;Username=postgres;Password=postgres";

        optionsBuilder.UseNpgsql(connectionString, b =>
        {
            b.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
        });

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
