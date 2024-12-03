---
layout: post
title:  "TypeScript type prettier"
date:   2024-09-30 09:08:58 +1000
categories: TypeScript
excerpt_separator: <!--more-->
---

TypeScript helper from [Matt Pocock](https://twitter.com/mattpocockuk) to reveal type details.
<!--more-->

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
![image](https://github.com/StormHub/stormhub/blob/main/resources/2024-09-30/type.png?raw=true)

With the following helper

```typescript
type Prettify<T> = {
  [K in keyof T]: T[K];
} & {};
```

![image](https://github.com/StormHub/stormhub/blob/main/resources/2024-09-30/prettier.png?raw=true)