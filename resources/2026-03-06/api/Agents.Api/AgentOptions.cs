using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Agents.Api;

public record WeatherForecastResult
{
    public string Date { get; init; } = "";
    
    public string Condition { get; init; } = "";
    
    public int TemperatureHigh { get; init; }
    
    public int TemperatureLow { get; init; }
    
    public int Humidity { get; init; }
    
    public int WindSpeed { get; init; }
    
    public int PrecipitationChance { get; init; }

    public string Description { get; init; } = "";
}

public record LocationCoordinates
{
    public double Latitude { get; init; }
    
    public double Longitude { get; init; }
    
    public string FullName { get; init; } = "";

    public bool Found { get; init; }
}

public record CurrentDateInfo
{
    public string CurrentDate { get; init; } = "";
    
    public string CurrentTime { get; init; } = "";
    
    public string DayOfWeek { get; init; } = "";
    
    public string FormattedDate { get; init; } = "";
    
    public string Timezone { get; init; } = "";
    
    public string UtcOffset { get; init; } = "";
}

public record ParsedDateResult
{
    public bool Success { get; init; }
    
    public string?  Date { get; init; }
    
    public string? EndDate { get; init; } // For ranges like "this weekend"
    
    public string?  FormattedDate { get; init; }
    
    public string?  DayOfWeek { get; init; }
    
    public int DaysFromToday { get; init; }
    
    public string? Interpretation { get; init; }
    
    public bool IsPastDate { get; init; }
    
    public bool IsForecastReliable { get; init; }
    
    public string?  Warning { get; init; }
    
    public string? OriginalExpression { get; init; }
    
    public string? Error { get; init; }
}

internal sealed class AgentOptions
{
    private readonly AIFunction[] _tools;
    private readonly ILogger _logger;
    
    public AgentOptions(ILogger<AgentOptions> logger)
    {
        _tools = [
            AIFunctionFactory.Create(GetCurrentDate),
            AIFunctionFactory.Create(ParseRelativeDate),
            AIFunctionFactory.Create(GetWeatherForecast),
            AIFunctionFactory.Create(GetLocationCoordinates)
        ];
        _logger = logger;
    }
    
