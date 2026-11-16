# Search behavior

- The query is lowercased and split on anything that is not a letter or digit.
- Filler words such as "show", "me", "find", "for", "the", "any", "available", and "please" are dropped.
- Words longer than three letters that end in `s` are singularized by removing the `s` ("laptops" -> "laptop").
- A product matches if any remaining word equals or is a prefix of a word in its name, category, or tags.
  Matching is on whole words, so "phone" does not match "headphones".

## Response shape

```json
[
  {
    "id": "p_laptop_dell",
    "name": "Dell XPS 13",
    "category": "laptop",
    "price": 999,
    "tags": ["laptop", "ultrabook", "work"]
  }
]
```

`price` is the list price before any deal. Use the `pricing` skill for the final price.
