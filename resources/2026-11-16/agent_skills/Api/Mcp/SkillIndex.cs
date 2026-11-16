using System.Text.Json;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Server;

namespace Api.Mcp;

/// <summary>
/// Serves the <c>skill://index.json</c> discovery document for clients that discover skills by reading that
/// resource instead of calling <c>skills/list</c> (for example Microsoft.Agents.AI.Mcp 1.28.0).
/// Each entry is a <c>skill-md</c> skill whose <c>url</c> points at the SKILL.md already served by the Skills extension.
/// </summary>
public static class SkillIndex
{
    public const string Uri = "skill://index.json";

    public static McpServerResource CreateResource(IEnumerable<McpServerSkillPackage> packages)
    {
        var index = new
        {
            skills = packages
                .Select(p => p.Skill.ProtocolSkill!)
                .Select(s => new { name = s.Name, type = "skill-md", description = s.Description, url = s.Uri })
                .ToArray(),
        };
        string json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });

        return McpServerResource.Create(
            () => json,
            new McpServerResourceCreateOptions
            {
                UriTemplate = Uri,
                Name = "index.json",
                Description = "Agent Skills discovery index.",
                MimeType = "application/json",
            });
    }
}
