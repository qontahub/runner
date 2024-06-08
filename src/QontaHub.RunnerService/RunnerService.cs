using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text.RegularExpressions;

namespace QontaHub.RunnerService;

public class RunnerService : ServiceBase
{
    private const uint CtrlCEvent = 0;
    private const uint CtrlBreakEvent = 1;

    private const string EventSourceName = "QontaHubRunnerService";
    private bool _restart = false;

    public RunnerService(string serviceName)
    {
        ServiceName = serviceName;
        ObjectLock = new object();
    }

    private object ObjectLock { get; }

    private bool Stopping { get; set; }

    private Process? RunnerProcess { get; set; }

    protected override void OnStart(string[] args)
    {
        Task.Run(async () =>
        {
            try
            {
                WriteInfo("Starting QontaHub Runner Service");
                bool stopping;
                var timeBetweenRetries = TimeSpan.FromSeconds(5);

                lock (ObjectLock)
                {
                    stopping = Stopping;
                }

                while (!stopping)
                {
                    lock (ObjectLock)
                    {
                        RunnerProcess = CreateRunnerProcess();
                        RunnerProcess.OutputDataReceived += RunnerProcessOnOutputDataReceived;
                        RunnerProcess.ErrorDataReceived += RunnerProcessOnErrorDataReceived;
                        RunnerProcess.Start();
                        RunnerProcess.BeginErrorReadLine();
                        RunnerProcess.BeginOutputReadLine();
                    }

                    await RunnerProcess.WaitForExitAsync();
                    var exitCode = RunnerProcess.ExitCode;

                    switch (exitCode)
                    {
                        case 0:
                            Stopping = true;
                            WriteInfo("Runner exited with no errors, stopping the service, no retry needed");
                            break;
                        case 1:
                            Stopping = true;
                            WriteInfo("Runner was terminated, stopping the service, no retry needed");
                            break;
                        case 2:
                            WriteInfo("Runner encountered a retryable error, restarting...");
                            break;
                        case 3:
                            WriteInfo("An update is in progress, restarting...");
                            var updateResult = await HandleRunnerUpdate();
                            if (updateResult == RunnerUpdateResult.Succeed)
                            {
                                WriteInfo("Runner updated successfully, re-launching...");
                            }
                            else if (updateResult == RunnerUpdateResult.Failed)
                            {
                                WriteInfo("Runner update failed, stopping service");
                                Stopping = true;
                            }
                            else if (updateResult == RunnerUpdateResult.SucceedNeedRestart)
                            {
                                WriteInfo("Runner updated successfully, restarting service to update the host...");
                                _restart = true;
                                ExitCode = int.MaxValue;
                                Stop();
                            }

                            break;
                        default:
                            WriteInfo("Runner exited unexpectedly, re-launching...");
                            break;
                    }

                    if (Stopping)
                    {
                        ExitCode = exitCode;
                        Stop();
                    }
                    else
                    {
                        await Task.Delay(timeBetweenRetries);
                    }

                    lock (ObjectLock)
                    {
                        WriteInfo($"Runner exited with exit code {exitCode}");
                        RunnerProcess.OutputDataReceived += RunnerProcessOnOutputDataReceived;
                        RunnerProcess.ErrorDataReceived += RunnerProcessOnErrorDataReceived;
                        RunnerProcess = null;
                        stopping = Stopping;
                    }
                }
            }
            catch (Exception e)
            {
                WriteException(e);
                ExitCode = 99;
                Stop();
            }
        });
    }

    private async Task<RunnerUpdateResult> HandleRunnerUpdate()
    {
        await Task.Delay(5000);
        var diagDirectory = new DirectoryInfo(GetDiagnosticFolderPath());
        var updateLogs = diagDirectory.GetFiles("SelfUpdate*");
        var pattern = @"update-(?<date>\d{8}-\d{6})\.(?<status>.*)";

        var regex = new Regex(pattern);
        foreach (var fileInfo in updateLogs)
        {
            var match = regex.Match(fileInfo.Name);

            if (match.Success)
            {
                var date = match.Groups["date"].Value;
                if (DateTimeOffset.TryParseExact(date, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var updateDate))
                {
                    var timeDiff = DateTimeOffset.Now - updateDate;
                    if (timeDiff.TotalSeconds <= 15)
                    {
                        if (Enum.TryParse(match.Groups["status"].Value, true,
                                out RunnerUpdateResult result))
                            return result;

                        return RunnerUpdateResult.Failed;
                    }
                }
            }
        }

        return RunnerUpdateResult.Failed;
    }

