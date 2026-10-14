using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace CarryOn.Client.Logic.TransformGroupResolvers.HiddenBackpack
{
    /// <summary>
    /// Reads Vanity's per-wearer slot visibility through its public client API, resolved by
    /// reflection so the mod assembly is never referenced. Vanity ships two API generations -
    /// 2.4.8's <c>ShouldRenderGear(Entity, EnumGearType)</c> and 2.5.1's renamed
    /// <c>ShouldRenderSlot(Entity, EquipmentSlot)</c> - both exposing the same static
    /// <c>Vanity.VanitySystem.Client</c> entry point, and both folding the server-side
    /// hide permission and the local ForceRenderOwnGear option into the result, so a call
    /// through the public method matches Vanity's own render decision. The scaffold selects
    /// the enum-typed overload and resolves the <c>Back</c> member by name (never by ordinal,
    /// the member lists just happen to match today).
    /// </summary>
    public sealed class VanityHiddenBackpackSignal : IHiddenBackpackSignal
    {
        public const string SignalCodeName = "vanity";
        public const string ModId = SignalCodeName;
        public const string VanitySystemTypeName = "Vanity.VanitySystem";
        public const string ClientPropertyName = "Client";
        public const string ShouldRenderSlotMethodName = "ShouldRenderSlot";
        public const string ShouldRenderGearMethodName = "ShouldRenderGear";
        public const string BackSlotName = "Back";

        // Vanity exposes no change token, so this signal re-evaluates on a short TTL instead:
        // the cache signature flips on TTL bucket boundaries, bounding the reflection call to
        // roughly four per wearer per second.
        public const long CacheTtlMilliseconds = 250;

        private readonly Type? vanitySystemTypeOverride;
        private readonly ILogger? logger;
        private Scaffold? scaffold;
        private bool scaffoldResolved;
        private bool loggedScaffoldFailure;

        public VanityHiddenBackpackSignal(ILogger? logger = null)
        {
            this.logger = logger;
        }

        public VanityHiddenBackpackSignal(Type vanitySystemType, ILogger? logger = null)
        {
            // Stand-in seam: scaffold against a type shaped like one of Vanity's API
            // generations instead of searching loaded assemblies.
            vanitySystemTypeOverride = vanitySystemType
                ?? throw new ArgumentNullException(nameof(vanitySystemType));
            this.logger = logger;
        }

        public string SignalCode => SignalCodeName;

        public bool IsBackpackHidden(EntityPlayer entity)
        {
            if (entity == null) return false;

            var resolved = EnsureScaffold();
            if (resolved == null) return false;

            try
            {
                var shouldRender = resolved.ShouldRenderMethod.Invoke(
                    resolved.ClientInstance, new object[] { entity, resolved.BackSlotValue });
                return shouldRender is bool render && !render;
            }
            catch (Exception ex)
            {
                LogOnce("Vanity's client API rejected the backpack visibility query; the hidden-backpack override stays inactive for Vanity.", ex);
                return false;
            }
        }

        public string? GetCacheSignature(EntityPlayer entity)
            => SignalCodeName + ":" + Environment.TickCount64 / CacheTtlMilliseconds;

        /// <summary>
        /// The result of an attempt to build a <see cref="Scaffold"/> from a type shaped like
        /// <c>Vanity.VanitySystem</c>.
        /// </summary>
        public enum ScaffoldResolution
        {
            /// <summary>A scaffold was built; read it from the out argument.</summary>
            Resolved,

            /// <summary>
            /// The static <c>Client</c> property exists but holds no instance yet - Vanity's
            /// client system has not started. Transient: a later attempt may succeed.
            /// </summary>
            NotReady,

            /// <summary>
            /// The type matches neither shipped Vanity API generation. Permanent for that type.
            /// </summary>
            UnknownShape
        }

        /// <summary>
        /// Builds a reflection scaffold from a type shaped like <c>Vanity.VanitySystem</c>:
        /// a static <c>Client</c> property whose instance exposes a two-parameter
        /// <c>ShouldRenderSlot</c>/<c>ShouldRenderGear</c> method taking <c>(Entity, enum)</c>
        /// and returning bool, where the enum carries a <c>Back</c> member. The tri-state
        /// result tells apart a scaffolded shape (<see cref="ScaffoldResolution.Resolved"/>), a
        /// known-but-not-started client (<see cref="ScaffoldResolution.NotReady"/>), and a
        /// permanent unknown API generation (<see cref="ScaffoldResolution.UnknownShape"/>).
        /// </summary>
        public static ScaffoldResolution TryBuildScaffold(Type vanitySystemType, out Scaffold? scaffold)
        {
            ArgumentNullException.ThrowIfNull(vanitySystemType);
            scaffold = null;

            var clientProperty = vanitySystemType.GetProperty(
                ClientPropertyName, BindingFlags.Public | BindingFlags.Static);
            if (clientProperty == null || !clientProperty.CanRead)
            {
                return ScaffoldResolution.UnknownShape;
            }

            var clientInstance = clientProperty.GetValue(null);
            if (clientInstance == null)
            {
                return ScaffoldResolution.NotReady;
            }

            foreach (var method in clientInstance.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != ShouldRenderSlotMethodName
                    && method.Name != ShouldRenderGearMethodName) continue;
                if (method.ReturnType != typeof(bool)) continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 2) continue;
                if (parameters[0].ParameterType != typeof(Entity)) continue;

                var slotType = parameters[1].ParameterType;
                if (!slotType.IsEnum) continue;
                if (!Enum.TryParse(slotType, BackSlotName, out var backValue) || backValue == null) continue;

                scaffold = new Scaffold(clientInstance, method, backValue);
                return ScaffoldResolution.Resolved;
            }

            return ScaffoldResolution.UnknownShape;
        }

        /// <summary>
        /// Builds a scaffold, or null when the type does not currently yield one - including
        /// while Vanity's client system has not started yet. Prefer the tri-state overload
        /// when a transient <see cref="ScaffoldResolution.NotReady"/> must be distinguished
        /// from a permanent unknown API generation.
        /// </summary>
        public static Scaffold? TryBuildScaffold(Type vanitySystemType)
            => TryBuildScaffold(vanitySystemType, out var scaffold) == ScaffoldResolution.Resolved
                ? scaffold
                : null;

        private Scaffold? EnsureScaffold()
        {
            if (scaffoldResolved) return scaffold;

            var type = vanitySystemTypeOverride ?? FindVanitySystemType();
            if (type == null)
            {
                // Vanity's assembly is not loaded; there is no shape to inspect yet. Leave the
                // latch off so a later render query (bounded by the signal cache TTL) re-checks.
                scaffold = null;
                return null;
            }

            ScaffoldResolution resolution;
            try
            {
                resolution = TryBuildScaffold(type, out scaffold);
            }
            catch (Exception ex)
            {
                scaffold = null;
                LogOnce("Failed to resolve Vanity's client API; the hidden-backpack override stays inactive for Vanity.", ex);
                return null;
            }

            if (resolution == ScaffoldResolution.Resolved)
            {
                scaffoldResolved = true;
                return scaffold;
            }

            if (resolution == ScaffoldResolution.NotReady)
            {
                // Vanity's client system has not started yet - transient, so leave the latch off
                // and re-inspect on the next render query (the signal cache TTL bounds the retry
                // cadence instead of probing every frame).
                scaffold = null;
                return null;
            }

            scaffoldResolved = true;
            LogOnce("Vanity is present but its client API shape was not recognized (neither ShouldRenderGear nor ShouldRenderSlot); the hidden-backpack override stays inactive for Vanity.");
            return null;
        }

        private static Type? FindVanitySystemType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(VanitySystemTypeName, throwOnError: false);
                if (type != null) return type;
            }
            return null;
        }

        private void LogOnce(string message, Exception? exception = null)
        {
            if (loggedScaffoldFailure) return;
            loggedScaffoldFailure = true;

            if (exception == null)
            {
                logger?.Warning("CarryOn: " + message);
            }
            else
            {
                logger?.Warning("CarryOn: " + message + " (" + exception.Message + ")");
            }
        }

        /// <summary>The cached reflection targets for one Vanity API generation.</summary>
        public sealed record Scaffold(object ClientInstance, MethodInfo ShouldRenderMethod, object BackSlotValue);
    }
}
