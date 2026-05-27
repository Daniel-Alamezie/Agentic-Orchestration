using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;

namespace Core.Infrastructure;

/// <summary>
/// Creates pre-configured Semantic Kernel instances backed by Ollama.
/// Ollama exposes an OpenAI-compatible API at http://localhost:11434/v1
/// so we use SK's OpenAI connector with a custom base URL.
/// </summary>
public static class KernelFactory
{
    public static Kernel Create(IConfiguration config)
    {
        var ollamaBaseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434";
        var modelId       = config["Ollama:ModelId"]  ?? "llama3.2";

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"{ollamaBaseUrl.TrimEnd('/')}/v1/"),
            Timeout = TimeSpan.FromMinutes(5)
        };

        return Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(
                modelId:    modelId,
                apiKey:     "ollama",   // required by SDK but ignored by Ollama
                httpClient: httpClient)
            .Build();
    }
}
