---
title: Kernel Memory default tokenizer
description: Kernel Memory default tokenizer.
date: 2024-12-04
tags: .NET AI KernelMemory
permalink: ".net/ai/kernelmemory/2024/12/03/KernelMemory-Tokenizer.html"
---

# {{title}}

*{{date | readableDate("LLLL yyyy")}}*

Choosing the right tokenizer for in AI is crucial because it directly impacts the accuracy and efficiency. There is an optional 'tokenizer' parameter when configure [Kernel Memory](https://github.com/microsoft/kernel-memory) for both text generation and embedding.
<!--more--> If no 'tokenizer' specified, [Kernel Memory](https://github.com/microsoft/kernel-memory) attempts to pick up 'default' one.  But it does this by the AI model name (Depolyment in Azure OpenAI) like this 
[TokenizerFactory](https://github.com/microsoft/kernel-memory/blob/41d51119f09cddd3e4896f35fcd52c3f35f5f995/extensions/Tiktoken/Tiktoken/TokenizerFactory.cs) shows.


## My take away:
- Always prefix your deployment name by the actual model name 
   For example, for 'gpt-4o' model, the name should be prefix with model like this 'gpt-4o-bot'

- Leave the 'tokenizer' parameter to default and let [Kernel Memory](https://github.com/microsoft/kernel-memory) pick one automatically.
