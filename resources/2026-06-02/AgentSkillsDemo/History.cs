using Microsoft.Extensions.AI;

namespace AgentSkillsDemo;

/// <summary>
/// Helpers to inspect the in-memory chat history of a session, highlighting the
/// messages that carry loaded skill content (so we can see what is retained).
/// </summary>
internal static class History
{
    private static readonly HashSet<string> SkillTools = new(StringComparer.Ordinal)
    {
        "load_skill",
        "read_skill_resource",
        "run_skill_script",
    };

    /// <summary>
    /// True when the message contains a call to one of the built-in skill tools.
    /// Enough to detect "a skill was triggered" without correlating result call ids.
    /// </summary>
    public static bool MentionsSkillTool(ChatMessage message) =>
        message.Contents.OfType<FunctionCallContent>().Any(call => SkillTools.Contains(call.Name));

    /// <summary>
    /// True when the message is a skill tool call, or the tool result of one.
    /// Result messages only carry a <see cref="FunctionResultContent.CallId"/>, so the
    /// caller must supply the set of skill call ids to recognise them.
    /// </summary>
    public static bool IsSkillMessage(ChatMessage message, IReadOnlySet<string> skillCallIds) =>
        message.Contents.Any(c =>
            (c is FunctionCallContent call && SkillTools.Contains(call.Name)) ||
            (c is FunctionResultContent result && result.CallId is { } id && skillCallIds.Contains(id)));

    /// <summary>
    /// Collects the call ids of every skill tool call in the history.
    /// </summary>
    public static IReadOnlySet<string> SkillCallIds(IEnumerable<ChatMessage> messages)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            foreach (var call in message.Contents.OfType<FunctionCallContent>())
            {
                if (SkillTools.Contains(call.Name) && call.CallId is { } id)
                {
                    ids.Add(id);
                }
            }
        }
        return ids;
    }

    /// <summary>
    /// Prints a compact, one-line-per-message listing of the history. Messages that
    /// carry skill content are tagged so the retained <c>SKILL.md</c> body is obvious.
    /// </summary>
    public static void Dump(IEnumerable<ChatMessage> messages, string title)
    {
        var list = messages.ToList();
        var skillCallIds = SkillCallIds(list);

        var skillChars = 0;
        Console.WriteLine();
        Console.WriteLine($"===== {title}: {list.Count} messages =====");
        for (var i = 0; i < list.Count; i++)
        {
            var message = list[i];
            var isSkill = IsSkillMessage(message, skillCallIds);
            var length = ContentLength(message);
            if (isSkill)
            {
                skillChars += length;
            }

            var tag = isSkill ? "[SKILL]" : "       ";
            Console.WriteLine($"  [{i,2}] {tag} {message.Role.Value,-9} len={length,5}  {Describe(message)}");
        }
        Console.WriteLine($"----- retained skill content: ~{skillChars} chars across {list.Count(m => IsSkillMessage(m, skillCallIds))} messages -----");
    }

    private static string Describe(ChatMessage message)
    {
        var calls = message.Contents.OfType<FunctionCallContent>().Select(c => c.Name).ToList();
        if (calls.Count > 0)
        {
            return "call -> " + string.Join(", ", calls);
        }

        if (message.Contents.OfType<FunctionResultContent>().Any())
        {
            return "tool result";
        }

        return Truncate(message.Text, 60);
    }

    private static int ContentLength(ChatMessage message)
    {
        var length = 0;
        foreach (var content in message.Contents)
        {
            length += content switch
            {
                TextContent text => text.Text?.Length ?? 0,
                FunctionResultContent result => result.Result?.ToString()?.Length ?? 0,
                FunctionCallContent call => call.Name.Length +
                    (call.Arguments is null ? 0 : string.Concat(call.Arguments.Select(kv => $"{kv.Key}{kv.Value}")).Length),
                _ => 0,
            };
        }
        return length;
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var single = value.ReplaceLineEndings(" ").Trim();
        return single.Length <= max ? single : single[..max] + "…";
    }
}
