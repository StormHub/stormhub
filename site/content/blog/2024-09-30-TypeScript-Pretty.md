---
title: TypeScript type prettier
description: TypeScript type prettier
date: 2024-09-30
tags: [ "TypeScript" ]
permalink: "typescript/2024/09/29/TypeScript-Pretty.html"
---

# {{title}}

*{{date | readableDate("LLLL yyyy")}}*

TypeScript helper from [Matt Pocock](https://twitter.com/mattpocockuk) to reveal type details.

```typescript
type ThemeMode = {
  mode: "light" | "dark";
};

type ThemeColor = {
  color: string;
};

type Theme = ThemeMode & ThemeColor;
```

For Theme type above, hover over it in VS Code, it does not actually drill into the actual type details.
<img eleventy:ignore src="https://github.com/StormHub/stormhub/blob/main/resources/2024-09-30/type.png?raw=true" alt="VS code no types">

With the following helper

```typescript
type Prettify<T> = {
  [K in keyof T]: T[K];
} & {};
```

VS Code shows type details.  
<img eleventy:ignore src="https://github.com/StormHub/stormhub/blob/main/resources/2024-09-30/prettier.png?raw=true" alt="VS code with types">




