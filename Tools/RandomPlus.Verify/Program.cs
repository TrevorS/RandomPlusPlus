using System.Reflection;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using System.Xml.Linq;

// Checks everything RandomPlus resolves by *name* at runtime against real RimWorld
// metadata.
//
// The compiler already validates direct API calls. It cannot validate Harmony patch
// targets or string-based reflection - those resolve at runtime, and when they fail
// they fail quietly: a missing GetMethod returns null and the mod degrades to a
// fallback path, or an arity mismatch throws into a catch that only writes a log
// line. Both look like "the mod stopped working" rather than an error. This tool
// turns that class of breakage, which is what usually kills a mod across a RimWorld
// update, into a build failure.
//
// Usage: RandomPlus.Verify <references.txt> <RandomPlusPlus.dll>
//   references.txt is written by the RandomPlus build (see WriteReferenceManifest),
//   and lists the assemblies the mod was compiled against.
//
// LIMITATION: reference assemblies carry metadata but no IL, so the two transpilers
// in HarmonyPatches.cs cannot be checked here. They scan the IL of
// CharacterCardUtility.DrawCharacterCard and MainMenuDrawer.DoMainMenuControls for
// opcode patterns, and if a pattern is not found they skip the injection silently.
// Only running the real game covers that.

const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic
                       | BindingFlags.Static | BindingFlags.Instance;

string manifestPath = args.Length > 0 ? args[0] : "obj/references.txt";
string modPath = args.Length > 1 ? args[1] : "Resources/1.6/Assemblies/RandomPlusPlus.dll";

if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"Reference manifest not found: {manifestPath}");
    Console.Error.WriteLine("Build RandomPlus first - the manifest is written during the build.");
    return 2;
}
if (!File.Exists(modPath))
{
    Console.Error.WriteLine($"Mod assembly not found: {modPath}");
    return 2;
}

// Resolve by simple name, first entry wins. The manifest can legitimately contain two
// assemblies with the same name - RimWorld ships its own Mono BCL alongside the .NET
// Framework reference assemblies - and either serves for reading metadata.
var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (var line in File.ReadAllLines(manifestPath))
{
    var path = line.Trim();
    if (path.Length == 0 || !File.Exists(path)) continue;
    var name = Path.GetFileNameWithoutExtension(path);
    if (!byName.ContainsKey(name)) byName[name] = path;
}
byName[Path.GetFileNameWithoutExtension(modPath)] = modPath;

if (!byName.ContainsKey("Assembly-CSharp"))
{
    Console.Error.WriteLine("Assembly-CSharp is not in the reference manifest.");
    return 2;
}

var coreAssembly = byName.ContainsKey("mscorlib") ? "mscorlib" : "netstandard";
var mlc = new MetadataLoadContext(new PathAssemblyResolver(byName.Values), coreAssembly);
var mod = mlc.LoadFromAssemblyPath(modPath);
var game = mlc.LoadFromAssemblyPath(byName["Assembly-CSharp"]);

int pass = 0, fail = 0;
void Ok(string m) { pass++; Console.WriteLine($"  PASS  {m}"); }
void Bad(string m) { fail++; Console.WriteLine($"  FAIL  {m}"); }
// For facts worth stating on every run that are not, on their own, a broken build.
void Note(string m) => Console.WriteLine($"  NOTE  {m}");

