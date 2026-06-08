using MCPServer.Models;
using MCPServer.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace MCPServer.Tools
{
    [McpServerToolType]
    public static class SafetyIncidentTools
    {
        [McpServerTool, Description("Starts a new safety incident report session. Returns the incident ID and the first page of questions.")]
        public static Task<SafetyIncident> StartNewIncident(
            SafetyIncidentService service)
        {
            var incident = service.StartNewIncident();
            return Task.FromResult(incident);
        }

        [McpServerTool, Description("Gets the current status and page for an incident.")]
        public static Task<SafetyIncident?> GetIncident(
            SafetyIncidentService service,
            [Description("The incident ID to retrieve.")] string incidentId)
        {
            var incident = service.GetIncident(incidentId);
            return Task.FromResult(incident);
        }

        [McpServerTool, Description("Gets the page components and questions for a specific page number.")]
        public static Task<PageComponent?> GetPageComponents(
            SafetyIncidentService service,
            [Description("The page number (1-based).")] int pageNumber)
        {
            var page = service.GetPageComponents(pageNumber);
            return Task.FromResult(page);
        }

        [McpServerTool, Description("Updates an incident page with answers to the questions. All required fields must be provided.")]
        public static Task<bool> UpdateIncidentPage(
            SafetyIncidentService service,
            [Description("The incident ID to update.")] string incidentId,
            [Description("The page number being updated.")] int pageNumber,
            [Description("Dictionary of field names and their answers.")] Dictionary<string, string> answers)
        {
            var result = service.UpdateIncidentPage(incidentId, pageNumber, answers);
            return Task.FromResult(result);
        }

        [McpServerTool, Description("Marks an incident as completed.")]
        public static Task<bool> CompleteIncident(
            SafetyIncidentService service,
            [Description("The incident ID to complete.")] string incidentId)
        {
            var result = service.CompleteIncident(incidentId);
            return Task.FromResult(result);
        }

        [McpServerTool, Description("Cancels an incident.")]
        public static Task<bool> CancelIncident(
            SafetyIncidentService service,
            [Description("The incident ID to cancel.")] string incidentId)
        {
            var result = service.CancelIncident(incidentId);
            return Task.FromResult(result);
        }

        [McpServerTool, Description("Gets all incidents in the system.")]
        public static Task<List<SafetyIncident>> GetAllIncidents(
            SafetyIncidentService service)
        {
            var incidents = service.GetAllIncidents();
            return Task.FromResult(incidents);
        }

        [McpServerTool, Description("Gets the total number of pages in the incident report form.")]
        public static Task<int> GetTotalPages(
            SafetyIncidentService service)
        {
            var totalPages = service.GetTotalPages();
            return Task.FromResult(totalPages);
        }
    }
}
