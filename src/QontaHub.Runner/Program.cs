// See https://aka.ms/new-console-template for more information


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

await Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddSingleton<RunnerContext>(new RunnerContext());
        services.AddHostedService<Runner>();
    })
    .Build()
    .RunAsync();

public class RunnerContext
{
    
}

public class Runner(RunnerContext context) : BackgroundService
{
    private readonly RunnerContext _context = context;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            Console.WriteLine("Looping the Runner");
            await Task.Delay(10000, stoppingToken);
            if (DateTimeOffset.Now.Second < 10)
            {
                Console.Error.WriteLine("Time has come to fail");
                await Task.Delay(5000);
                Environment.Exit(2);
            }
        }
    }
}