// Harmony patch targets, read straight off the [HarmonyPatch] attributes so this
// section needs no maintenance when a patch is added or removed.
Console.WriteLine("=== Harmony patch targets ===");
foreach (var t in mod.GetTypes())
{
    foreach (var attr in t.GetCustomAttributesData())
    {
        if (attr.AttributeType.Name != "HarmonyPatch") continue;
        var ctorArgs = attr.ConstructorArguments;
        if (ctorArgs.Count is not (2 or 3)) continue;
        if (ctorArgs[0].Value is not Type target || ctorArgs[1].Value is not string member) continue;

        // (Type, string, MethodType) targets a property accessor: the member is a
        // property name, and what has to exist is the getter or setter itself.
        if (ctorArgs.Count == 3)
        {
            if (ctorArgs[2].ArgumentType.Name != "MethodType" || ctorArgs[2].Value is not int methodType) continue;
            var accessorKind = methodType switch { 1 => "getter", 2 => "setter", _ => null };
            if (accessorKind is null) continue;

            var prop = target.GetProperty(member, ALL);
            var accessor = methodType == 1 ? prop?.GetMethod : prop?.SetMethod;
            if (accessor is not null) Ok($"{t.Name}: {target.FullName}.{member} ({accessorKind})");
            else Bad($"{t.Name}: {target.FullName}.{member} {accessorKind} DOES NOT EXIST");
            continue;
        }

        var found = target.GetMember(member, ALL);
        if (found.Length > 0) Ok($"{t.Name}: {target.FullName}.{member} ({found.Length} overload(s))");
        else Bad($"{t.Name}: {target.FullName}.{member} DOES NOT EXIST");
    }
}

// Reflection lookups. This table mirrors the GetMethod/GetProperty/GetField calls in
// PawnRandomizer.Init() and HarmonyPatches.GoToConfigPawnPage(); keep it in step with
// them. Argc is the argument count the matching Invoke passes - a method that exists
// but took on an extra parameter throws at runtime, so arity is checked too.
Console.WriteLine("\n=== Reflected methods (name and call-site arity) ===");
foreach (var (typeName, member, argc) in new (string, string, int)[]
{
    ("Verse.PawnGenerator", "GenerateRandomAge", 2),
    ("Verse.PawnGenerator", "GenerateTraits", 2),
    ("Verse.PawnGenerator", "GenerateSkills", 2),
    ("Verse.PawnGenerator", "GenerateInitialHediffs", 2),
    ("Verse.PawnGenerator", "GenerateBodyType", 2),
    ("Verse.PawnGenerator", "GenerateGearFor", 2),
    ("Verse.PawnGenerator", "GenerateGenes", 3),
    ("RimWorld.Page_SelectScenario", "CanDoNext", 0),
    ("RimWorld.Page_SelectScenario", "DoNext", 0),
    ("RimWorld.Page_SelectStoryteller", "CanDoNext", 0),
    ("RimWorld.Page_SelectStoryteller", "DoNext", 0),
    ("RimWorld.Page_CreateWorldParams", "CanDoNext", 0),
    ("RimWorld.Page_SelectStartingSite", "CanDoNext", 0),
    ("RimWorld.Page_SelectStartingSite", "DoNext", 0),
    ("RimWorld.Page_ChooseIdeoPreset", "CanDoNext", 0),
    ("RimWorld.Page_ChooseIdeoPreset", "DoNext", 0),
})
{
    var type = game.GetType(typeName);
    if (type is null) { Bad($"type {typeName} DOES NOT EXIST"); continue; }

    var overloads = type.GetMethods(ALL).Where(m => m.Name == member).ToArray();
    if (overloads.Length == 0) { Bad($"{typeName}.{member} DOES NOT EXIST"); continue; }

    var match = overloads.FirstOrDefault(m => m.GetParameters().Length == argc);
    if (match is not null)
        Ok($"{typeName}.{member}({string.Join(", ", match.GetParameters().Select(p => p.ParameterType.Name))})");
    else
        Bad($"{typeName}.{member} ARITY MISMATCH: call site passes {argc}, found " +
            $"[{string.Join(" | ", overloads.Select(m => m.GetParameters().Length))}]");
}