    public ChatClientAgentOptions CreateAgentOptions(string? agentId, string modelId) => new()
        {
            Id = agentId,
            Name = "WeatherAgent",
            Description = "A helpful weather forecast assistant",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                               You are a helpful weather forecast assistant.  You can provide weather forecasts 
                               for any location and date. When users ask about weather: 

                               1. Use the get_location_coordinates tool to get the coordinates for the location
                               2. Use the get_weather_forecast tool to get the weather forecast
                               3. Present the weather information in a friendly, conversational manner

                               Always include temperature, conditions, and any relevant weather warnings.
                               If the user asks about a date more than 7 days in the future, let them know 
                               that forecasts beyond 7 days are less reliable.
                               """,
                Temperature = 0,
                Tools = [.._tools],
                ModelId = modelId
            }
        };
    
    // Get current date and time information
    [Description("Get the current date, time, and timezone information.  Use this to understand the current temporal context.")]
    public CurrentDateInfo GetCurrentDate()
    {
        var now = DateTime.Now;
        _logger.LogInformation("GetCurrentDate {Now}", now);
        return new CurrentDateInfo
        {
            CurrentDate = now.ToString("yyyy-MM-dd"),
            CurrentTime = now.ToString("HH:mm:ss"),
            DayOfWeek = now.DayOfWeek.ToString(),
            FormattedDate = now.ToString("dddd, MMMM d, yyyy"),
            Timezone = TimeZoneInfo. Local.DisplayName,
            UtcOffset = TimeZoneInfo.Local.GetUtcOffset(now).ToString()
        };
    }
    
    // Parse relative date expressions to actual dates
    [Description("Parse a relative date expression (like 'today', 'tomorrow', 'next Monday', 'this weekend', 'in 3 days') into an actual date.  Returns the parsed date in YYYY-MM-DD format.")]
    public ParsedDateResult ParseRelativeDate(
        [Description("The relative date expression to parse (e.g., 'today', 'tomorrow', 'next Friday', 'this weekend', 'in 5 days')")]
        string dateExpression)
    {
        _logger.LogInformation("ParseRelativeDate {Expression}", dateExpression);
        
        var today = DateTime.Today;
        var expression = dateExpression.Trim().ToLowerInvariant();

        DateTime? parsedDate = null;
        string? endDate = null;
        var interpretation = "";

        // Handle common relative expressions
        if (expression == "today")
        {
            parsedDate = today;
            interpretation = "today";
        }
        else if (expression == "tomorrow")
        {
            parsedDate = today.AddDays(1);
            interpretation = "tomorrow";
        }
        else if (expression == "yesterday")
        {
            parsedDate = today.AddDays(-1);
            interpretation = "yesterday (past date - cannot forecast)";
        }
        else if (expression is "this weekend" or "weekend")
        {
            // Find next Saturday
            var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)today.DayOfWeek + 7) % 7;
            if (daysUntilSaturday == 0 && today.DayOfWeek == DayOfWeek.Saturday)
                daysUntilSaturday = 0;
            else if (today.DayOfWeek == DayOfWeek.Sunday)
                daysUntilSaturday = 6; // Next Saturday

            parsedDate = today.AddDays(daysUntilSaturday);
            endDate = today.AddDays(daysUntilSaturday + 1).ToString("yyyy-MM-dd"); // Sunday
            interpretation = "this weekend (Saturday and Sunday)";
        }
        else if (expression == "next week")
        {
            var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
            if (daysUntilMonday == 0) daysUntilMonday = 7;
            parsedDate = today.AddDays(daysUntilMonday);
            endDate = parsedDate.Value.AddDays(6).ToString("yyyy-MM-dd");
            interpretation = "next week (Monday through Sunday)";
        }
        else if (expression.StartsWith("in ") && expression.EndsWith(" days"))
        {
            var daysStr = expression.Replace("in ", "").Replace(" days", "").Trim();
            if (int.TryParse(daysStr, out int days))
            {
                parsedDate = today.AddDays(days);
                interpretation = $"in {days} days";
            }
        }
        else if (expression.StartsWith("in ") && expression.EndsWith(" day"))
        {
            parsedDate = today.AddDays(1);
            interpretation = "in 1 day (tomorrow)";
        }
        else if (expression.StartsWith("next "))
        {
            // Handle "next Monday", "next Tuesday", etc.
            var dayName = expression.Replace("next ", "").Trim();
            if (Enum.TryParse<DayOfWeek>(dayName, true, out var targetDay))
            {
                var daysUntil = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;
                if (daysUntil == 0) daysUntil = 7; // If today is that day, go to next week
                parsedDate = today.AddDays(daysUntil);
                interpretation = $"next {targetDay}";
            }
        }
        else if (expression.StartsWith("this "))
        {
            // Handle "this Monday", "this Friday", etc.
            var dayName = expression.Replace("this ", "").Trim();
            if (Enum.TryParse<DayOfWeek>(dayName, true, out var targetDay))
            {
                var daysUntil = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;
                parsedDate = today.AddDays(daysUntil);
                interpretation = $"this {targetDay}";
            }
        }
        else
        {
            // Try to parse as an absolute date
            if (DateTime.TryParse(expression, out var absoluteDate))
            {
                parsedDate = absoluteDate;
                interpretation = "absolute date";
            }
            // Try common formats
            else if (DateTime.TryParseExact(expression,
                         ["MM/dd", "M/d", "MMMM d", "MMM d"],
                         CultureInfo.InvariantCulture,
                         DateTimeStyles.None,
                         out absoluteDate))
            {
                // Assume current year, but if date is in the past, assume next year
                parsedDate = new DateTime(today.Year, absoluteDate.Month, absoluteDate.Day);
                if (parsedDate < today)
                    parsedDate = parsedDate.Value.AddYears(1);
                interpretation = "parsed date";
            }
        }

        if (parsedDate.HasValue)
        {
            var daysFromToday = (parsedDate.Value - today).Days;
            var isPast = daysFromToday < 0;
            var isReliable = daysFromToday is <= 7 and >= 0;

            return new ParsedDateResult
            {
                Success = true,
                Date = parsedDate.Value.ToString("yyyy-MM-dd"),
                EndDate = endDate,
                FormattedDate = parsedDate.Value.ToString("dddd, MMMM d, yyyy"),
                DayOfWeek = parsedDate.Value.DayOfWeek.ToString(),
                DaysFromToday = daysFromToday,
                Interpretation = interpretation,
                IsPastDate = isPast,
                IsForecastReliable = isReliable,
                Warning = isPast ? "This is a past date.  Weather forecasts are only available for today and future dates." :
                    !isReliable ? "This date is more than 7 days away. Forecast accuracy decreases for dates beyond 7 days." :
                    null
            };
        }

        return new ParsedDateResult
        {
            Success = false,
            OriginalExpression = dateExpression,
            Error =
                $"Could not parse date expression:  '{dateExpression}'.  Try expressions like 'today', 'tomorrow', 'next Monday', 'this weekend', or a specific date like '2024-01-15'."
        };
    }

    // Weather forecast tool
    [Description("Get weather forecast for a specific location and date")]
    public WeatherForecastResult GetWeatherForecast(
        [Description("Latitude of the location")] double latitude,
        [Description("Longitude of the location")] double longitude,
        [Description("Date for the forecast (YYYY-MM-DD format)")] string date)
    {
        _logger.LogInformation("GetWeatherForecast {Latitude} {Longitude} {Date}", latitude, longitude, date);
        
        // Simulated weather data - in production, call a real weather API
        var random = new Random((int)(latitude * 1000 + longitude * 100 + date.GetHashCode()));
    
        var conditions = new[] { "Sunny", "Partly Cloudy", "Cloudy", "Light Rain", "Heavy Rain", "Thunderstorms", "Snow", "Foggy" };
        var condition = conditions[random. Next(conditions.Length)];
    
        var tempHigh = random.Next(50, 95);
        var tempLow = tempHigh - random.Next(10, 25);
        var humidity = random.Next(30, 90);
        var windSpeed = random. Next(5, 30);
        var precipitation = condition. Contains("Rain") || condition.Contains("Snow") ? random.Next(20, 100) : random.Next(0, 20);

        return new WeatherForecastResult
        {
            Date = date,
            Condition = condition,
            TemperatureHigh = tempHigh,
            TemperatureLow = tempLow,
            Humidity = humidity,
            WindSpeed = windSpeed,
            PrecipitationChance = precipitation,
            Description = $"Expect {condition. ToLower()} conditions with a high of {tempHigh}°F and a low of {tempLow}°F.  " +
                          $"Humidity around {humidity}% with winds at {windSpeed} mph."
        };
    }
    
    // Location coordinates tool
    [Description("Get the latitude and longitude coordinates for a given location name")]
    public LocationCoordinates GetLocationCoordinates([Description("Name of the city or location")] string location)
    {
        _logger.LogInformation("GetLocationCoordinates {Location}", location);

        // Simulated geocoding - in production, use a real geocoding service
        var locations = new Dictionary<string, (double lat, double lon, string fullName)>(StringComparer.OrdinalIgnoreCase)
        {
            ["new york"] = (40.7128, -74.0060, "New York City, NY, USA"),
            ["los angeles"] = (34.0522, -118.2437, "Los Angeles, CA, USA"),
            ["chicago"] = (41.8781, -87.6298, "Chicago, IL, USA"),
            ["houston"] = (29.7604, -95.3698, "Houston, TX, USA"),
            ["phoenix"] = (33.4484, -112.0740, "Phoenix, AZ, USA"),
            ["seattle"] = (47.6062, -122.3321, "Seattle, WA, USA"),
            ["san francisco"] = (37.7749, -122.4194, "San Francisco, CA, USA"),
            ["miami"] = (25.7617, -80.1918, "Miami, FL, USA"),
            ["boston"] = (42.3601, -71.0589, "Boston, MA, USA"),
            ["denver"] = (39.7392, -104.9903, "Denver, CO, USA"),
            ["london"] = (51.5074, -0.1278, "London, UK"),
            ["paris"] = (48.8566, 2.3522, "Paris, France"),
            ["tokyo"] = (35.6762, 139.6503, "Tokyo, Japan"),
            ["sydney"] = (-33.8688, 151.2093, "Sydney, Australia"),
        };

        if (locations.TryGetValue(location.Trim(), out var coords))
        {
            return new LocationCoordinates
            {
                Latitude = coords.lat,
                Longitude = coords.lon,
                FullName = coords.fullName,
                Found = true
            };
        }

        // Default fallback - generate random coordinates for unknown locations
        var random = new Random(location.GetHashCode());
        return new LocationCoordinates
        {
            Latitude = random. NextDouble() * 180 - 90,
            Longitude = random.NextDouble() * 360 - 180,
            FullName = $"{location} (approximate)",
            Found = false
        };
    }    
}

// JSON serialization context for AOT compatibility
[JsonSerializable(typeof(WeatherForecastResult))]
[JsonSerializable(typeof(LocationCoordinates))]
internal partial class AgentJsonContext : JsonSerializerContext { }
