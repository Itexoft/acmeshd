using System.Text;
using Itexoft.IO.FileSystem;
using Itexoft.Threading;

namespace Acmeshd;

internal static class DnsProvisioning
{
    private const string DnsNsupdate = "dns_nsupdate";
    private const string EnvServer = "NSUPDATE_SERVER";
    private const string EnvServerPort = "NSUPDATE_SERVER_PORT";
    private const string EnvKey = "NSUPDATE_KEY";
    private const string EnvZone = "NSUPDATE_ZONE";
    private const string EnvKeyName = "NSUPDATE_KEY_NAME";
    private const string EnvZoneFile = "NSUPDATE_ZONE_FILE";
    private const string DefaultServer = "127.0.0.1";
    private const string DefaultServerPort = "53";
    private const string DefaultKeyName = "acme";
    private const string DefaultKeyPath = "/etc/bind/keys/acme.key";
    private const string DefaultZoneDirectory = "/var/lib/bind";
    private const string NamedLocalConfigPath = "/etc/bind/named.conf.local";
    private const string AcmeshdBindConfigPath = "/etc/bind/named.conf.acmeshd";
    private const string LegacyBindConfigPrefix = "/etc/bind/named.conf.acme-";
    private const string BindDirectoryPrefix = "/etc/bind/";
    private const string BindIncludeKeyword = "include";
    private const string ShellPath = "/bin/sh";
    private const string ChmodPath = "/bin/chmod";
    private const string ChownPath = "/bin/chown";
    private const string NamedCheckConfPath = "named-checkconf";
    private const string NamedCheckZonePath = "named-checkzone";
    private const string SystemctlPath = "systemctl";
    private const string BindServiceName = "named.service";
    private const char WildcardLabel = '*';
    private const char DomainLabelSeparator = '.';
    private const string ChallengePrefix = "_acme-challenge.";

    public static EnvVar[] Prepare(
        CertificateSpec[] certificates,
        EnvVar[] environment,
        ProcessRunner runner,
        CancelToken cancelToken)
    {
        if (!HasNsupdateCertificate(certificates))
            return environment;

        var zone = ResolveNsupdateZone(certificates, environment);
        var keyPath = GetEnvironmentValueOrDefault(environment, EnvKey, DefaultKeyPath);
        var keyName = GetEnvironmentValueOrDefault(environment, EnvKeyName, DefaultKeyName);
        var zoneFile = GetEnvironmentValueOrDefault(environment, EnvZoneFile, DefaultZoneFilePath(zone));
        var result = EnsureNsupdateEnvironment(environment, zone, keyPath);

        ProvisionNsupdate(certificates, zone, keyPath, keyName, zoneFile, runner, cancelToken);

        return result;
    }

    private static bool HasNsupdateCertificate(CertificateSpec[] certificates)
    {
        for (var i = 0; i < certificates.Length; i++)
        {
            if (certificates[i].Mode == CertificateMode.Dns && certificates[i].Dns == DnsNsupdate)
                return true;
        }

        return false;
    }

    private static string ResolveNsupdateZone(CertificateSpec[] certificates, EnvVar[] environment)
    {
        if (TryGetEnvironmentValue(environment, EnvZone, out var configured))
            return RequireEnvironmentValue(EnvZone, configured);

        string? zone = null;

        for (var i = 0; i < certificates.Length; i++)
        {
            var spec = certificates[i];

            if (spec.Mode != CertificateMode.Dns || spec.Dns != DnsNsupdate)
                continue;

            var resolved = ResolveEffectiveDomain(spec.MainDomain);

            if (zone is null)
            {
                zone = resolved;

                continue;
            }

            if (zone != resolved)
                throw new InvalidOperationException("[env] " + EnvZone + " is required when dns_nsupdate certificates use different zones");
        }

        if (zone is null)
            throw new InvalidOperationException("dns_nsupdate certificate is required");

        return zone;
    }

