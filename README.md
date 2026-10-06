# KeysharpDocs

<p align="center">
  <img src="docs/static/keysharp_logo.png" width="128" height="128" alt="Keysharp logo">
</p>

KeysharpDocs is the reference documentation for
[Keysharp](https://github.com/keysharp-org/Keysharp), a cross-platform C#
implementation of the AutoHotkey v2 language.

> **Status:** Keysharp is under active development and is being brought up to
> AutoHotkey v2.1-alpha.33 compatibility. Windows currently has the broadest
> compatibility; Linux and macOS support continues to improve. Some inherited
> reference pages can describe functionality which has not yet been verified
> on every Keysharp platform.

- [Read KeysharpDocs online](https://keysharp-org.github.io/KeysharpDocs/)
- [Download Keysharp](https://github.com/keysharp-org/Keysharp/releases)
- [Keysharp source and issues](https://github.com/keysharp-org/Keysharp)
- [Documentation issues](https://github.com/keysharp-org/KeysharpDocs/issues)

## Independence and provenance

Keysharp is an independent project. It is not affiliated with, sponsored by,
or endorsed by the AutoHotkey project.

This repository was derived from
[AutoHotkeyDocs](https://github.com/AutoHotkey/AutoHotkeyDocs) with permission.
The original project history and copyright notices have been retained. See
[NOTICE.md](NOTICE.md) and the
[documentation provenance page](docs/license.htm) for details.

## Repository layout

- `docs/` contains the documentation site.
- `docs/static/` contains the shared navigation, search, styles, and images.
- `docs/static/source/` contains the generated navigation/search data and its
  maintenance scripts.
- `Project.hhp` and `compile_chm.ahk` build the optional Windows CHM help file.
- `scripts/Test-Docs.ps1` validates local links, branding invariants, and the
  generated site structure.

## Preview locally

From the repository root:

```powershell
python -m http.server 8000
```

Then open <http://localhost:8000/>.

Loading through a local web server is preferred over opening `index.html`
directly because the search and framed navigation use browser APIs that can be
restricted for `file://` pages.

## Validate changes

```powershell
pwsh ./scripts/Test-Docs.ps1
```

On Windows PowerShell 5.1, this also works:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/Test-Docs.ps1
```

The same validation runs for pull requests. Pushes to `alpha` deploy the static
site through GitHub Pages after validation succeeds.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) before editing inherited pages. In
particular, do not globally replace `AutoHotkey`: names such as
`#Requires AutoHotkey`, `.ahk`, `A_AhkVersion`, historical attribution, and
links to upstream specifications are intentional compatibility references.

Upstream documentation changes are reviewed and ported manually according to
[UPSTREAM.md](UPSTREAM.md).
