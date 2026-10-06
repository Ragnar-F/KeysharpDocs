using System.Net;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

const string HiddenAttribute = "Keysharp.Runtime.PublicHiddenFromUser";
const string GlobalClassHiddenAttribute = "Keysharp.Runtime.GlobalClassHiddenFromUser";
const string StaticAttribute = "Keysharp.Runtime.StaticAttribute";
const string UserNameAttribute = "Keysharp.Runtime.UserDeclaredNameAttribute";
const string ClassStaticPrefix = "static";

var options = ParseOptions(args);
var scope = options.TryGetValue("scope", out var requestedScope)
    ? requestedScope.ToLowerInvariant()
    : "global";
if (scope is not ("global" or "class-members" or "module-exports"))
    throw new ArgumentException("--scope must be global, class-members, or module-exports.");
var assemblyPath = RequiredPath(options, "assembly", File.Exists);
var sourceRepo = RequiredPath(options, "source-repo", Directory.Exists);
var docsRepo = RequiredPath(options, "docs-repo", Directory.Exists);
var docsRoot = RequiredPath(options, "docs", Directory.Exists);
var jsonOutput = RequiredOption(options, "json");
var markdownOutput = RequiredOption(options, "markdown");

var docsCatalog = BuildDocumentationCatalog(docsRoot);
var loadContext = new InspectionLoadContext(assemblyPath);

try
{
    var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
    var candidates = scope switch
    {
        "global" => BuildGlobalApiEntries(assembly),
        "class-members" => BuildClassMemberEntries(assembly),
        _ => BuildModuleExportEntries(assembly),
    };
    var entries = candidates
        .Select(entry =>
        {
            var documentation = scope == "module-exports"
                ? docsCatalog.FindModuleExport(entry.Owner, entry.Name, entry.Kind)
                : entry.Owner is null
                    ? docsCatalog.Find(entry.Name)
                    : docsCatalog.FindMember(entry.Owner, entry.Name, entry.Kind, entry.Placement!);
            return entry with
            {
                Documentation = documentation,
                DocumentationStatus = documentation.Count > 0
                    ? "locator-found"
                    : "needs-review"
            };
        })
        .OrderBy(entry => entry.Kind, StringComparer.Ordinal)
        .ThenBy(entry => entry.Owner, StringComparer.OrdinalIgnoreCase)
        .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    var sourceState = ReadRepositoryState(sourceRepo);
    var docsState = ReadRepositoryState(docsRepo);
    var inventory = new Inventory(
        4,
        scope,
        scope switch
        {
            "global" => "Global class objects, functions, and properties",
            "class-members" => "Declared methods and properties of built-in classes",
            _ => "Built-in modules and their importable exports",
        },
        new Evidence(
            sourceState.Remote,
            sourceState.Commit,
            sourceState.TreeState,
            docsState.Commit,
            docsState.TreeState,
            GetPlatformName(),
            RuntimeInformation.FrameworkDescription,
            Path.GetFileName(assemblyPath),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))).ToLowerInvariant()),
        BuildCounts(entries, scope),
        entries);

    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonOutput))!);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(markdownOutput))!);

    var json = JsonSerializer.Serialize(inventory, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
    File.WriteAllText(jsonOutput, json + Environment.NewLine, new UTF8Encoding(false));
    File.WriteAllText(markdownOutput, BuildMarkdown(inventory), new UTF8Encoding(false));

    Console.WriteLine(
        $"Catalogued {entries.Length} API names: " +
        $"{inventory.Counts.WithLocator} with a documentation locator, " +
        $"{inventory.Counts.NeedsReview} needing review.");
    Console.WriteLine($"JSON: {Path.GetFullPath(jsonOutput)}");
    Console.WriteLine($"Review queue: {Path.GetFullPath(markdownOutput)}");
}
finally
{
    loadContext.Unload();
}