    private static EnvVar[] EnsureNsupdateEnvironment(EnvVar[] environment, string zone, string keyPath)
    {
        var missing = 0;

        if (!HasEnvironmentValue(environment, EnvServer))
            missing++;

        if (!HasEnvironmentValue(environment, EnvServerPort))
            missing++;

        if (!HasEnvironmentValue(environment, EnvKey))
            missing++;

        if (!HasEnvironmentValue(environment, EnvZone))
            missing++;

        if (missing == 0)
            return environment;

        var result = new EnvVar[environment.Length + missing];
        var index = 0;

        for (; index < environment.Length; index++)
            result[index] = environment[index];

        AddMissingEnvironment(environment, result, ref index, EnvServer, DefaultServer);
        AddMissingEnvironment(environment, result, ref index, EnvServerPort, DefaultServerPort);
        AddMissingEnvironment(environment, result, ref index, EnvKey, keyPath);
        AddMissingEnvironment(environment, result, ref index, EnvZone, zone);

        return result;
    }

    private static void AddMissingEnvironment(EnvVar[] source, EnvVar[] target, ref int index, string name, string value)
    {
        if (HasEnvironmentValue(source, name))
            return;

        target[index] = new EnvVar(name, value);
        index++;
    }

    private static void ProvisionNsupdate(
        CertificateSpec[] certificates,
        string zone,
        string keyPath,
        string keyName,
        string zoneFile,
        ProcessRunner runner,
        CancelToken cancelToken)
    {
        Console.WriteLine("acmeshd: provision dns_nsupdate");
        ValidateZoneName(zone);
        RequireSafeFilePath(keyPath, EnvKey);
        RequireSafeFilePath(zoneFile, EnvZoneFile);
        RequireKeyName(keyName);
        EnsureBindTools(runner, cancelToken);

        EnsureKey(keyPath, keyName, runner, cancelToken);
        EnsureZoneFile(zone, zoneFile, runner, cancelToken);
        EnsureBindConfig(certificates, zone, keyPath, keyName, zoneFile, cancelToken);
        EnsureNamedLocalInclude(cancelToken);
        EnsureLegacyBindConfigTarget(zone, cancelToken);

        runner.Run("named-checkzone " + zone, NamedCheckZonePath, [zone, zoneFile], null, [], cancelToken);
        runner.Run("named-checkconf", NamedCheckConfPath, [], null, [], cancelToken);
        runner.Run("enable bind service", SystemctlPath, ["enable", BindServiceName], null, [], cancelToken);
        runner.Run("start bind service", SystemctlPath, ["start", BindServiceName], null, [], cancelToken);
        runner.Run("reload bind service", SystemctlPath, ["reload", BindServiceName], null, [], cancelToken);
    }

    private static void EnsureBindTools(ProcessRunner runner, CancelToken cancelToken) =>
        runner.Run(
            "install bind9 tools",
            ShellPath,
            [
                "-c",
                """
                if command -v nsupdate >/dev/null 2>&1 && command -v named-checkconf >/dev/null 2>&1 && command -v named-checkzone >/dev/null 2>&1 && command -v tsig-keygen >/dev/null 2>&1; then
                    exit 0
                fi
                apt-get update
                apt-get install -y bind9 bind9-dnsutils bind9utils
                """,
            ],
            null,
            [],
            cancelToken);

    private static bool EnsureKey(string keyPath, string keyName, ProcessRunner runner, CancelToken cancelToken)
    {
        if (IFileSystem.Sys.FileExists(keyPath))
            return false;

        var directory = Path.GetDirectoryName(Path.GetFullPath(keyPath));

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("unable to resolve " + EnvKey + " directory");

        Console.WriteLine("acmeshd: create " + keyPath);
        IFileSystem.Sys.CreateDirectory(directory);
        runner.Run(
            "create nsupdate key",
            ShellPath,
            ["-c", "umask 027 && tsig-keygen -a hmac-sha256 \"$1\" > \"$2\"", "tsig-keygen", keyName, keyPath],
            null,
            [],
            cancelToken);
        runner.Run("chmod nsupdate key", ChmodPath, ["640", keyPath], null, [], cancelToken);
        runner.Run("chown nsupdate key", ChownPath, ["root:bind", keyPath], null, [], cancelToken);

        return true;
    }

