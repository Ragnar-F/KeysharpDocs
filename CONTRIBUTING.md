# Contributing to KeysharpDocs

Thank you for helping document Keysharp.

## Source of truth

Documentation should describe observed Keysharp behavior. AutoHotkey
v2.1-alpha.33 is the compatibility target and a valuable language reference,
but it is not evidence that a feature is already implemented on every Keysharp
platform.

For current implementation status, consult:

- the [Keysharp platform reference](https://github.com/keysharp-org/Keysharp/blob/master/docs/reference.md);
- the Keysharp source and tests; and
- a reproducible script run against the current Keysharp build.

The capability matrix is pending a thorough review. Do not use it as evidence
for a documentation claim unless the relevant entry has been independently
verified against the current runtime, source or tests.

See [`SOURCE_OF_TRUTH.md`](SOURCE_OF_TRUTH.md) for the evidence model and the
generated source-to-documentation audit workflow. The audit establishes
candidate script-visible names, not behavioral or platform support.

If support is partial or unverified, say so explicitly. Prefer a narrowly
scoped qualification near the relevant behavior over a broad claim that the
whole page is cross-platform. Put each platform qualification in the section
for the part of the contract it affects: a parameter, return value, error,
general behavior, or example. Ordinary prose is the default; use a note for
important supplemental information which is easy to miss, including an
entire API which is unavailable on a supported platform. Reserve warnings for
high-consequence traps. In `Remarks`, put general OS-dependent behavior under
`Windows`, `Linux`, or `macOS` H3 headings rather than a generic `Platform
Behavior` heading.

## Preserve compatibility names

Do not globally replace `AutoHotkey` or `AHK`. These can be intentional:

- `#Requires AutoHotkey`
- `.ahk` file names
- `A_AhkVersion`, `A_AhkPath`, and other compatibility variables
- `ahk_class` and related window matching syntax
- historical comparisons with AutoHotkey v1 or v2
- links to upstream specifications, issues, pull requests, and forum material
- acknowledgements and copyright notices

Use **Keysharp** when describing this implementation, executable, installer,
runtime behavior, downloads, issue tracker, or project community.

In KeysharpDocs reference pages, Keysharp is already the implied subject. Use
the product name when contrasting it with AutoHotkey, naming a
Keysharp-specific component or resource, or avoiding ambiguity; do not prefix
ordinary behavior with "In Keysharp."

## Page titles and links

- Page titles must end with `| Keysharp`.
- Product and support links should use the current Keysharp or KeysharpDocs
  repository.
- Upstream citations should remain pointed at their original AutoHotkey source
  and should be described as upstream references.
- Do not remove the independence or compatibility notices.

## Generated data

After adding, removing, or renaming a page:

1. Update `docs/static/source/data_toc.js`.
2. Update `docs/static/source/data_index.js` when an index entry is useful.
3. Rebuild `docs/static/source/data_search.js`:

   ```powershell
   & "C:\Program Files\AutoHotkey\v2\AutoHotkey32.exe" `
     .\docs\static\source\build_search.ahk
   ```

The upstream search builder currently depends on the Windows HTML document COM
component and therefore requires the 32-bit AutoHotkey v2 executable.

## Validation

Run:

```powershell
pwsh ./scripts/Test-Docs.ps1
```

Also preview the site through a local HTTP server and check:

- sidebar navigation, index, and search;
- light and dark modes;
- narrow/mobile layouts;
- the page edit link;
- code highlighting and downloaded examples; and
- any changed external links.

## Pull requests

Keep branding/shell changes separate from large semantic ports when practical.
State which platforms and Keysharp build were used to verify behavioral
changes. Do not combine an unreviewed upstream sync with unrelated
Keysharp-specific documentation.