static IReadOnlyList<ApiEntry> BuildGlobalApiEntries(Assembly assembly)
{
    var builtinTypes = assembly
        .GetExportedTypes()
        .Where(type =>
            type.IsClass
            && type.Namespace?.StartsWith("Keysharp.Builtins", StringComparison.Ordinal) == true
            && !HasAttribute(type, HiddenAttribute))
        .ToArray();
    var anyType = assembly.GetType("Keysharp.Builtins.Any", throwOnError: true)!;
    var moduleType = assembly.GetType("Keysharp.Runtime.Module", throwOnError: true)!;
    var globalClassTypes = builtinTypes
        .Where(type =>
            !type.IsNested
            && !HasAttribute(type, GlobalClassHiddenAttribute)
            && anyType.IsAssignableFrom(type)
            && !moduleType.IsAssignableFrom(type))
        .ToArray();

    var declarations = new List<ApiDeclaration>();

    foreach (var type in globalClassTypes)
    {
        declarations.Add(new ApiDeclaration(
            "type",
            UserVisibleName(type),
            type.FullName ?? type.Name));
    }

    foreach (var type in builtinTypes.Where(type => type.IsAbstract && type.IsSealed))
    {
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.IsSpecialName || HasAttribute(method, HiddenAttribute))
                continue;

            declarations.Add(new ApiDeclaration(
                "function",
                UserVisibleName(method),
                $"{type.FullName}.{method.Name}"));
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (HasAttribute(property, HiddenAttribute))
                continue;

            declarations.Add(new ApiDeclaration(
                "property",
                UserVisibleName(property),
                $"{type.FullName}.{property.Name}"));
        }
    }

    return declarations
        .GroupBy(
            declaration => (declaration.Kind, declaration.Name),
            new ApiDeclarationKeyComparer())
        .Select(group => new ApiEntry(
            group.Key.Kind,
            group.Key.Name,
            null,
            null,
            group.Select(item => item.Declaration)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray(),
            "needs-review",
            []))
        .ToArray();
}

static IReadOnlyList<ApiEntry> BuildClassMemberEntries(Assembly assembly)
{
    var anyType = assembly.GetType("Keysharp.Builtins.Any", throwOnError: true)!;
    var moduleType = assembly.GetType("Keysharp.Runtime.Module", throwOnError: true)!;
    var aliases = ReadTypeNameAliases(assembly);
    var classTypes = assembly
        .GetExportedTypes()
        .Where(type =>
            type.IsClass
            && type.Namespace?.StartsWith("Keysharp.Builtins", StringComparison.Ordinal) == true
            && anyType.IsAssignableFrom(type)
            && !HasAttribute(type, HiddenAttribute)
            && !moduleType.IsAssignableFrom(RootDeclaringType(type)))
        .ToArray();

    var declarations = new List<ClassMemberDeclaration>();

    foreach (var type in classTypes)
    {
        var owner = ScriptTypeName(type, aliases);

        foreach (var method in type.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (method.IsSpecialName || HasAttribute(method, HiddenAttribute))
                continue;

            var userName = UserDeclaredNameOrNull(method);
            var methodName = method.Name;
            var isStatic = HasAttribute(method, StaticAttribute);
            if (methodName.StartsWith(ClassStaticPrefix, StringComparison.Ordinal))
            {
                isStatic = true;
                methodName = methodName[ClassStaticPrefix.Length..];
            }

            var kind = "method";
            if (methodName.StartsWith("get_", StringComparison.Ordinal)
                || methodName.StartsWith("set_", StringComparison.Ordinal))
            {
                kind = "property";
                methodName = methodName[4..];
                if (methodName == "Item")
                    methodName = "__Item";
            }

            declarations.Add(new ClassMemberDeclaration(
                kind,
                owner,
                userName ?? methodName,
                isStatic ? "class" : "prototype",
                $"{type.FullName}.{method.Name}"));
        }

        foreach (var property in type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (HasAttribute(property, HiddenAttribute))
                continue;

            var userName = UserDeclaredNameOrNull(property);
            var propertyName = property.Name == "Item" ? "__Item" : property.Name;
            var isStatic = HasAttribute(property, StaticAttribute);
            if (propertyName.StartsWith(ClassStaticPrefix, StringComparison.Ordinal))
            {
                isStatic = true;
                propertyName = propertyName[ClassStaticPrefix.Length..];
            }

            declarations.Add(new ClassMemberDeclaration(
                "property",
                owner,
                userName ?? propertyName,
                isStatic ? "class" : "prototype",
                $"{type.FullName}.{property.Name}"));
        }

        // InitClass defines this method directly rather than through a CLR member.
        if (type == anyType)
        {
            declarations.Add(new ClassMemberDeclaration(
                "method",
                owner,
                "Props",
                "prototype",
                "Keysharp.Runtime.Script.InitClass (Any.Props)"));
        }
    }

    return declarations
        .GroupBy(
            declaration => (
                declaration.Kind,
                declaration.Owner,
                declaration.Name,
                declaration.Placement),
            new ClassMemberDeclarationKeyComparer())
        .Select(group => new ApiEntry(
            group.Key.Kind,
            group.Key.Name,
            group.Key.Owner,
            group.Key.Placement,
            group.Select(item => item.Declaration)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray(),
            "needs-review",
            []))
        .ToArray();
}

