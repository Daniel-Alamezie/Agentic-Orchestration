using MCPServer.Forms;
using MCPServer.Models;
using System.Collections.Concurrent;

namespace MCPServer.Services
{
    public class SafetyIncidentService
    {
        private readonly ConcurrentDictionary<string, SafetyIncident> _incidents = new();
        private readonly Dictionary<int, PageComponent> _componentPageDefinitions;

        public SafetyIncidentService()
        {
            _componentPageDefinitions = PageDefinitions.GetPageDefinitions();
        }

        public SafetyIncident StartNewIncident()
        {
            var incident = new SafetyIncident();
            _incidents[incident.IncidentId] = incident;
            return incident;
        }

        public SafetyIncident? GetIncident(string incidentId)
        {
            _incidents.TryGetValue(incidentId, out var incident);
            return incident;
        }

        public PageComponent? GetPageComponents(int pageNumber)
        {
            _componentPageDefinitions.TryGetValue(pageNumber, out var page);
            return page;
        }

        public bool UpdateIncidentPage(string incidentId, int pageNumber, Dictionary<string, string> answers)
        {
            if (!_incidents.TryGetValue(incidentId, out var incident))
                return false;

            if (!_componentPageDefinitions.TryGetValue(pageNumber, out var page))
                return false;

            // Validate all required questions are answered
            var requiredFields = GetRequiredFieldsFromComponents(page);
            foreach (var field in requiredFields)
            {
                if (!answers.ContainsKey(field) || string.IsNullOrWhiteSpace(answers[field]))
                    return false;
            }

            // Update incident with answers using reflection
            var incidentType = typeof(SafetyIncident);
            foreach (var answer in answers)
            {
                var property = incidentType.GetProperty(answer.Key);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(incident, answer.Value);
                }
            }

            incident.CurrentPage = pageNumber;
            incident.UpdatedAt = DateTime.UtcNow;

            return true;
        }

        private List<string> GetRequiredFieldsFromComponents(PageComponent page)
        {
            var requiredFields = new List<string>();
            ExtractRequiredFields(page.Components, requiredFields);
            return requiredFields;
        }

        private void ExtractRequiredFields(List<ComponentBase> components, List<string> requiredFields)
        {
            foreach (var component in components)
            {
                switch (component)
                {
                    case DateTimePicker picker when picker.Validation?.Required == true:
                        requiredFields.Add(picker.FieldName);
                        break;
                    case TextInput input when input.Validation?.Required == true:
                        requiredFields.Add(input.FieldName);
                        break;
                    case TextAreaInput textarea when textarea.Validation?.Required == true:
                        requiredFields.Add(textarea.FieldName);
                        break;
                    case RadioButtonGroup radio when radio.Validation?.Required == true:
                        requiredFields.Add(radio.FieldName);
                        break;
                    case CardContainer card:
                        ExtractRequiredFields(card.Components, requiredFields);
                        break;
                    case SemanticBox box:
                        ExtractRequiredFields(box.Components, requiredFields);
                        break;
                }
            }
        }

        public bool CompleteIncident(string incidentId)
        {
            if (!_incidents.TryGetValue(incidentId, out var incident))
                return false;

            incident.Status = IncidentStatus.Completed;
            incident.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        public bool CancelIncident(string incidentId)
        {
            if (!_incidents.TryGetValue(incidentId, out var incident))
                return false;

            incident.Status = IncidentStatus.Cancelled;
            incident.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        public List<SafetyIncident> GetAllIncidents()
        {
            return _incidents.Values.ToList();
        }

        public int GetTotalPages()
        {
            return _componentPageDefinitions.Count;
        }
    }
}
