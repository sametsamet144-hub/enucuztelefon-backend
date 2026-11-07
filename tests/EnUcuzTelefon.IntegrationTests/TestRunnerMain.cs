using System.Reflection;
using Xunit;

namespace EnUcuzTelefon.IntegrationTests;

public static class TestRunnerMain
{
    public static async Task<int> Main(string[] args)
    {
        if (!args.Contains("--verify-all"))
        {
            return 0;
        }

        int passed = 0;
        int failed = 0;

        var assemblies = new Assembly[]
        {
            typeof(EnUcuzTelefon.UnitTests.PriceVerificationTests).Assembly,
            typeof(EnUcuzTelefon.ArchitectureTests.CleanArchitectureTests).Assembly,
            typeof(ApiIntegrationTests).Assembly
        };

        using var factory = new CustomWebApplicationFactory();

        foreach (var asm in assemblies)
        {
            Console.WriteLine($"\n==========================================");
            Console.WriteLine($"TEST SUITE: {asm.GetName().Name}");
            Console.WriteLine($"==========================================");

            foreach (var type in asm.GetExportedTypes().Where(t => t.IsClass && !t.IsAbstract))
            {
                var methods = type.GetMethods()
                    .Where(m => m.GetCustomAttributes(typeof(FactAttribute), true).Any())
                    .ToList();

                if (!methods.Any()) continue;

                foreach (var method in methods)
                {
                    try
                    {
                        var ctors = type.GetConstructors();
                        object instance = ctors[0].GetParameters().Length == 1
                            ? Activator.CreateInstance(type, factory)!
                            : Activator.CreateInstance(type)!;

                        var res = method.Invoke(instance, null);
                        if (res is Task task)
                        {
                            await task;
                        }

                        Console.WriteLine($"  [PASS] {type.Name}.{method.Name}");
                        passed++;
                    }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException ?? ex;
                        Console.WriteLine($"  [FAIL] {type.Name}.{method.Name}: {inner.GetType().Name} - {inner.Message}");
                        failed++;
                    }
                }
            }
        }

        Console.WriteLine($"\n==========================================");
        Console.WriteLine($"LIVE API ENDPOINT VERIFICATION (TestServer)");
        Console.WriteLine($"==========================================");

        var client = factory.CreateClient();
        var endpoints = new[]
        {
            "/health",
            "/api/v1/products",
            "/api/v1/products/iphone-16-pro-max",
            "/api/v1/products/iphone-16-pro-max/offers",
            "/api/v1/products/iphone-16-pro-max/price-history",
            "/api/v1/brands",
            "/api/v1/stores",
            "/api/v1/content/articles",
            "/api/v1/content/qa",
            "/api/v1/admin/dashboard",
            "/api/v1/redirect?store=trendyol&product=iPhone+16+Pro+Max&mode=json"
        };

        foreach (var ep in endpoints)
        {
            var resp = await client.GetAsync(ep);
            var body = await resp.Content.ReadAsStringAsync();
            var preview = body.Length > 140 ? body[..140] + "..." : body;
            Console.WriteLine($"  GET {ep} -> {(int)resp.StatusCode} {resp.StatusCode} ({body.Length} bytes) | {preview}");
            if (!resp.IsSuccessStatusCode) failed++;
        }

        var npgsqlFactory = new EnUcuzTelefon.Infrastructure.Persistence.ApplicationDbContextFactory();
        using (var npgsqlContext = npgsqlFactory.CreateDbContext(Array.Empty<string>()))
        {
            var sql = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.GenerateCreateScript(npgsqlContext.Database);
            var sqlPath = Path.Combine(Directory.GetCurrentDirectory(), "deploy", "scripts", "initial_schema.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(sqlPath)!);
            await File.WriteAllTextAsync(sqlPath, sql);
            Console.WriteLine($"\n[SQL] Generated PostgreSQL schema DDL ({sql.Length} bytes) -> {sqlPath}");
        }

        Console.WriteLine($"\n==========================================");
        Console.WriteLine($"FINAL RESULT: {passed} Passed, {failed} Failed, {passed + failed} Total");
        Console.WriteLine($"==========================================");

        return failed > 0 ? 1 : 0;
    }
}
