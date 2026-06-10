using Core.Infrastructure.Mcp;
using Core.Models;

namespace Core.Interfaces;

/// <summary>
/// Abstraction over an MCP (Model Context Protocol) server that exposes
/// form-filling tools for the Assist platform's specialist domains.
///
/// The form fill is split into three steps so the HybridPatternRunner can pause
/// BETWEEN discovery and submission to ask the user for missing details
/// (a conversational, form-driven flow):
///
///   1. GetFormFieldsAsync  — discover the form's fields from the server
///   2. ExtractAnswersAsync — AI pre-fills what it can from the incident
///   3. SubmitFormAsync      — send the completed answers back to the server
///
/// Implementations:
///   LiveMcpFormClient    — connects to the Safety MCP server over stdio (registered).
///   DormantMcpFormClient — no-op; returns empty so the caller falls back to
///                          structured text extraction.
/// </summary>
public interface IMcpFormClient
{
    /// <summary>
    /// True when the client is actively connected to a live MCP server.
    /// The HybridPatternRunner uses this to decide whether to run the
    /// conversational MCP flow or fall back to text extraction.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Describes the tools this client expects the MCP server to expose.
    /// Use this as the spec when building the local MCP server.
    /// </summary>
    IReadOnlyList<McpToolDefinition> ExpectedTools { get; }

    /// <summary>
    /// Step 1 — Discovery. Connects to the MCP server and returns the form's fields
    /// (one entry per input, in page order). The runner uses these to know exactly
    /// which questions exist and which are required.
    ///
    /// Returns an empty list when the domain isn't MCP-backed or the connection fails;
    /// the caller should fall back to FormSchemaRegistry in that case.
    /// </summary>
    Task<IReadOnlyList<McpFormField>> GetFormFieldsAsync(
        string domain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 2 — Pre-fill. Uses the LLM to extract whatever values it can confidently
    /// pull from <paramref name="context"/> for the given fields. Whatever it can't
    /// fill is left out, so the runner knows exactly which required fields to ask the
    /// user about. Returns field-id → value pairs (only the ones it could fill).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ExtractAnswersAsync(
        string domain,
        IReadOnlyList<McpFormField> fields,
        string context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Step 3 — Submit. Sends the complete set of answers to the MCP server
    /// (start → update each page → complete) and returns the finished FilledForm
    /// for the UI. The form is always built from the answers, so the user's input
    /// is preserved even if the server round-trip fails.
    ///
    /// Returns null only when the domain isn't MCP-backed.
    /// </summary>
    Task<FilledForm?> SubmitFormAsync(
        string domain,
        IReadOnlyDictionary<string, string> answers,
        CancellationToken cancellationToken = default);
}