static IReadOnlyList<ApiEntry> BuildModuleExportEntries(Assembly assembly)
{
    var moduleType = assembly.GetType("Keysharp.Runtime.Module", throwOnError: true)!;
    var ahkType = assembly.GetType("Keysharp.Runtime.Ahk", throwOnError: true)!;
    var moduleTypes = assembly
        .GetExportedTypes()
        .Where(type =>
            type.IsClass
            && type != moduleType
            && moduleType.IsAssignableFrom(type))
        .OrderBy(UserVisibleName, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var declarations = new List<ClassMemberDeclaration>();

    foreach (var type in moduleTypes)
    {
        var moduleName = type == ahkType ? "AHK" : UserVisibleName(type);
        declarations.Add(new ClassMemberDeclaration(
            "module",
            "",
            moduleName,
            "module",
            type.FullName ?? type.Name));

        // AHK is a dynamic proxy over the filtered global built-in surface.
        // Duplicating every global entry here would obscure rather than verify
        // that relationship, so its exports remain covered by the global audit.
        if (type == ahkType)
            continue;

        // DeclaredOnly mirrors the runtime binder: a module exports what it declares. A module type derives
        // from Module -> Any -> object, so a flattened lookup would additionally report the inherited public
        // statics of those bases (object's Equals/ReferenceEquals, Any's HasBase/HasProp/HasMethod/GetMethod)
        // as if the module had declared them.
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName)
                continue;

            declarations.Add(new ClassMemberDeclaration(
                "function",
                moduleName,
                UserVisibleName(method),
                "module",
                $"{method.DeclaringType?.FullName}.{method.Name}"));
        }

        foreach (var property in type.GetProperties(flags))
        {
            declarations.Add(new ClassMemberDeclaration(
                "property",
                moduleName,
                UserVisibleName(property),
                "module",
                $"{property.DeclaringType?.FullName}.{property.Name}"));
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
        {
            declarations.Add(new ClassMemberDeclaration(
                "type",
                moduleName,
                UserVisibleName(nested),
                "module",
                nested.FullName ?? nested.Name));
        }
    }

    return declarations
        .GroupBy(
            declaration => (
                declaration.Kind,
                declaration.Owner,
                declaration.Name,
                declaration.Placement),
            new ClassMemberDeclarationKeyComparer())
        .Select(group => new ApiEntry(
            group.Key.Kind,
            group.Key.Name,
            string.IsNullOrEmpty(group.Key.Owner) ? null : group.Key.Owner,
            group.Key.Placement,
            group.Select(item => item.Declaration)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray(),
            "needs-review",
            []))
        .ToArray();
}

static Type RootDeclaringType(Type type)
{
    while (type.DeclaringType is not null)
        type = type.DeclaringType;
    return type;
}

static IReadOnlyDictionary<string, string> ReadTypeNameAliases(Assembly assembly)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var keywords = assembly.GetType("Keysharp.Language.Keywords", throwOnError: false);
    var field = keywords?.GetField(
        "TypeNameAliases",
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    if (field?.GetValue(null) is IDictionary aliases)
    {
        foreach (DictionaryEntry alias in aliases)
        {
            if (alias.Key is string runtimeName && alias.Value is string scriptName)
                result[runtimeName] = scriptName;
        }
    }
    return result;
}

static string ScriptTypeName(Type type, IReadOnlyDictionary<string, string> aliases)
{
    var ownName = UserDeclaredNameOrNull(type)
        ?? (aliases.TryGetValue(type.Name, out var alias) ? alias : type.Name);
    return type.DeclaringType is null
        ? ownName
        : $"{ScriptTypeName(type.DeclaringType, aliases)}.{ownName}";
}

