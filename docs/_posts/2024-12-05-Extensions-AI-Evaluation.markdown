---
layout: post
title:  "Evaluate AI with Microsoft.Extensions.AI.Evaluation"
date:   2024-12-04 9:25:01 +1000
categories: .NET AI Microsoft.Extensions.AI
excerpt_separator: <!--more-->
---

Implementing a 'demo' RAG is straightforward with AI libraries like [Kernel Memory](https://github.com/microsoft/kernel-memory) or [Semantic Kernel](https://github.com/microsoft/semantic-kernel). However, production grade AI applications are significantly more challenging. AI programs are sensitive to changes and inherently probabilistic, unlike traditional deterministic tests. Tools like [Ragas](https://docs.ragas.io/en/stable/) address these issues. Microsoft has released a preview of [Microsoft.Extensions.AI.Evaluation](https://devblogs.microsoft.com/dotnet/evaluate-the-quality-of-your-ai-applications-with-ease/), followed by [Microsoft.Extensions.AI](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai-preview). Notably, Microsoft.Extensions.AI.Evaluation uses an 'LLM-as-a-Judge' approach to rank outcomes.
<!--more-->
Lets implement a fact evaluation from [OpenAI fact prompt](https://github.com/openai/evals/blob/a32c9826cd7d5d33d60a39b54fb96d1085498d9a/evals/registry/modelgraded/fact.yaml)

# Implement IEvaluator
- generating prompt of input, expected output and actual outcome
- interpret the prompt result to a rating

Note that there are evaluators out of the box from [Microsoft.Extensions.AI.Evaluation](https://devblogs.microsoft.com/dotnet/evaluate-the-quality-of-your-ai-applications-with-ease/) such as RelevanceTruthAndCompletenessEvaluator, CoherenceEvaluator, FluencyEvaluator

# Setup IChatClient and create chat configuration
- Configure chat client from [Microsoft.Extensions.AI](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai-preview)

Note that Microsoft.ML.Tokenizers package needs be added along with the actual token types (names all starts with Microsoft.ML.Tokenizers.Data) depending on the LLM types. For example, "gpt-4o" uses "Microsoft.ML.Tokenizers.Data.O200kBase".

# Setup ReportingConfiguration with evaluators, create ScenarioRun and execute against actual/expected results 
```csharp
var answerEvaluator = new FactEvaluator();
var reportConfiguration = DiskBasedReportingConfiguration.Create(
    storageRootPath: "./reports", // Json result files in this folder
    chatConfiguration: chatConfiguration,
    evaluators: [
        answerEvaluator
      ],
    executionName: documentId);

    await using var scenario = await reportConfiguration.CreateScenarioRunAsync(indexName);

    var evalResult = await scenario.EvaluateAsync(
      messages: [
        new ChatMessage(ChatRole.User, question)
      ],
      modelResponse: new ChatMessage(ChatRole.Assistant, response.Result),
      additionalContext: [new FactEvaluator.EvaluationExpert("Brazil and Bolivia")]);
```
Note that additional parameters need to be implemented as EvaluationContext

# Issues/Feedback
- Built in evaluators such as CoherenceEvaluator have prompts 'hardcoded', it is a bit hard to understand the outcome of them.
- No support for prompt templating mechanism
- Evaluation results need more support for 'What does this mean' kind of context in metrics.(Only Diagnostics at the moment)

[Sample code here](https://github.com/StormHub/stormhub/tree/main/resources/2024-12-05/ConsoleApp)