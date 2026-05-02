using System.Runtime.InteropServices;
using Itexoft.IO.FileSystem;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Acmeshd;

internal static class AcmeshdApp
{
    public static int Run(string[] args)
    {
        if (args.Length > 1)
            throw new InvalidOperationException("usage: acmeshd [config.ini]");

        var paths = AcmeshdPaths.Create(args);

        if (!IFileSystem.Sys.FileExists(paths.ConfigPath))
        {
            AcmeshdTemplate.Create(paths.ConfigPath);
            Console.WriteLine("acmeshd: created config: " + paths.ConfigPath);

            return 0;
        }

        var stopToken = CancelToken.New();
        var runner = new ProcessRunner();
        var stopRequested = 0;

        void stopNow()
        {
            if (Interlocked.Exchange(ref stopRequested, 1) != 0)
            {
                Environment.Exit(130);

                return;
            }

            runner.KillActiveProcess();
            stopToken.Cancel();
        }

        using var sigint = !OperatingSystem.IsWindows()
            ? PosixSignalRegistration.Create(
                PosixSignal.SIGINT,
                context =>
                {
                    context.Cancel = true;
                    stopNow();
                })
            : null;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stopNow();
        };

        for (;;)
        {
            stopToken.ThrowIf();
            var config = AcmeshdConfig.Load(paths);
            var runtime = new AcmeRuntime(paths, config, runner);

            runtime.RunCycle(stopToken);

            Console.WriteLine("acmeshd: waiting " + config.Period);
            Promise.Delay(config.Period, stopToken).GetAwaiter().GetResult();
        }
    }
}
