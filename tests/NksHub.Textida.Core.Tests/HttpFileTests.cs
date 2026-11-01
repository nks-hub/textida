using System.Text;
using NksHub.Textida.Core.Http;

namespace NksHub.Textida.Core.Tests;

public class HttpFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "textida-http-" + Guid.NewGuid().ToString("N")[..8]);

    public HttpFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private const string File = """
        @host = https://api.example.test
        @token = {{apiToken}}

        # a comment
        # @name list
        GET {{host}}/items
            ?page=2
            &size=10 HTTP/1.1
        Accept: application/json
        Authorization: Bearer {{token}}

        ###

        // @name create
        POST {{host}}/items
        Content-Type: application/json

        {"name": "x"}

        > {% client.global.set("id", 1) %}
        ###
        https://other.example.test/ping
        """;

    [Fact]
    public void Requests_are_split_at_the_separators_with_method_url_headers_and_body()
    {
        var file = HttpRequestFile.Parse(File.Replace("\r\n", "\n"));
        Assert.Equal(3, file.Requests.Count);

        var list = file.Requests[0];
        Assert.Equal(("list", "GET", "{{host}}/items?page=2&size=10"), (list.Name, list.Method, list.Url));
        Assert.Equal([("Accept", "application/json"), ("Authorization", "Bearer {{token}}")], list.Headers);

        var create = file.Requests[1];
        Assert.Equal(("create", "POST", "{\"name\": \"x\"}"), (create.Name, create.Method, create.Body));
        Assert.Equal([("Content-Type", "application/json")], create.Headers); // the handler line after the body is not part of it and not run

        var ping = file.Requests[2];
        Assert.Equal(("GET", "https://other.example.test/ping"), (ping.Method, ping.Url)); // the method may be left out
        Assert.Equal("https://api.example.test", file.Variables["host"]);
    }

    [Fact]
    public void The_request_at_a_line_is_the_one_of_its_block()
    {
        var file = HttpRequestFile.Parse(File.Replace("\r\n", "\n"));
        Assert.Equal("list", file.At(4)!.Name);
        Assert.Equal("create", file.At(file.Requests[1].FirstLine + 3)!.Name);
        Assert.Equal("list", file.At(0)!.Name); // the lines above the first request belong to it
    }

    [Fact]
    public void Variables_come_from_the_file_the_environment_and_the_system_and_a_missing_one_is_reported()
    {
        var file = HttpRequestFile.Parse(File.Replace("\r\n", "\n"));
        var values = new Dictionary<string, string>(file.Variables) { ["apiToken"] = "secret-123" };
        var missing = new List<string>();
        var resolved = HttpVariables.Resolve(file.Requests[0], n => values.GetValueOrDefault(n), missing);
        Assert.Equal("https://api.example.test/items?page=2&size=10", resolved.Url);
        Assert.Equal("Bearer secret-123", resolved.Headers[1].Value); // a value that names another variable is expanded
        Assert.Empty(missing);

        values.Remove("apiToken");
        HttpVariables.Resolve(file.Requests[0], n => values.GetValueOrDefault(n), missing);
        Assert.Equal(["apiToken"], missing);

        string guid = HttpVariables.Expand("{{$guid}}", _ => null, []);
        Assert.True(Guid.TryParse(guid, out _));
        Assert.Matches(@"^\d{10}$", HttpVariables.Expand("{{$timestamp}}", _ => null, []));
    }

    [Fact]
    public void The_private_file_wins_and_its_values_are_known_as_secrets_so_they_can_be_masked()
    {
        System.IO.File.WriteAllText(Path.Combine(_folder, HttpEnvironments.SharedFileName), """{"$shared": {"host": "https://a.test"}, "dev": {"user": "ann", "password": "not-a-secret"}, "prod": {"user": "root"}}""", Encoding.UTF8);
        System.IO.File.WriteAllText(Path.Combine(_folder, HttpEnvironments.PrivateFileName), """{"dev": {"password": "hunter2", "key": 12345}}""", Encoding.UTF8);
        var environments = HttpEnvironments.Load(_folder);

        Assert.Equal(["dev", "prod"], environments.Names);
        var (values, secret) = environments.Values("dev");
        Assert.Equal(("https://a.test", "ann", "hunter2", "12345"), (values["host"], values["user"], values["password"], values["key"]));
        Assert.Equal(["key", "password"], secret.Order());
        Assert.Equal("login ann ***", HttpEnvironments.Mask("login ann hunter2", values.Where(v => secret.Contains(v.Key)).Select(v => v.Value)));
        Assert.DoesNotContain("hunter2", System.IO.File.ReadAllText(Path.Combine(_folder, HttpEnvironments.SharedFileName))); // the secret is only in the private file

        var (prod, prodSecret) = environments.Values("prod");
        Assert.Equal("root", prod["user"]);
        Assert.Empty(prodSecret);
        Assert.Empty(HttpEnvironments.Load(Path.Combine(_folder, "nowhere")).Names); // a folder without the files has no environments
    }

    [Fact]
    public void A_request_becomes_a_curl_command_and_back()
    {
        var request = new HttpRequest(null, "POST", "https://a.test/x?y=1", [("Content-Type", "application/json"), ("X-Note", "it's")], "{\"a\": \"it's\"}", 0, 0, 0);
        string curl = CurlCommand.From(request);
        Assert.StartsWith("curl -X POST 'https://a.test/x?y=1' \\\n", curl);
        Assert.Contains("-H 'X-Note: it'\\''s'", curl);

        var back = CurlCommand.Parse(curl)!;
        Assert.Equal(("POST", request.Url, request.Body), (back.Method, back.Url, back.Body));
        Assert.Equal(request.Headers, back.Headers);
    }

    [Fact]
    public void Curl_commands_in_the_forms_people_copy_are_understood()
    {
        var get = CurlCommand.Parse("curl -sSL --compressed https://a.test/ping")!;
        Assert.Equal(("GET", "https://a.test/ping"), (get.Method, get.Url));

        var post = CurlCommand.Parse("curl \"https://a.test/x\" -H \"Accept: text/plain\" -d 'a=1' -d 'b=2' -u ann:pw")!;
        Assert.Equal("POST", post.Method); // a body without -X is a POST
        Assert.Equal("a=1&b=2", post.Body);
        Assert.Contains(("Authorization", "Basic " + Convert.ToBase64String("ann:pw"u8.ToArray())), post.Headers);

        Assert.Equal("PUT", CurlCommand.Parse("curl -X PUT --url https://a.test/ -H 'A: b'")!.Method);
        Assert.Null(CurlCommand.Parse("wget https://a.test/"));
        Assert.Null(CurlCommand.Parse("curl -s"));
    }
}
