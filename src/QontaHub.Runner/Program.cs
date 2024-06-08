// See https://aka.ms/new-console-template for more information


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

await Host.CreateDefaultBuilder()
    .ConfigureServices(config =>
    {
        config.AddHostedService<Runner>();
    })
    .Build()
    .RunAsync();

public class Runner: BackgroundService
{
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