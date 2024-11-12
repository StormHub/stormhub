---
layout: post
title:  "TypeScript type prettier"
date:   2024-09-30 09:08:58 +1000
categories: TypeScript
---
Reveal type from [Matt Pocock](https://twitter.com/mattpocockuk)

```typescript
type ThemeMode = {
  mode: "light" | "dark";
};

type ThemeColor = {
  color: string;
};

type Theme = ThemeMode & ThemeColor;
```

For Theme type above, hover over it in VSCode, it does not actually drill into the actual type details.
![image](https://github.com/StormHub/stormhub/blob/87b8d3bee515251856d90f715cc262d4a5ddd97c/resources/2024-09-30/type.png)

With the following helper

```typescript
type Prettify<T> = {
  [K in keyof T]: T[K];
} & {};
```
![image](https://github.com/StormHub/stormhub/blob/87b8d3bee515251856d90f715cc262d4a5ddd97c/resources/2024-09-30/prettier.png)
