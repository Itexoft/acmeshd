namespace Acmeshd;

internal sealed class AcmeshdPaths
{
    private const string DefaultConfigFileName = "config.ini";
    private const string StateDirectoryName = ".acmeshd";
    private const string AcmeFileName = "acme.sh";
    private const string HomeDirectoryName = "home";
    private const string ConfigDirectoryName = "config";
    private const string DnsApiDirectoryName = "dnsapi";

    private AcmeshdPaths(
        string binaryDirectory,
        string configPath,
        string configDirectory,
        string stateDirectory,
        string acmeScriptPath,
        string acmeHomePath,
        string acmeConfigPath)
    {
        this.BinaryDirectory = binaryDirectory;
        this.ConfigPath = configPath;
        this.ConfigDirectory = configDirectory;
        this.StateDirectory = stateDirectory;
        this.AcmeScriptPath = acmeScriptPath;
        this.AcmeHomePath = acmeHomePath;
        this.AcmeConfigPath = acmeConfigPath;
    }

    public string BinaryDirectory { get; }

    public string ConfigPath { get; }

    public string ConfigDirectory { get; }

    public string StateDirectory { get; }

    public string AcmeScriptPath { get; }

    public string AcmeHomePath { get; }

    public string AcmeConfigPath { get; }

    public string AcmeDnsApiPath => Path.Combine(this.StateDirectory, DnsApiDirectoryName);

    public static AcmeshdPaths Create(string[] args)
    {
        var processPath = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(processPath))
            throw new InvalidOperationException("unable to resolve process path");

        var binaryDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));

        if (string.IsNullOrWhiteSpace(binaryDirectory))
            throw new InvalidOperationException("unable to resolve binary directory");

        var configPath = args.Length == 0
            ? Path.Combine(binaryDirectory, DefaultConfigFileName)
            : Path.GetFullPath(args[0]);

        var configDirectory = Path.GetDirectoryName(Path.GetFullPath(configPath));

        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException("unable to resolve config directory");

        var stateDirectory = Path.Combine(binaryDirectory, StateDirectoryName);

        return new AcmeshdPaths(
            binaryDirectory,
            configPath,
            configDirectory,
            stateDirectory,
            Path.Combine(stateDirectory, AcmeFileName),
            Path.Combine(stateDirectory, HomeDirectoryName),
            Path.Combine(stateDirectory, ConfigDirectoryName));
    }
}
