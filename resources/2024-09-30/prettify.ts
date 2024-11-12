type Prettify<T> = {
  [K in keyof T]: T[K];
} & {};

type ThemeMode = {
  mode: "light" | "dark";
};

type ThemeColor = {
  color: string;
};

type Theme = Prettify<ThemeMode & ThemeColor>;
