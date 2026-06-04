# Test-Font Licenses

This file documents the licenses and upstream sources of the **font files committed
to this repository for testing**. These fonts are used only by the KernSmith test
suite and comparison harness.

> **These fonts are test fixtures.** They are **not** redistributed in any published
> KernSmith NuGet package or application binary. They live in the repository solely
> so the automated tests and the BMFont comparison harness have real font data to
> run against. They are not part of the KernSmith product.

Each font below is freely licensed (Apache-2.0 or SIL Open Font License 1.1) and may
be redistributed in source form, which is why committing them to a public repository
is permitted. Copyright lines are reproduced verbatim from each font's upstream
metadata / license file.

---

## Fonts

### Roboto-Regular.ttf

- **Path:** `tests/KernSmith.Tests/Fixtures/Roboto-Regular.ttf`
- **Family:** Roboto
- **Foundry / Source:** Google
- **License (SPDX):** `Apache-2.0` (classic Roboto) — see note below
- **Project / Source URL:** https://github.com/google/roboto
- **Copyright:** `Copyright 2011 Google Inc. All Rights Reserved.` (classic Roboto,
  Apache-2.0)

> **Which Roboto license applies?** The long-standing **"classic" Roboto** (the
> Roboto 2.x family historically shipped as `Roboto-Regular.ttf` in game-dev /
> BMFont contexts, and published at https://github.com/google/roboto) is licensed
> under the **Apache License 2.0**. In 2024 Google re-released **Roboto 3.x** on
> Google Fonts under the **SIL Open Font License 1.1**
> (https://github.com/googlefonts/roboto-3-classic). The committed file is treated
> here as the classic **Apache-2.0** Roboto. **[UNCONFIRMED]** — the exact build of
> the committed `Roboto-Regular.ttf` was not verified against its embedded `name`
> table during this pass, so if it is in fact a Roboto 3.x build, **OFL-1.1** would
> apply instead. Both licenses permit committing the font to a public repository and
> using it for testing.

### NotoColorEmoji.ttf

- **Path:** `tests/KernSmith.Tests/Fixtures/NotoColorEmoji.ttf`
- **Family:** Noto Color Emoji
- **Foundry / Source:** Google (Noto project)
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1)
- **Project / Source URL:** https://github.com/googlefonts/noto-emoji
- **Copyright (verbatim):** `Copyright 2013 Google LLC`

### RobotoFlex-Variable.ttf

- **Path:** `tests/KernSmith.Tests/Fixtures/RobotoFlex-Variable.ttf`
- **Family:** Roboto Flex (variable font)
- **Foundry / Source:** Google
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1)
- **Project / Source URL:** https://github.com/googlefonts/roboto-flex
- **Copyright (verbatim):**
  `Copyright 2011 The Roboto Flex Project Authors (https://github.com/googlefonts/roboto-flex)`

### DancingScript-Variable.ttf

- **Path:** `tests/bmfont-compare/gum-bmfont/DancingScript-Variable.ttf`
- **Family:** Dancing Script (variable font)
- **Foundry / Source:** Google / Pablo Impallari (Impallari Type)
- **License (SPDX):** `OFL-1.1` (SIL Open Font License, Version 1.1)
- **Project / Source URL:** https://github.com/googlefonts/DancingScript (originally
  https://github.com/impallari/DancingScript)
- **Copyright (verbatim):**
  `Copyright 2016 The Dancing Script Project Authors (https://github.com/googlefonts/DancingScript), with Reserved Font Name 'Dancing Script'.`

---

## SIL Open Font License 1.1 — reservation note

The OFL-1.1 fonts above carry **Reserved Font Names** (e.g. *"Dancing Script"*). Per
the OFL, a derivative or modified version of an OFL font **must not** use any
Reserved Font Name of the original. The font files in this repository are used
**unmodified**, for testing only, and are not redistributed under a different name.

The SIL Open Font License, Version 1.1 (26 February 2007) preamble, reproduced
verbatim:

```
PREAMBLE
The goals of the Open Font License (OFL) are to stimulate worldwide
development of collaborative font projects, to support the font creation
efforts of academic and linguistic communities, and to provide a free and
open framework in which fonts may be shared and improved in partnership
with others.

The OFL allows the licensed fonts to be used, studied, modified and
redistributed freely as long as they are not sold by themselves. The
fonts, including any derivative works, can be bundled, embedded,
redistributed and/or sold with any software provided that any reserved
names are not used by derivative works. The fonts and derivatives,
however, cannot be released under any other type of license. The
requirement for fonts to remain under this license does not apply
to any document created using the fonts or their derivatives.
```

The complete SIL OFL-1.1 text and FAQ are available at:
https://openfontlicense.org (formerly http://scripts.sil.org/OFL). Each OFL font
above ships the full license text in its own `OFL.txt` at its upstream source URL.

The Apache License 2.0 (for classic Roboto) is available at:
https://www.apache.org/licenses/LICENSE-2.0
