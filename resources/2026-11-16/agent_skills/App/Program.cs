using App.Extensions;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenAI(builder.Configuration);
builder.Services.AddAIAgent(builder.Configuration);
builder.Services.AddAGUIServer();

var app = builder.Build();
var agentBuilder = app.Services.GetRequiredService<AIAgentBuilder>();
var agent = await agentBuilder.Build();
app.MapAGUIServer("/chat", agent);

await app.RunAsync();