static Counts BuildCounts(IReadOnlyList<ApiEntry> entries, string scope)
{
    return new Counts(
        scope == "class-members"
            ? entries.Select(entry => entry.Owner)
                .Where(owner => owner is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
            : entries.Count(entry => entry.Kind == "type"),
        entries.Count(entry => entry.Kind == "module"),
        entries.Count(entry => entry.Kind == "function"),
        entries.Count(entry => entry.Kind == "method"),
        entries.Count(entry => entry.Kind == "property"),
        entries.Count(entry => entry.DocumentationStatus == "locator-found"),
        entries.Count(entry => entry.DocumentationStatus == "needs-review"),
        entries.Count(entry => entry.Declarations.Count > 1));
}

static string BuildMarkdown(Inventory inventory)
{
    var isClassMemberAudit = inventory.ScopeId == "class-members";
    var isModuleAudit = inventory.ScopeId == "module-exports";
    var builder = new StringBuilder();
    builder.AppendLine(inventory.ScopeId switch
    {
        "class-members" => "# Source-derived built-in class member audit",
        "module-exports" => "# Source-derived built-in module export audit",
        _ => "# Source-derived global API audit",
    });
    builder.AppendLine();
    builder.AppendLine("> Generated by `scripts/Update-SourceAudit.ps1`. Do not edit by hand.");
    builder.AppendLine();
    builder.AppendLine("This is a review queue, not a capability matrix. It records script-visible");
    builder.AppendLine(inventory.ScopeId switch
    {
        "class-members" => "members declared by built-in classes and whether a strong documentation locator was found.",
        "module-exports" => "built-in modules and importable exports and whether an explicit module locator was found.",
        _ => "global names exposed by the runtime and whether a strong documentation locator was found.",
    });
    builder.AppendLine("It does not assert that an API is complete or works on every platform.");
    if (isClassMemberAudit)
    {
        builder.AppendLine();
        builder.AppendLine("Inherited members are recorded on the class which declares them, not repeated");
        builder.AppendLine("for every derived class. Importable modules are audited separately.");
    }
    else if (isModuleAudit)
    {
        builder.AppendLine();
        builder.AppendLine("The AHK module dynamically forwards the filtered global API and is represented");
        builder.AppendLine("by the module entry itself; its individual exports are covered by the global audit.");
    }
    builder.AppendLine();
    builder.AppendLine("## Evidence identity");
    builder.AppendLine();
    builder.AppendLine($"- Source: `{inventory.Evidence.SourceRemote}`");
    builder.AppendLine($"- Source commit: `{inventory.Evidence.SourceCommit}`");
    builder.AppendLine($"- Source tree: **{inventory.Evidence.SourceTreeState}**");
    builder.AppendLine($"- Docs commit: `{inventory.Evidence.DocsCommit}`");
    builder.AppendLine($"- Docs tree at generation: **{inventory.Evidence.DocsTreeState}**");
    builder.AppendLine($"- Host platform: **{inventory.Evidence.Platform}**");
    builder.AppendLine($"- Runtime: `{inventory.Evidence.Framework}`");
    builder.AppendLine($"- Assembly SHA-256: `{inventory.Evidence.AssemblySha256}`");
    builder.AppendLine();
    if (inventory.Evidence.SourceTreeState != "clean")
    {
        builder.AppendLine("> **Provisional:** The source tree had uncommitted changes. Regenerate this");
        builder.AppendLine("> audit from the reviewed source commit before treating it as a baseline.");
        builder.AppendLine();
    }

    builder.AppendLine("## Summary");
    builder.AppendLine();
    builder.AppendLine("| Scope | Count |");
    builder.AppendLine("| --- | ---: |");
    if (isClassMemberAudit)
    {
        builder.AppendLine($"| Built-in classes declaring members | {inventory.Counts.Types} |");
        builder.AppendLine($"| Declared methods | {inventory.Counts.Methods} |");
        builder.AppendLine($"| Declared properties | {inventory.Counts.Properties} |");
        builder.AppendLine($"| Members with multiple runtime declarations | {inventory.Counts.MultipleDeclarations} |");
    }
    else if (isModuleAudit)
    {
        builder.AppendLine($"| Built-in modules | {inventory.Counts.Modules} |");
        builder.AppendLine($"| Exported classes | {inventory.Counts.Types} |");
        builder.AppendLine($"| Exported functions | {inventory.Counts.Functions} |");
        builder.AppendLine($"| Exported properties | {inventory.Counts.Properties} |");
        builder.AppendLine($"| Exports with multiple runtime declarations | {inventory.Counts.MultipleDeclarations} |");
    }
    else
    {
        builder.AppendLine($"| Global class objects | {inventory.Counts.Types} |");
        builder.AppendLine($"| Global functions | {inventory.Counts.Functions} |");
        builder.AppendLine($"| Global properties | {inventory.Counts.Properties} |");
        builder.AppendLine($"| Names with multiple runtime declarations | {inventory.Counts.MultipleDeclarations} |");
    }
    builder.AppendLine($"| Names with a documentation locator | {inventory.Counts.WithLocator} |");
    builder.AppendLine($"| Names needing review | {inventory.Counts.NeedsReview} |");
    builder.AppendLine();

    var needsReview = inventory.Entries
        .Where(entry => entry.DocumentationStatus == "needs-review")
        .GroupBy(entry => entry.Kind)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .ToArray();

    builder.AppendLine("## Review queue");
    builder.AppendLine();
    if (needsReview.Length == 0)
    {
        builder.AppendLine("No entries require review in this scope.");
        builder.AppendLine();
    }
    else
    {
        foreach (var group in needsReview)
        {
            builder.AppendLine($"### {KindHeading(group.Key, isClassMemberAudit, isModuleAudit)}");
            builder.AppendLine();
            if (isClassMemberAudit)
            {
                builder.AppendLine("| Class | Member | Placement | Runtime declaration |");
                builder.AppendLine("| --- | --- | --- | --- |");
            }
            else if (isModuleAudit && group.Key != "module")
            {
                builder.AppendLine("| Module | Export | Runtime declaration |");
                builder.AppendLine("| --- | --- | --- |");
            }
            else
            {
                builder.AppendLine("| Script name | Runtime declaration |");
                builder.AppendLine("| --- | --- |");
            }
            foreach (var entry in group
                .OrderBy(entry => entry.Owner, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (isClassMemberAudit)
                {
                    builder.Append("| `")
                        .Append(EscapeMarkdown(entry.Owner!))
                        .Append("` | `")
                        .Append(EscapeMarkdown(entry.Name))
                        .Append("` | ")
                        .Append(entry.Placement)
                        .Append(" | `");
                }
                else if (isModuleAudit && entry.Kind != "module")
                {
                    builder.Append("| `")
                        .Append(EscapeMarkdown(entry.Owner!))
                        .Append("` | `")
                        .Append(EscapeMarkdown(entry.Name))
                        .Append("` | `");
                }
                else
                {
                    builder.Append("| `")
                        .Append(EscapeMarkdown(entry.Name))
                        .Append("` | `");
                }
                builder
                    .Append(EscapeMarkdown(string.Join("; ", entry.Declarations)))
                    .AppendLine("` |");
            }
            builder.AppendLine();
        }
    }

    var collisions = inventory.Entries
        .Where(entry => entry.Declarations.Count > 1)
        .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (inventory.ScopeId == "global" && collisions.Length > 0)
    {
        builder.AppendLine("## Runtime name collisions");
        builder.AppendLine();
        builder.AppendLine("These names have multiple public declarations. Confirm which declaration");
        builder.AppendLine("the runtime resolves and whether the duplicate surface is intentional.");
        builder.AppendLine();
        builder.AppendLine("| Kind | Script name | Runtime declarations |");
        builder.AppendLine("| --- | --- | --- |");
        foreach (var entry in collisions)
        {
            builder.Append("| ")
                .Append(entry.Kind)
                .Append(" | `")
                .Append(EscapeMarkdown(entry.Name))
                .Append("` | `")
                .Append(EscapeMarkdown(string.Join("; ", entry.Declarations)))
                .AppendLine("` |");
        }
        builder.AppendLine();
    }

    return builder.ToString().TrimEnd() + Environment.NewLine;
}

static string KindHeading(string kind, bool isClassMemberAudit, bool isModuleAudit) => kind switch
{
    "function" => isModuleAudit ? "Exported functions" : "Global functions",
    "method" => "Methods",
    "module" => "Built-in modules",
    "property" => isModuleAudit ? "Exported properties" : isClassMemberAudit ? "Properties" : "Global properties",
    "type" => isModuleAudit ? "Exported classes" : "Public built-in types",
    _ => kind
};

static string EscapeMarkdown(string value) =>
    value.Replace("|", "\\|", StringComparison.Ordinal);

static string? UserDeclaredNameOrNull(MemberInfo member)
{
    var attribute = member.CustomAttributes.FirstOrDefault(item =>
        item.AttributeType.FullName == UserNameAttribute);
    if (attribute?.ConstructorArguments.Count > 0
        && attribute.ConstructorArguments[0].Value is string name
        && !string.IsNullOrWhiteSpace(name))
    {
        return name;
    }

    return null;
}

static string UserVisibleName(MemberInfo member) =>
    UserDeclaredNameOrNull(member) ?? member.Name;

static bool HasAttribute(MemberInfo member, string fullName) =>
    member.CustomAttributes.Any(item => item.AttributeType.FullName == fullName);

static RepositoryState ReadRepositoryState(string repository)
{
    var commit = RunGit(repository, "rev-parse", "HEAD");
    var remote = SanitizeRemote(RunGit(repository, "remote", "get-url", "origin"));
    var status = RunGit(repository, "status", "--porcelain");
    return new RepositoryState(
        string.IsNullOrWhiteSpace(remote) ? "(no origin)" : remote,
        string.IsNullOrWhiteSpace(commit) ? "(unknown)" : commit,
        string.IsNullOrWhiteSpace(status) ? "clean" : "dirty");
}

static string SanitizeRemote(string remote)
{
    if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo))
        return remote;

    var builder = new UriBuilder(uri)
    {
        UserName = "",
        Password = ""
    };
    return builder.Uri.ToString();
}

