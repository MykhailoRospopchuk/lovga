---
name: dotnet-sdk-project
description: >-
  Manages .NET 10 SDK projects, Directory.Build.props, Directory.Packages.props (Central Package Management),
  C# 14 language features, nullable reference types, AOT/trimming readiness, and dotnet CLI workflows.
  Use when upgrading TargetFramework, configuring MSBuild properties, managing NuGet packages, or structuring solutions.
---

# .NET 10 SDK, MSBuild & C# 14 Project Management

This skill guides configuration, modernization, and build optimization for .NET 10 / C# 14 solutions.

## 1. Centralized MSBuild Configuration (`Directory.Build.props`)
Avoid repeating target frameworks and compiler flags across every `.csproj`. Place a `Directory.Build.props` at the solution root:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

## 2. C# 14 & Modern C# Idioms
- **Primary Constructors**: Use primary constructors on classes and structs (`public sealed class BrokerService(ILogger<BrokerService> logger)`).
- **`field` Keyword (C# 14)**: Access the compiler-synthesized backing field directly inside property accessors without declaring a manual `_field`:
  ```csharp
  public int BatchSize
  {
      get;
      init => field = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
  } = 100;
  ```
- **Collection Expressions**: Use `[1, 2, 3]` and `[]` instead of `new List<int> { ... }` or `Array.Empty<T>()`.
- **Seal classes by default**: Mark leaf classes `sealed` to enable devirtualization and JIT guarded devirtualization optimizations.

## 3. Useful `dotnet` CLI Commands
- `dotnet build -c Release`: Compile solution in Release mode.
- `dotnet test --no-build -c Release`: Run all unit and integration tests.
- `dotnet list package --outdated`: Check for outdated NuGet packages across the solution.
- `dotnet list package --vulnerable`: Audit NuGet dependencies for known CVEs.
- `dotnet format`: Automatically format code and apply `.editorconfig` analyzers.
