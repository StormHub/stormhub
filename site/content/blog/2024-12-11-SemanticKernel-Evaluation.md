---
title: Lightweight AI Evaluation
description: Lightweight AI Evaluation with SemanticKernel.
date: 2024-12-11
tags: [ ".NET", "AI", "Semantic Kernel" ]
permalink: ".net/ai/semantickernel/2024/12/11/SemanticKernel-Evaluation.html"
---

# {{title}}

*{{date | readableDate }}*

For quick and easy evaluation or comparison of AI responses in .NET applications, particularly tests. We can leverage [autoevals](https://github.com/braintrustdata/autoevals) excellent 'LLM-as-a-Judge' prompts with the help of [Semantic Kernel](https://github.com/microsoft/semantic-kernel).


## Sample code
Note that you need to setup semantic kernel with chat completion first. It is also recommended to set 'Temperature' to 0.

```csharp
var json = 
    """
    {
        "humor" : {
            "output" : "this maybe funny"
        }
    }
    """;
await foreach (var result in 
        kernel.Run(json, executionSettings: executionSettings))
{
    Console.WriteLine($"[{result.Key}]: result: {result.Value?.Item1}, score: {result.Value?.Item2}");
}
```

[Source](https://github.com/StormHub/TinyToolBox.AI)

While [Microsoft.Extensions.AI.Evaluation](https://devblogs.microsoft.com/dotnet/evaluate-the-quality-of-your-ai-applications-with-ease/) is in the making, it currently involves  a little too much 'ceremonies' for simple use cases.
