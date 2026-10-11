using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Network;
using CarryOn.Common.Entities;
using CarryOn.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Client.Logic.TransformTemplates;
using CarryOn.Server.Logic;
using CarryOn.Server.Logic.BehavioralConditions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn
{
    public partial class CarrySystem
    {
        private Action<CarryOnConfig>? onConfigChangedInvalidateBackpackCache;
        private Action<CarryOnConfig>? onConfigChangedRefreshClientHandlers;
        private Action<CarryOnConfig>? onConfigChangedUpdateEntityConfig;
        private Action<CarryOnConfig>? onConfigChangedPersistToDisk;
        private Action<CarryOnConfig>? onConfigChangedBroadcastToClients;
        private Action<CarryOnConfig>? onConfigChangedRegenerateReport;
        private Action<CarryOnConfig>? onConfigReloadedRegenerateReport;
        private Action<CarryOnConfig>? onBehavioralReconditionedBroadcast;

        /// <summary>
        /// Managed block codes the client has applied via <see cref="CarryabilityStateMessage"/>
        /// so a shrinking snapshot can remove no-longer-managed codes exactly once.
        /// Only codes successfully applied in the current sync enter this set.
        /// </summary>
        private readonly HashSet<string> syncedCarryabilityCodes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Snapshot entries the client could not apply yet because their block was not in
        /// the client registry. Retried on the next snapshot; each entry logs its
        /// deferral once, so a permanently unresolvable code is diagnosable without
        /// spamming every snapshot. A deferred code is not treated as applied for the
        /// shrink pass, and keeps its earlier mirror tracked until it resolves.
        /// </summary>
        private readonly List<CarryabilityStateMessage.CarryabilityEntry> pendingCarryabilityEntries = new();

        /// <summary>
        /// Per managed block code and behavior kind, the mirror instances the client
        /// mounted from <see cref="CarryabilityStateMessage"/> snapshots. The client owns
        /// exactly these mirrors plus the engine-loaded CONDITIONAL behaviors of a managed
        /// code (the client never runs the server's conditioning, so raw conditionals stay
        /// until a sync reconciles them): a sync replaces those by reference/predicate -
        /// never by runtime type - so an unconditional base carryable survives client-side
        /// reconciliation, mirroring the server's instance-owned conditioning fix.
        /// <para>
        /// <b>One shared instance in both views - deliberately.</b> A block's
        /// <c>BlockBehaviors</c> and <c>CollectibleBehaviors</c> arrays normally hold
        /// distinct copies, but when an engine behavior is patch-loaded or the block is
        /// remounted at pass 0 both arrays can end up referencing the <i>same</i> object,
        /// and the client mirrors inherit that shape. It is left as-is rather than split,
        /// because <see cref="RemoveSyncedBehaviors"/> is written to handle both shapes: it
        /// removes by exact reference, so a shared instance is retired once and a per-view
        /// clone is retired from its own view without double-removing or leaving a
        /// half-reconciled twin. Splitting would defeat that reference-based removal, and
        /// no defect was found to fix. Test-side counterpart:
        /// <c>CarryOnParityAssert.SharedInstance</c>.
        /// </para>
        /// <para>See <c>docs/technical-overview/carryability-sync.md</c> for the full
        /// reconciliation contract these three containers implement.</para>
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, List<BlockBehavior>>> syncedOwnedMirrors
            = new(StringComparer.OrdinalIgnoreCase);

        private void WireConfigChangeHandlers()
        {
            onConfigChangedInvalidateBackpackCache = _ => Config.InvalidateBackpackCache();
            onConfigChangedRefreshClientHandlers = _ => this.CarryClientHandlers?.RefreshConfigCache();
            onConfigChangedUpdateEntityConfig = _ => EntityCarriedBlock.Config = Config.CarriedBlockEntity;
            // The `ServerApi == null` early-return below is the only thing standing between a
            // client that received a server config broadcast (OnClientConfigSync ->
            // ConfigService.Replace -> OnConfigChanged, reaching here with persistSuspended
            // still false because fromFileReload defaults to false) and this callback
            // rewriting the *client's* copy of CarryOnConfig.json. The wrong fix is to pass
            // fromFileReload: true at that Replace call site: the flag models reload origin
            // ("the file is already the source of truth"), not permission to skip a write,
            // and setting it there would suppress legitimate server-side writeback for a
            // reason that does not apply. If this guard is ever restructured, keep it an
            // explicit side check.
            onConfigChangedPersistToDisk = _ =>
            {
                if (ServerApi == null) return;
                if (ServerApi.World.Config is ITreeAttribute worldTree)
                    worldTree[ModId] = Config.ToTreeAttribute();

                if (ConfigService.IsReloadOriginated)
                {
                    // The change came from the file itself - the file is already the source
// of truth, and writing it again would clobber the user's edits - the data-loss
                // case this guard exists for. In-memory state (world config tree) is still
                    // updated above so conditional behaviors re-evaluate correctly.
                    ServerApi.Logger.Debug("CarryOn: Skipping StoreModConfig (reload-originated change)");
                    return;
                }

                ServerApi.Logger.Debug("CarryOn: Persisting config to disk via StoreModConfig");
                ServerApi.StoreModConfig(Config, ConfigFile);
            };
            onConfigChangedBroadcastToClients = _ =>
                {
                    if (ServerChannel == null || ServerApi == null) return;
                    var json = JsonConvert.SerializeObject(Config);
                    ServerApi.Logger.Debug("CarryOn: Broadcasting config sync to clients");
                    ServerChannel.BroadcastPacket(new ConfigSyncMessage(json));
                };
            onConfigChangedRegenerateReport = _ =>
                {
                    // Reload-originated replaces regenerate after behavioral conditioning
                    // (see onConfigReloadedRegenerateReport), so the report reflects the
                    // actually applied carryable behaviors.
                    if (ConfigService.IsReloadOriginated) return;
                    carryableReportService?.Generate();
                };
            onConfigReloadedRegenerateReport = _ => carryableReportService?.Generate();
            onBehavioralReconditionedBroadcast = _ => RebuildAndBroadcastCarryabilitySnapshot();

            ConfigService.OnConfigChanged += onConfigChangedInvalidateBackpackCache;
            ConfigService.OnConfigChanged += onConfigChangedRefreshClientHandlers;
            ConfigService.OnConfigChanged += onConfigChangedUpdateEntityConfig;
            ConfigService.OnConfigChanged += onConfigChangedPersistToDisk;
            ConfigService.OnConfigChanged += onConfigChangedBroadcastToClients;
            ConfigService.OnConfigChanged += onConfigChangedRegenerateReport;
            ConfigService.OnConfigReloadedFromFile += onConfigReloadedRegenerateReport;
            ConfigService.OnBehavioralReconditioned += onBehavioralReconditionedBroadcast;
        }

        private void UnwireConfigChangeHandlers()
        {
            ConfigService.OnConfigChanged -= onConfigChangedInvalidateBackpackCache;
            ConfigService.OnConfigChanged -= onConfigChangedRefreshClientHandlers;
            ConfigService.OnConfigChanged -= onConfigChangedUpdateEntityConfig;
            ConfigService.OnConfigChanged -= onConfigChangedPersistToDisk;
            ConfigService.OnConfigChanged -= onConfigChangedBroadcastToClients;
            ConfigService.OnConfigChanged -= onConfigChangedRegenerateReport;
            ConfigService.OnConfigReloadedFromFile -= onConfigReloadedRegenerateReport;
            ConfigService.OnBehavioralReconditioned -= onBehavioralReconditionedBroadcast;

            onConfigChangedInvalidateBackpackCache = null;
            onConfigChangedRefreshClientHandlers = null;
            onConfigChangedUpdateEntityConfig = null;
            onConfigChangedPersistToDisk = null;
            onConfigChangedBroadcastToClients = null;
            onConfigChangedRegenerateReport = null;
            onConfigReloadedRegenerateReport = null;
            onBehavioralReconditionedBroadcast = null;
        }

        private void OnClientConfigSync(ConfigSyncMessage message)
        {
            if (message.ConfigJson == null || ClientApi == null) return;

            try
            {
                var reloaded = JsonConvert.DeserializeObject<CarryOnConfig>(message.ConfigJson);
                if (reloaded != null)
                {
                    reloaded.UpgradeVersion();
                    ConfigService.Replace(reloaded);
                    reloaded.ValidateBackpackTransformGroups(ClientApi.Logger);
                    ClientApi.Logger.Notification("CarryOn: Config synced from server and applied");
                }
            }
            catch (Exception ex)
            {
                ClientApi.Logger.Warning("CarryOn: Failed to apply synced config: " + ex.Message);
            }
        }

        // ------------------------------
        //  Carryability state sync
        // ------------------------------

        /// <summary>
        /// Rebuilds the boot/reload snapshot cache and broadcasts it to all connected
        /// clients. Called from <see cref="CarryOnConfigService.OnBehavioralReconditioned"/>
        /// (Mode B only, dirty gate passed).
        /// </summary>
        private void RebuildAndBroadcastCarryabilitySnapshot()
        {
            if (ServerApi == null) return;
            carryabilitySnapshot = BehavioralConditioning.BuildStateMessage(ServerApi);
            if (ServerChannel == null) return;

            ServerApi.Logger.Debug("CarryOn: Broadcasting carryability state to clients");
            ServerChannel.BroadcastPacket(carryabilitySnapshot);
        }

        /// <summary>
        /// Sends the current carryability snapshot to a joining player so the client
        /// reconciles onto server reality - including the disable direction, which a relog
        /// alone never fixed because joiners receive the engine's boot-built block/behavior
        /// transfer. Mode B only.
        /// </summary>
        private void OnServerPlayerJoin(IServerPlayer player)
        {
            if (carryabilitySnapshot == null || ServerChannel == null) return;
            ServerChannel.SendPacket(carryabilitySnapshot, player);
        }

        /// <summary>
        /// Reconciles the client's local block registry with the server's carryability
        /// snapshot: each managed code is set to exactly the sent state (mounting fresh
        /// behaviors from the post-condition properties JSON, or removing them), then
        /// derived client state is rebuilt - the transform template manager from the
        /// reconciled registry, and the render caches by full invalidation, because a
        /// remounted behavior is invisible to both cache layers (the plan-cache
        /// signature excludes behavior properties, and the per-slot sidecar memo
        /// bypasses the plan cache unless one of its own dirty inputs changed).
        /// <para><b>This handler must not wedge.</b> Every entry is applied through
        /// <see cref="TryApplyEntry"/>, which catches per entry and logs the offending
        /// (BlockCode, kind) before continuing, so one mod's malformed output can no longer
        /// abort the loop and leave the client half-synced with no log. Three throw vectors
        /// are covered: <c>new AssetLocation</c> on a syntactically invalid code,
        /// <c>JToken.Parse</c> on a malformed properties payload, and anything the behavior's
        /// own <c>Initialize</c> throws. <c>RebuildClientDerivedState</c> runs from a
        /// <c>finally</c>, so the transform template manager and the render caches are never
        /// left built from the pre-sync registry.</para>
        /// <para>An entry whose block is not in the client registry yet is <i>deferred</i>,
        /// not skipped forever: it is retried at the head of the next snapshot, gated on
        /// snapshot arrival rather than a timer, and logs its deferral once. Decided in the
        /// same change (the interaction with the retry is the point): a deferred
        /// code does <b>not</b> enter the "seen" set, and is exempt from the shrink pass, so
        /// a mirror mounted by an earlier snapshot survives until the entry either resolves or
        /// genuinely leaves the managed set.</para>
        /// <para><b>Two behavior shapes, deliberately not normalized.</b> A synced
        /// behavior may appear in <c>BlockBehaviors</c> and <c>CollectibleBehaviors</c> as
        /// one shared instance (the engine's own shape for a patch-loaded behavior, and the
        /// shape a pass-0 remount can produce) or as per-view clones. The mirror records
        /// whichever shape it mounted and
        /// <see cref="RemoveSyncedBehaviorsOfKind"/> removes by exact reference, so both
        /// reconcile correctly. Normalizing to per-view instances would break the
        /// reference-identity removal the instance-owned fix relies on and fix no observed
        /// defect - this is a documentation item for that reason. See the
        /// <c>syncedOwnedMirrors</c> doc for the full statement.</para>
        /// </summary>
        private void OnCarryabilityStateSync(CarryabilityStateMessage message)
        {
            var clientApi = ClientApi;
            if (clientApi == null || message.Entries == null) return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var previouslySynced = new HashSet<string>(syncedCarryabilityCodes, StringComparer.OrdinalIgnoreCase);

            try
            {
                // Deferred entries first: an entry whose block was missing from the
                // client registry when its snapshot landed is retried here, before this
                // snapshot's own entries, so a code that was merely early gets applied even
                // if the newer snapshot no longer mentions it. Gated on snapshot arrival
                // rather than a timer - never a spin - and each entry logs its deferral once.
                RetryDeferredEntries(clientApi, seen);

                foreach (var entry in message.Entries)
                {
                    if (string.IsNullOrWhiteSpace(entry?.BlockCode)) continue;
                    if (TryApplyEntry(clientApi, entry, seen)) DeferEntry(clientApi, entry);
                }

                var pending = PendingCodes();

                // Codes this sync has touched in a previous snapshot but that are no longer
                // part of the managed set: remove any mirrored behaviors so a shrinking set
                // does not linger on the client. Pending codes are exempt: their entry
                // is still live, its block is simply not in the registry yet, so tearing its
                // mirror down would make the eventual retry re-mount from scratch for no gain
                // and would drop the engine-loaded conditionals the entry has not replaced.
                foreach (var code in previouslySynced)
                {
                    if (seen.Contains(code) || pending.Contains(code)) continue;
                    var block = clientApi.World.GetBlock(new AssetLocation(code));
                    if (block != null && block.Id != 0)
                    {
                        RemoveSyncedCarryabilityBehaviors(block, code);
                    }
                    syncedOwnedMirrors.Remove(code);
                }

                syncedCarryabilityCodes.Clear();
                syncedCarryabilityCodes.UnionWith(seen);

                // A deferred code whose mirror is still mounted from an earlier snapshot stays
                // tracked: dropping it here would leave the shrink pass with nothing to clean
                // up once the entry finally applies (or leaves the managed set for good).
                foreach (var code in pending)
                {
                    if (previouslySynced.Contains(code)) syncedCarryabilityCodes.Add(code);
                }
            }
            finally
            {
                // Always runs, including when an entry threw: without it the client keeps a
                // transform template manager and render caches built from the pre-sync
                // registry, which is the silent half of the wedge this fixes.
                RebuildClientDerivedState();
            }
        }

        /// <summary>
        /// Retries every deferred entry once, keeping only those still unresolvable.
        /// See <see cref="OnCarryabilityStateSync"/> for the gating rationale.
        /// </summary>
        private void RetryDeferredEntries(ICoreClientAPI clientApi, HashSet<string> seen)
        {
            if (pendingCarryabilityEntries.Count == 0) return;

            var stillDeferred = new List<CarryabilityStateMessage.CarryabilityEntry>();
            foreach (var entry in pendingCarryabilityEntries)
            {
                if (TryApplyEntry(clientApi, entry, seen)) stillDeferred.Add(entry);
            }

            pendingCarryabilityEntries.Clear();
            pendingCarryabilityEntries.AddRange(stillDeferred);
        }

        /// <summary>
        /// Applies one snapshot entry, reporting whether it must stay deferred because its
        /// block is not in the client registry yet. Every failure path logs (code, kind) and
        /// returns, so one mod's bad conditioning output can never abort the loop or leave
        /// the client half-synced.
        /// </summary>
        private bool TryApplyEntry(
            ICoreClientAPI clientApi, CarryabilityStateMessage.CarryabilityEntry entry, HashSet<string> seen)
        {
            var code = entry.BlockCode;
            var kind = entry.Kind ?? string.Empty;

            AssetLocation location;
            try
            {
                location = new AssetLocation(code);
            }
            catch (Exception ex)
            {
                // Syntactically invalid: no retry can ever succeed, so it is dropped rather
                // than deferred. Logged every time - it is a permanent authoring error.
                clientApi.Logger.Warning(
                    "CarryOn: Carryability sync skipped a malformed BlockCode '{0}' (kind '{1}'): {2}",
                    code, kind, ex.Message);
                return false;
            }

            var block = clientApi.World.GetBlock(location);
            if (block == null || block.Id == 0) return true;

            try
            {
                SyncCarryabilityBehavior(block, code, kind, entry.PropertiesJson);
            }
            catch (Exception ex)
            {
                // A bad properties payload costs this one entry; the rest still apply.
                clientApi.Logger.Warning(
                    "CarryOn: Carryability sync failed to apply BlockCode '{0}' (kind '{1}'): {2}",
                    code, kind, ex.Message);
                return false;
            }

            seen.Add(code);
            return false;
        }

        /// <summary>
        /// Records an entry whose block is not in the client registry yet, logging once per
        /// entry so a permanently unresolvable code stays diagnosable without spamming every
        /// snapshot.
        /// </summary>
        private void DeferEntry(ICoreClientAPI clientApi, CarryabilityStateMessage.CarryabilityEntry entry)
        {
            foreach (var pending in pendingCarryabilityEntries)
            {
                if (!string.Equals(pending.BlockCode, entry.BlockCode, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(pending.Kind, entry.Kind, StringComparison.Ordinal))
                {
                    continue;
                }

                // Already deferred and still unresolvable - its warning was logged once.
                return;
            }

            pendingCarryabilityEntries.Add(entry);
            clientApi.Logger.Warning(
                "CarryOn: Carryability sync deferred BlockCode '{0}' (kind '{1}'): not in this "
                + "client's block registry yet. It is retried on the next snapshot; if it never "
                + "resolves, this code stays out of sync on the client.",
                entry.BlockCode, entry.Kind ?? string.Empty);
        }

        private HashSet<string> PendingCodes()
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in pendingCarryabilityEntries) codes.Add(entry.BlockCode);
            return codes;
        }

        /// <summary>
        /// Sets one managed code's behavior of <paramref name="kind"/> to exactly the
        /// snapshot state. The client-owned mirrors of that kind and the engine-loaded
        /// conditional behaviors of that kind are removed (by reference / by the same
        /// predicate the server's deferred strip uses - never by runtime type, so an
        /// unconditional sibling survives), then a fresh mirror is mounted from the
        /// post-condition properties JSON. A snapshot whose surviving behavior is
        /// unconditional (an unconditional base won the server's resolver, or a
        /// slot-conditional's slots are all stripped) mounts nothing: the engine-loaded
        /// behavior already provides that state, and a second copy would never be
        /// collapsed (the client runs no multiple-behavior resolver).
        /// <para>A malformed properties payload throws out of here into
        /// <see cref="TryApplyEntry"/>, which logs it with the entry's (code, kind) and
        /// continues - so one bad entry costs that entry, not the snapshot.</para>
        /// </summary>
        private void SyncCarryabilityBehavior(Block block, string code, string kind, string? propertiesJson)
        {
            if (kind != BlockBehaviorCarryable.Name && kind != BlockBehaviorCarryableInteract.Name) return;

            RemoveSyncedBehaviorsOfKind(block, code, kind);

            if (string.IsNullOrEmpty(propertiesJson)) return;

            var behavior = kind == BlockBehaviorCarryable.Name
                ? (BlockBehavior)new BlockBehaviorCarryable(block)
                : new BlockBehaviorCarryableInteract(block);

            // A malformed payload throws here. It is deliberately not caught in this method:
            // TryApplyEntry owns the entry's (code, kind) context for the log, so catching
            // here would either lose that context or log it twice.
            behavior.Initialize(new JsonObject(JToken.Parse(propertiesJson)));

            if (!BehavioralConditioning.IsConditionalBehavior(behavior)) return;

            block.BlockBehaviors = block.BlockBehaviors.Append(behavior).ToArray();
            block.CollectibleBehaviors = block.CollectibleBehaviors.Append(behavior).ToArray();
            TrackOwnedMirror(code, kind, behavior);
        }

        /// <summary>
        /// Removes every client-owned carryability artifact of a code that left the
        /// managed set: its snapshot mirrors (both kinds, by reference) and the
        /// engine-loaded conditional behaviors (the server no longer conditions this code,
        /// so raw conditionals are stale the same way). Unconditional siblings survive.
        /// </summary>
        private void RemoveSyncedCarryabilityBehaviors(Block block, string code)
        {
            RemoveSyncedBehaviorsOfKind(block, code, BlockBehaviorCarryable.Name);
            RemoveSyncedBehaviorsOfKind(block, code, BlockBehaviorCarryableInteract.Name);
        }

        /// <summary>
        /// Removes, for one managed code and kind, the client's earlier snapshot mirrors
        /// (by reference) and the engine-loaded conditional behaviors of that kind (by
        /// <see cref="BehavioralConditioning.IsConditionalBehavior"/> - the client never
        /// runs the server's conditioning, so raw conditionals are present until this sync
        /// reconciles them). Never an unconditional sibling of the same kind.
        /// </summary>
        private void RemoveSyncedBehaviorsOfKind(Block block, string code, string kind)
        {
            var kindType = kind == BlockBehaviorCarryable.Name
                ? typeof(BlockBehaviorCarryable)
                : typeof(BlockBehaviorCarryableInteract);
            var owned = OwnedMirrors(code, kind);

            block.BlockBehaviors = RemoveSyncedBehaviors(block.BlockBehaviors, kindType, owned);
            block.CollectibleBehaviors = RemoveSyncedBehaviors(block.CollectibleBehaviors, kindType, owned);
            owned?.Clear();
        }

        private static T[] RemoveSyncedBehaviors<T>(T[] behaviors, Type kindType, List<BlockBehavior>? owned)
            where T : CollectibleBehavior
        {
            var list = behaviors.ToList();
            list.RemoveAll(b => kindType.IsInstanceOfType(b)
                && BehavioralConditioning.IsConditionalBehavior((BlockBehavior)(object)b));
            if (owned != null)
            {
                foreach (var mirror in owned)
                {
                    list.RemoveAll(b => ReferenceEquals(b, mirror));
                }
            }
            return list.ToArray();
        }

        private List<BlockBehavior>? OwnedMirrors(string code, string kind)
            => syncedOwnedMirrors.TryGetValue(code, out var byKind)
                && byKind.TryGetValue(kind, out var owned)
                ? owned
                : null;

        private void TrackOwnedMirror(string code, string kind, BlockBehavior behavior)
        {
            if (!syncedOwnedMirrors.TryGetValue(code, out var byKind))
            {
                byKind = new Dictionary<string, List<BlockBehavior>>(StringComparer.OrdinalIgnoreCase);
                syncedOwnedMirrors[code] = byKind;
            }
            if (!byKind.TryGetValue(kind, out var owned))
            {
                owned = new List<BlockBehavior>();
                byKind[kind] = owned;
            }
            owned.Add(behavior);
        }

        /// <summary>
        /// Rebuilds the derived client state that caches carryability-driven data: the
        /// transform template manager (rebuilt from the reconciled block registry),
        /// placeholder registration, and the render/pack-adjustment references. The
        /// render caches are fully invalidated afterwards: anything already
        /// materialized from the old manager/behaviors (cached transform plans and
        /// per-slot render infos) survives the swap otherwise, because the plan-cache
        /// key omits every behavior property and the manager identity, and the
        /// per-slot sidecar memo never consults the plan cache unless one of its own
        /// dirty inputs changed. The interaction-help HUD reflects the registry on
        /// the next help query, so it needs no explicit refresh.
        /// </summary>
        private void RebuildClientDerivedState()
        {
            if (ClientApi is not ICoreClientAPI capi) return;

            var templateManager = TransformTemplateManager.InitializeFromBlocks(capi);
            TransformTemplateManager = templateManager;
            templateManager.DiscoverItemPoseTemplates(capi);
            templateManager.RegisterRenderPlaceholderSettings(capi);
            if (EntityCarryRenderer != null)
            {
                EntityCarryRenderer.TemplateManager = templateManager;
                EntityCarryRenderer.InvalidateRenderCaches();
            }
            if (PackAdjustmentHandler != null) PackAdjustmentHandler.TemplateManager = templateManager;
        }
    }
}