    private string GetDiagnosticFolderPath()
    {
        return Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location))!, "_diag");
    }

    private void RunnerProcessOnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data)) WriteToEventLog(e.Data, EventLogEntryType.Error);
    }

    private void RunnerProcessOnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data)) WriteToEventLog(e.Data, EventLogEntryType.Information);
    }

    private Process CreateRunnerProcess()
    {
        var location = Assembly.GetEntryAssembly()!.Location;
        var runnerExePath = Path.Combine(Path.GetDirectoryName(location)!, "QontaHub.Runner.exe");
        WriteInfo($"Launching process {runnerExePath}");
        var process = new Process();
        process.StartInfo = new ProcessStartInfo(runnerExePath, "")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return process;
    }

    protected override void OnStop()
    {
        WriteInfo($"OnStop: stopping={Stopping}, restart={_restart}");
        lock (ObjectLock)
        {
            Stopping = true;

            if (_restart) throw new Exception("Crash service host to trigger service restart");

            SendCtrlSignalToRunnerListener(CtrlCEvent);
        }
    }

    // this will send either Ctrl-C or Ctrl-Break to runner.listener
    // Ctrl-C will be used for OnStop()
    // Ctrl-Break will be used for OnShutdown()
    private void SendCtrlSignalToRunnerListener(uint signal)
    {
        try
        {
            if (RunnerProcess is { HasExited: false })
            {
                // Try to let the runner process know that we are stopping
                //Attach service process to console of Runner.Listener process. This is needed,
                //because windows service doesn't use its own console.
                if (AttachConsole((uint)RunnerProcess.Id))
                {
                    //Prevent main service process from stopping because of Ctrl + C event with SetConsoleCtrlHandler
                    SetConsoleCtrlHandler(null, true);
                    try
                    {
                        //Generate console event for current console with GenerateConsoleCtrlEvent (processGroupId should be zero)
                        GenerateConsoleCtrlEvent(signal, 0);
                        //Wait for the process to finish (give it up to 30 seconds)
                        RunnerProcess.WaitForExit(30000);
                    }
                    finally
                    {
                        //Disconnect from console and restore Ctrl+C handling by main process
                        FreeConsole();
                        SetConsoleCtrlHandler(null, false);
                    }
                }

                // if runner is still running, kill it
                if (!RunnerProcess.HasExited) RunnerProcess.Kill();
            }
        }
        catch (Exception exception)
        {
            // InvalidOperationException is thrown when there is no process associated to the process object. 
            // There is no process to kill, Log the exception and shutdown the service. 
            // If we don't handle this here, the service get into a state where it can neither be stoped nor restarted (Error 1061)
            WriteException(exception);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GenerateConsoleCtrlEvent(uint dwCtrlEvent, uint dwProcessGroupId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate? handlerRoutine, bool add);


    protected override void OnShutdown()
    {
        WriteInfo($"OnStop: stopping={Stopping}, restart={_restart}");
        SendCtrlSignalToRunnerListener(CtrlBreakEvent);
        base.OnShutdown();
    }

    private void WriteInfo(string message)
    {
        WriteToEventLog(message, EventLogEntryType.Information);
    }

    private void WriteException(Exception exception)
    {
        WriteToEventLog(exception.ToString(), EventLogEntryType.Error);
    }

    private void WriteToEventLog(string eventText, EventLogEntryType entryType)
    {
        EventLog.WriteEntry(EventSourceName, eventText, entryType, 100);
    }

    private enum RunnerUpdateResult
    {
        Succeed,
        Failed,
        SucceedNeedRestart
    }

    // Delegate type to be used as the Handler Routine for SetConsoleCtrlHandler
    private delegate bool ConsoleCtrlDelegate(uint ctrlType);
}