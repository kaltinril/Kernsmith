# Phase 79 — Replace FluentAssertions with Shouldly

> **Status**: Complete
> **Created**: 2026-03-24
> **Goal**: Replace FluentAssertions with Shouldly across the entire test suite due to FluentAssertions moving to paid/commercial licensing.

---

## Motivation

FluentAssertions is moving to a paid/commercial licensing model. Shouldly is a mature, MIT-licensed alternative with a similar fluent assertion style. This phase replaces all FluentAssertions usage with Shouldly to keep the project fully open-source and avoid future licensing costs.

## Scope

- **27 files** total (2 config files + 25 test files)
- **~761 assertions** to convert
- **No behavioral changes** — only assertion syntax changes

## Package Change

| | FluentAssertions (remove) | Shouldly (add) |
|---|---|---|
| NuGet package | `FluentAssertions` 8.9.0 | `Shouldly` 4.3.0 |
| Namespace | `using FluentAssertions;` | `using Shouldly;` |
| License | Commercial (new) | MIT |

---

## Assertion Conversion Reference

### Equality / Basic Value

| FluentAssertions | Shouldly |
|---|---|
| `.Should().Be(expected)` | `.ShouldBe(expected)` |
| `.Should().NotBe(expected)` | `.ShouldNotBe(expected)` |
| `.Should().BeNull()` | `.ShouldBeNull()` |
| `.Should().NotBeNull()` | `.ShouldNotBeNull()` |
| `.Should().BeTrue()` | `.ShouldBeTrue()` |
| `.Should().BeFalse()` | `.ShouldBeFalse()` |

### Numeric Comparisons

| FluentAssertions | Shouldly |
|---|---|
| `.Should().BeGreaterThan(y)` | `.ShouldBeGreaterThan(y)` |
| `.Should().BeGreaterThanOrEqualTo(y)` | `.ShouldBeGreaterThanOrEqualTo(y)` |
| `.Should().BeLessThan(y)` | `.ShouldBeLessThan(y)` |
| `.Should().BeLessThanOrEqualTo(y)` | `.ShouldBeLessThanOrEqualTo(y)` |
| `.Should().BeInRange(low, high)` | `.ShouldBeInRange(low, high)` |
| `.Should().BePositive()` | `.ShouldBePositive()` |
| `.Should().BeNegative()` | `.ShouldBeNegative()` |
| `.Should().BeCloseTo(expected, tolerance)` | No direct equivalent -- use manual range check or `ShouldBe(expected, tolerance)` |

### Type Checks

| FluentAssertions | Shouldly |
|---|---|
| `.Should().BeOfType<T>()` | `.ShouldBeOfType<T>()` |
| `.Should().BeAssignableTo<T>()` | `.ShouldBeAssignableTo<T>()` |

### Collections

| FluentAssertions | Shouldly |
|---|---|
| `.Should().BeEquivalentTo(expected)` | `.ShouldBe(expected, ignoreOrder: true)` |
| `.Should().HaveCount(n)` | `.Count().ShouldBe(n)` |
| `.Should().BeEmpty()` | `.ShouldBeEmpty()` |
| `.Should().NotBeEmpty()` | `.ShouldNotBeEmpty()` |
| `.Should().Contain(item)` | `.ShouldContain(item)` |
| `.Should().NotContain(item)` | `.ShouldNotContain(item)` |
| `.Should().Contain(predicate)` | `.ShouldContain(predicate)` |
| `.Should().OnlyContain(predicate)` | `.ShouldAllBe(predicate)` |
| `.Should().HaveCountGreaterThan(n)` | `.Count().ShouldBeGreaterThan(n)` |
| `.Should().HaveCountGreaterThanOrEqualTo(n)` | `.Count().ShouldBeGreaterThanOrEqualTo(n)` |
| `.Should().ContainSingle()` | `.ShouldHaveSingleItem()` |
| `.Should().OnlyHaveUniqueItems()` | No direct equivalent -- use `.Distinct().Count().ShouldBe(col.Count())` |
| `.Should().BeInAscendingOrder()` | No direct equivalent -- use `.ShouldBe(col.OrderBy(x => x))` |
| `.Should().BeSubsetOf(superset)` | No direct equivalent -- use `.ShouldAllBe(item => superset.Contains(item))` |

### Dictionaries

| FluentAssertions | Shouldly |
|---|---|
| `.Should().ContainKey(key)` | `.ShouldContainKey(key)` |