Console.WriteLine("\n=== Reflected properties and fields ===");
foreach (var (typeName, member, isProperty) in new (string, string, bool)[]
{
    ("Verse.StartingPawnUtility", "StartingAndOptionalPawns", true),
    ("RimWorld.Page_CreateWorldParams", "planetCoverage", false),
    ("RimWorld.Page_ChooseIdeoPreset", "selectedIdeo", false),
})
{
    var type = game.GetType(typeName);
    if (type is null) { Bad($"type {typeName} DOES NOT EXIST"); continue; }

    object found = isProperty ? type.GetProperty(member, ALL) : type.GetField(member, ALL);
    if (found is not null) Ok($"{typeName}.{member} ({(isProperty ? "property" : "field")})");
    else Bad($"{typeName}.{member} ({(isProperty ? "property" : "field")}) DOES NOT EXIST");
}

Console.WriteLine("\n=== Types used by the reroll path ===");
foreach (var typeName in new[]
{
    "Verse.StartingPawnUtility",
    "Verse.PawnGenerator",
    "Verse.PawnGenerationRequest",
    "RimWorld.Dialog_ChooseNewWanderers",
    "RimWorld.SpouseRelationUtility",
    "RimWorld.PawnBioAndNameGenerator",
    // Gear generation, suppressed for candidate pawns
    "RimWorld.PawnApparelGenerator",
    "RimWorld.PawnWeaponGenerator",
    "RimWorld.PawnInventoryGenerator",
})
{
    if (game.GetType(typeName) is not null) Ok(typeName);
    else Bad($"{typeName} DOES NOT EXIST");
}

// ------------------------------------------------------------------ mod content
//
// RimWorld reads these files at load. A malformed one, a missing translation key or
// a Defs reference that does not resolve does not crash anything - the mod just
// misbehaves quietly, which is the same failure mode the checks above exist for.

Console.WriteLine("\n=== Mod content ===");

// Walk up from the assembly to whichever ancestor holds Resources/. Deriving it by
// counting directories assumed the mod was at Resources/<ver>/Assemblies/, which is
// false whenever the assembly is a scratch build outside the tree - and checking
// another version's build is exactly what this tool is for.
string root = null;
for (var d = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(modPath))!); d is not null; d = d.Parent)
{
    if (Directory.Exists(Path.Combine(d.FullName, "Resources", "About")))
    {
        root = d.FullName;
        break;
    }
}

if (root is null)
{
    Console.WriteLine("  SKIP  no Resources/About above the assembly, so there is no mod folder to check");
    Console.WriteLine($"\n{pass} passed, {fail} failed");
    return fail == 0 ? 0 : 1;
}

foreach (var xml in Directory.GetFiles(Path.Combine(root, "Resources"), "*.xml", SearchOption.AllDirectories))
{
    var shown = Path.GetRelativePath(root, xml);
    try { XDocument.Load(xml); Ok($"{shown} parses"); }
    catch (Exception ex) { Bad($"{shown} is malformed: {ex.Message}"); }
}

var aboutPath = Path.Combine(root, "Resources", "About", "About.xml");
XElement about = null;
try { about = XDocument.Load(aboutPath).Root; } catch { /* already reported above */ }

