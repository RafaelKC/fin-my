using System.Text.RegularExpressions;

namespace FinMy.Architecture.Tests;

/// <summary>Cabeçalhos de segurança de <c>frontend/nginx.conf</c> (T029).</summary>
public partial class NginxSecurityHeadersTests
{
    private static readonly string Conf = File.ReadAllText(Path.Combine(SourceTree.BackendRoot, "..", "frontend", "nginx.conf"));

    private static string? Header(string name)
    {
        var match = Regex.Match(Conf, $@"add_header\s+{Regex.Escape(name)}\s+""([^""]+)""\s+always;");
        return match.Success ? match.Groups[1].Value : null;
    }

    [Fact]
    public void Csp_restringe_tudo_a_mesma_origem_sem_script_inline_nem_eval()
    {
        var csp = Header("Content-Security-Policy").ShouldNotBeNull();

        csp.ShouldContain("default-src 'self'");
        csp.ShouldContain("script-src 'self'");
        csp.ShouldContain("object-src 'none'");
        csp.ShouldContain("frame-ancestors 'none'");
        csp.ShouldContain("base-uri 'self'");
        var scriptSrc = ScriptSrcRegex().Match(csp).Groups[1].Value;
        scriptSrc.ShouldNotContain("unsafe-inline");
        scriptSrc.ShouldNotContain("unsafe-eval");
        csp.ShouldNotContain("http://");
        csp.ShouldNotContain("https://");
    }

    [Fact]
    public void Define_HSTS_nosniff_referrer_policy_e_frame_options()
    {
        Header("Strict-Transport-Security").ShouldNotBeNull().ShouldMatch(@"^max-age=\d{8,}");
        Header("X-Content-Type-Options").ShouldBe("nosniff");
        Header("Referrer-Policy").ShouldBe("strict-origin-when-cross-origin");
        Header("X-Frame-Options").ShouldBe("DENY");
    }

    [Fact]
    public void Nenhum_location_repete_add_header_pois_anularia_os_do_server()
    {
        var locations = string.Join('\n', LocationSplitRegex().Split(Conf).Skip(1));

        locations.ShouldNotContain("add_header");
    }

    [GeneratedRegex(@"script-src([^;]*)")]
    private static partial Regex ScriptSrcRegex();

    [GeneratedRegex(@"\n\s*location\s")]
    private static partial Regex LocationSplitRegex();
}
