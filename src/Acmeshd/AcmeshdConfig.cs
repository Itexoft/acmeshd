using Itexoft.Formats.Configuration.Ini;

namespace Acmeshd;

internal sealed class AcmeshdConfig
{
    private const string SectionDaemon = "daemon";
    private const string SectionCert = "cert";
    private const string SectionEnv = "env";

    private const string KeyPeriod = "period";
    private const string KeyEmail = "email";
    private const string KeyServer = "server";
    private const string KeyDomain = "domain";
    private const string KeyMode = "mode";
    private const string KeyDns = "dns";
    private const string KeyWebroot = "webroot";
    private const string KeyOut = "out";
    private const string KeyReload = "reload";
    private const string KeyKeyLength = "keylength";
    private const string KeyDnsSleep = "dnssleep";

    private const string DefaultPeriod = "1d";
    private const string DefaultServer = "letsencrypt";
    private const char DomainLabelSeparator = '.';
    private const char WildcardLabel = '*';

    private static readonly IniReaderOptions IniOptions = new()
    {
        SectionNameComparer = StringComparer.Ordinal,
        KeyComparer = StringComparer.Ordinal,
        AllowEmptyKeys = false,
        AllowEntriesBeforeFirstSection = false,
        MergeDuplicateSections = false,
    };

    private AcmeshdConfig(TimeSpan period, CertificateSpec[] certificates, EnvVar[] environment)
    {
        this.Period = period;
        this.Certificates = certificates;
        this.Environment = environment;
    }

    public TimeSpan Period { get; }

    public CertificateSpec[] Certificates { get; }

    public EnvVar[] Environment { get; }

    public static AcmeshdConfig Load(AcmeshdPaths paths)
    {
        var document = new IniReader(IniOptions).ParseFile(paths.ConfigPath);

        if (document.Global.Entries.Count != 0)
            throw new InvalidOperationException("global entries are not allowed in config.ini");

        var certCount = CountSections(document, SectionCert);

        if (certCount == 0)
            throw new InvalidOperationException("at least one [cert] section is required");

        var daemon = GetSingleSection(document, SectionDaemon, false);
        var env = GetSingleSection(document, SectionEnv, false);
        var period = daemon is null ? TimeSpan.FromDays(1) : ReadDaemon(daemon);
        var environment = env is null ? [] : ReadEnvironment(env);
        var certificates = new CertificateSpec[certCount];
        var certIndex = 0;

        for (var i = 0; i < document.Sections.Count; i++)
        {
            var section = document.Sections[i];

            if (section.Name == SectionCert)
            {
                certificates[certIndex] = ReadCertificate(section, paths);
                certIndex++;

                continue;
            }

            if (section.Name == SectionDaemon || section.Name == SectionEnv)
                continue;

            throw new InvalidOperationException("unknown section: " + section.Name);
        }

        return new AcmeshdConfig(period, certificates, environment);
    }

    private static int CountSections(IniDocument document, string name)
    {
        var count = 0;

        for (var i = 0; i < document.Sections.Count; i++)
        {
            if (document.Sections[i].Name == name)
                count++;
        }

        return count;
    }

    private static IniSection? GetSingleSection(IniDocument document, string name, bool required)
    {
        IniSection? result = null;

        for (var i = 0; i < document.Sections.Count; i++)
        {
            var section = document.Sections[i];

            if (section.Name != name)
                continue;

            if (result is not null)
                throw new InvalidOperationException("duplicate section is not allowed: " + name);

            result = section;
        }

        if (result is null && required)
            throw new InvalidOperationException("section is required: " + name);

        return result;
    }

    private static TimeSpan ReadDaemon(IniSection section)
    {
        var period = TimeSpan.FromDays(1);

        foreach (var entry in section.KeyValues)
        {
            if (entry.Key.Text != KeyPeriod)
                throw new InvalidOperationException("unknown [daemon] key: " + entry.Key.Text);

            var value = entry.Value.ToString();

            if (value == DefaultPeriod)
            {
                period = TimeSpan.FromDays(1);

                continue;
            }

            if (!entry.Value.TryGetInt64(out var seconds) || seconds <= 0)
                throw new InvalidOperationException("[daemon] period must be 1d or a positive number of seconds");

            period = TimeSpan.FromSeconds(seconds);
        }

        return period;
    }

    private static EnvVar[] ReadEnvironment(IniSection section)
    {
        var count = 0;

        foreach (var _ in section.KeyValues)
            count++;

        if (count == 0)
            return [];

        var result = new EnvVar[count];
        var index = 0;

        foreach (var entry in section.KeyValues)
        {
            var name = entry.Key.Text;

            for (var i = 0; i < index; i++)
            {
                if (result[i].Name == name)
                    throw new InvalidOperationException("duplicate [env] key: " + name);
            }

            result[index] = new EnvVar(name, entry.Value.ToString());
            index++;
        }

        return result;
    }