### Strings

| FluentAssertions | Shouldly |
|---|---|
| `.Should().StartWith(prefix)` | `.ShouldStartWith(prefix)` |
| `.Should().EndWith(suffix)` | `.ShouldEndWith(suffix)` |
| `.Should().Contain(sub)` | `.ShouldContain(sub)` |
| `.Should().NotContain(sub)` | `.ShouldNotContain(sub)` |
| `.Should().NotBeNullOrWhiteSpace()` | `.ShouldNotBeNullOrWhiteSpace()` |
| `.Should().Match(wildcard)` | `.ShouldMatch(regex)` (note: Shouldly uses regex, not wildcards -- convert wildcard patterns to regex) |

### Exceptions

| FluentAssertions | Shouldly |
|---|---|
| `act.Should().Throw<T>()` | `Should.Throw<T>(act)` |
| `act.Should().Throw<T>().WithMessage(msg)` | `Should.Throw<T>(act).Message.ShouldContain(msg)` |
| `await act.Should().ThrowAsync<T>()` | `await Should.ThrowAsync<T>(act)` |
| `act.Should().NotThrow()` | `Should.NotThrow(act)` |

### Object Equivalence

| FluentAssertions | Shouldly |
|---|---|
| `.Should().BeSameAs(other)` | `.ShouldBeSameAs(other)` |
| `.Should().NotBeSameAs(other)` | `.ShouldNotBeSameAs(other)` |

---

## Implementation Steps

### Step 1 — Update package references

Update the two config files:

- [ ] `Directory.Packages.props` -- replace `<PackageVersion Include="FluentAssertions" Version="8.9.0" />` with `<PackageVersion Include="Shouldly" Version="4.3.0" />`
- [ ] `tests/KernSmith.Tests/KernSmith.Tests.csproj` -- replace `<PackageReference Include="FluentAssertions" />` with `<PackageReference Include="Shouldly" />`
- [ ] Run `dotnet restore` to confirm package resolution

### Step 2 — Convert Atlas test files (92 assertions)

- [ ] `tests/KernSmith.Tests/Atlas/AtlasSizeEstimatorTests.cs` (44 assertions)
- [ ] `tests/KernSmith.Tests/Atlas/AtlasSizeQueryTests.cs` (20 assertions)
- [ ] `tests/KernSmith.Tests/Atlas/RenderToExistingTests.cs` (28 assertions)
- [ ] Run `dotnet test --filter "FullyQualifiedName~Atlas"` -- all pass

### Step 3 — Convert CLI test file (73 assertions)

- [ ] `tests/KernSmith.Tests/Cli/CliTests.cs` (73 assertions)
- [ ] Run `dotnet test --filter "FullyQualifiedName~Cli"` -- all pass

### Step 4 — Convert Font test files (93 assertions)

- [ ] `tests/KernSmith.Tests/Font/CharacterSetTests.cs` (11 assertions)
- [ ] `tests/KernSmith.Tests/Font/SubsettingTests.cs` (55 assertions)
- [ ] `tests/KernSmith.Tests/Font/TtfFontReaderTests.cs` (5 assertions)
- [ ] `tests/KernSmith.Tests/Font/TtfParserTests.cs` (18 assertions)
- [ ] `tests/KernSmith.Tests/Font/WoffDecompressorTests.cs` (4 assertions)
- [ ] Run `dotnet test --filter "FullyQualifiedName~Font"` -- all pass

### Step 5 — Convert Integration test files (97 assertions)

- [ ] `tests/KernSmith.Tests/Integration/CombinedBatchTests.cs` (24 assertions)
- [ ] `tests/KernSmith.Tests/Integration/EndToEndTests.cs` (73 assertions)
- [ ] Run `dotnet test --filter "FullyQualifiedName~Integration"` -- all pass

### Step 6 — Convert Output test files (81 assertions)

- [ ] `tests/KernSmith.Tests/Output/BinaryFormatterTests.cs` (7 assertions)
- [ ] `tests/KernSmith.Tests/Output/BmFontReaderTests.cs` (50 assertions)
- [ ] `tests/KernSmith.Tests/Output/TextFormatterTests.cs` (15 assertions)
- [ ] `tests/KernSmith.Tests/Output/XmlFormatterTests.cs` (9 assertions)
- [ ] Run `dotnet test --filter "FullyQualifiedName~Output"` -- all pass