    private static bool EnsureZoneFile(string zone, string zoneFile, ProcessRunner runner, CancelToken cancelToken)
    {
        if (IFileSystem.Sys.FileExists(zoneFile))
            return false;

        var directory = Path.GetDirectoryName(Path.GetFullPath(zoneFile));

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("unable to resolve " + EnvZoneFile + " directory");

        Console.WriteLine("acmeshd: create " + zoneFile);
        IFileSystem.Sys.CreateDirectory(directory);

        using (var stream = IFileSystem.Sys.OpenString(zoneFile, SysFileMode.Overwrite))
            stream.WriteAllText(CreateZoneFile(zone), cancelToken);

        runner.Run("chown bind zone", ChownPath, ["bind:bind", zoneFile], null, [], cancelToken);
        runner.Run("chmod bind zone", ChmodPath, ["640", zoneFile], null, [], cancelToken);

        return true;
    }

    private static bool EnsureBindConfig(
        CertificateSpec[] certificates,
        string zone,
        string keyPath,
        string keyName,
        string zoneFile,
        CancelToken cancelToken)
    {
        if (IFileSystem.Sys.FileExists(AcmeshdBindConfigPath))
            return false;

        Console.WriteLine("acmeshd: create " + AcmeshdBindConfigPath);

        using var stream = IFileSystem.Sys.OpenString(AcmeshdBindConfigPath, SysFileMode.Overwrite);
        stream.WriteAllText(CreateBindConfig(certificates, zone, keyPath, keyName, zoneFile), cancelToken);

        return true;
    }

    private static bool EnsureNamedLocalInclude(CancelToken cancelToken)
    {
        var includeLine = "include \"" + AcmeshdBindConfigPath + "\";";

        if (!IFileSystem.Sys.FileExists(NamedLocalConfigPath))
        {
            Console.WriteLine("acmeshd: create " + NamedLocalConfigPath);

            using var created = IFileSystem.Sys.OpenString(NamedLocalConfigPath, SysFileMode.Overwrite);
            created.WriteAllText(includeLine + Environment.NewLine, cancelToken);

            return true;
        }

        using (var stream = IFileSystem.Sys.OpenString(NamedLocalConfigPath, SysFileMode.Read))
        {
            if (HasExactLine(stream.ReadAllText(cancelToken), includeLine))
                return false;
        }

        Console.WriteLine("acmeshd: append " + NamedLocalConfigPath);
        File.AppendAllText(NamedLocalConfigPath, Environment.NewLine + includeLine + Environment.NewLine);

        return true;
    }

    private static bool EnsureLegacyBindConfigTarget(string zone, CancelToken cancelToken)
    {
        var legacyPath = LegacyBindConfigPrefix + zone;

        if (IFileSystem.Sys.FileExists(legacyPath))
            return EnsureLegacyBindConfigIncludes(legacyPath, cancelToken);

        Console.WriteLine("acmeshd: create " + legacyPath);

        using var created = IFileSystem.Sys.OpenString(legacyPath, SysFileMode.Overwrite);
        created.WriteAllText(string.Empty, cancelToken);

        return true;
    }

    private static bool EnsureLegacyBindConfigIncludes(string path, CancelToken cancelToken)
    {
        using var stream = IFileSystem.Sys.OpenString(path, SysFileMode.Read);
        var text = stream.ReadAllText(cancelToken);
        var changed = false;
        var start = 0;

        while (start <= text.Length)
        {
            var end = start;

            while (end < text.Length && text[end] != '\n')
                end++;

            var lineEnd = end > start && text[end - 1] == '\r' ? end - 1 : end;

            if (TryReadBindIncludeTarget(text, start, lineEnd, out var target)
                && IsSafeBindPath(target)
                && !IFileSystem.Sys.FileExists(target))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(target));

                if (!string.IsNullOrWhiteSpace(directory) && IFileSystem.Sys.DirectoryExists(directory))
                {
                    Console.WriteLine("acmeshd: create " + target);

                    using var created = IFileSystem.Sys.OpenString(target, SysFileMode.Overwrite);
                    created.WriteAllText(string.Empty, cancelToken);
                    changed = true;
                }
            }

