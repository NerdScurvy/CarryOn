using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// B5c/B10 - the structural coverage rows for the two clone kinds. The
    /// initial recon found no vanilla shape-only clone on either world's
    /// default surface (the <c>AllowedShapeOnlyMatches</c> shapes' vanilla
    /// users are all patched or entityclass-less - <c>block/reed/basket-normal</c>
    /// covers no vanilla block at all), and it initially claimed the same for
    /// interact clones: doors/fencegates/trapdoors carry <c>entityClass
    /// "Generic"</c>/null and never register donor keys. That half was corrected
    /// 2026-10-07: <c>GenericTypedContainer</c> storage vessels are not matched
    /// by the shipped <c>clay/storagevessel*</c> wildcard, so ten
    /// <c>game:storagevessel-*-fired</c> blocks do receive automapped interact
    /// clones on a default surface - now pinned behaviourally by
    /// <c>InteractTwinPayloadScenarios</c> and <c>InteractTwinStripScenarios</c>.
    /// B5c keeps its structural waiver; B10 keeps only the structural half (the
    /// marker field itself).
    /// <para>
    /// These are plain <c>[Fact]</c>s with no Atlas attributes: the marker they
    /// pin is what the baseline restore pass un-creates clones by, and it is a
    /// property of the behavior <i>classes</i>, not of any world state - so no
    /// world boots for them. Both are RED until the restore pass adds the marker.
    /// </para>
    /// </summary>
    [Trait("area", "Recondition")]
    [Trait("summary", "Structural coverage rows for the two automap clone kinds: both behavior classes carry the CreatedByConditioning marker the restore pass un-creates clones by, pinned as a class property without booting a world.")]
    public class ConvergenceStructureScenarios
    {
        private const string MarkerFieldName = "CreatedByConditioning";

        /// <summary>
        /// Resolves a behavior type without booting a world.
        /// <para>
        /// These two rows are plain <c>[Fact]</c>s, so unlike every Atlas scenario row nothing has
        /// necessarily loaded the CarryOn assembly by the time they run. Every ProjectReference into
        /// the mod is <c>Private="false"</c> and <c>CarryOn</c> is absent from deps.json, so the
        /// name only resolves once some Atlas scenario class has booted a server in the same process.
        /// A bare <c>typeof(BlockBehaviorCarryable)</c> therefore threw
        /// <c>FileNotFoundException: Could not load file or assembly 'CarryOn, Version=2.0.0.0'</c>
        /// and both rows passed or failed purely on class-discovery order - in a full-suite run they
        /// failed whenever discovery happened to put this class ahead of every Atlas class. Prefer the
        /// already-loaded instance so the common case binds to the same assembly the harness booted,
        /// and otherwise load this run's own build output so a filtered run of just this class works.
        /// </para>
        /// </summary>
        private static Type? BehaviorType(string simpleName, out string how)
        {
            const string prefix = "CarryOn.Common.Behaviors.";

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var loaded = assembly.GetType(prefix + simpleName, throwOnError: false);
                if (loaded != null)
                {
                    how = $"resolved from already-loaded {assembly.GetName().Name} at '{assembly.Location}'";
                    return loaded;
                }
            }

            // The test output is <repo>/tests/CarryOn.Scenarios/bin/<Configuration>/net10.0, so the
            // configuration of this very run is the parent directory's name, and the mod's build
            // output is five levels up. (Same count as the CarryOn.zip path in AssemblyInfo.cs.)
            var netDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            string? configuration = netDir.Parent?.Name;
            if (configuration == null)
            {
                how = $"no loaded assembly has {prefix}{simpleName}, and the test output path "
                    + $"'{AppContext.BaseDirectory}' has no parent directory to read a configuration from";
                return null;
            }

            string modDll = Path.GetFullPath(Path.Combine(
                netDir.FullName, "..", "..", "..", "..", "..", "bin", configuration, "net10.0", "CarryOn.dll"));
            if (!File.Exists(modDll))
            {
                how = $"no loaded assembly has {prefix}{simpleName}, and this run's mod build output "
                    + $"is not at '{modDll}'";
                return null;
            }

            InstallEngineResolver(modDll);

            Assembly fallback;
            try
            {
                fallback = Assembly.LoadFrom(modDll);
            }
            catch (Exception ex)
            {
                how = $"no loaded assembly has {prefix}{simpleName}, and LoadFrom('{modDll}') threw "
                    + $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }

            var type = fallback.GetType(prefix + simpleName, throwOnError: false);
            how = type == null
                ? $"loaded '{modDll}' but could not materialise {prefix}{simpleName} - see [resolver]"
                : $"resolved by LoadFrom('{modDll}')";
            return type;
        }

        private static readonly object ResolverGate = new();
        private static bool resolverInstalled;

        /// <summary>
        /// Teaches the default load context where this run's engine assemblies are.
        /// <para>
        /// Every VintageStory reference in the scenario project is <c>Private="false"</c> and the
        /// engine is hand-staged into the output directory by Stage-Engine.ps1, so neither the engine
        /// nor <c>CarryOn</c> appears in deps.json. Atlas's own harness resolves them explicitly for
        /// the rows that boot a server, which is why every scenario row works; a plain <c>[Fact]</c>
        /// has no such resolver. Without this, <c>Assembly.LoadFrom(modDll)</c> succeeds but the mod's
        /// types cannot be materialised - their base types are unresolvable - so
        /// <c>GetType(name, throwOnError: false)</c> quietly returns null and the row fails on a
        /// missing field rather than on a missing type. Test-process-wide by nature, but it only
        /// answers for names nothing else can resolve, from the three output directories this
        /// project already builds: the staged engine, the mod, and the sibling CarryOnLib.
        /// </para>
        /// </summary>
        private static void InstallEngineResolver(string modDll)
        {
            lock (ResolverGate)
            {
                if (resolverInstalled) return;
                resolverInstalled = true;

                var netDir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
                string configuration = netDir.Parent?.Name ?? "Release";

                // Six levels up from bin/<Configuration>/net10.0 is the repo parent, which is where
                // the CarryOnLib checkout lives (same count as its zip path in AssemblyInfo.cs).
                string libDir = Path.GetFullPath(Path.Combine(
                    netDir.FullName, "..", "..", "..", "..", "..", "..",
                    "CarryOnLib", "bin", configuration, "net10.0"));

                string[] probeDirs =
                {
                    AppContext.BaseDirectory,
                    Path.GetDirectoryName(modDll)!,
                    libDir,
                };

                AssemblyLoadContext.Default.Resolving += (context, name) =>
                {
                    foreach (string dir in probeDirs)
                    {
                        string candidate = Path.Combine(dir, name.Name + ".dll");
                        if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
                    }
                    return null;
                };
            }
        }

        private static FieldInfo? MarkerOf(Type? behaviorType)
            => behaviorType?.GetField(MarkerFieldName, BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// B5c - the automapper's carryable clones must be un-creatable by the restore
        /// pass regardless of the match key that produced them. The original row
        /// (drop a shape from <c>AllowedShapeOnlyMatches</c>) could not be fixtured: no
        /// vanilla shape-only clone exists on either default surface. The marker is the
        /// coverage the row wanted to prove - key-agnostic, so a Class-key clone like
        /// <c>game:flowerpot-amber</c>'s is covered exactly like a shape-only clone
        /// would be.
        /// </summary>
        [Trait("summary", "BlockBehaviorCarryable exposes the private CreatedByConditioning instance field the restore pass un-creates carryable clones by.")]
        [Trait("pins", "The restore pass can un-create automap carryable clones regardless of the match key that produced them.")]
        [Trait("role", "coverage")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the restore pass added the marker field to the behavior class.")]
        [Fact]
        public void CarryableClones_AreMarked_B5cStructural()
        {
            var type = BehaviorType("BlockBehaviorCarryable", out var how);
            var field = MarkerOf(type);
            Assert.True(
                field != null,
                $"structural pin: "
                + $"BlockBehaviorCarryable needs an internal instance field "
                + $"'{MarkerFieldName}' (non-serialized, set only by the CarryOn factory) so "
                + "the restore pass can un-create automap clones regardless of the match key "
                + "that produced them. B5c waiver: no vanilla shape-only clone exists on "
                + $"either default surface, so the coverage is pinned structurally [{how}]");
        }

        /// <summary>
        /// B10 - the automapper's interact clones (built in
        /// <c>BehaviorAutoMapper</c>'s interact pass) must be un-creatable by the same
        /// restore pass, with the same marker. The original waiver claimed no interact
        /// clone exists on any default surface; corrected 2026-10-07: ten
        /// <c>game:storagevessel-*-fired</c> blocks do receive one
        /// (<c>GenericTypedContainer</c> is not matched by the shipped
        /// <c>clay/storagevessel*</c> wildcard), and their payload and config-off strip
        /// are pinned by <c>InteractTwinPayloadScenarios</c> and
        /// <c>InteractTwinStripScenarios</c>. What remains here is the structural half:
        /// the marker field itself.
        /// </summary>
        [Trait("summary", "BlockBehaviorCarryableInteract exposes the same private CreatedByConditioning marker field, so the restore pass un-creates interact clones too.")]
        [Trait("pins", "Automapped interact clones go through the same un-create path as carryable clones.")]
        [Trait("role", "coverage")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before the restore pass added the marker field to the interact behavior class.")]
        [Fact]
        public void InteractClones_AreMarked_B10Structural()
        {
            var type = BehaviorType("BlockBehaviorCarryableInteract", out var how);
            var field = MarkerOf(type);
            Assert.True(
                field != null,
                $"structural pin: "
                + $"BlockBehaviorCarryableInteract needs the same internal "
                + $"instance field '{MarkerFieldName}' the carryable class gets, so the "
                + "restore pass un-creates interact clones at the automapper's interact "
                + "append sites too. B10: ten game:storagevessel-*-fired clones DO exist "
                + "on a default surface (corrected 2026-10-07) and are pinned "
                + "behaviourally by the InteractTwin* rows; this row keeps the structural "
                + $"half - the marker field itself [{how}]");
        }
    }
}