static string RunGit(string repository, params string[] arguments)
{
    var startInfo = new System.Diagnostics.ProcessStartInfo("git")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    startInfo.ArgumentList.Add("-C");
    startInfo.ArgumentList.Add(repository);
    foreach (var argument in arguments)
        startInfo.ArgumentList.Add(argument);

    using var process = System.Diagnostics.Process.Start(startInfo);
    if (process is null)
        return "";

    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return process.ExitCode == 0 ? output.Trim() : "";
}

static string GetPlatformName()
{
    if (OperatingSystem.IsWindows())
        return "Windows";
    if (OperatingSystem.IsMacOS())
        return "macOS";
    if (OperatingSystem.IsLinux())
        return "Linux";
    return RuntimeInformation.OSDescription;
}

static Dictionary<string, string> ParseOptions(string[] arguments)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Options must use --name value pairs.");
        result[arguments[index][2..]] = arguments[index + 1];
    }
    return result;
}

static string RequiredOption(IReadOnlyDictionary<string, string> options, string name)
{
    if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        throw new ArgumentException($"Missing required option --{name}.");
    return Path.GetFullPath(value);
}

static string RequiredPath(
    IReadOnlyDictionary<string, string> options,
    string name,
    Func<string, bool> exists)
{
    var path = RequiredOption(options, name);
    if (!exists(path))
        throw new FileNotFoundException($"Path supplied to --{name} does not exist.", path);
    return path;
}

