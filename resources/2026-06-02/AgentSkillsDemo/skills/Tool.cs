using System.ComponentModel;
using System.Text.Json;

namespace AgentSkillsDemo.skills;

internal static class Tool
{
    [Description("Multiplies a value by a conversion factor and returns the result as JSON.")]
    internal static string Convert(double value, double factor)
    {
        var result = Math.Round(value * factor, 4);
        return JsonSerializer.Serialize(new { value, factor, result });
    }    
}