using System;
using System.Collections.Generic;
using System.Text;

namespace MCPServer.Models
{
    public class SafetyIncident
    {
        public string IncidentId { get; set; } = Guid.NewGuid().ToString();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
        public IncidentStatus Status { get; set; } = IncidentStatus.InProgress;
        public int CurrentPage { get; set; } = 1;

        // Page 1 Questions
        public string? IncidentDate { get; set; }
        public string? IncidentLocation { get; set; }
        public string? IncidentType { get; set; }
        public string? SeverityLevel { get; set; }

        // Page 2 Questions
        public string? PersonsInvolved { get; set; }
        public string? InjuriesReported { get; set; }
        public string? IncidentDescription { get; set; }
        public string? ImmediateActionsTaken { get; set; }
    }

    public enum IncidentStatus
    {
        InProgress,
        Completed,
        Cancelled
    }
}
