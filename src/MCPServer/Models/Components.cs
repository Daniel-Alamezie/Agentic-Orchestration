using System.Text.Json.Serialization;

namespace MCPServer.Models
{
    // Polymorphic serialisation: without this, a List<ComponentBase> only serialises
    // the base Type property and drops every derived-type field (FieldName, Options,
    // nested Components, etc.). These attributes tell System.Text.Json to emit each
    // concrete type's full shape, so get_page_components returns the real field tree.
    // The "$type" discriminator is used to avoid clashing with the existing Type property.
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
    [JsonDerivedType(typeof(PageComponent),    nameof(PageComponent))]
    [JsonDerivedType(typeof(TitleDisplay),     nameof(TitleDisplay))]
    [JsonDerivedType(typeof(TextDisplay),      nameof(TextDisplay))]
    [JsonDerivedType(typeof(CardContainer),    nameof(CardContainer))]
    [JsonDerivedType(typeof(SemanticBox),      nameof(SemanticBox))]
    [JsonDerivedType(typeof(Hyperlink),        nameof(Hyperlink))]
    [JsonDerivedType(typeof(DateTimePicker),   nameof(DateTimePicker))]
    [JsonDerivedType(typeof(TextInput),        nameof(TextInput))]
    [JsonDerivedType(typeof(TextAreaInput),    nameof(TextAreaInput))]
    [JsonDerivedType(typeof(RadioButtonGroup), nameof(RadioButtonGroup))]
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