if (about is not null)
{
    foreach (var field in new[] { "packageId", "name", "author", "description", "supportedVersions" })
    {
        var el = about.Element(field);
        if (el is not null && !string.IsNullOrWhiteSpace(el.Value)) Ok($"About.xml has {field}");
        else Bad($"About.xml is missing {field}");
    }

    // RimWorld requires author.name style, and refuses to load a mod whose id does not match.
    var packageId = about.Element("packageId")?.Value?.Trim() ?? "";
    if (Regex.IsMatch(packageId, @"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)+$") && packageId.Length <= 60)
        Ok($"packageId '{packageId}' is well formed");
    else
        Bad($"packageId '{packageId}' is not a valid RimWorld package id");

    // Every version the mod claims to support needs an assembly to load for it.
    // RimWorld picks the folder matching the running game, so which build a
    // player gets is decided here and nowhere else. The fork's code is
    // identified by PawnRandomizer, the class the original calls RandomSettings.
    foreach (var v in about.Element("supportedVersions")?.Elements("li") ?? Enumerable.Empty<XElement>())
    {
        var ver = v.Value.Trim();
        var dir = Path.Combine(root, "Resources", ver, "Assemblies");
        var dlls = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.dll") : Array.Empty<string>();
        if (dlls.Length == 0)
        {
            Bad($"supported version {ver} has no assembly in Resources/{ver}/Assemblies");
            continue;
        }

        Ok($"supported version {ver} has an assembly");
        var forked = dlls.Any(d =>
        {
            using var f = File.OpenRead(d);
            using var pe = new System.Reflection.PortableExecutable.PEReader(f);
            var md = pe.GetMetadataReader();
            return md.TypeDefinitions.Any(h => md.GetString(md.GetTypeDefinition(h).Name) == "PawnRandomizer");
        });
        if (!forked)
            Note($"the {ver} assembly predates this fork - a player on RimWorld {ver} gets "
               + "the original mod's code, without the fixes this one describes");
    }

    // The version is stated twice and the two are easy to let drift apart.
    var csproj = XDocument.Load(Path.Combine(root, "RandomPlus.csproj"));
    var asmVersion = csproj.Descendants("Version").FirstOrDefault()?.Value?.Trim();
    var modVersion = about.Element("modVersion")?.Value?.Trim();
    if (asmVersion is not null && asmVersion == modVersion)
        Ok($"modVersion matches the project version ({modVersion})");
    else
        Bad($"modVersion '{modVersion}' does not match the project version '{asmVersion}'");
}

// A translation key with no entry renders as the raw key in the UI.
var keyed = new HashSet<string>();
foreach (var f in Directory.GetFiles(Path.Combine(root, "Resources", "Languages"), "*.xml", SearchOption.AllDirectories))
    foreach (var e in XDocument.Load(f).Root?.Elements() ?? Enumerable.Empty<XElement>())
        keyed.Add(e.Name.LocalName);

var missingKeys = new SortedSet<string>();
var usedKeys = new SortedSet<string>();

// Every segment of a key is PascalCase, which is what separates a key literal from
// the other RandomPlus-prefixed strings in Source - "RandomPlus.xml", for one.
var keyLiteral = new Regex("\"(RandomPlus(?:\\.[A-Z][A-Za-z0-9_]*)+)\"");
foreach (var cs in Directory.GetFiles(Path.Combine(root, "Source"), "*.cs", SearchOption.AllDirectories))
    foreach (Match m in keyLiteral.Matches(File.ReadAllText(cs)))
    {
        usedKeys.Add(m.Groups[1].Value);
        if (!keyed.Contains(m.Groups[1].Value)) missingKeys.Add(m.Groups[1].Value);
    }

if (missingKeys.Count == 0) Ok($"all {usedKeys.Count} translation keys used in Source resolve");
else foreach (var k in missingKeys) Bad($"translation key '{k}' is used but not defined");

// DefDatabase.GetNamed throws when the def is absent.
var defNames = new HashSet<string>();
foreach (var f in Directory.GetFiles(Path.Combine(root, "Resources", "Defs"), "*.xml", SearchOption.AllDirectories))
    foreach (var e in XDocument.Load(f).Descendants("defName"))
        defNames.Add(e.Value.Trim());

foreach (var cs in Directory.GetFiles(Path.Combine(root, "Source"), "*.cs", SearchOption.AllDirectories))
    foreach (Match m in Regex.Matches(File.ReadAllText(cs), @"GetNamed\(""([^""]+)""\)"))
    {
        if (defNames.Contains(m.Groups[1].Value)) Ok($"def '{m.Groups[1].Value}' is defined");
        else Bad($"def '{m.Groups[1].Value}' is looked up by name but not defined in Resources/Defs");
    }

Console.WriteLine($"\n{pass} passed, {fail} failed");
if (fail > 0)
    Console.WriteLine("A name the mod resolves at runtime is missing or changed shape in this RimWorld version.");
return fail == 0 ? 0 : 1;
