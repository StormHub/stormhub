---
title: Should Your Prompt Store Pick Your Model
description: How to handle prompt store and AI model mapping
date: 2026-08-27
tags: [ ".NET", "AI" ]
---

# {{title}}

*{{date | readableDate }}*

[Langfuse](https://langfuse.com) with `Microsoft.Extensions.AI` has an appealing story: update prompts without redeploying. A prompt fetches its config blob—model, tokens, temperature—which the code passes straight to the LLM.

It works. But it puts a boundary in what I'd suggest might be better placed elsewhere — and moving it is a small enough change to be worth exploring.

This post is about where to move that line in a .NET codebase using `Microsoft.Extensions.AI` against OpenAI or Azure OpenAI, with Langfuse as the source of prompts.

## What the current setup buys you

Let me be fair to it first, because the coupling is a deliberate design, not an accident.

Langfuse's prompt `config` is an optional JSON object versioned alongside the prompt. That means someone can open the Langfuse UI, change the model or a parameter, and ship it — no code change, no redeploy. Combined with labels (pointers to specific versions that your code references), a rollback is just moving the `production` label back to an earlier version. For prompt *content* iteration, that story is genuinely good, and there is a real audience of people who want model config coupled to prompt versions *more* tightly so each version is fully self-describing and reproducible.

So this is a trade-off, not a bug. The question is whether the thing you are optimizing for — non-engineers tuning prompts without a deploy — is worth what the coupling costs.

## Why I think this deserves consideration

Three points stand out.

**It is an untyped blob feeding provider selection.** The Langfuse `config` is arbitrary JSON without schema enforcement. On the other end, whatever LLM plumbing you use will treat that model string as authoritative. A missing key, a stray `max_tokens`, or a `gpt4o` typo might not fail at build time or deploy time — it could fail on a live request, or silently do something unintended. You have a loosely-typed value driving an infrastructure decision, and the mistake may not surface until traffic hits it.

**It conflates two change lifecycles with different risk profiles.** Prompt wording is a content decision: low risk, iterate freely. *Which model runs* and *what the token ceiling is* are infrastructure, cost, and reliability decisions with different review considerations. When both live in the same editable object, whoever edits prompts effectively has influence over production model selection and spend. That's considerable authority to place in a text field.

**Environment parameterization gets awkward.** The config travels with the prompt version. So "cheap model in dev, strong model in prod" tends to push you toward label gymnastics or separate projects, rather than a straightforward per-environment mapping that lives with your other infra config.

## The .NET insight that makes the fix clean

Here is the detail that makes this pleasant in `Microsoft.Extensions.AI`: for OpenAI and Azure OpenAI, the model identity is bound when you *construct* the client, not when you call it.

```csharp
IChatClient client = new AzureOpenAIClient(
        new Uri(endpoint), new DefaultAzureCredential())
    .GetChatClient("my-deployment")   // <-- model bound HERE
    .AsIChatClient();
```

The provider comes from which client type you build and its endpoint. The deployment comes from `GetChatClient`. By the time you have an `IChatClient`, it already knows what model it is. You barely need to touch `ChatOptions.ModelId` at all.

That means model selection reduces to: *pick the right pre-built client.* And "pick a thing by name" is exactly what keyed dependency injection is for. So the plan is:

- **Langfuse** owns the prompt template, its variables, and a *logical alias* like `extractor-strong`.
- **Your app** owns a typed, environment-aware registry mapping each alias to a concrete client plus guardrails, resolved through keyed DI and validated at startup.

The prompt says *what* it wants done. Your infra config says *which deployment and token budget* executes it.

## 1. The registry: engineering-owned, typed, env-aware

Model wiring goes in `appsettings.{Environment}.json`, so the same aliases map to different deployments per environment:

```jsonc
// appsettings.Production.json
{
  "ModelRegistry": {
    "Models": {
      "extractor-strong": {
        "Provider": "AzureOpenAI",
        "Endpoint": "https://my-res.openai.azure.com/",
        "Deployment": "gpt-4o-prod",
        "MaxOutputTokensCeiling": 4096,
        "DefaultTemperature": 0
      },
      "summarizer-cheap": {
        "Provider": "AzureOpenAI",
        "Endpoint": "https://my-res.openai.azure.com/",
        "Deployment": "gpt-4o-mini",
        "MaxOutputTokensCeiling": 1024
      }
    }
  }
}
```

```csharp
public sealed class ModelRegistryOptions
{
    public const string Section = "ModelRegistry";
    public Dictionary<string, ModelDefinition> Models { get; init; } = new();
}

public sealed class ModelDefinition
{
    [Required] public string Provider { get; init; } = default!;   // "AzureOpenAI" | "OpenAI"
    public string? Endpoint { get; init; }                          // Azure only
    [Required] public string Deployment { get; init; } = default!;  // Azure deployment or OpenAI model id
    [Range(1, 200_000)] public int MaxOutputTokensCeiling { get; init; } = 4096;
    public float? DefaultTemperature { get; init; }
}
```

## 2. Register one keyed client per alias, and fail fast

Each alias becomes a keyed `IChatClient` with its own middleware pipeline. Validation runs at startup, so a broken registry fails the deploy rather than the first request.

```csharp
var registry = builder.Configuration
    .GetSection(ModelRegistryOptions.Section)
    .Get<ModelRegistryOptions>() ?? new();

builder.Services
    .AddOptions<ModelRegistryOptions>()
    .Bind(builder.Configuration.GetSection(ModelRegistryOptions.Section))
    .Validate(o => o.Models.Count > 0, "No models configured")
    .Validate(o => o.Models.Values.All(m =>
        m.Provider is not "AzureOpenAI" || !string.IsNullOrWhiteSpace(m.Endpoint)),
        "AzureOpenAI models require an Endpoint")
    .ValidateOnStart();

foreach (var (alias, def) in registry.Models)
{
    builder.Services
        .AddKeyedChatClient(alias, _ => BuildInner(def))
        .UseOpenTelemetry()     // per-model pipeline: telemetry, caching, retries…
        .UseLogging();
}

static IChatClient BuildInner(ModelDefinition d) => d.Provider switch
{
    "AzureOpenAI" => new AzureOpenAIClient(
            new Uri(d.Endpoint!), new DefaultAzureCredential())   // or ApiKeyCredential
        .GetChatClient(d.Deployment)   // model identity bound here
        .AsIChatClient(),
    "OpenAI" => new OpenAI.Chat.ChatClient(
            d.Deployment, Environment.GetEnvironmentVariable("OPENAI_API_KEY")!)
        .AsIChatClient(),
    _ => throw new InvalidOperationException($"Unknown provider {d.Provider}")
};
```

Because `AddKeyedChatClient` returns a `ChatClientBuilder`, you can attach caching, retries, rate limiting, and OpenTelemetry per model — centrally, once, instead of scattering it across call sites.

## 3. The validation boundary: the actual fix

This is the part that addresses the core complaint. Whatever comes back from Langfuse gets parsed *once* into a typed spec. Unknown aliases are rejected. Requested generation parameters are clamped to the registry's ceiling. Nothing free-form reaches the provider.

Note what the prompt spec deliberately does **not** contain: no provider, no endpoint, no deployment. A prompt may *request* a temperature or a token budget; it may not choose infrastructure.

```csharp
// Fetching from Langfuse is a plain HTTP GET — no SDK, no magic.
// Whatever you use, hand this boundary the raw template + config dictionary.
public sealed record PromptSpec(string ModelAlias, int? MaxOutputTokens, float? Temperature);

public sealed class ResolvedPrompt
{
    public required IChatClient Client { get; init; }
    public required ChatOptions Options { get; init; }
    public required string Template { get; init; }
}

public sealed class PromptResolver(
    IServiceProvider sp,
    IOptions<ModelRegistryOptions> registry)
{
    public ResolvedPrompt Resolve(string template, IReadOnlyDictionary<string, object?> config)
    {
        // 1. Parse the loose blob into something typed — fail loudly, not at call time.
        if (!config.TryGetValue("modelAlias", out var aliasObj) || aliasObj is not string alias)
            throw new PromptConfigException("prompt config missing 'modelAlias'");

        if (!registry.Value.Models.TryGetValue(alias, out var def))
            throw new PromptConfigException($"unknown model alias '{alias}'"); // typo caught here

        // 2. Clamp requested params to the registry guardrails.
        int? requested = config.TryGetValue("maxOutputTokens", out var t) ? Convert.ToInt32(t) : null;
        int maxTokens = Math.Min(requested ?? def.MaxOutputTokensCeiling, def.MaxOutputTokensCeiling);

        float? temp = config.TryGetValue("temperature", out var tp)
            ? Convert.ToSingle(tp) : def.DefaultTemperature;

        return new ResolvedPrompt
        {
            Client   = sp.GetRequiredKeyedService<IChatClient>(alias),  // model already bound
            Options  = new ChatOptions { MaxOutputTokens = maxTokens, Temperature = temp },
            Template = template,
        };
    }
}
```

## 4. The call site

```csharp
var prompt   = await _prompts.GetAsync("invoice-extractor", label: "production");
var resolved = _resolver.Resolve(prompt.Template, prompt.Config);

var messages = new[] { new ChatMessage(ChatRole.System, Render(resolved.Template, vars)) };
var response = await resolved.Client.GetResponseAsync(messages, resolved.Options);
```

That is the whole flow. Alias in, correct pre-built client out, guardrailed options attached.

## Who owns what now

Langfuse keeps the prompt template, its variables, and a logical `modelAlias`. If you want, it can also carry `temperature` and `maxOutputTokens` — but as *requests* that the boundary clamps, not as commands. Non-engineers can still iterate freely and use the Playground. What they can no longer do is pin a raw provider deployment or exceed a token ceiling, because provider, endpoint, deployment, and hard limits live in your environment-specific `ModelRegistry`: under code review, resolved through keyed DI, validated at startup.

The trade-off is a genuine one. You give up some of Langfuse's "one object fully describes the run" reproducibility. In exchange, you get fail-at-startup instead of fail-on-request, environment-parameterized model selection you didn't have before, and a clearer separation between a content decision and an infrastructure one.

## If you only do one thing

Add the validation boundary in step 3, even if you defer the keyed-registry refactor. Parsing the untyped blob into a typed spec and rejecting unknown aliases is the highest-leverage, lowest-effort change here — it turns a class of production-time failures into a single, obvious throw. The keyed registry is where you want to end up; the boundary is what you can ship this afternoon.

A nice follow-up, once the registry exists: a startup check that every `modelAlias` referenced by your live production prompts actually resolves in the registry, so a missing mapping fails the deploy rather than the first customer request. But start with the boundary. Your prompt store is a good place to manage prompts. It might be worth reconsidering whether it should also dictate which GPU your requests use.
