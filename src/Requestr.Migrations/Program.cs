using DbUp;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace Requestr.Migrations;

class Program
{
    static int Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No connection string found. Please set the DefaultConnection in appsettings.json or via environment variables.");
            Console.ResetColor();
            return -1;
        }

        Console.WriteLine($"Connecting to database...");

        try
        {
            // Ensure database exists
            EnsureDatabase.For.SqlDatabase(connectionString);

            // Configure DbUp
            var upgrader = DeployChanges.To
                .SqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
                .WithTransaction()
                .LogToConsole()
                .Build();

            Console.WriteLine("Checking for pending migrations...");

            var result = upgrader.PerformUpgrade();

            // Display result
            if (!result.Successful)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Migration failed: {result.Error}");
                Console.ResetColor();
                return -1;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Database migration completed successfully!");
            Console.ResetColor();

            if (result.Scripts.Any())
            {
                Console.WriteLine("Applied migrations:");
                foreach (var script in result.Scripts)
                {
                    Console.WriteLine($"  - {script.Name}");
                }
            }
            else
            {
                Console.WriteLine("No migrations were applied (database is up to date).");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            Console.ResetColor();
            return -1;
        }
    }
}
