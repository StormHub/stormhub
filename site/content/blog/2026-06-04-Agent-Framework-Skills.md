---
title: Agent Skills in Microsoft Agent Framework
description: How progressive disclosure works for skills in Microsoft Agent Framework, and the one question the docs don't answer — when loaded skill content is evicted.
date: 2026-06-04
tags: [ "AI", "Agent", "Skills" ]
---

# {{title}}

*{{date | readableDate }}*

The [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview) recently added **skills** support, built around *progressive disclosure* (still in beta). The [Give Your Agents Domain Expertise with Agent Skills](https://devblogs.microsoft.com/agent-framework/give-your-agents-domain-expertise-with-agent-skills-in-microsoft-agent-framework/) devblog is an excellent introduction, so I won't re-tread the basics here.

If you've used skills in a coding agent, the idea is familiar: a skill is just a folder — a `SKILL.md` manifest plus reference documents and scripts — that the agent discovers and pulls in *only when it needs to*. Instead of stuffing every capability into the system prompt, the agent sees a lightweight catalog of skill names and descriptions, and loads the full content on demand. That's the whole point of progressive disclosure: an agent's context is a budget, and skills are a way to spend it lazily.

In practice that part just works: when a request matches a skill, the model is nudged to call the built-in `load_skill` tool, and the framework returns the skill's full content for the model to use. Triggering and loading behave exactly as advertised.

But spending the budget is only half the story. Once a skill's content is loaded, *where does it actually live — and is it ever dropped?* The docs are silent on this, and it's the question the rest of this post digs into.

The short answer: within a session, it isn't dropped at all. Starting a new session drops everything, of course — that much is obvious. The part worth knowing is what happens *inside* a single session: once loaded, a skill's full content stays in the conversation for the entire life of that session. There's no budget, no sliding window, no eviction. The rest of the post shows how I confirmed this, and why it matters.

## Watching a skill get triggered

The sample is a tiny console app running entirely against a local [Ollama](https://ollama.com) model — no cloud keys, and every HTTP call is traced so I can see exactly what goes over the wire ([complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2026-06-02/AgentSkillsDemo)). There's a single skill on disk:

```
skills/unit-converter/
├── SKILL.md                        # name + description + usage steps
└── references/conversion-table.md  # the actual conversion factors
```

Wiring it into the agent is one line — `AgentSkillsProvider` is just an `AIContextProvider`:

```csharp
var agentOptions = new ChatClientAgentOptions
{
    Name = "UnitConverterAgent",
    ChatOptions = new ChatOptions
    {
        Instructions = "You are a helpful assistant that can convert units. ...",
        Tools = [AIFunctionFactory.Create(Tool.Convert)]
    },
    AIContextProviders = [skillsProvider],   // <-- skills plug in here
};
```

On every request, that provider does two things. First, it injects a **catalog** of skills — names and descriptions only — into the system prompt. That's the entire "advertisement" the model sees up front; no factors, no usage steps:

```xml
<available_skills>
  <skill>
    <name>unit-converter</name>
    <description>Convert between common units using a multiplication factor.
      Use when asked to convert miles, kilometers, pounds, or kilograms.</description>
  </skill>
</available_skills>
```

Second, it registers three tools the model can call to pull in more on demand: `load_skill`, `read_skill_resource`, and `run_skill_script`.

### Intercepting the tool calls

To watch the triggering happen, I don't need to read the trace — the framework lets you intercept every tool call with function-invocation middleware. `AIAgentBuilder.Use(...)` wraps the agent and hands you each call before it runs:

```csharp
var agent = chatClient.AsAIAgent(agentOptions);

return new AIAgentBuilder(agent)
    .Use(async (_, ctx, next, ct) =>
    {
        if (ctx.Function.Name is "load_skill" or "read_skill_resource" or "run_skill_script")
        {
            Console.WriteLine($"Skill triggered: {ctx.Function.Name}({ctx.Arguments.GetValueOrDefault("skillName")})");
        }
        return await next(ctx, ct);
    })
    .Build();
```

The three skill tools are supplied by the provider, but they flow through the same function-invoking pipeline as my own `Convert` tool — so this one interceptor sees them all, and I just filter by name.

Now I ask a question that needs the skill:

> How many kilometers is a marathon (26.2 miles)? And how many pounds is 75 kilograms?

and the triggering shows up live:

```
Skill triggered: load_skill(unit-converter)
Skill triggered: read_skill_resource(unit-converter)
Agent: A marathon of 26.2 miles is approximately 42.16 kilometers, and 75 kilograms is approximately 165.35 pounds.
```

So the disclosure unfolds in stages, exactly as designed:

1. The model sees only the catalog, decides `unit-converter` is relevant, and calls `load_skill("unit-converter")`.
2. The framework returns the full `SKILL.md` as the tool result. Its usage steps tell the model to consult `references/conversion-table.md`.
3. The model calls `read_skill_resource` to pull that reference, then runs the actual conversion.

Each step pulls in a little more context, only when it is needed. This is progressive disclosure working as promised — the part the docs cover well. The interesting question is what happens to all that loaded content next.

## Loaded once, kept for the whole session

So where does that loaded content go? Straight into the session history — and it stays. After the run I read the history back and tagged the skill messages:

```
===== Session history after run: 8 messages =====
  [ 1] [SKILL] assistant call -> load_skill
  [ 2] [SKILL] tool      tool result          ← the full SKILL.md body
  [ 3] [SKILL] assistant call -> read_skill_resource
  [ 4] [SKILL] tool      tool result          ← the reference content
  ...
```

The `load_skill` body and the reference are sitting right there as ordinary tool messages, and nothing removes them. That's the part to take away: within a session, loaded skill content lives **forever**. It's not the skills provider holding on to it — `load_skill` just returns a normal tool message, and a tool message is history like any other. So every subsequent turn on that session re-sends the whole thing. No budget, no sliding window, no eviction; the only thing that clears it is starting a new session.

## Compact automatically — but only when skills are in play

Skills can be large, so on a long-lived session this adds up fast: you can't keep carrying every loaded skill forward. The fix is compaction, and the framework ships it out of the box. `CompactionProvider` is just another `AIContextProvider` you add alongside the skills provider, and `SummarizationCompactionStrategy` *summarizes* older history instead of dropping it — and it groups messages so a `load_skill` call is never split from its result.

I don't want to compact on *every* turn, though — only when there's actually skill content to reclaim. A `CompactionTrigger` is just a predicate over the message groups, so I gate it on whether a skill tool was called:

```csharp
CompactionTrigger skillsTriggered = index =>
    index.Groups.SelectMany(g => g.Messages).Any(History.MentionsSkillTool);

AIContextProviders =
[
    skillsProvider,
    new CompactionProvider(
        new SummarizationCompactionStrategy(chatClient, skillsTriggered, minimumPreservedGroups: 2)),
];
```

Compaction runs before each turn. On a fresh first turn there's nothing to compact; once a skill has been loaded, the next turn triggers a one-off summarization call and the bulky `SKILL.md` body drops out of what's sent to the model — replaced by a short summary, while the conversation keeps going. Spend the budget lazily on the way in, reclaim it automatically on the way out.

[Complete sample code](https://github.com/StormHub/stormhub/tree/main/resources/2026-06-02/AgentSkillsDemo)