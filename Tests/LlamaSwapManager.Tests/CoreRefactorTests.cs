using System.Net;
using System.Text;
using LlamaSwapManager.Services;

namespace LlamaSwapManager.Tests;

public sealed class CoreRefactorTests
{
    [Fact]
    public void ProcessManager_MatchesOnlyExpectedExecutablePath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"process-match-{Guid.NewGuid():N}");
        var expected = Path.Combine(root, "llama-swap.exe");

        Assert.True(LlamaSwapProcessManager.IsExpectedExecutable(expected, expected));
        Assert.False(LlamaSwapProcessManager.IsExpectedExecutable(
            Path.Combine(root, "other", "llama-swap.exe"),
            expected));
        Assert.False(LlamaSwapProcessManager.IsExpectedExecutable(null, expected));
    }

    [Theory]
    [InlineData("Qwen3-30B-A3B-Q4_K_M.gguf", "Q4_K_M")]
    [InlineData("model-IQ4_XS.gguf", "IQ4_XS")]
    [InlineData("model-BF16.gguf", "BF16")]
    [InlineData("model.gguf", null)]
    public void HuggingFaceCatalog_ExtractsQuantization(
        string fileName,
        string? expected)
    {
        Assert.Equal(
            expected,
            HuggingFaceModelCatalog.ExtractQuantizationLabel(fileName));
    }

    [Fact]
    public async Task HuggingFaceCatalog_SearchesAndDeduplicatesRepositories()
    {
        using var http = new HttpClient(new StubHttpHandler((request, _) =>
        {
            Assert.Contains("filter=gguf", request.RequestUri?.Query);
            return Task.FromResult(JsonResponse(
                """
                [
                  {"modelId":"owner/model-a"},
                  {"modelId":"owner/model-a"},
                  {"modelId":"owner/model-b"},
                  {"missing":"ignored"}
                ]
                """));
        }));
        var catalog = new HuggingFaceModelCatalog(http);

        var repositories = await catalog.SearchRepositoriesAsync("qwen");

        Assert.Equal(new[] { "owner/model-a", "owner/model-b" }, repositories);
    }

    [Fact]
    public async Task HuggingFaceCatalog_FallsBackAndSortsGgufFiles()
    {
        var requests = new List<string>();
        using var http = new HttpClient(new StubHttpHandler((request, _) =>
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            requests.Add(uri);
            if (uri.Contains("recursive=1", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(JsonResponse(
                """
                [
                  {"path":"model-F16.gguf"},
                  {"path":"model-Q4_K_M.gguf"},
                  {"path":"notes.txt"},
                  {"path":"nested/model.gguf"}
                ]
                """));
        }));
        var catalog = new HuggingFaceModelCatalog(http);

        var files = await catalog.GetGgufFilesAsync("owner/model");

        Assert.Equal(2, requests.Count);
        Assert.Equal("F16", files[0].Quantization);
        Assert.Equal("Q4_K_M", files[1].Quantization);
        Assert.Null(files[2].Quantization);
        Assert.Equal("nested/model.gguf", files[2].SelectionToken);
    }

    [Fact]
    public void ProcessManager_ParsesWindowsNetstatForExpectedPidOnly()
    {
        const string output = """
          TCP    127.0.0.1:8080       0.0.0.0:0       LISTENING       1234
          TCP    127.0.0.1:9000       0.0.0.0:0       LISTENING       9999
          TCP    [::1]:5801           [::]:0          LISTENING       1234
        """;
        var ports = new HashSet<int>();

        LlamaSwapProcessManager.ParseWindowsNetstat(output, 1234, ports);

        Assert.Equal(new[] { 5801, 8080 }, ports.OrderBy(value => value));
    }

    [Fact]
    public void ProcessManager_ParsesLsofListeningPorts()
    {
        const string output = """
        COMMAND      PID USER   FD   TYPE DEVICE SIZE/OFF NODE NAME
        llama-swap  1234 user   10u  IPv4  0x1       0t0  TCP 127.0.0.1:8080 (LISTEN)
        llama-server 2345 user  11u  IPv6  0x2       0t0  TCP *:5801 (LISTEN)
        """;
        var ports = new HashSet<int>();

        LlamaSwapProcessManager.ParseLsof(output, ports);

        Assert.Equal(new[] { 5801, 8080 }, ports.OrderBy(value => value));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public StubHttpHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            _handler(request, cancellationToken);
    }
}