static DocumentationCatalog BuildDocumentationCatalog(string docsRoot)
{
    var references = new Dictionary<string, List<DocReference>>(StringComparer.OrdinalIgnoreCase);
    var indexEntries = new List<IndexReference>();
    var archivedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AutoHotkeyChangeLog.htm",
        "AutoHotkeyLicense.htm"
    };

    foreach (var path in Directory.EnumerateFiles(docsRoot, "*.htm", SearchOption.AllDirectories))
    {
        if (archivedNames.Contains(Path.GetFileName(path)))
            continue;

        var relativePath = Path.GetRelativePath(docsRoot, path).Replace('\\', '/');
        var pageName = Path.GetFileNameWithoutExtension(path);
        AddReference(references, pageName, new DocReference(relativePath, "page-name"));

        var content = File.ReadAllText(path);
        foreach (Match match in Regex.Matches(
            content,
            """\bid\s*=\s*["'](?<id>[^"']+)["']""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var id = WebUtility.HtmlDecode(match.Groups["id"].Value);
            AddReference(references, id, new DocReference($"{relativePath}#{id}", "anchor"));
        }

        foreach (Match match in Regex.Matches(
            content,
            """<h[1-6]\b[^>]*>(?<heading>.*?)</h[1-6]>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var heading = Regex.Replace(match.Groups["heading"].Value, "<[^>]+>", "");
            heading = WebUtility.HtmlDecode(heading).Trim();
            AddReference(references, heading, new DocReference(relativePath, "heading"));
        }

        foreach (Match match in Regex.Matches(
            content,
            """<span\b[^>]*\bclass\s*=\s*["'][^"']*\bfunc\b[^"']*["'][^>]*>(?<name>.*?)</span>""",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var name = Regex.Replace(match.Groups["name"].Value, "<[^>]+>", "");
            name = WebUtility.HtmlDecode(name).Trim();
            AddReference(references, name, new DocReference(relativePath, "syntax"));
        }
    }

    var indexPath = Path.Combine(docsRoot, "static", "source", "data_index.js");
    if (File.Exists(indexPath))
    {
        var indexContent = File.ReadAllText(indexPath);
        foreach (Match match in Regex.Matches(
            indexContent,
            @"^\s*\[""(?<term>(?:\\.|[^""])*)""\s*,\s*""(?<href>(?:\\.|[^""])*)""",
            RegexOptions.Multiline | RegexOptions.CultureInvariant))
        {
            var term = JsonSerializer.Deserialize<string>($"\"{match.Groups["term"].Value}\"");
            var href = JsonSerializer.Deserialize<string>($"\"{match.Groups["href"].Value}\"");
            if (!string.IsNullOrWhiteSpace(term) && !string.IsNullOrWhiteSpace(href))
            {
                AddReference(references, term, new DocReference(href, "index"));
                indexEntries.Add(new IndexReference(term, href));
            }
        }
    }

    return new DocumentationCatalog(references, indexEntries);
}

static void AddReference(
    IDictionary<string, List<DocReference>> references,
    string name,
    DocReference reference)
{
    if (string.IsNullOrWhiteSpace(name))
        return;

    if (!references.TryGetValue(name, out var items))
    {
        items = [];
        references[name] = items;
    }

    if (!items.Contains(reference))
        items.Add(reference);
}

sealed class DocumentationCatalog(
    IReadOnlyDictionary<string, List<DocReference>> references,
    IReadOnlyList<IndexReference> indexEntries)
{
    public IReadOnlyList<DocReference> Find(string apiName)
    {
        var result = new HashSet<DocReference>();
        if (references.TryGetValue(apiName, out var exact))
            result.UnionWith(exact);

        return result
            .OrderBy(item => item.Href, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Evidence, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<DocReference> FindModuleExport(
        string? module,
        string name,
        string kind)
    {
        if (kind == "module" || module is null)
            return Find(name);

        // Module exports require an explicit entry in the module reference.
        // A same-named global page does not establish that the name can be
        // imported from this module.
        return Find($"{module.ToUpperInvariant()}_{name}");
    }

    public IReadOnlyList<DocReference> FindMember(
        string owner,
        string memberName,
        string kind,
        string placement)
    {
        var result = new HashSet<DocReference>();

        // Some public methods are intentionally documented as a family or as an
        // alias of another spelling. Keep those relationships explicit instead
        // of relying on a loose text match.
        if (owner.Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            if (Regex.IsMatch(
                memberName,
                @"^Read(?:Char|Double|Float|Int|Int64|Short|UChar|UInt|UShort)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                result.Add(new DocReference("lib/File.htm#ReadNum", "member-family"));
            }
            else if (Regex.IsMatch(
                memberName,
                @"^Write(?:Char|Double|Float|Int|Int64|Short|UChar|UInt|UShort)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                result.Add(new DocReference("lib/File.htm#WriteNum", "member-family"));
            }
        }
        else if (owner.Equals("Gui", StringComparison.OrdinalIgnoreCase))
        {
            var aliasHref = memberName.ToUpperInvariant() switch
            {
                "ADDDDL" => "lib/GuiControls.htm#DropDownList",
                "ADDPIC" => "lib/GuiControls.htm#Picture",
                "ADDTAB2" or "ADDTAB3" => "lib/GuiControls.htm#Tab",
                _ => null,
            };
            if (aliasHref is not null)
                result.Add(new DocReference(aliasHref, "member-alias"));
        }
        else if ((owner.Equals("RegExMatchInfo", StringComparison.OrdinalIgnoreCase)
                  || owner.Equals("RegExMatchInfoCs", StringComparison.OrdinalIgnoreCase))
                 && memberName is "__Enum" or "__Get" or "__Item")
        {
            result.Add(new DocReference("lib/RegExMatch.htm#MatchObject", "protocol-member"));
        }
        else if ((owner.Equals("RegExMatchInfo", StringComparison.OrdinalIgnoreCase)
                  || owner.Equals("RegExMatchInfoCs", StringComparison.OrdinalIgnoreCase))
                 && memberName is "Count" or "Len" or "Mark" or "Name" or "Pos" or "ToString")
        {
            result.Add(new DocReference("lib/RegExMatch.htm#MatchObject", "member-family"));
        }
        else if (owner.Equals("BoundFunc", StringComparison.OrdinalIgnoreCase)
                 && memberName is "Bind" or "Call" or "Name")
        {
            result.Add(new DocReference($"lib/Func.htm#{memberName}", "inherited-contract"));
        }
        else if ((owner.Equals("ComValue", StringComparison.OrdinalIgnoreCase)
                  && memberName.Equals("__Item", StringComparison.OrdinalIgnoreCase))
                 || (owner.Equals("ComValueRef", StringComparison.OrdinalIgnoreCase)
                     && memberName.Equals("__Value", StringComparison.OrdinalIgnoreCase)))
        {
            result.Add(new DocReference("lib/ComValue.htm#ByRef", "protocol-member"));
        }
        else if (memberName.Equals("__New", StringComparison.OrdinalIgnoreCase)
                 && owner is "Error" or "OSError")
        {
            result.Add(new DocReference(
                owner == "Error" ? "lib/Error.htm#Call" : "lib/Error.htm#OSError",
                "class-construction"));
        }
        else if (owner.Equals("Any", StringComparison.OrdinalIgnoreCase)
                 && memberName.Equals("__Init", StringComparison.OrdinalIgnoreCase))
        {
            result.Add(new DocReference(
                placement == "class"
                    ? "Objects.htm#static__New"
                    : "Objects.htm#Custom_Classes_var",
                "language-defined-member"));
        }

        var expectedIndexTerm = $"{memberName} {kind} ({owner})";
        foreach (var entry in indexEntries)
        {
            if (entry.Term.Equals(expectedIndexTerm, StringComparison.OrdinalIgnoreCase))
                result.Add(new DocReference(entry.Href, "member-index"));
        }

        var ownerPages = Find(owner)
            .Where(reference => reference.Evidence is "page-name" or "index")
            .Select(reference => PagePart(reference.Href))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (owner.Equals("Gui", StringComparison.OrdinalIgnoreCase))
            ownerPages.Add("lib/GuiControls.htm");
        else if (owner.Equals("Gui.Control", StringComparison.OrdinalIgnoreCase))
        {
            ownerPages.Add("lib/GuiControl.htm");
            ownerPages.Add("lib/GuiControls.htm");
            ownerPages.Add("lib/ListView.htm");
            ownerPages.Add("lib/TreeView.htm");
        }
        else if (owner.Equals("RegExMatchInfoCs", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var basePage in Find("RegExMatchInfo").Select(reference => PagePart(reference.Href)))
                ownerPages.Add(basePage);
        }

        if ((memberName.Equals("Call", StringComparison.OrdinalIgnoreCase) && placement == "class")
            || (memberName.Equals("__New", StringComparison.OrdinalIgnoreCase) && placement == "prototype"))
        {
            foreach (var ownerReference in Find(owner)
                .Where(reference =>
                    reference.Evidence == "syntax"
                    && ownerPages.Contains(PagePart(reference.Href))))
            {
                result.Add(new DocReference(
                    PagePart(ownerReference.Href),
                    "class-construction"));
            }
        }

        if (ownerPages.Count > 0)
        {
            foreach (var memberReference in Find(memberName))
            {
                if (memberReference.Evidence is "anchor" or "heading" or "syntax"
                    && ownerPages.Contains(PagePart(memberReference.Href)))
                {
                    result.Add(memberReference with { Evidence = "owner-page-" + memberReference.Evidence });
                }
            }
        }

        return result
            .OrderBy(item => item.Href, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Evidence, StringComparer.Ordinal)
            .ToArray();
    }

    private static string PagePart(string href)
    {
        var hash = href.IndexOf('#');
        return hash >= 0 ? href[..hash] : href;
    }
}

sealed class InspectionLoadContext(string assemblyPath)
    : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(assemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}

sealed class ApiDeclarationKeyComparer
    : IEqualityComparer<(string Kind, string Name)>
{
    public bool Equals(
        (string Kind, string Name) x,
        (string Kind, string Name) y) =>
        StringComparer.Ordinal.Equals(x.Kind, y.Kind)
        && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

    public int GetHashCode((string Kind, string Name) value) =>
        HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(value.Kind),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name));
}

sealed class ClassMemberDeclarationKeyComparer
    : IEqualityComparer<(string Kind, string Owner, string Name, string Placement)>
{
    public bool Equals(
        (string Kind, string Owner, string Name, string Placement) x,
        (string Kind, string Owner, string Name, string Placement) y) =>
        StringComparer.Ordinal.Equals(x.Kind, y.Kind)
        && StringComparer.OrdinalIgnoreCase.Equals(x.Owner, y.Owner)
        && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name)
        && StringComparer.Ordinal.Equals(x.Placement, y.Placement);

    public int GetHashCode(
        (string Kind, string Owner, string Name, string Placement) value) =>
        HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(value.Kind),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Owner),
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name),
            StringComparer.Ordinal.GetHashCode(value.Placement));
}

sealed record ApiDeclaration(string Kind, string Name, string Declaration);
sealed record ClassMemberDeclaration(
    string Kind,
    string Owner,
    string Name,
    string Placement,
    string Declaration);
sealed record DocReference(string Href, string Evidence);
sealed record IndexReference(string Term, string Href);
sealed record ApiEntry(
    string Kind,
    string Name,
    string? Owner,
    string? Placement,
    IReadOnlyList<string> Declarations,
    string DocumentationStatus,
    IReadOnlyList<DocReference> Documentation);
sealed record Evidence(
    string SourceRemote,
    string SourceCommit,
    string SourceTreeState,
    string DocsCommit,
    string DocsTreeState,
    string Platform,
    string Framework,
    string Assembly,
    string AssemblySha256);
sealed record Counts(
    int Types,
    int Modules,
    int Functions,
    int Methods,
    int Properties,
    int WithLocator,
    int NeedsReview,
    int MultipleDeclarations);
sealed record Inventory(
    int SchemaVersion,
    string ScopeId,
    string Scope,
    Evidence Evidence,
    Counts Counts,
    IReadOnlyList<ApiEntry> Entries);
sealed record RepositoryState(string Remote, string Commit, string TreeState);