            if (end == text.Length)
                break;

            start = end + 1;
        }

        return changed;
    }

    private static bool TryReadBindIncludeTarget(string text, int start, int end, out string target)
    {
        var index = start;

        SkipWhitespace(text, ref index, end);

        if (!TryReadKeyword(text, ref index, end, BindIncludeKeyword))
        {
            target = string.Empty;

            return false;
        }

        if (!TryReadRequiredWhitespace(text, ref index, end))
        {
            target = string.Empty;

            return false;
        }

        if (index >= end || text[index] != '"')
        {
            target = string.Empty;

            return false;
        }

        index++;
        var targetStart = index;

        while (index < end && text[index] != '"')
            index++;

        if (index == end || index == targetStart)
        {
            target = string.Empty;

            return false;
        }

        target = text[targetStart..index];
        index++;

        SkipWhitespace(text, ref index, end);

        if (index >= end || text[index] != ';')
        {
            target = string.Empty;

            return false;
        }

        index++;
        SkipWhitespace(text, ref index, end);

        return index == end;
    }

    private static void SkipWhitespace(string text, ref int index, int end)
    {
        while (index < end && (text[index] == ' ' || text[index] == '\t'))
            index++;
    }

    private static bool TryReadRequiredWhitespace(string text, ref int index, int end)
    {
        var start = index;
        SkipWhitespace(text, ref index, end);

        return index > start;
    }

    private static bool TryReadKeyword(string text, ref int index, int end, string keyword)
    {
        if (end - index < keyword.Length)
            return false;

        for (var i = 0; i < keyword.Length; i++)
        {
            if (text[index + i] != keyword[i])
                return false;
        }

        index += keyword.Length;

        return true;
    }

    private static bool IsSafeBindPath(string path)
    {
        if (!Path.IsPathRooted(path))
            return false;

        var fullPath = Path.GetFullPath(path);

        if (fullPath.Length <= BindDirectoryPrefix.Length)
            return false;

        for (var i = 0; i < BindDirectoryPrefix.Length; i++)
        {
            if (fullPath[i] != BindDirectoryPrefix[i])
                return false;
        }

        return true;
    }

    private static string CreateZoneFile(string zone) =>
        "$TTL 300" + Environment.NewLine
        + "@ IN SOA ns1." + zone + ". admin." + zone + ". (" + Environment.NewLine
        + "    1" + Environment.NewLine
        + "    300" + Environment.NewLine
        + "    300" + Environment.NewLine
        + "    1209600" + Environment.NewLine
        + "    300 )" + Environment.NewLine
        + "@ IN NS ns1." + zone + "." + Environment.NewLine
        + "ns1 IN A 127.0.0.1" + Environment.NewLine;

    private static string CreateBindConfig(
        CertificateSpec[] certificates,
        string zone,
        string keyPath,
        string keyName,
        string zoneFile)
    {
        var builder = new StringBuilder();
        builder.Append("include \"").Append(keyPath).Append("\";").AppendLine();
        builder.AppendLine();
        builder.Append("zone \"").Append(zone).AppendLine("\" {");
        builder.AppendLine("    type primary;");
        builder.Append("    file \"").Append(zoneFile).AppendLine("\";");
        builder.AppendLine("    update-policy {");

        for (var i = 0; i < certificates.Length; i++)
        {
            var spec = certificates[i];

            if (spec.Mode != CertificateMode.Dns || spec.Dns != DnsNsupdate)
                continue;

            for (var domainIndex = 0; domainIndex < spec.Domains.Length; domainIndex++)
            {
                var challenge = ResolveChallengeName(spec.Domains[domainIndex]);

                if (HasPreviousChallenge(certificates, i, domainIndex, challenge))
                    continue;

                builder.Append("        grant ").Append(keyName).Append(" name ").Append(challenge).AppendLine(". txt;");
            }
        }

        builder.AppendLine("    };");
        builder.AppendLine("};");

        return builder.ToString();
    }

    private static bool HasPreviousChallenge(CertificateSpec[] certificates, int certIndex, int domainIndex, string challenge)
    {
        for (var i = 0; i <= certIndex; i++)
        {
            var spec = certificates[i];

            if (spec.Mode != CertificateMode.Dns || spec.Dns != DnsNsupdate)
                continue;

            var end = i == certIndex ? domainIndex : spec.Domains.Length;

            for (var j = 0; j < end; j++)
            {
                if (ResolveChallengeName(spec.Domains[j]) == challenge)
                    return true;
            }
        }

        return false;
    }

    private static string ResolveChallengeName(string domain) =>
        ChallengePrefix + ResolveEffectiveDomain(domain);

    private static string ResolveEffectiveDomain(string domain)
    {
        ValidateDomainName(domain);

        if (domain.Length > 2 && domain[0] == WildcardLabel && domain[1] == DomainLabelSeparator)
            return domain[2..];

        return domain;
    }

    private static string DefaultZoneFilePath(string zone) =>
        Path.Combine(DefaultZoneDirectory, zone + ".zone");

    private static string GetEnvironmentValueOrDefault(EnvVar[] environment, string name, string defaultValue) =>
        TryGetEnvironmentValue(environment, name, out var value)
            ? RequireEnvironmentValue(name, value)
            : defaultValue;

    private static bool HasEnvironmentValue(EnvVar[] environment, string name) =>
        TryGetEnvironmentValue(environment, name, out _);

    private static bool TryGetEnvironmentValue(EnvVar[] environment, string name, out string? value)
    {
        for (var i = 0; i < environment.Length; i++)
        {
            if (environment[i].Name == name)
            {
                value = environment[i].Value;

                return true;
            }
        }

        value = null;

        return false;
    }

    private static string RequireEnvironmentValue(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("[env] " + name + " is empty");

        return value;
    }

    private static void RequireSafeFilePath(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("[env] " + name + " is empty");

        if (!Path.IsPathRooted(path))
            throw new InvalidOperationException("[env] " + name + " must be an absolute path");
    }

    private static void RequireKeyName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("[env] " + EnvKeyName + " is empty");

        for (var i = 0; i < value.Length; i++)
        {
            if (!IsDnsLabelCharacter(value[i]))
                throw new InvalidOperationException("[env] " + EnvKeyName + " must contain only ASCII letters, digits or dash");
        }
    }

    private static void ValidateDomainName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("[cert] domain is empty");

        var start = value.Length > 2 && value[0] == WildcardLabel && value[1] == DomainLabelSeparator ? 2 : 0;

        ValidateDomainName(value, start, "[cert] domain");
    }

    private static void ValidateZoneName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("[env] " + EnvZone + " is empty");

        ValidateDomainName(value, 0, "[env] " + EnvZone);
    }

    private static void ValidateDomainName(string value, int start, string name)
    {
        var labelLength = 0;

        for (var i = start; i < value.Length; i++)
        {
            if (value[i] == DomainLabelSeparator)
            {
                if (labelLength == 0)
                    throw new InvalidOperationException(name + " contains an empty DNS label");

                labelLength = 0;

                continue;
            }

            if (!IsDnsLabelCharacter(value[i]))
                throw new InvalidOperationException(name + " contains an invalid DNS label character");

            labelLength++;
        }

        if (labelLength == 0)
            throw new InvalidOperationException(name + " contains an empty DNS label");
    }

    private static bool IsDnsLabelCharacter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-';

    private static bool HasExactLine(string text, string line)
    {
        var start = 0;

        while (start <= text.Length)
        {
            var end = start;

            while (end < text.Length && text[end] != '\n')
                end++;

            var lineEnd = end > start && text[end - 1] == '\r' ? end - 1 : end;

            if (lineEnd - start == line.Length)
            {
                var equals = true;

                for (var i = 0; i < line.Length; i++)
                {
                    if (text[start + i] != line[i])
                    {
                        equals = false;

                        break;
                    }
                }

                if (equals)
                    return true;
            }

            if (end == text.Length)
                break;

            start = end + 1;
        }

        return false;
    }
}
