// See https://aka.ms/new-console-template for more information


using System.CommandLine;
using System.Security.Cryptography;
using System.Text;
using System.Text.Unicode;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var configCommand = new Command("configure", "Configure the runner");
var urlOption = new Option<Uri>("--url", "The API url");
var apiKeyOption = new Option<string>("--api-key", "The API key");
configCommand.AddOption(urlOption);
configCommand.AddOption(apiKeyOption);
configCommand.SetHandler((url, apiKey) =>
{
   Console.WriteLine($"configuration {url} {apiKey}");
   var context = new HostContext();
   var manager = context.Services.GetRequiredService<ConfigurationManager>();
   manager.Configure(url, apiKey);
}, urlOption, apiKeyOption);

var updateCommand = new Command("update", "Update to the latest version");
var rootCommand = new RootCommand("Configure/Run/Update the QontaHub Runner");
rootCommand.AddCommand(configCommand);
rootCommand.AddCommand(updateCommand);

await rootCommand.InvokeAsync(args);

return;

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
    public RunnerContext()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddDataProtection();

        Services = serviceCollection.BuildServiceProvider();
    }

    public ServiceProvider Services { get; set; }
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

public class ConfigurationManager
{
    private readonly IConfigurationStore _configStore;

    public ConfigurationManager(IConfigurationStore configStore)
    {
        _configStore = configStore;
    }
    
    public void Configure(Uri url, string apiKey)
    {
        if (IsConfigured())
        {
            throw new InvalidOperationException("Cannot configure the runner because it is already configured");
        }
        
        
        
        var dataProtector = new HostContext().Services.GetRequiredService<IDataProtectionProvider>();
        var protector = dataProtector.CreateProtector("local-config");
        Console.WriteLine($"Protecting {apiKey}");
        var encrypted = protector.Protect(Encoding.UTF8.GetBytes(apiKey));
        var decrypted = protector.Unprotect(encrypted);
        Console.WriteLine(Encoding.UTF8.GetString(decrypted));
    }

    private bool IsConfigured()
    {
        return _configStore.IsConfigured();
    }
}

public interface IConfigurationStore
{
    bool IsConfigured();
}

internal class ConfigurationStore(IHostContext hostContext) : IConfigurationStore
{
    private readonly string _configFilePath = hostContext.GetConfigFile(WellKnownConfigFile.Runner);

    public bool IsConfigured()
    {
        return new FileInfo(_configFilePath).Exists;
    }
}

public enum WellKnownConfigFile
{
    Runner
}

internal interface IHostContext
{
    string GetConfigFile(WellKnownConfigFile runner);
}

public class HostContext : IHostContext
{
    public HostContext()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IHostContext>(this);
        serviceCollection.AddSingleton<ConfigurationManager>();
        serviceCollection.AddSingleton<IConfigurationStore, ConfigurationStore>();
        serviceCollection.AddDataProtection();

        Services = serviceCollection.BuildServiceProvider();
    }

    public ServiceProvider Services { get; set; }

    public string GetConfigFile(WellKnownConfigFile runner)
    {
        return Environment.SystemDirectory;
    }
}