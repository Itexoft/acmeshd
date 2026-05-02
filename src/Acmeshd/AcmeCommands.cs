namespace Acmeshd;

internal static class AcmeCommands
{
    private const string CertFileName = "cert.pem";
    private const string KeyFileName = "key.pem";
    private const string CaFileName = "ca.pem";
    private const string FullchainFileName = "fullchain.pem";
    private const string PfxFileName = "cert.pfx";

    public static string[] RegisterAccount(AcmeshdPaths paths, CertificateSpec spec) =>
    [
        "--register-account",
        "--home", paths.AcmeHomePath,
        "--config-home", paths.AcmeConfigPath,
        "--server", spec.Server,
        "-m", spec.Email,
    ];

    public static string[] Issue(AcmeshdPaths paths, CertificateSpec spec)
    {
        var count = 7 + spec.Domains.Length * 2 + ModeArgumentCount(spec) + OptionalArgumentCount(spec);
        var result = new string[count];
        var index = 0;

        result[index++] = "--issue";
        result[index++] = "--home";
        result[index++] = paths.AcmeHomePath;
        result[index++] = "--config-home";
        result[index++] = paths.AcmeConfigPath;
        result[index++] = "--server";
        result[index++] = spec.Server;

        for (var i = 0; i < spec.Domains.Length; i++)
        {
            result[index++] = "-d";
            result[index++] = spec.Domains[i];
        }

        if (spec.Mode == CertificateMode.Standalone)
            result[index++] = "--standalone";
        else if (spec.Mode == CertificateMode.Webroot)
        {
            result[index++] = "-w";
            result[index++] = spec.Webroot!;
        }
        else
        {
            result[index++] = "--dns";
            result[index++] = spec.Dns!;
        }

        AppendOptional(ref index, result, "--keylength", spec.KeyLength);
        AppendOptional(ref index, result, "--dnssleep", spec.DnsSleep);

        return result;
    }

    public static string[] Install(AcmeshdPaths paths, CertificateSpec spec)
    {
        var count = 15 + (spec.Reload is null ? 0 : 2);
        var result = new string[count];
        var index = 0;

        result[index++] = "--install-cert";
        result[index++] = "--home";
        result[index++] = paths.AcmeHomePath;
        result[index++] = "--config-home";
        result[index++] = paths.AcmeConfigPath;
        result[index++] = "-d";
        result[index++] = spec.MainDomain;
        result[index++] = "--cert-file";
        result[index++] = CertPath(spec);
        result[index++] = "--key-file";
        result[index++] = KeyPath(spec);
        result[index++] = "--ca-file";
        result[index++] = CaPath(spec);
        result[index++] = "--fullchain-file";
        result[index++] = FullchainPath(spec);

        AppendOptional(ref index, result, "--reloadcmd", spec.Reload);

        return result;
    }

    public static string[] Cron(AcmeshdPaths paths) =>
    [
        "--cron",
        "--home", paths.AcmeHomePath,
        "--config-home", paths.AcmeConfigPath,
    ];

    public static string FullchainPath(CertificateSpec spec) => Path.Combine(spec.OutputDirectory, FullchainFileName);

    public static string[] ExportPfx(CertificateSpec spec) =>
    [
        "pkcs12",
        "-export",
        "-out", PfxPath(spec),
        "-inkey", KeyPath(spec),
        "-in", CertPath(spec),
        "-certfile", CaPath(spec),
        "-passout", "pass:",
    ];

    private static string PfxPath(CertificateSpec spec) => Path.Combine(spec.OutputDirectory, PfxFileName);

    private static string CertPath(CertificateSpec spec) => Path.Combine(spec.OutputDirectory, CertFileName);

    private static string KeyPath(CertificateSpec spec) => Path.Combine(spec.OutputDirectory, KeyFileName);

    private static string CaPath(CertificateSpec spec) => Path.Combine(spec.OutputDirectory, CaFileName);

    private static int ModeArgumentCount(CertificateSpec spec) => spec.Mode == CertificateMode.Standalone ? 1 : 2;

    private static int OptionalArgumentCount(CertificateSpec spec)
    {
        var count = 0;

        if (spec.KeyLength is not null)
            count += 2;

        if (spec.DnsSleep is not null)
            count += 2;

        return count;
    }

    private static void AppendOptional(ref int index, string[] target, string name, string? value)
    {
        if (value is null)
            return;

        target[index++] = name;
        target[index++] = value;
    }
}
