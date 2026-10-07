# KeysharpDocs Agent Guide

## Purpose

KeysharpDocs is the reference documentation for
[Keysharp](https://github.com/keysharp-org/Keysharp), an independent,
cross-platform C# implementation of the AutoHotkey v2 language. Keysharp
parses `.ahk` and `.ks` source, lowers it to C#, compiles it with Roslyn, and
runs the resulting .NET assembly.

This repository began as the AutoHotkeyDocs `alpha` branch with permission
from the AutoHotkey team. Its current compatibility target is AutoHotkey
v2.1-alpha.33. That target defines intended language semantics; it does not
prove that every feature is implemented on every Keysharp platform.

Windows currently has the broadest coverage. Linux support varies between X11
and Wayland compositors and can depend on installed helpers. macOS support can
depend on Accessibility, Input Monitoring, and Screen Recording permissions.
Treat platform support as a property to verify, not infer.

Keysharp and KeysharpDocs are not affiliated with or endorsed by AutoHotkey.
Retain the independence and provenance statements in the site.

## Repository map

- `docs/` contains the source documentation.
- `docs/lib/` contains most function, directive, object, and statement pages.
- `docs/howto/` and `docs/misc/` contain tutorials and conceptual material.
- `docs/static/` contains the shared page shell, theme, search, and assets.
- `docs/static/source/data_toc.js` is the manually maintained table of
  contents.
- `docs/static/source/data_index.js` is the manually maintained keyword index.
- `docs/static/source/data_search.js` is generated; do not hand-edit it.
- `docs/index.htm` is the production homepage.
- Root `index.html` is a source-tree convenience redirect and is not included
  in the Pages artifact. Do not edit it to change the production homepage.
- `.github/workflows/docs.yml` copies the contents of `docs/` to the root of
  the Pages artifact. Production URLs therefore do not contain `/docs/`.
- `Project.hhp` and `compile_chm.ahk` define the optional Windows CHM build.
- `scripts/Test-Docs.ps1` checks required files, local links, titles, and
  branding invariants.
- `SOURCE_OF_TRUTH.md` defines how claims are evidenced and how generated API
  inventories must be interpreted.
- `audits/` contains generated, internal source-to-documentation review
  queues. They are not public capability matrices.
- `scripts/Update-SourceAudit.ps1` regenerates the host platform's global and
  built-in class-member inventories from a compiled Keysharp runtime.
- `AutoHotkeyChangeLog.htm` and `AutoHotkeyLicense.htm` are archived upstream
  pages. Do not present them as Keysharp release or license information.

Also read `CONTRIBUTING.md` before semantic edits and `UPSTREAM.md` before
porting an AutoHotkeyDocs change. Read `SOURCE_OF_TRUTH.md` before changing
behavioral or platform claims.

## Source of truth

Documentation must describe Keysharp behavior, not merely repeat the
compatibility target.

Use evidence in this order:

1. A reproducible script run with the current Keysharp build on the named
   platform.
2. Keysharp source and tests.
3. `docs/reference.md` in the Keysharp repository. This is the maintained
   source for current platform, implementation and compatibility notes while
   its content is migrated into KeysharpDocs.
4. `docs/capabilities.json` in the Keysharp repository, but only after the
   relevant row has been independently reviewed. The rendered
   `docs/capabilities.md` matrix is generated from this currently unreviewed
   data and is not evidence by itself.
5. AutoHotkeyDocs and AutoHotkey behavior, but only as the intended
   compatibility specification or a historical reference.

Do not turn `Unknown` into `Unsupported`, or `Partial` into a vague statement
that a feature "works." State the verified boundary:

- **Full**: implemented and generally usable on the named platform.
- **Partial**: implemented, with the exact missing behavior or prerequisite
  stated.
- **Planned/unsupported**: unavailable; say whether it is intentionally
  unsupported or simply not implemented yet when that is known.
- **Unknown/unverified**: no reliable result is available. Do not guess.

If the evidence conflicts, prefer the tested current runtime and update or
report the stale capability data separately.

The generated source audit establishes discoverable API names only.
`locator-found` means that a structural documentation locator was found; it
does not establish that the locator fully documents the member, correct
behavior, or platform support. Do not turn its review queue into a
compatibility claim.

## Keysharp and AutoHotkey wording

Use **Keysharp** for the implementation, executable, installer, runtime
behavior, downloads, issue tracker, and project community.

On KeysharpDocs reference pages, the subject is already implicit. Do not
prefix ordinary statements with "In Keysharp" or repeatedly name Keysharp in
descriptions of its own behavior. Use the name when contrasting Keysharp with
AutoHotkey, identifying a Keysharp-specific component or external resource,
or avoiding genuine ambiguity. Otherwise prefer "this function", "the
runtime", "the script's process", or a direct statement of the behavior.

Do not globally replace `AutoHotkey` or `AHK`. Preserve names and references
which are part of compatibility or attribution, including:

- `#Requires AutoHotkey`;
- `.ahk` file names;
- `A_AhkVersion`, `A_AhkPath`, `ahk_class`, and similar compatibility names;
- comparisons between AutoHotkey v1, v2.0, and v2.1;
- upstream issues, pull requests, changelogs, forums, and specifications; and
- copyright, acknowledgements, and provenance.

When behavior differs, identify the subject explicitly:

> In AutoHotkey v2.1, ... . In Keysharp, ... .

Do not silently rewrite an inherited AutoHotkey rule into a Keysharp rule if
the difference matters to compatible scripts.

## Adding platform-specific material

Platform material should answer a user-visible question: Is the feature
available? Does it behave differently? Does it require permission, a helper,
or a particular desktop backend? Avoid implementation trivia which does not
change setup, observable behavior, portability, security, or performance.

### Inherited placement and Keysharp visibility

AutoHotkeyDocs organizes qualifications by the part of the contract they
affect. Preserve that semantic placement, while using the visibility
conventions below for restrictions which cross-platform readers need to find
quickly:

| The difference affects... | Put it... |
| --- | --- |
| The availability of the whole feature | In a `note` immediately after the description or syntax |
| One parameter | In that parameter's `<dd>` |
| The returned value | In `Return Value` |
| Whether or how the operation fails | In `Error Handling` |
| General runtime behavior | Under the named platform's subsection in `Remarks` |
| One example | In that example's description, before the code |
| A lengthy setup procedure | In the relevant how-to or platform guide, linked from the reference page |

Write the common contract once, then qualify it at the narrowest accurate
scope. Do not move a parameter-specific limitation into `Remarks` merely
because it concerns an operating system. Conversely, state a whole-feature
restriction early enough that a reader will see it before planning around the
API.

Ordinary prose is the default for a qualification scoped to a parameter,
return value, error, or example:

```html
<p>On Linux, this behavior depends on the active desktop backend.</p>
```

Name the observed result when known: compile-time error, thrown exception,
empty value, no-op, unavailable integration, or different behavior. Do not
write only "Windows-specific" or "not fully supported."

### Notes and warnings express importance

Use the inherited `note` and `warning` styles according to their editorial
meaning, not merely because a statement is platform-specific:

- Use ordinary prose for a normal qualification.
- Use `note` for supplemental information or a prerequisite which is
  important and easy to miss, including whole-feature platform availability.
- Use `warning` for a high-consequence trap, such as data loss, a security or
  privacy concern, or a surprising hard failure.

Keep the conventional label and put the platform in the sentence:

```html
<p class="note"><strong>Note:</strong> On macOS, reading the screen requires
Screen Recording permission.</p>

<p class="note"><strong>Note:</strong> This function is available only
on Windows.</p>
```

Do not introduce `Platform:`, `Windows:`, `Linux:`, or `macOS:` as a separate
callout taxonomy. Treat whole-feature unavailability as an availability
notice, not a warning. Use `warning` only when the consequence itself warrants
that severity.

### Subsections and tables

For general platform-dependent behavior in `Remarks`, make the operating
system directly scannable:

```html
<h3 id="Linux">Linux</h3>
<p><strong>X11:</strong> Describe the verified behavior.</p>
<p><strong>Wayland:</strong> Describe the verified behavior and backend
requirements.</p>

<h3 id="macOS">macOS</h3>
<p>Describe the verified behavior and permission requirements.</p>
```

Use only the platform headings needed by that page, in the standard Windows,
Linux, macOS order. Within Linux, use bold **X11:** and **Wayland:** lead-ins
for short differences; use H4 subsections when each backend needs multiple
paragraphs. Avoid generic headings such as `Platform Behavior` and `Platform
Notes`, which require readers to inspect the content before finding their
operating system.

Use `table.info` only when readers need to compare the same attributes across
platforms or backends. The number of platforms is not a threshold: two dense,
parallel cases may justify a table, while four unrelated caveats may be
clearer as prose beside the behavior each one affects. Avoid empty cells,
repeated boilerplate, and a full copy of the capability matrix. Put platform
names in the first column so the table remains scannable.

If the project needs a uniform, highly visible availability summary on many
pages, treat that as a separate design change. Define a neutral shared
component, its status vocabulary, and its source-of-truth/update process;
preview it in the static site and CHM; and pilot it across representative API
families before adopting it repository-wide. Do not approximate such a
component by overloading `note` or `warning`.

### Keep documentation responsibilities separate

- An individual reference page describes the local, observable contract a
  reader needs to use that feature.
- The capability matrix inventories implementation status and broad coverage.
- Platform and how-to guides explain shared setup, permissions, helpers, and
  backend limitations in depth.

Link between those layers instead of copying the same explanation into each
one. A page may summarize the portion needed to use its feature, but it should
not embed a second capability matrix or repeat a long setup procedure.

### Keysharp platform terminology

The following are Keysharp-specific consistency rules, not inherited
AutoHotkeyDocs conventions:

- When several platforms are presented together, use the order **Windows,
  Linux, macOS**.
- Within Linux, use **X11, Wayland**. Split Wayland into GNOME, KDE/KWin,
  Cinnamon, or another compositor only when the distinction changes the
  documented result.
- Do not use "Unix" as a synonym for Linux and macOS unless the statement was
  verified for both.
- Do not generalize a result from one Wayland compositor to all Wayland
  environments.

### Availability is not the same as prerequisites

Keep these ideas separate:

- "Supported on macOS; requires Accessibility permission."
- "Supported on Linux when `keysharp-input` is installed."
- "Partially supported on Wayland; activation depends on the compositor
  protocol."
- "Unverified on macOS."

A required permission or privileged helper does not automatically make a
feature partial. Conversely, successful parsing or compilation does not prove
that the native operation works.

### Keep platform material concise

- Lead with the difference, then the reason only if it helps the user.
- Prefer one exact limitation over a list of internal backends.
- Link to the Keysharp platform reference for lengthy installation steps.
- Avoid repeating the full capability matrix on individual pages.
- Include versions only when behavior actually changed at that boundary.
- State tested compositor or permission assumptions in the pull request when
  they would clutter the reference page.

## Page editing conventions

- Documentation pages are HTML, not Markdown.
- Page titles end with `| Keysharp`, except the explicitly archived
  AutoHotkey changelog and license pages.
- Preserve the existing heading hierarchy and stable anchor IDs.
- Put platform qualifications in the section for the part of the contract
  they affect; do not collect them in `Remarks` by default.
- Put runnable material under `Examples`.
- Use `<code>` for identifiers and inline code, `<pre>` for Keysharp script
  examples, and `<pre class="no-highlight">` for shell commands or plain text.
- Use relative links between documentation pages.
- Use current Keysharp/KeysharpDocs links for product and support actions.
- Retain upstream links when they are citations, specifications, history, or
  attribution, and make their context clear.
- Prefer the existing `note`, `warning`, `info`, `Syntax`, and `NoIndent`
  styles. Do not introduce a one-off visual language for platform notes.
- Keep HTML and CSS compatible with the static site and CHM viewer. Do not add
  a framework, build-time dependency, web font, remote script, or feature
  which is required for the content to remain readable.
- Keep the light and dark themes usable. Do not encode platform status by
  color alone.
- Use plain platform names instead of emoji or icon-only badges; they survive
  search, translation, accessibility tools, printing, and CHM rendering.

When an inherited paragraph is correct for Keysharp, leave it intact apart
from necessary Keysharp subject wording. Small, reviewable addenda are easier
to audit and port than broad rewrites.

Do not add a platform label merely because an inherited page mentions
Windows. Determine whether it means the Windows operating system, a GUI
window, or historical AutoHotkey behavior, and whether Keysharp actually
differs. If a whole family of pages shares one limitation, keep each page's
local statement short and link to one maintained explanation instead of
copying a large platform essay everywhere.

### The change log

`docs/ChangeLog.htm` is running documentation of what a release provides. It
is not a history of how the implementation arrived there. Describe the current
contract and link to the reference page which documents it.

- Reference earlier behavior only where a reader must recognize their own code
  in order to change it. That is what `Breaking Changes` is for, and there an
  old-to-new mapping is the point: name the previous spelling, parameter, or
  return value alongside the current one.
- Elsewhere, state what a feature does, not what it used to do. Do not narrate
  that something was previously a no-op, a stub, unimplemented, parsed and
  ignored, or broken. Write "the `Break` menu option is supported", not "is
  implemented rather than parsed and ignored".
- Do not describe a feature which was added and then removed within the same
  unreleased cycle. It never shipped, so it is neither new nor a breaking
  change. Document only the state the release ships in.
- Avoid "now" and "finally" as framing. A dated release section already
  establishes when the behavior arrived.
- Keep `Fixes and Performance` to a short paragraph naming what works, without
  diagnosing the defect.

Before listing an addition, verify it against the previous release tag in the
Keysharp repository. A new row in `docs/capabilities.md` frequently records a
pre-existing API which was only now inventoried, not a new feature; confirm
that the symbol was actually absent at the previous tag.

## Examples

- Prefer a portable example first.
- Label an OS-specific example in its prose and comments.
- Use `.ks` for explicitly Keysharp-oriented file examples; retain `.ahk`
  where compatibility or editor tooling is the point.
- Do not claim that an example is cross-platform unless it was verified on
  each named platform.
- If an example requires elevation, Accessibility, Input Monitoring, Screen
  Recording, a Linux helper, or a desktop extension, state that before the
  code.
- Keep examples safe to run. Do not make destructive filesystem, registry, or
  process changes without an explicit warning and narrowly scoped target.

## Adding or moving pages

After adding, removing, or renaming a page:

1. Update `docs/static/source/data_toc.js`.
2. Add useful terms to `docs/static/source/data_index.js`.
3. Add the file or asset to `Project.hhp` when the CHM build needs an explicit
   entry.
4. Regenerate `docs/static/source/data_search.js` on Windows:

   ```powershell
   & "C:\Program Files\AutoHotkey\v2\AutoHotkey32.exe" `
     .\docs\static\source\build_search.ahk
   ```

The inherited search builder requires the 32-bit AutoHotkey v2 executable
because it uses the Windows HTML document COM component. If that tool is not
available, do not fabricate or hand-edit generated search data; disclose that
the regeneration is pending.

## Validation

Run the repository validator from the root:

```powershell
pwsh ./scripts/Test-Docs.ps1
```

Windows PowerShell 5.1 is also supported:

```powershell
powershell -ExecutionPolicy Bypass -File ./scripts/Test-Docs.ps1
```

Preview through a local server:

```powershell
python -m http.server 8000
```

Then check the changed page through `http://localhost:8000/`. For layout or
shell changes, also check:

- sidebar navigation, index, and search;
- direct page loading and back/forward navigation;
- light and dark schemes;
- a narrow/mobile viewport;
- code highlighting and example download controls;
- local and changed external links; and
- the Keysharp independence/compatibility banner and footer.

The Pages workflow serves `docs/*` at the production root. If deployment
packaging changes, build the same `_site` layout locally and verify that `/`,
`/static/keysharp_logo.png`, and at least one nested reference page return
without a redirect.

## Upstream and scope discipline

- Port AutoHotkeyDocs changes manually; do not use GitHub's **Sync fork**
  button or merge the upstream branch wholesale.
- Verify new upstream behavior exists in Keysharp before documenting it as
  implemented.
- Preserve Keysharp branding, navigation, title suffixes, repository links,
  compatibility notices, and Pages workflow.
- Keep an upstream semantic port separate from unrelated Keysharp-specific
  documentation when practical.
- Do not modify Keysharp runtime code from this repository. Report or fix
  runtime gaps in the Keysharp repository, then document the resulting
  behavior here.
- Do not invent a documentation license. Preserve `NOTICE.md`,
  `docs/license.htm`, and the archived upstream attribution.

## Git and deployment

- `alpha` is the maintained default branch.
- Pull requests to `alpha` run documentation validation.
- Pushes to `alpha` validate and deploy the production Pages site.
- Treat a push to `alpha` as a production deployment; do not push merely to
  preview work or unless the task authorizes deployment.
- Keep the worktree's unrelated changes intact.
- Prefer a focused documentation commit over mixing generated churn,
  upstream ports, branding changes, and semantic edits.
- After changing the AutoHotkey compatibility target, update the homepage,
  README, change page, every reference page which states the target, and this
  guide together.

## Completion checklist

Before finishing a documentation change, confirm:

- the claim is supported by current Keysharp evidence;
- `Keysharp` and `AutoHotkey` are used in the correct roles;
- availability, prerequisites, and unverified status are not conflated;
- platform material uses the smallest readable pattern from this guide;
- headings, anchors, titles, local links, TOC, index, and search data are
  consistent;
- the page remains readable without remote assets or modern-only browser
  features;
- `scripts/Test-Docs.ps1` passes; and
- the site was previewed in proportion to the visual risk of the change.
