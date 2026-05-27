namespace Core.Models;

public record PatternInfo(
    string Id,
    string Name,
    string Icon,
    string ShortDescription,
    string DetailedDescription,
    string ScenarioTitle,
    string ScenarioDescription,
    string DefaultPrompt,
    string[] Pros,
    string[] Cons,
    string[] AgentsInvolved
);
