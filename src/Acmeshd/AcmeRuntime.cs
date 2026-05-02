using Itexoft.IO;
using Itexoft.IO.FileSystem;
using Itexoft.Net.Http;
using Itexoft.Threading;

namespace Acmeshd;

internal sealed class AcmeRuntime
{
    private const string AcmeRawRoot = "https://raw.githubusercontent.com/acmesh-official/acme.sh/refs/heads/master";
    private const string AcmeUrl = AcmeRawRoot + "/acme.sh";
    private const string DnsApiPath = "/dnsapi/";
    private const string ShellExtension = ".sh";
    private const string ShellPath = "/bin/sh";
    private const string ChmodPath = "/bin/chmod";
    private const string OpenSslPath = "openssl";
    private const string OpenSslEnvironmentName = "ACME_OPENSSL_BIN";
    private const string DnsHookPrefix = "dns_";
    private const int AcmeIssueSkippedExitCode = 2;

    private readonly AcmeshdPaths paths;
    private readonly AcmeshdConfig config;
    private readonly ProcessRunner runner;

    public AcmeRuntime(AcmeshdPaths paths, AcmeshdConfig config, ProcessRunner runner)
    {
        this.paths = paths;
        this.config = config;
        this.runner = runner;
    }

    public void RunCycle(CancelToken cancelToken)
    {
        Console.WriteLine("acmeshd: cycle started");
        var environment = DnsProvisioning.Prepare(this.config.Certificates, this.config.Environment, this.runner, cancelToken);
        this.PrepareAcme(cancelToken);

        for (var i = 0; i < this.config.Certificates.Length; i++)
            this.PrepareCertificate(this.config.Certificates[i], environment, cancelToken);

        this.RunAcme("cron", AcmeCommands.Cron(this.paths), environment, cancelToken);
        Console.WriteLine("acmeshd: cycle completed");
    }

    private void PrepareAcme(CancelToken cancelToken)
    {
        IFileSystem.Sys.CreateDirectory(this.paths.StateDirectory);
        IFileSystem.Sys.CreateDirectory(this.paths.AcmeHomePath);
        IFileSystem.Sys.CreateDirectory(this.paths.AcmeConfigPath);

        this.Download("acme.sh", AcmeUrl, this.paths.AcmeScriptPath, cancelToken);
        this.MakeExecutable(this.paths.AcmeScriptPath, cancelToken);
        this.PrepareDnsHooks(cancelToken);
    }

    private void PrepareDnsHooks(CancelToken cancelToken)
    {
        for (var i = 0; i < this.config.Certificates.Length; i++)
        {
            var spec = this.config.Certificates[i];

            if (spec.Mode != CertificateMode.Dns)
                continue;

            if (this.IsPreviousDnsHook(i, spec.Dns!))
                continue;

            this.PrepareDnsHook(spec.Dns!, cancelToken);
        }
    }

    private bool IsPreviousDnsHook(int index, string name)
    {
        for (var i = 0; i < index; i++)
        {
            var spec = this.config.Certificates[i];

            if (spec.Mode == CertificateMode.Dns && spec.Dns == name)
                return true;
        }

        return false;
    }

    private void PrepareDnsHook(string name, CancelToken cancelToken)
    {
        RequireDnsHookName(name);
        IFileSystem.Sys.CreateDirectory(this.paths.AcmeDnsApiPath);

        var path = Path.Combine(this.paths.AcmeDnsApiPath, name + ShellExtension);
        var url = AcmeRawRoot + DnsApiPath + name + ShellExtension;

        this.Download("dns hook " + name, url, path, cancelToken);
        this.MakeExecutable(path, cancelToken);
    }

    private void Download(string operation, string url, string path, CancelToken cancelToken)
    {
        var uri = new Uri(url);
        using var client = new NetHttpClient(uri);
        client.DefaultHeaders.UserAgent = "acmeshd";

        Console.WriteLine("acmeshd: downloading " + operation);
        var response = client.Get(uri, cancelToken);
        response.EnsureSuccess();
        var body = response.ReadAsBytes(cancelToken);

        using var file = IFileSystem.Sys.Open(path, SysFileMode.Overwrite);
        file.Overwrite(body.Span, cancelToken);
    }

    private void MakeExecutable(string path, CancelToken cancelToken) =>
        this.runner.Run("chmod", ChmodPath, ["+x", path], this.paths.StateDirectory, [], cancelToken);

    private static void RequireDnsHookName(string name)
    {
        if (name.Length <= DnsHookPrefix.Length)
            throw new InvalidOperationException("[cert] dns must be an acme.sh dns hook name");

        for (var i = 0; i < DnsHookPrefix.Length; i++)
        {
            if (name[i] != DnsHookPrefix[i])
                throw new InvalidOperationException("[cert] dns must be an acme.sh dns hook name");
        }

        for (var i = DnsHookPrefix.Length; i < name.Length; i++)
        {
            if (!IsDnsHookCharacter(name[i]))
                throw new InvalidOperationException("[cert] dns must contain only ASCII letters, digits or underscore");
        }
    }

    private static bool IsDnsHookCharacter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';

    private void PrepareCertificate(CertificateSpec spec, EnvVar[] environment, CancelToken cancelToken)
    {
        IFileSystem.Sys.CreateDirectory(spec.OutputDirectory);

        Console.WriteLine("acmeshd: certificate " + spec.MainDomain);
        this.RunAcme("register-account", AcmeCommands.RegisterAccount(this.paths, spec), environment, cancelToken);

        if (!IFileSystem.Sys.FileExists(AcmeCommands.FullchainPath(spec)))
        {
            var issueExitCode = this.RunAcme(
                "issue " + spec.MainDomain,
                AcmeCommands.Issue(this.paths, spec),
                environment,
                AcmeIssueSkippedExitCode,
                cancelToken);

            if (issueExitCode == AcmeIssueSkippedExitCode)
                Console.WriteLine("acmeshd: issue " + spec.MainDomain + " skipped by acme.sh");
        }

        this.RunAcme("install-cert " + spec.MainDomain, AcmeCommands.Install(this.paths, spec), environment, cancelToken);
        this.runner.Run("export-pfx " + spec.MainDomain, ResolveOpenSslPath(environment), AcmeCommands.ExportPfx(spec), this.paths.StateDirectory, environment, cancelToken);
    }

    private static string ResolveOpenSslPath(EnvVar[] environment)
    {
        for (var i = 0; i < environment.Length; i++)
        {
            if (environment[i].Name != OpenSslEnvironmentName)
                continue;

            if (string.IsNullOrWhiteSpace(environment[i].Value))
                throw new InvalidOperationException("[env] " + OpenSslEnvironmentName + " is empty");

            return environment[i].Value!;
        }

        return OpenSslPath;
    }

    private void RunAcme(string operation, string[] arguments, EnvVar[] environment, CancelToken cancelToken) =>
        this.RunAcme(operation, arguments, environment, 0, cancelToken);

    private int RunAcme(string operation, string[] arguments, EnvVar[] environment, int acceptedExitCode, CancelToken cancelToken)
    {
        var shellArguments = new string[arguments.Length + 3];
        shellArguments[0] = "-c";
        shellArguments[1] = "exec \"$0\" \"$@\" 2>&1";
        shellArguments[2] = this.paths.AcmeScriptPath;

        for (var i = 0; i < arguments.Length; i++)
            shellArguments[i + 3] = arguments[i];

        return this.runner.Run(operation, ShellPath, shellArguments, this.paths.StateDirectory, environment, acceptedExitCode, cancelToken);
    }
}
