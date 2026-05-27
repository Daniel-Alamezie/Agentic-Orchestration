using Core.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace Core.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a shared Kernel instance (Ollama-backed) into DI.
    /// </summary>
    public static IServiceCollection AddOllamaKernel(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register the kernel as a singleton – all agents share the same LLM connection.
        services.AddSingleton<Kernel>(_ => KernelFactory.Create(configuration));
        return services;
    }
}
