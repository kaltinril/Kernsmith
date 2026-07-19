# Third-Party Notices

KernSmith itself is licensed under the **MIT License** (see [`LICENSE`](LICENSE)).

This file lists the third-party components that KernSmith **redistributes** (ships
inside its NuGet packages or alongside its CLI/UI binaries) or **links against**,
together with their licenses and the attribution they require. It exists so that
the obligations attaching to *shipped binaries* — the CLI, the desktop UI, and the
bundled native libraries — are satisfied in one canonical place.

A note on delivery channels:

- **Managed NuGet dependencies** carry their own license metadata, which NuGet
  delivers transitively to anyone who installs a KernSmith package. Consumers
  therefore already receive each managed dependency's license through the normal
  package-restore process.
- **This file consolidates** those obligations and, more importantly, covers the
  pieces NuGet does **not** carry automatically: the **native libraries** bundled
  into application output (FreeType, SDL2, OpenAL Soft) and a small amount of
  **third-party-derived source code** vendored into the rasterizer backends.

Copyright lines and full license texts below are reproduced **verbatim** from each
component's upstream `LICENSE`/`COPYING` file. Nothing here is paraphrased.

> This is an attribution/compliance document, not legal advice.

---

## Table of Contents

- [1. Components by license](#1-components-by-license)
- [2. FreeType (native) — FreeType License (FTL)](#2-freetype-native--freetype-license-ftl)
  - [2.1 FreeType-derived source in KernSmith](#21-freetype-derived-source-in-kernsmith)
- [3. MIT-licensed components](#3-mit-licensed-components)
- [4. Public-Domain components (stb / StbSharp)](#4-public-domain-components-stb--stbsharp)
- [5. Microsoft Public License (Ms-PL) components](#5-microsoft-public-license-ms-pl-components)
- [6. Zlib-licensed components](#6-zlib-licensed-components)
- [7. OpenAL Soft — GNU Library General Public License](#7-openal-soft--gnu-library-general-public-license)
- [Appendix A: Full license texts](#appendix-a-full-license-texts)
  - [A.1 The FreeType Project License (FTL)](#a1-the-freetype-project-license-ftl)
  - [A.2 MIT License (template)](#a2-mit-license-template)
  - [A.3 Microsoft Public License (Ms-PL)](#a3-microsoft-public-license-ms-pl)
  - [A.4 Zlib License](#a4-zlib-license)
  - [A.5 GNU Library General Public License, Version 2](#a5-gnu-library-general-public-license-version-2)

---

## 1. Components by license

| Component | Version | License (SPDX) | Distribution surface |
|-----------|---------|----------------|----------------------|
| FreeType (native, bundled by FreeTypeSharp) | 2.13.2 | `FTL` (FreeType License) | Native binary in FreeType backend / CLI / UI |
| FreeTypeSharp | 3.1.0 | `MIT` | NuGet (FreeType backend) |
| FreeType-derived embolden port (`OutlineTransforms.cs`) | n/a (source) | `FTL` (covered by FreeType) | Source in StbTrueType backend |
| StbImageSharp | 2.30.15 | Public Domain (`Unlicense`-equivalent) | NuGet (core) |
| StbImageWriteSharp | 1.16.7 | Public Domain (`Unlicense`-equivalent) | NuGet (core) |
| StbTrueTypeSharp | 1.26.12 | Public Domain (`Unlicense`-equivalent) | NuGet (StbTrueType backend) |
| stb_truetype SDF (`StbTrueTypeSdfVendored.cs`) | n/a (source) | Public Domain / `MIT` | Source in StbTrueType backend |
| TerraFX.Interop.Windows | 10.0.26100.6 | `MIT` | NuGet (DirectWrite backend) |
| MonoGame.Framework.DesktopGL | 3.8.5 | `Ms-PL` | NuGet + native runtime (KernSmith.Ui desktop app) |
| SDL2 (bundled by MonoGame DesktopGL) | n/a (native) | `Zlib` | Native binary (UI) |
| OpenAL Soft (bundled by MonoGame DesktopGL) | 1.24.3 | `LGPL-2.0-or-later` (GNU Library GPL v2) | Native binary (UI) |
| Gum.MonoGame | 2026.7.6.1 | `MIT` | NuGet (KernSmith.Ui desktop app) |
| Gum.Themes.Editor.MonoGame | 2026.7.6.1 | `MIT` | NuGet (KernSmith.Ui desktop app) |
| NativeFileDialogNET | 2.0.2 | `Zlib` | NuGet (UI / CLI file dialogs) |
| nativefiledialog-extended (native, underlying) | n/a (native) | `Zlib` | Native binary (UI / CLI) |

> **Not included:** `System.Drawing.Common` is **not** a KernSmith dependency. The
> GDI rasterizer backend (`KernSmith.Rasterizers.Gdi`) talks to Windows
> `gdi32.dll`/`user32.dll` directly via P/Invoke; those are operating-system
> components, not redistributed third-party packages. `MonoGame.Extended` and
> `NativeFileDialogSharp` appear in `Directory.Packages.props` but are not
> referenced by any project, so they are not redistributed and are omitted here.

---

## 2. FreeType (native) — FreeType License (FTL)

- **Name:** FreeType
- **Version:** **2.13.2** (the native build bundled by **FreeTypeSharp 3.1.0**; confirmed from the `FREETYPE_MAJOR/MINOR/PATCH` macros at the FreeType submodule commit pinned by FreeTypeSharp `v3.1.0`)
- **Project URL:** https://www.freetype.org
- **SPDX license:** `FTL` (the FreeType Project License) — FreeType is dual-licensed **FTL OR GPLv2**
- **License election:** KernSmith **distributes FreeType under the FreeType License (FTL) option, not under the GNU GPL v2.**

**Copyright (verbatim, from `docs/FTL.TXT`):**

```
Copyright 1996-2002, 2006 by
David Turner, Robert Wilhelm, and Werner Lemberg
```

**Required FTL credit line** (with the year for the FreeType version actually used,
per `FTL.TXT`'s instruction to "replace `<year>` with the value from the FreeType
version you actually use" — FreeType 2.13.2 carries a `Copyright (C) 1996-2023`
header, so the year is **2023**):

```
Portions of this software are copyright © 2023 The FreeType Project (www.freetype.org). All rights reserved.
```

The full FTL text is reproduced in [Appendix A.1](#a1-the-freetype-project-license-ftl).

### 2.1 FreeType-derived source in KernSmith

`src/KernSmith.Rasterizers.StbTrueType/OutlineTransforms.cs` contains a port of
FreeType's `FT_Outline_EmboldenXY` (synthetic-bold algorithm). Even though the file
lives inside an otherwise public-domain (stb) backend, this derived expression is
covered by the **FreeType copyright and the FTL** shown above. This is a notice
entry, not a separate license: the same FreeType credit line and FTL text apply.

---

## 3. MIT-licensed components

Each component below is under the **MIT License**. The standard MIT permission text
is identical across them and is reproduced once in
[Appendix A.2](#a2-mit-license-template); only the copyright lines differ.

### FreeTypeSharp 3.1.0
- **Project URL:** https://github.com/ryancheung/FreeTypeSharp
- **SPDX:** `MIT`
- **Copyright (verbatim):**
  ```
  Copyright (c) 2022 ryancheung
  ```

### TerraFX.Interop.Windows 10.0.26100.6
- **Project URL:** https://github.com/terrafx/terrafx.interop.windows
- **SPDX:** `MIT`
- **Copyright (verbatim):**
  ```
  Copyright © Tanner Gooding and Contributors
  ```

### Gum packages
Applies to **Gum.MonoGame 2026.7.6.1** and **Gum.Themes.Editor.MonoGame 2026.7.6.1**
(dependencies of the `KernSmith.Ui` desktop app).
- **Project URL:** https://github.com/vchelaru/Gum
- **SPDX:** `MIT`
- **Copyright (verbatim):**
  ```
  Copyright (c) 2013-2024 FlatRedBall, LLC
  ```

---

## 4. Public-Domain components (stb / StbSharp)

**StbImageSharp 2.30.15**, **StbImageWriteSharp 1.16.7**, and
**StbTrueTypeSharp 1.26.12** are C# ports maintained by the **StbSharp**
organization (Roman Shapiro). Each project declares its license simply as
**"Public Domain"** and ships no copyright notice or license-text file.

- **Project URLs:**
  - https://github.com/StbSharp/StbImageSharp
  - https://github.com/StbSharp/StbImageWriteSharp
  - https://github.com/StbSharp/StbTrueTypeSharp
- **SPDX:** Public Domain (no embedded license; `Unlicense`-equivalent)

These are ports of the underlying **stb** single-file C libraries by **Sean Barrett**
(https://github.com/nothings/stb), which are **dual-licensed Public Domain / MIT** at
the user's choice.

**Vendored stb source in KernSmith.**
`src/KernSmith.Rasterizers.StbTrueType/StbTrueTypeSdfVendored.cs` vendors the SDF
rendering routine from `stb_truetype.h` by **Sean Barrett**, modified to accept
pre-transformed vertices. It is **Public Domain / MIT** (matching the original
dual license). The file already carries an in-source attribution header.

---

## 5. Microsoft Public License (Ms-PL) components

The following components are under the **Microsoft Public License (Ms-PL)**. The
full Ms-PL text is reproduced once in [Appendix A.3](#a3-microsoft-public-license-ms-pl).

### MonoGame.Framework.DesktopGL 3.8.5
- **Project URL:** https://github.com/MonoGame/MonoGame
- **SPDX:** `Ms-PL` (with a portion under MIT — see below)
- **Copyright (verbatim, from MonoGame `LICENSE.txt`):**
  ```
  Microsoft Public License (Ms-PL)
  MonoGame - Copyright © 2009-2025 MonoGame Foundation, Inc
  ```
- MonoGame's `LICENSE.txt` also includes an MIT-licensed portion:
  ```
  The MIT License (MIT)
  Portions Copyright © The Mono.Xna Team
  ```
  (The MIT text is the standard one in [Appendix A.2](#a2-mit-license-template).)
- MonoGame DesktopGL bundles native libraries that have their own licenses; see
  [§6 (SDL2)](#6-zlib-licensed-components) and
  [§7 (OpenAL Soft)](#7-openal-soft--gnu-library-general-public-license).

---

## 6. Zlib-licensed components

The following components are under the **Zlib License**. The full Zlib text is
reproduced once in [Appendix A.4](#a4-zlib-license).

### SDL2 (bundled by MonoGame.Framework.DesktopGL)
- **Project URL:** https://github.com/libsdl-org/SDL
- **SPDX:** `Zlib`
- **Copyright (verbatim):**
  ```
  Copyright (C) 1997-2026 Sam Lantinga <slouken@libsdl.org>
  ```
- SDL2 is shipped as a native shared library inside MonoGame DesktopGL application
  output.

### NativeFileDialogNET 2.0.2 and nativefiledialog-extended (underlying native)
- **Project URLs:**
  - https://github.com/atomsk-0/NativeFiledialogNET (managed binding)
  - https://github.com/btzy/nativefiledialog-extended (underlying native library)
- **SPDX:** `Zlib`
- `nativefiledialog-extended` is maintained by **Bernard Teo (btzy)** and is based on
  the original **Native File Dialog** by **Michael Labbe**
  (https://github.com/mlabbe/nativefiledialog); both are distributed under the Zlib
  license. The shipped `LICENSE` file contains the Zlib permission text (see
  [Appendix A.4](#a4-zlib-license)).

---

## 7. OpenAL Soft — GNU Library General Public License

- **Name:** OpenAL Soft
- **Version:** **1.24.3** (bundled by MonoGame.Framework.DesktopGL 3.8.4.1 via the
  `MonoGame.Library.OpenAL` 1.24.3.2 native package)
- **Project URL:** https://github.com/kcat/openal-soft
- **SPDX license:** `LGPL-2.0-or-later`

OpenAL Soft 1.24.3 ships its `COPYING` file as the **GNU Library General Public
License, Version 2 (June 1991)**, and its source headers state the library may be
used under "the GNU Library General Public License … version 2 of the License, or
(at your option) any later version." The SPDX expression is therefore
`LGPL-2.0-or-later` (the "or later" clause means a recipient may also use it under
LGPL-2.1 or LGPL-3.0). The full Library GPL v2 text is reproduced in
[Appendix A.5](#a5-gnu-library-general-public-license-version-2).

**Copyright (verbatim, from OpenAL Soft source-file headers):**

```
Copyright (C) 1999-2007 by authors.
```

(OpenAL Soft is a community project; its source files carry the notice above and
defer to "authors." The project is principally maintained by **Chris Robinson** and
contributors — https://github.com/kcat/openal-soft.)

**Dynamic linking / relink notice (LGPL requirement):**
MonoGame links OpenAL Soft **dynamically**, as a separate shared library
(`soft_oal.dll` / `libopenal.so` / `libopenal.dylib`) that ships next to the
application binaries. As permitted by the LGPL, **recipients of a KernSmith
application may modify the OpenAL Soft library and relink or replace the bundled
OpenAL Soft shared library** with their own compatible build. The complete
corresponding source for OpenAL Soft is available from the upstream project at
**https://github.com/kcat/openal-soft** (release tag `1.24.3`).

---

## Appendix A: Full license texts

### A.1 The FreeType Project License (FTL)

```
                    The FreeType Project LICENSE
                    ----------------------------

                            2006-Jan-27

                    Copyright 1996-2002, 2006 by
          David Turner, Robert Wilhelm, and Werner Lemberg



Introduction
============

  The FreeType  Project is distributed in  several archive packages;
  some of them may contain, in addition to the FreeType font engine,
  various tools and  contributions which rely on, or  relate to, the
  FreeType Project.

  This  license applies  to all  files found  in such  packages, and
  which do not  fall under their own explicit  license.  The license
  affects  thus  the  FreeType   font  engine,  the  test  programs,
  documentation and makefiles, at the very least.

  This  license   was  inspired  by  the  BSD,   Artistic,  and  IJG
  (Independent JPEG  Group) licenses, which  all encourage inclusion
  and  use of  free  software in  commercial  and freeware  products
  alike.  As a consequence, its main points are that:

    o We don't promise that this software works. However, we will be
      interested in any kind of bug reports. (`as is' distribution)

    o You can  use this software for whatever you  want, in parts or
      full form, without having to pay us. (`royalty-free' usage)

    o You may not pretend that  you wrote this software.  If you use
      it, or  only parts of it,  in a program,  you must acknowledge
      somewhere  in  your  documentation  that  you  have  used  the
      FreeType code. (`credits')

  We  specifically  permit  and  encourage  the  inclusion  of  this
  software, with  or without modifications,  in commercial products.
  We  disclaim  all warranties  covering  The  FreeType Project  and
  assume no liability related to The FreeType Project.


  Finally,  many  people  asked  us  for  a  preferred  form  for  a
  credit/disclaimer to use in compliance with this license.  We thus
  encourage you to use the following text:

   """
    Portions of this software are copyright © <year> The FreeType
    Project (www.freetype.org).  All rights reserved.
   """

  Please replace <year> with the value from the FreeType version you
  actually use.


Legal Terms
===========

0. Definitions
--------------

  Throughout this license,  the terms `package', `FreeType Project',
  and  `FreeType  archive' refer  to  the  set  of files  originally
  distributed  by the  authors  (David Turner,  Robert Wilhelm,  and
  Werner Lemberg) as the `FreeType Project', be they named as alpha,
  beta or final release.

  `You' refers to  the licensee, or person using  the project, where
  `using' is a generic term including compiling the project's source
  code as  well as linking it  to form a  `program' or `executable'.
  This  program is  referred to  as  `a program  using the  FreeType
  engine'.

  This  license applies  to all  files distributed  in  the original
  FreeType  Project,   including  all  source   code,  binaries  and
  documentation,  unless  otherwise  stated   in  the  file  in  its
  original, unmodified form as  distributed in the original archive.
  If you are  unsure whether or not a particular  file is covered by
  this license, you must contact us to verify this.

  The FreeType  Project is copyright (C) 1996-2000  by David Turner,
  Robert Wilhelm, and Werner Lemberg.  All rights reserved except as
  specified below.

1. No Warranty
--------------

  THE FREETYPE PROJECT  IS PROVIDED `AS IS' WITHOUT  WARRANTY OF ANY
  KIND, EITHER  EXPRESS OR IMPLIED,  INCLUDING, BUT NOT  LIMITED TO,
  WARRANTIES  OF  MERCHANTABILITY   AND  FITNESS  FOR  A  PARTICULAR
  PURPOSE.  IN NO EVENT WILL ANY OF THE AUTHORS OR COPYRIGHT HOLDERS
  BE LIABLE  FOR ANY DAMAGES CAUSED  BY THE USE OR  THE INABILITY TO
  USE, OF THE FREETYPE PROJECT.

2. Redistribution
-----------------

  This  license  grants  a  worldwide, royalty-free,  perpetual  and
  irrevocable right  and license to use,  execute, perform, compile,
  display,  copy,   create  derivative  works   of,  distribute  and
  sublicense the  FreeType Project (in  both source and  object code
  forms)  and  derivative works  thereof  for  any  purpose; and  to
  authorize others  to exercise  some or all  of the  rights granted
  herein, subject to the following conditions:

    o Redistribution of  source code  must retain this  license file
      (`FTL.TXT') unaltered; any  additions, deletions or changes to
      the original  files must be clearly  indicated in accompanying
      documentation.   The  copyright   notices  of  the  unaltered,
      original  files must  be  preserved in  all  copies of  source
      files.

    o Redistribution in binary form must provide a  disclaimer  that
      states  that  the software is based in part of the work of the
      FreeType Team,  in  the  distribution  documentation.  We also
      encourage you to put an URL to the FreeType web page  in  your
      documentation, though this isn't mandatory.

  These conditions  apply to any  software derived from or  based on
  the FreeType Project,  not just the unmodified files.   If you use
  our work, you  must acknowledge us.  However, no  fee need be paid
  to us.

3. Advertising
--------------

  Neither the  FreeType authors and  contributors nor you  shall use
  the name of the  other for commercial, advertising, or promotional
  purposes without specific prior written permission.

  We suggest,  but do not require, that  you use one or  more of the
  following phrases to refer  to this software in your documentation
  or advertising  materials: `FreeType Project',  `FreeType Engine',
  `FreeType library', or `FreeType Distribution'.

  As  you have  not signed  this license,  you are  not  required to
  accept  it.   However,  as  the FreeType  Project  is  copyrighted
  material, only  this license, or  another one contracted  with the
  authors, grants you  the right to use, distribute,  and modify it.
  Therefore,  by  using,  distributing,  or modifying  the  FreeType
  Project, you indicate that you understand and accept all the terms
  of this license.

4. Contacts
-----------

  There are two mailing lists related to FreeType:

    o freetype@nongnu.org

      Discusses general use and applications of FreeType, as well as
      future and  wanted additions to the  library and distribution.
      If  you are looking  for support,  start in  this list  if you
      haven't found anything to help you in the documentation.

    o freetype-devel@nongnu.org

      Discusses bugs,  as well  as engine internals,  design issues,
      specific licenses, porting, etc.

  Our home page can be found at

    https://www.freetype.org


--- end of FTL.TXT ---
```

### A.2 MIT License (template)

The MIT License text below applies to **FreeTypeSharp**, **TerraFX.Interop.Windows**,
the **Gum** packages, and the **Mono.Xna** portion of MonoGame. Insert
the relevant copyright line (see each component's entry above) where indicated.

```
MIT License

Copyright (c) <copyright holders — see component entry above>

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### A.3 Microsoft Public License (Ms-PL)

Applies to **MonoGame.Framework.DesktopGL**. Reproduced verbatim from
MonoGame's `LICENSE.txt` (the operative copyright line is listed in
[§5](#5-microsoft-public-license-ms-pl-components)).

```
Microsoft Public License (Ms-PL)

This license governs use of the accompanying software. If you use the software,
you accept this license. If you do not accept the license, do not use the
software.

1. Definitions

The terms "reproduce," "reproduction," "derivative works," and "distribution"
have the same meaning here as under U.S. copyright law.

A "contribution" is the original software, or any additions or changes to the
software.

A "contributor" is any person that distributes its contribution under this
license.

"Licensed patents" are a contributor's patent claims that read directly on its
contribution.

2. Grant of Rights

(A) Copyright Grant- Subject to the terms of this license, including the
license conditions and limitations in section 3, each contributor grants you a
non-exclusive, worldwide, royalty-free copyright license to reproduce its
contribution, prepare derivative works of its contribution, and distribute its
contribution or any derivative works that you create.

(B) Patent Grant- Subject to the terms of this license, including the license
conditions and limitations in section 3, each contributor grants you a
non-exclusive, worldwide, royalty-free license under its licensed patents to
make, have made, use, sell, offer for sale, import, and/or otherwise dispose of
its contribution in the software or derivative works of the contribution in the
software.

3. Conditions and Limitations

(A) No Trademark License- This license does not grant you rights to use any
contributors' name, logo, or trademarks.

(B) If you bring a patent claim against any contributor over patents that you
claim are infringed by the software, your patent license from such contributor
to the software ends automatically.

(C) If you distribute any portion of the software, you must retain all
copyright, patent, trademark, and attribution notices that are present in the
software.

(D) If you distribute any portion of the software in source code form, you may
do so only under this license by including a complete copy of this license with
your distribution. If you distribute any portion of the software in compiled or
object code form, you may only do so under a license that complies with this
license.

(E) The software is licensed "as-is." You bear the risk of using it. The
contributors give no express warranties, guarantees or conditions. You may have
additional consumer rights under your local laws which this license cannot
change. To the extent permitted under your local laws, the contributors exclude
the implied warranties of merchantability, fitness for a particular purpose and
non-infringement.
```

### A.4 Zlib License

Applies to **SDL2**, **NativeFileDialogNET**, and **nativefiledialog-extended**.
The operative copyright line for SDL2 is shown in
[§6](#6-zlib-licensed-components); the nativefiledialog `LICENSE` ships the
permission text below without a separate copyright line.

```
zlib License

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
```

### A.5 GNU Library General Public License, Version 2

Applies to **OpenAL Soft 1.24.3** (bundled by MonoGame DesktopGL). Reproduced
verbatim from the OpenAL Soft `COPYING` file. See the dynamic-linking / relink
notice in [§7](#7-openal-soft--gnu-library-general-public-license).

```
                  GNU LIBRARY GENERAL PUBLIC LICENSE
                       Version 2, June 1991

 Copyright (C) 1991 Free Software Foundation, Inc.
 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
 Everyone is permitted to copy and distribute verbatim copies
 of this license document, but changing it is not allowed.

[This is the first released version of the library GPL.  It is
 numbered 2 because it goes with version 2 of the ordinary GPL.]

                            Preamble

  The licenses for most software are designed to take away your
freedom to share and change it.  By contrast, the GNU General Public
Licenses are intended to guarantee your freedom to share and change
free software--to make sure the software is free for all its users.

  This license, the Library General Public License, applies to some
specially designated Free Software Foundation software, and to any
other libraries whose authors decide to use it.  You can use it for
your libraries, too.

  When we speak of free software, we are referring to freedom, not
price.  Our General Public Licenses are designed to make sure that you
have the freedom to distribute copies of free software (and charge for
this service if you wish), that you receive source code or can get it
if you want it, that you can change the software or use pieces of it
in new free programs; and that you know you can do these things.

  To protect your rights, we need to make restrictions that forbid
anyone to deny you these rights or to ask you to surrender the rights.
These restrictions translate to certain responsibilities for you if
you distribute copies of the library, or if you modify it.

  For example, if you distribute copies of the library, whether gratis
or for a fee, you must give the recipients all the rights that we gave
you.  You must make sure that they, too, receive or can get the source
code.  If you link a program with the library, you must provide
complete object files to the recipients so that they can relink them
with the library, after making changes to the library and recompiling
it.  And you must show them these terms so they know their rights.

  Our method of protecting your rights has two steps: (1) copyright
the library, and (2) offer you this license which gives you legal
permission to copy, distribute and/or modify the library.

  Also, for each distributor's protection, we want to make certain
that everyone understands that there is no warranty for this free
library.  If the library is modified by someone else and passed on, we
want its recipients to know that what they have is not the original
version, so that any problems introduced by others will not reflect on
the original authors' reputations.

  Finally, any free program is threatened constantly by software
patents.  We wish to avoid the danger that companies distributing free
software will individually obtain patent licenses, thus in effect
transforming the program into proprietary software.  To prevent this,
we have made it clear that any patent must be licensed for everyone's
free use or not licensed at all.

  Most GNU software, including some libraries, is covered by the ordinary
GNU General Public License, which was designed for utility programs.  This
license, the GNU Library General Public License, applies to certain
designated libraries.  This license is quite different from the ordinary
one; be sure to read it in full, and don't assume that anything in it is
the same as in the ordinary license.

  The reason we have a separate public license for some libraries is that
they blur the distinction we usually make between modifying or adding to a
program and simply using it.  Linking a program with a library, without
changing the library, is in some sense simply using the library, and is
analogous to running a utility program or application program.  However, in
a textual and legal sense, the linked executable is a combined work, a
derivative of the original library, and the ordinary General Public License
treats it as such.

  Because of this blurred distinction, using the ordinary General
Public License for libraries did not effectively promote software
sharing, because most developers did not use the libraries.  We
concluded that weaker conditions might promote sharing better.

  However, unrestricted linking of non-free programs would deprive the
users of those programs of all benefit from the free status of the
libraries themselves.  This Library General Public License is intended to
permit developers of non-free programs to use free libraries, while
preserving your freedom as a user of such programs to change the free
libraries that are incorporated in them.  (We have not seen how to achieve
this as regards changes in header files, but we have achieved it as regards
changes in the actual functions of the Library.)  The hope is that this
will lead to faster development of free libraries.

  The precise terms and conditions for copying, distribution and
modification follow.  Pay close attention to the difference between a
"work based on the library" and a "work that uses the library".  The
former contains code derived from the library, while the latter only
works together with the library.

  Note that it is possible for a library to be covered by the ordinary
General Public License rather than by this special one.

                  GNU LIBRARY GENERAL PUBLIC LICENSE
   TERMS AND CONDITIONS FOR COPYING, DISTRIBUTION AND MODIFICATION

  0. This License Agreement applies to any software library which
contains a notice placed by the copyright holder or other authorized
party saying it may be distributed under the terms of this Library
General Public License (also called "this License").  Each licensee is
addressed as "you".

  A "library" means a collection of software functions and/or data
prepared so as to be conveniently linked with application programs
(which use some of those functions and data) to form executables.

  The "Library", below, refers to any such software library or work
which has been distributed under these terms.  A "work based on the
Library" means either the Library or any derivative work under
copyright law: that is to say, a work containing the Library or a
portion of it, either verbatim or with modifications and/or translated
straightforwardly into another language.  (Hereinafter, translation is
included without limitation in the term "modification".)

  "Source code" for a work means the preferred form of the work for
making modifications to it.  For a library, complete source code means
all the source code for all modules it contains, plus any associated
interface definition files, plus the scripts used to control compilation
and installation of the library.

  Activities other than copying, distribution and modification are not
covered by this License; they are outside its scope.  The act of
running a program using the Library is not restricted, and output from
such a program is covered only if its contents constitute a work based
on the Library (independent of the use of the Library in a tool for
writing it).  Whether that is true depends on what the Library does
and what the program that uses the Library does.
  
  1. You may copy and distribute verbatim copies of the Library's
complete source code as you receive it, in any medium, provided that
you conspicuously and appropriately publish on each copy an
appropriate copyright notice and disclaimer of warranty; keep intact
all the notices that refer to this License and to the absence of any
warranty; and distribute a copy of this License along with the
Library.

  You may charge a fee for the physical act of transferring a copy,
and you may at your option offer warranty protection in exchange for a
fee.

  2. You may modify your copy or copies of the Library or any portion
of it, thus forming a work based on the Library, and copy and
distribute such modifications or work under the terms of Section 1
above, provided that you also meet all of these conditions:

    a) The modified work must itself be a software library.

    b) You must cause the files modified to carry prominent notices
    stating that you changed the files and the date of any change.

    c) You must cause the whole of the work to be licensed at no
    charge to all third parties under the terms of this License.

    d) If a facility in the modified Library refers to a function or a
    table of data to be supplied by an application program that uses
    the facility, other than as an argument passed when the facility
    is invoked, then you must make a good faith effort to ensure that,
    in the event an application does not supply such function or
    table, the facility still operates, and performs whatever part of
    its purpose remains meaningful.

    (For example, a function in a library to compute square roots has
    a purpose that is entirely well-defined independent of the
    application.  Therefore, Subsection 2d requires that any
    application-supplied function or table used by this function must
    be optional: if the application does not supply it, the square
    root function must still compute square roots.)

These requirements apply to the modified work as a whole.  If
identifiable sections of that work are not derived from the Library,
and can be reasonably considered independent and separate works in
themselves, then this License, and its terms, do not apply to those
sections when you distribute them as separate works.  But when you
distribute the same sections as part of a whole which is a work based
on the Library, the distribution of the whole must be on the terms of
this License, whose permissions for other licensees extend to the
entire whole, and thus to each and every part regardless of who wrote
it.

Thus, it is not the intent of this section to claim rights or contest
your rights to work written entirely by you; rather, the intent is to
exercise the right to control the distribution of derivative or
collective works based on the Library.

In addition, mere aggregation of another work not based on the Library
with the Library (or with a work based on the Library) on a volume of
a storage or distribution medium does not bring the other work under
the scope of this License.

  3. You may opt to apply the terms of the ordinary GNU General Public
License instead of this License to a given copy of the Library.  To do
this, you must alter all the notices that refer to this License, so
that they refer to the ordinary GNU General Public License, version 2,
instead of to this License.  (If a newer version than version 2 of the
ordinary GNU General Public License has appeared, then you can specify
that version instead if you wish.)  Do not make any other change in
these notices.

  Once this change is made in a given copy, it is irreversible for
that copy, so the ordinary GNU General Public License applies to all
subsequent copies and derivative works made from that copy.

  This option is useful when you wish to copy part of the code of
the Library into a program that is not a library.

  4. You may copy and distribute the Library (or a portion or
derivative of it, under Section 2) in object code or executable form
under the terms of Sections 1 and 2 above provided that you accompany
it with the complete corresponding machine-readable source code, which
must be distributed under the terms of Sections 1 and 2 above on a
medium customarily used for software interchange.

  If distribution of object code is made by offering access to copy
from a designated place, then offering equivalent access to copy the
source code from the same place satisfies the requirement to
distribute the source code, even though third parties are not
compelled to copy the source along with the object code.

  5. A program that contains no derivative of any portion of the
Library, but is designed to work with the Library by being compiled or
linked with it, is called a "work that uses the Library".  Such a
work, in isolation, is not a derivative work of the Library, and
therefore falls outside the scope of this License.

  However, linking a "work that uses the Library" with the Library
creates an executable that is a derivative of the Library (because it
contains portions of the Library), rather than a "work that uses the
library".  The executable is therefore covered by this License.
Section 6 states terms for distribution of such executables.

  When a "work that uses the Library" uses material from a header file
that is part of the Library, the object code for the work may be a
derivative work of the Library even though the source code is not.
Whether this is true is especially significant if the work can be
linked without the Library, or if the work is itself a library.  The
threshold for this to be true is not precisely defined by law.

  If such an object file uses only numerical parameters, data
structure layouts and accessors, and small macros and small inline
functions (ten lines or less in length), then the use of the object
file is unrestricted, regardless of whether it is legally a derivative
work.  (Executables containing this object code plus portions of the
Library will still fall under Section 6.)

  Otherwise, if the work is a derivative of the Library, you may
distribute the object code for the work under the terms of Section 6.
Any executables containing that work also fall under Section 6,
whether or not they are linked directly with the Library itself.

  6. As an exception to the Sections above, you may also compile or
link a "work that uses the Library" with the Library to produce a
work containing portions of the Library, and distribute that work
under terms of your choice, provided that the terms permit
modification of the work for the customer's own use and reverse
engineering for debugging such modifications.

  You must give prominent notice with each copy of the work that the
Library is used in it and that the Library and its use are covered by
this License.  You must supply a copy of this License.  If the work
during execution displays copyright notices, you must include the
copyright notice for the Library among them, as well as a reference
directing the user to the copy of this License.  Also, you must do one
of these things:

    a) Accompany the work with the complete corresponding
    machine-readable source code for the Library including whatever
    changes were used in the work (which must be distributed under
    Sections 1 and 2 above); and, if the work is an executable linked
    with the Library, with the complete machine-readable "work that
    uses the Library", as object code and/or source code, so that the
    user can modify the Library and then relink to produce a modified
    executable containing the modified Library.  (It is understood
    that the user who changes the contents of definitions files in the
    Library will not necessarily be able to recompile the application
    to use the modified definitions.)

    b) Accompany the work with a written offer, valid for at
    least three years, to give the same user the materials
    specified in Subsection 6a, above, for a charge no more
    than the cost of performing this distribution.

    c) If distribution of the work is made by offering access to copy
    from a designated place, offer equivalent access to copy the above
    specified materials from the same place.

    d) Verify that the user has already received a copy of these
    materials or that you have already sent this user a copy.

  For an executable, the required form of the "work that uses the
Library" must include any data and utility programs needed for
reproducing the executable from it.  However, as a special exception,
the source code distributed need not include anything that is normally
distributed (in either source or binary form) with the major
components (compiler, kernel, and so on) of the operating system on
which the executable runs, unless that component itself accompanies
the executable.

  It may happen that this requirement contradicts the license
restrictions of other proprietary libraries that do not normally
accompany the operating system.  Such a contradiction means you cannot
use both them and the Library together in an executable that you
distribute.

  7. You may place library facilities that are a work based on the
Library side-by-side in a single library together with other library
facilities not covered by this License, and distribute such a combined
library, provided that the separate distribution of the work based on
the Library and of the other library facilities is otherwise
permitted, and provided that you do these two things:

    a) Accompany the combined library with a copy of the same work
    based on the Library, uncombined with any other library
    facilities.  This must be distributed under the terms of the
    Sections above.

    b) Give prominent notice with the combined library of the fact
    that part of it is a work based on the Library, and explaining
    where to find the accompanying uncombined form of the same work.

  8. You may not copy, modify, sublicense, link with, or distribute
the Library except as expressly provided under this License.  Any
attempt otherwise to copy, modify, sublicense, link with, or
distribute the Library is void, and will automatically terminate your
rights under this License.  However, parties who have received copies,
or rights, from you under this License will not have their licenses
terminated so long as such parties remain in full compliance.

  9. You are not required to accept this License, since you have not
signed it.  However, nothing else grants you permission to modify or
distribute the Library or its derivative works.  These actions are
prohibited by law if you do not accept this License.  Therefore, by
modifying or distributing the Library (or any work based on the
Library), you indicate your acceptance of this License to do so, and
all its terms and conditions for copying, distributing or modifying
the Library or works based on it.

  10. Each time you redistribute the Library (or any work based on the
Library), the recipient automatically receives a license from the
original licensor to copy, distribute, link with or modify the Library
subject to these terms and conditions.  You may not impose any further
restrictions on the recipients' exercise of the rights granted herein.
You are not responsible for enforcing compliance by third parties to
this License.

  11. If, as a consequence of a court judgment or allegation of patent
infringement or for any other reason (not limited to patent issues),
conditions are imposed on you (whether by court order, agreement or
otherwise) that contradict the conditions of this License, they do not
excuse you from the conditions of this License.  If you cannot
distribute so as to satisfy simultaneously your obligations under this
License and any other pertinent obligations, then as a consequence you
may not distribute the Library at all.  For example, if a patent
license would not permit royalty-free redistribution of the Library by
all those who receive copies directly or indirectly through you, then
the only way you could satisfy both it and this License would be to
refrain entirely from distribution of the Library.

If any portion of this section is held invalid or unenforceable under any
particular circumstance, the balance of the section is intended to apply,
and the section as a whole is intended to apply in other circumstances.

It is not the purpose of this section to induce you to infringe any
patents or other property right claims or to contest validity of any
such claims; this section has the sole purpose of protecting the
integrity of the free software distribution system which is
implemented by public license practices.  Many people have made
generous contributions to the wide range of software distributed
through that system in reliance on consistent application of that
system; it is up to the author/donor to decide if he or she is willing
to distribute software through any other system and a licensee cannot
impose that choice.

This section is intended to make thoroughly clear what is believed to
be a consequence of the rest of this License.

  12. If the distribution and/or use of the Library is restricted in
certain countries either by patents or by copyrighted interfaces, the
original copyright holder who places the Library under this License may add
an explicit geographical distribution limitation excluding those countries,
so that distribution is permitted only in or among countries not thus
excluded.  In such case, this License incorporates the limitation as if
written in the body of this License.

  13. The Free Software Foundation may publish revised and/or new
versions of the Library General Public License from time to time.
Such new versions will be similar in spirit to the present version,
but may differ in detail to address new problems or concerns.

Each version is given a distinguishing version number.  If the Library
specifies a version number of this License which applies to it and
"any later version", you have the option of following the terms and
conditions either of that version or of any later version published by
the Free Software Foundation.  If the Library does not specify a
license version number, you may choose any version ever published by
the Free Software Foundation.

  14. If you wish to incorporate parts of the Library into other free
programs whose distribution conditions are incompatible with these,
write to the author to ask for permission.  For software which is
copyrighted by the Free Software Foundation, write to the Free
Software Foundation; we sometimes make exceptions for this.  Our
decision will be guided by the two goals of preserving the free status
of all derivatives of our free software and of promoting the sharing
and reuse of software generally.

                            NO WARRANTY

  15. BECAUSE THE LIBRARY IS LICENSED FREE OF CHARGE, THERE IS NO
WARRANTY FOR THE LIBRARY, TO THE EXTENT PERMITTED BY APPLICABLE LAW.
EXCEPT WHEN OTHERWISE STATED IN WRITING THE COPYRIGHT HOLDERS AND/OR
OTHER PARTIES PROVIDE THE LIBRARY "AS IS" WITHOUT WARRANTY OF ANY
KIND, EITHER EXPRESSED OR IMPLIED, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
PURPOSE.  THE ENTIRE RISK AS TO THE QUALITY AND PERFORMANCE OF THE
LIBRARY IS WITH YOU.  SHOULD THE LIBRARY PROVE DEFECTIVE, YOU ASSUME
THE COST OF ALL NECESSARY SERVICING, REPAIR OR CORRECTION.

  16. IN NO EVENT UNLESS REQUIRED BY APPLICABLE LAW OR AGREED TO IN
WRITING WILL ANY COPYRIGHT HOLDER, OR ANY OTHER PARTY WHO MAY MODIFY
AND/OR REDISTRIBUTE THE LIBRARY AS PERMITTED ABOVE, BE LIABLE TO YOU
FOR DAMAGES, INCLUDING ANY GENERAL, SPECIAL, INCIDENTAL OR
CONSEQUENTIAL DAMAGES ARISING OUT OF THE USE OR INABILITY TO USE THE
LIBRARY (INCLUDING BUT NOT LIMITED TO LOSS OF DATA OR DATA BEING
RENDERED INACCURATE OR LOSSES SUSTAINED BY YOU OR THIRD PARTIES OR A
FAILURE OF THE LIBRARY TO OPERATE WITH ANY OTHER SOFTWARE), EVEN IF
SUCH HOLDER OR OTHER PARTY HAS BEEN ADVISED OF THE POSSIBILITY OF SUCH
DAMAGES.

                     END OF TERMS AND CONDITIONS
```