### Step 7 — Convert Packing test files (32 assertions)

- [ ] `tests/KernSmith.Tests/Packing/MaxRectsPackerTests.cs` (16 assertions)
- [ ] `tests/KernSmith.Tests/Packing/SkylinePackerTests.cs` (16 assertions)
- [ ] Run `dotnet test --filter "FullyQualifiedName~Packing"` -- all pass

### Step 8 — Convert Rasterizer test files (105 assertions)

- [ ] `tests/KernSmith.Tests/Rasterizer/ColorFontTests.cs` (44 assertions)
- [ ] `tests/KernSmith.Tests/Rasterizer/LayeredRenderingTests.cs` (33 assertions)
- [ ] `tests/KernSmith.Tests/Rasterizer/VariableFontTests.cs` (28 assertions)
- [ ] Run `dotnet test --filter "FullyQualifiedName~Rasterizer"` -- all pass

### Step 9 — Convert root test files (188 assertions)

- [ ] `tests/KernSmith.Tests/EdgeCaseTests.cs` (30 assertions)
- [ ] `tests/KernSmith.Tests/FeatureCoverageTests.cs` (40 assertions)
- [ ] `tests/KernSmith.Tests/InputValidationTests.cs` (17 assertions)
- [ ] `tests/KernSmith.Tests/OutputFormatTests.cs` (54 assertions)
- [ ] `tests/KernSmith.Tests/ApiUsabilityTests.cs` (47 assertions)
- [ ] Run `dotnet test` -- all pass

### Step 10 — Final verification

- [ ] Run full test suite: `dotnet test` -- all 761+ assertions pass
- [ ] Confirm no remaining references to FluentAssertions: `grep -r "FluentAssertions" tests/`
- [ ] Confirm build has no warnings related to the migration

### Step 11 — Update project documentation and tech stack references

- [ ] `plan/master-plan.md` — Update Resolved Decision #8 from "xUnit + FluentAssertions" to "xUnit + Shouldly"
- [ ] `plan/master-plan.md` — Add "Disallowed Technologies" section listing FluentAssertions as banned (paid licensing)
- [ ] `plan/done/plan-testing.md` — Update any references to FluentAssertions with Shouldly
- [ ] `plan/done/plan-data-types.md` — Update any assertion library references if present
- [ ] `CLAUDE.md` — Update testing dependencies line from FluentAssertions to Shouldly
- [ ] Verify no other plan docs reference FluentAssertions as current tech

---

## Gotchas

1. **Wildcard vs regex in string matching** -- FluentAssertions `.Should().Match()` uses wildcard patterns (`*` = any chars). Shouldly `.ShouldMatch()` uses regex. Convert `*` to `.*`, `?` to `.`, and escape regex special characters.
2. **`BeCloseTo` for numeric tolerance** -- Shouldly does not have a direct `BeCloseTo` equivalent. Use `ShouldBe(expected, tolerance)` if available, or write a manual range assertion: `value.ShouldBeGreaterThanOrEqualTo(expected - tolerance)` + `value.ShouldBeLessThanOrEqualTo(expected + tolerance)`.
3. **`BeEquivalentTo` semantics** -- FluentAssertions `BeEquivalentTo` does deep structural comparison with property-by-property matching. Shouldly `ShouldBe` with `ignoreOrder: true` compares collection elements. For complex object graphs, may need `ShouldBeEquivalentTo` or manual property assertions.
4. **Exception assertion syntax is inverted** -- FluentAssertions chains from the action (`act.Should().Throw<T>()`), while Shouldly uses a static method (`Should.Throw<T>(act)`). Pay attention to async variants as well.
5. **`ContainSingle` with predicate** -- FluentAssertions supports `.Should().ContainSingle(predicate)`. Shouldly's `.ShouldHaveSingleItem()` takes no predicate. Use `.Where(predicate).ShouldHaveSingleItem()` instead.
6. **Chained assertions** -- FluentAssertions supports chaining like `.Should().NotBeNull().And.HaveCount(3)`. Shouldly does not chain. Split into separate assertion statements.

## Key Source Files

| What | Location |
|------|----------|
| Central package versions | `Directory.Packages.props` |
| Test project file | `tests/KernSmith.Tests/KernSmith.Tests.csproj` |
| Test suite root | `tests/KernSmith.Tests/` |
