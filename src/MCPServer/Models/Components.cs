namespace MCPServer.Models
{
    public class ComponentBase
    {
        public string Type { get; set; } = string.Empty;
    }

    public class PageComponent : ComponentBase
    {
        public List<ComponentBase> Components { get; set; } = new();
    }

    public class TitleDisplay : ComponentBase
    {
        public string Value { get; set; } = string.Empty;
    }

    public class TextDisplay : ComponentBase
    {
        public string Value { get; set; } = string.Empty;
    }

    public class CardContainer : ComponentBase
    {
        public List<ComponentBase> Components { get; set; } = new();
    }

    public class SemanticBox : ComponentBase
    {
        public string Style { get; set; } = string.Empty;
        public List<ComponentBase> Components { get; set; } = new();
    }

    public class Hyperlink : ComponentBase
    {
        public string BeginTextValue { get; set; } = string.Empty;
        public string EndTextValue { get; set; } = string.Empty;
        public string HyperLinkValue { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
    }

    public class DateTimePicker : ComponentBase
    {
        public string FieldName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Placeholder { get; set; } = string.Empty;
        public string PickerType { get; set; } = string.Empty;
        public string CalendarType { get; set; } = string.Empty;
        public string DefaultValue { get; set; } = string.Empty;
        public ValidationRule? Validation { get; set; }
    }

    public class TextInput : ComponentBase
    {
        public string FieldName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Placeholder { get; set; } = string.Empty;
        public ValidationRule? Validation { get; set; }
    }

    public class TextAreaInput : ComponentBase
    {
        public string FieldName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Placeholder { get; set; } = string.Empty;
        public int Rows { get; set; } = 4;
        public ValidationRule? Validation { get; set; }
    }

    public class RadioButtonGroup : ComponentBase
    {
        public string FieldName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new();
        public ValidationRule? Validation { get; set; }
    }

    public class ValidationRule
    {
        public bool Required { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
