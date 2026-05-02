using Itexoft.Processes;
using Itexoft.Threading;

namespace Acmeshd;

internal sealed class ProcessRunner
{
    private readonly Lock sync = new();
    private SysProcess? activeProcess;

    public void KillActiveProcess()
    {
        SysProcess? process;

        lock (this.sync)
            process = this.activeProcess;

        if (process is not null)
            TryKill(process);
    }

    public void Run(
        string operation,
        string executable,
        string[] arguments,
        string? workingDirectory,
        EnvVar[] environment,
        CancelToken cancelToken) =>
        this.Run(operation, executable, arguments, workingDirectory, environment, 0, cancelToken);

    public int Run(
        string operation,
        string executable,
        string[] arguments,
        string? workingDirectory,
        EnvVar[] environment,
        int acceptedExitCode,
        CancelToken cancelToken)
    {
        Console.WriteLine("acmeshd: run " + operation);
        cancelToken.ThrowIf();

        var options = new SysProcessOptions(executable)
        {
            Arguments = new SysProcessArguments(arguments),
            WorkingDirectory = workingDirectory,
            Environment = environment.Length == 0 ? null : new ArrayEnvironment(environment),
        };

        var process = SysProcess.Start(options);
        this.SetActiveProcess(process);

        if (cancelToken.IsRequested)
            TryKill(process);

        int exitCode;

        try
        {
            exitCode = process.WaitAsync(cancelToken).GetAwaiter().GetResult();
        }
        finally
        {
            this.ClearActiveProcess(process);
        }

        if (exitCode != 0 && exitCode != acceptedExitCode)
            throw new InvalidOperationException(operation + " failed with exit code " + exitCode);

        return exitCode;
    }

    private void SetActiveProcess(SysProcess process)
    {
        lock (this.sync)
            this.activeProcess = process;
    }

    private void ClearActiveProcess(SysProcess process)
    {
        lock (this.sync)
        {
            if (ReferenceEquals(this.activeProcess, process))
                this.activeProcess = null;
        }
    }

    private static void TryKill(SysProcess process)
    {
        try
        {
            process.Kill(tree: true);
        }
        catch { }
    }
}
