# KernSmith.Tests

The test suite for the KernSmith library.

## Overview

This project contains unit and integration tests covering font parsing, glyph rasterization, atlas packing, BMFont output formatting, and the Gum integration layer. Tests use xUnit as the test framework and Shouldly for assertions.

Test font fixtures (e.g., `Roboto-Regular.ttf`) are located in the `Fixtures/` directory.

On Windows, additional tests exercise the GDI rasterizer backend via a conditional project reference.

## Running Tests

```
dotnet test tests/KernSmith.Tests/KernSmith.Tests.csproj
```

See the [root README](../../README.md) for full project documentation.