    private static CertificateSpec ReadCertificate(IniSection section, AcmeshdPaths paths)
    {
        var domainCount = CountKeys(section, KeyDomain);

        if (domainCount == 0)
            throw new InvalidOperationException("[cert] domain is required");

        var domains = new string[domainCount];
        var domainIndex = 0;
        string? email = null;
        var server = DefaultServer;
        string? modeText = null;
        string? dns = null;
        string? webroot = null;
        string? outPath = null;
        string? reload = null;
        string? keyLength = null;
        string? dnsSleep = null;

        foreach (var entry in section.KeyValues)
        {
            var key = entry.Key.Text;
            var value = entry.Value.ToString();

            if (key == KeyDomain)
            {
                RequireNotEmpty(value, "[cert] domain");
                domains[domainIndex] = value;
                domainIndex++;

                continue;
            }

            if (key == KeyEmail)
            {
                AssignOnce(ref email, value, "[cert] email");

                continue;
            }

            if (key == KeyServer)
            {
                RequireNotEmpty(value, "[cert] server");
                server = value;

                continue;
            }

            if (key == KeyMode)
            {
                AssignOnce(ref modeText, value, "[cert] mode");

                continue;
            }

            if (key == KeyDns)
            {
                AssignOnce(ref dns, value, "[cert] dns");

                continue;
            }

            if (key == KeyWebroot)
            {
                AssignOnce(ref webroot, value, "[cert] webroot");

                continue;
            }

            if (key == KeyOut)
            {
                AssignOnce(ref outPath, value, "[cert] out");

                continue;
            }

            if (key == KeyReload)
            {
                AssignOnce(ref reload, value, "[cert] reload");

                continue;
            }

            if (key == KeyKeyLength)
            {
                AssignOnce(ref keyLength, value, "[cert] keylength");

                continue;
            }

            if (key == KeyDnsSleep)
            {
                if (!entry.Value.TryGetInt64(out var seconds) || seconds <= 0)
                    throw new InvalidOperationException("[cert] dnssleep must be a positive number of seconds");

                AssignOnce(ref dnsSleep, value, "[cert] dnssleep");

                continue;
            }

            throw new InvalidOperationException("unknown [cert] key: " + key);
        }

        if (email is null)
            throw new InvalidOperationException("[cert] email is required");

        if (modeText is null)
            throw new InvalidOperationException("[cert] mode is required");

        var mode = ReadMode(modeText);

        if (mode == CertificateMode.Dns && dns is null)
            throw new InvalidOperationException("[cert] dns is required when mode=dns");

        if (mode != CertificateMode.Dns && dns is not null)
            throw new InvalidOperationException("[cert] dns is only allowed when mode=dns");

        if (mode == CertificateMode.Webroot && webroot is null)
            throw new InvalidOperationException("[cert] webroot is required when mode=webroot");

        if (mode != CertificateMode.Webroot && webroot is not null)
            throw new InvalidOperationException("[cert] webroot is only allowed when mode=webroot");

        var outputDirectory = ResolveOutputDirectory(paths, outPath, domains[0]);

        return new CertificateSpec(email, server, domains, mode, dns, webroot, outputDirectory, reload, keyLength, dnsSleep);
    }

    private static int CountKeys(IniSection section, string key)
    {
        var count = 0;

        foreach (var entry in section.KeyValues)
        {
            if (entry.Key.Text == key)
                count++;
        }

        return count;
    }

    private static CertificateMode ReadMode(string value) => value switch
    {
        "standalone" => CertificateMode.Standalone,
        "webroot" => CertificateMode.Webroot,
        "dns" => CertificateMode.Dns,
        _ => throw new InvalidOperationException("[cert] mode must be standalone, webroot or dns"),
    };

    private static string ResolveOutputDirectory(AcmeshdPaths paths, string? configured, string mainDomain)
    {
        if (configured is null)
            return Path.Combine(paths.BinaryDirectory, ResolveDefaultOutputName(mainDomain));

        return Path.IsPathRooted(configured)
            ? configured
            : Path.GetFullPath(Path.Combine(paths.ConfigDirectory, configured));
    }

    private static string ResolveDefaultOutputName(string domain)
    {
        var start = IsWildcardDomain(domain) ? 2 : 0;

        if (!TryReadDomainName(domain, start, out var name))
            throw new InvalidOperationException("[cert] domain must be a DNS name");

        return name;
    }

    private static bool IsWildcardDomain(string domain) =>
        domain.Length > 2 && domain[0] == WildcardLabel && domain[1] == DomainLabelSeparator;

    private static bool TryReadDomainName(string domain, int start, out string name)
    {
        var labelLength = 0;

        for (var i = start; i < domain.Length; i++)
        {
            if (domain[i] == DomainLabelSeparator)
            {
                if (labelLength == 0)
                {
                    name = string.Empty;

                    return false;
                }

                labelLength = 0;

                continue;
            }

            if (!IsDomainLabelCharacter(domain[i]))
            {
                name = string.Empty;

                return false;
            }

            labelLength++;
        }

        if (labelLength == 0)
        {
            name = string.Empty;

            return false;
        }

        name = domain[start..];

        return true;
    }

    private static bool IsDomainLabelCharacter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';

    private static void AssignOnce(ref string? target, string value, string name)
    {
        if (target is not null)
            throw new InvalidOperationException(name + " is duplicated");

        RequireNotEmpty(value, name);
        target = value;
    }

    private static void RequireNotEmpty(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException(name + " is empty");
    }
}

internal sealed class CertificateSpec
{
    public CertificateSpec(
        string email,
        string server,
        string[] domains,
        CertificateMode mode,
        string? dns,
        string? webroot,
        string outputDirectory,
        string? reload,
        string? keyLength,
        string? dnsSleep)
    {
        this.Email = email;
        this.Server = server;
        this.Domains = domains;
        this.Mode = mode;
        this.Dns = dns;
        this.Webroot = webroot;
        this.OutputDirectory = outputDirectory;
        this.Reload = reload;
        this.KeyLength = keyLength;
        this.DnsSleep = dnsSleep;
    }

    public string Email { get; }

    public string Server { get; }

    public string[] Domains { get; }

    public string MainDomain => this.Domains[0];

    public CertificateMode Mode { get; }

    public string? Dns { get; }

    public string? Webroot { get; }

    public string OutputDirectory { get; }

    public string? Reload { get; }

    public string? KeyLength { get; }

    public string? DnsSleep { get; }
}

internal enum CertificateMode
{
    Standalone,
    Webroot,
    Dns,
}

internal readonly record struct EnvVar(string Name, string? Value);
