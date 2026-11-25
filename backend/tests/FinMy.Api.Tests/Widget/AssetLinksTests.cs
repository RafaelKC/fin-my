using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinMy.Api.Tests.Support;
using FinMy.Application.Tests.Infrastructure;

namespace FinMy.Api.Tests.Widget;

/// <summary>009 T001 / FR-002: <c>GET /.well-known/assetlinks.json</c> sai da configuração (<c>Android__PackageName</c>, <c>Android__CertSha256__N</c>).</summary>
public class AssetLinksTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    private const string Path = "/.well-known/assetlinks.json";
    private const string Fingerprint1 = "14:6D:E9:83:C5:73:06:50:D8:EE:B9:95:2F:34:FC:64:16:A0:83:42:E6:1D:BE:A8:8A:04:96:B2:3F:CF:44:E5";
    private const string Fingerprint2 = "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99";

    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?>
    {
        ["Android:PackageName"] = "br.app.finmy",
        ["Android:CertSha256:0"] = Fingerprint1,
        ["Android:CertSha256:1"] = Fingerprint2,
    };

    [Fact]
    public async Task Publica_o_pacote_e_as_impressoes_digitais_configuradas_sem_login()
    {
        var response = await Client.GetAsync(Path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var statement = body.EnumerateArray().ShouldHaveSingleItem();
        statement.GetProperty("relation").EnumerateArray().Select(e => e.GetString()).ShouldBe(["delegate_permission/common.handle_all_urls"]);
        var target = statement.GetProperty("target");
        target.GetProperty("namespace").GetString().ShouldBe("android_app");
        target.GetProperty("package_name").GetString().ShouldBe("br.app.finmy");
        target.GetProperty("sha256_cert_fingerprints").EnumerateArray().Select(e => e.GetString()).ShouldBe([Fingerprint1, Fingerprint2]);
    }
}

/// <summary>Sem <c>Android__PackageName</c> ou sem impressões digitais, o arquivo não existe (404).</summary>
public class AssetLinksNotConfiguredTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    [Fact]
    public async Task Sem_configuracao_responde_404()
    {
        (await Client.GetAsync("/.well-known/assetlinks.json")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

/// <summary>Só o pacote, sem impressão digital: o Chrome não verificaria nada; 404 também.</summary>
public class AssetLinksPartialConfigTests(PostgresFixture postgres) : ApiTestBase(postgres)
{
    protected override IReadOnlyDictionary<string, string?>? Settings => new Dictionary<string, string?> { ["Android:PackageName"] = "br.app.finmy" };

    [Fact]
    public async Task So_com_o_pacote_responde_404()
    {
        (await Client.GetAsync("/.well-known/assetlinks.json")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
