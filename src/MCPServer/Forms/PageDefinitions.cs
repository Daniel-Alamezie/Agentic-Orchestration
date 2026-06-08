using MCPServer.Models;

namespace MCPServer.Forms
{
    public static class PageDefinitions
    {
        public static Dictionary<int, PageComponent> GetPageDefinitions()
        {
            return new Dictionary<int, PageComponent>
            {
                { 1, GetPage1() },
                { 2, GetPage2() }
            };
        }

        private static PageComponent GetPage1()
        {
            var page = new PageComponent
            {
                Type = "Page",
                Components = new List<ComponentBase>
                {
                    Component<TitleDisplay>(title =>
                    {
                        title.Value = "Incident Basic Information";
                    }),
                    Component<CardContainer>(card =>
                    {
                        card.Components =
                        [
                            Component<SemanticBox>(box =>
                            {
                                box.Style = "information";
                                box.Components =
                                [
                                    Component<TextDisplay>(text =>
                                    {
                                        text.Value = "Basic Information";
                                    }),
                                    Component<TextDisplay>(text =>
                                    {
                                        text.Value = "Step 1 of 2";
                                    }),
                                    Component<TextDisplay>(text =>
                                    {
                                        text.Value = "Please provide the basic details about the safety incident.";
                                    })
                                ];
                            }),
                            Component<DateTimePicker>(picker =>
                            {
                                picker.FieldName = "IncidentDate";
                                picker.Label = "When did the incident occur?";
                                picker.Placeholder = "YYYY-MM-DD";
                                picker.PickerType = "date";
                                picker.CalendarType = "presentPast";
                                picker.DefaultValue = string.Empty;
                                picker.Validation = new ValidationRule
                                {
                                    Required = true,
                                    Message = "Incident date is required"
                                };
                            }),
                            Component<TextInput>(input =>
                            {
                                input.FieldName = "IncidentLocation";
                                input.Label = "Where did the incident occur?";
                                input.Placeholder = "Building name, room number, or specific location";
                                input.Validation = new ValidationRule
                                {
                                    Required = true,
                                    Message = "Incident location is required"
                                };
                            }),
                            Component<RadioButtonGroup>(radio =>
                            {
                                radio.FieldName = "IncidentType";
                                radio.Label = "What type of incident was it?";
                                radio.Options =
                                [
                                    "Slip/Trip/Fall",
                                    "Equipment Failure",
                                    "Chemical Spill",
                                    "Fire/Explosion",
                                    "Electrical",
                                    "Vehicle Accident",
                                    "Other"
                                ];
                                radio.Validation = new ValidationRule
                                {
                                    Required = true,
                                    Message = "Incident type is required"
                                };
                            }),
                            Component<RadioButtonGroup>(radio =>
                            {
                                radio.FieldName = "SeverityLevel";
                                radio.Label = "What is the severity level of the incident?";
                                radio.Options =
                                [
                                    "Minor (No injury, minimal damage)",
                                    "Moderate (First aid required, some damage)",
                                    "Serious (Medical attention required, significant damage)",
                                    "Critical (Hospitalization, major damage or loss)"
                                ];
                                radio.Validation = new ValidationRule
                                {
                                    Required = true,
                                    Message = "Severity level is required"
                                };
                            })
                        ];
                    })
                }
            };

            return page;
        }

        private static PageComponent GetPage2()
        {
            var page = new PageComponent
            {
                Type = "Page",
                Components = new List<ComponentBase>
                {
                    Component<TitleDisplay>(title =>
                    {
                        title.Value = "Incident Details and Actions";
                    }),
                    Component<CardContainer>(card =>
                    {
                        card.Components =
                        [
                            Component<SemanticBox>(box =>
                            {
                                box.Style = "information";
                                box.Components =
                                [
                                    Component<TextDisplay>(text =>
                                    {
                                        text.Value = "Detailed Information";
                                    }),
                                    Component<TextDisplay>(text =>
                                    {
                                        text.Value = "Step 2 of 2";
                                    }),
                                    Component<TextDisplay>(text =>
                                    {
                                        text.Value = "Please provide detailed information about the incident and actions taken.";
                                    })
                                ];
                            }),
                            Component<TextAreaInput>(textarea =>
                            {
                                textarea.FieldName = "PersonsInvolved";
                                textarea.Label = "Who was involved in or witnessed the incident?";
                                textarea.Placeholder = "List names and roles of all persons involved or who witnessed the incident";
                                textarea.Rows = 4;
                                textarea.Validation = new ValidationRule
                                {
                                    Required = true,
                                    Message = "Persons involved information is required"
                                };
                            }),
                            Component<TextAreaInput>(textarea =>
                            {
                                textarea.FieldName = "InjuriesReported";
                                textarea.Label = "Were there any injuries reported? If yes, please describe.";
                                textarea.Placeholder = "Describe any injuries, or write 'None' if no injuries occurred";
                                textarea.Rows = 4;
                                textarea.Validation = new ValidationRule
                                {
                                    Required = true,
                                    Message = "Injury information is required"
                                };
                            }),
                            Component<TextAreaInput>(textarea =>
                            {
                                textarea.FieldName = "IncidentDescription";
                                textarea.Label = "Please provide a detailed description of what happened.";
                                textarea.Placeholder = "Provide a comprehensive description of the incident, including sequence of events";
                                textarea.Rows = 6;
                                textarea.Validation = new ValidationRule
                                {
                                    Required = true,
                                    Message = "Incident description is required"
                                };
                            }),
                            Component<TextAreaInput>(textarea =>
                            {
                                textarea.FieldName = "ImmediateActionsTaken";
                                textarea.Label = "What immediate actions were taken following the incident?";
                                textarea.Placeholder = "Describe emergency response, first aid, notifications, or other immediate actions";
                                textarea.Rows = 4;
                                textarea.Validation = new ValidationRule
                                {
                                    Required = true,
                                    Message = "Immediate actions information is required"
                                };
                            })
                        ];
                    })
                }
            };

            return page;
        }

        private static T Component<T>(Action<T> configure) where T : ComponentBase, new()
        {
            var component = new T { Type = typeof(T).Name };
            configure(component);
            return component;
        }
    }
}
