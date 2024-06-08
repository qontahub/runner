using System.ServiceProcess;
using QontaHub.RunnerService;

ServiceBase.Run([new RunnerService(args.Length > 0 ? args[0] : "QontaHubRunnerService")]);

return 0;
