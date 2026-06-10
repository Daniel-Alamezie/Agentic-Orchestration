using Core.Infrastructure.Mcp;
using Core.Models;

namespace Core.Interfaces;

/// <summary>
/// Abstraction over an MCP (Model Context Protocol) server that exposes
/// form-filling tools for the Assist platform's specialist domains.
///
/// Forms are filled page by page over a single persistent session, because the
/// live forms are branching: a later page's questions depend on how earlier pages
/// were answered, so a page's content is only known once the previous page has been
/// submitted. The session keeps one MCP connection open across the whole
/// conversation (fetch page → ask the user → submit page → fetch the next page).
///
/// Implementations:
///   LiveMcpFormClient    — connects to the Safety MCP server over stdio (registered).
///   DormantMcpFormClient — no-op; returns null so the caller falls back to
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
    /// Opens a stateful, page-by-page form session over a single persistent connection
    /// and starts a new incident on the server. The caller drives it page by page,
    /// pausing to talk to the user between pages, then disposes it to close the connection.
    ///
    /// Returns null when the domain isn't MCP-backed or the connection fails;
    /// the caller should fall back to FormSchemaRegistry in that case.
    /// </summary>
    Task<IMcpFormSession?> BeginSessionAsync(
        string domain,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uses the LLM to extract whatever values it can confidently pull from
    /// <paramref name="context"/> for the given page's fields. Whatever it can't fill
    /// is left out, so the runner knows exactly which required fields to ask about.
    /// Returns field-id → value pairs (only the ones it could fill).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> ExtractAnswersAsync(
        string domain,
        IReadOnlyList<McpFormField> fields,
        string context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A single stateful form-filling session over one persistent MCP connection.
/// The connection stays open for the session's lifetime — including across the
/// pauses while the user answers questions — because each page is fetched from,
/// and submitted to, the same in-memory incident on the server.
///
/// Lifecycle:  for each page:  GetPageAsync → (ask the user) → SubmitPageAsync;
///             then CompleteAsync; then DisposeAsync (closes the connection).
/// </summary>
public interface IMcpFormSession : IAsyncDisposable
{
    /// <summary>The server-assigned incident id for this session.</summary>
    string IncidentId { get; }

    /// <summary>
    /// Fetches the fields for page <paramref name="pageNumber"/>. For branching forms
    /// the server shapes this page from the answers submitted on earlier pages.
    /// Returns null when the page does not exist — i.e. there are no more pages.
    /// </summary>
    Task<McpFormPage?> GetPageAsync(int pageNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits the answers for a page. The server validates required fields and,
    /// for branching forms, decides what the next page becomes. Returns true if accepted.
    /// </summary>
    Task<bool> SubmitPageAsync(
        int pageNumber,
        IReadOnlyDictionary<string, string> answers,
        CancellationToken cancellationToken = default);

    /// <summary>Finalises the incident on the server.</summary>
    Task CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the UI form from the pages served in this session and the gathered
    /// answers — so the form reflects exactly the pages the server actually presented.
    /// </summary>
    FilledForm BuildForm(IReadOnlyDictionary<string, string> answers);
}
