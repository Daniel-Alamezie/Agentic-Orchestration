using System;
using System.Collections.Generic;
using System.Text;

namespace MCPServer.Models
{
    public class IncidentQuestion
    {
        public string QuestionId { get; set; } = string.Empty;
        public string QuestionText { get; set; } = string.Empty;
        public string FieldName { get; set; } = string.Empty;
        public QuestionType Type { get; set; }
        public bool Required { get; set; }
        public List<string>? Options { get; set; }
        public string? Placeholder { get; set; }
        public string? ValidationRule { get; set; }
    }

    public enum QuestionType
    {
        Text,
        TextArea,
        Date,
        SingleChoice,
        MultipleChoice,
        Number
    }

    public class PageQuestions
    {
        public int PageNumber { get; set; }
        public string PageTitle { get; set; } = string.Empty;
        public string PageDescription { get; set; } = string.Empty;
        public List<IncidentQuestion> Questions { get; set; } = new();
        public bool IsLastPage { get; set; }
    }
}
