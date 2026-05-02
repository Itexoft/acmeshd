using Itexoft.IO.FileSystem;

namespace Acmeshd;

internal static class AcmeshdTemplate
{
    private const string Text = """
                               # acmeshd config
                               # If no file is passed as the first argument, acmeshd looks for config.ini next to the binary.
                               # acme.sh is downloaded to .acmeshd/acme.sh next to the binary and gets chmod +x on each cycle.
                               # For mode=dns, the required hook is downloaded to .acmeshd/dnsapi/ and gets chmod +x on each cycle.
                               # acme.sh decides whether each certificate must be issued or skipped on each cycle.
                               # dns_nsupdate also provisions default BIND9 files when they do not exist.
                               # Logs are written to stdout.
                               #
                               # A relative out value in [cert] is resolved from the config.ini directory.
                               # If out is omitted, certificates are stored next to the binary in a directory named by the main domain.
                               # For domain=*.itexoft.com, the default directory is itexoft.com.
                               # After a successful install, cert.pfx is created next to the PEM files without a password.
                               
                               [daemon]
                               # Optional. Supported default value: 1d.
                               period=1d
                               
                               [cert]
                               # Required. ACME account email.
                               email=admin@itexoft.com
                               
                               # Optional. Default: letsencrypt.
                               server=letsencrypt
                               
                               # Required. The first domain is the primary certificate name.
                               domain=itexoft.com
                               domain=*.itexoft.com
                               
                               # Required. standalone, webroot or dns.
                               mode=dns
                               
                               # Required only for mode=dns.
                               dns=dns_nsupdate
                               
                               # Required only for mode=webroot.
                               # webroot=/var/www/itexoft.com
                               
                               # Optional. Default: a directory next to the binary named by the main domain.
                               out=certs/itexoft.com
                               
                               # Optional. Command after a successful certificate install or renewal.
                               reload=systemctl reload nginx
                               
                               [env]
                               # Optional. Environment variables for acme.sh DNS hooks.
                               # ACME_OPENSSL_BIN can be set when openssl is not available through PATH.
                               # dns_nsupdate defaults:
                               # NSUPDATE_SERVER=127.0.0.1
                               # NSUPDATE_SERVER_PORT=53
                               # NSUPDATE_KEY=/etc/bind/keys/acme.key
                               # NSUPDATE_ZONE=itexoft.com
                               """;

    public static void Create(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("unable to resolve config directory");

        IFileSystem.Sys.CreateDirectory(directory);

        using var stream = IFileSystem.Sys.OpenString(path, SysFileMode.Overwrite);
        stream.WriteAllText(Text);
        stream.Flush();
    }
}
