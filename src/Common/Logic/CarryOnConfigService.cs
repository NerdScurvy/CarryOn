using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CarryOn.Common.Models;
using CarryOn.Server.Logic;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Logic
{
    /// <summary>
    /// Origin of a config reload. Decides where the reload's notices go besides the server
    /// log: watcher reloads have no invoking player, so their notices are broadcast to
    /// admin chat; command reloads already have a result channel (the command result
    /// itself) and must not also broadcast, or the same notice would reach the caller
    /// twice.
    /// </summary>
    public enum ConfigReloadOrigin
    {
        /// <summary>File watcher / debounce reload. Notices are broadcast to admin chat, with a non-dedicated fallback to every online player.</summary>
        Watcher,

        /// <summary>Chat command reload. Notices are returned in the command result; from the server console, log only.</summary>
        Command
    }

    /// <summary>
    /// A single reload-time message, paired with the chat type it renders as when
    /// delivered to a player. Warning-class notices use
    /// <see cref="EnumChatType.CommandError"/> (renders distinctly); informational
    /// notices use <see cref="EnumChatType.Notification"/>.
    /// </summary>
    public readonly record struct ConfigReloadNotice(EnumChatType Type, string Message);

    /// <summary>Outcome of a reload attempt: whether it applied, plus every notice it produced.</summary>
    public sealed record ConfigReloadResult(bool Applied, IReadOnlyList<ConfigReloadNotice> Notices);

    /// <summary>Manages CarryOn configuration loading, hot-reload via file watcher, and change notifications.</summary>
    public sealed class CarryOnConfigService
    {
        /// <summary>Quiet window a file must hold before a reload is attempted (trailing debounce).</summary>
        private const int DebounceMilliseconds = 500;

        private FileSystemWatcher? configWatcher;
        private Timer? debounceTimer;
        private ICoreServerAPI? serverApi;
        private int suppressWatcher;
        private bool persistSuspended;

        /// <summary>
        /// Warning-class notice texts already delivered to player chat this session. The
        /// watcher broadcast path only: recurring warnings such as the mode-mismatch one
        /// fire on every reload while their cause persists, so without this a burst of file
        /// saves would repeat the same line to admin chat. Informational notices (the
        /// applied confirmation) are per-event feedback and are never deduped; the server
        /// log is never deduped; the command-result path never consults or populates this
        /// set, so an explicit command always shows its notices.
        /// </summary>
        private readonly HashSet<string> sentChatNotices = new(StringComparer.Ordinal);

        public CarryOnConfig Config { get; private set; }

        /// <summary>
        /// Outcome of the most recent reload attempt, from either entry point (watcher or
        /// command). Null until the first reload of the session.
        /// </summary>
        public ConfigReloadResult? LastReloadResult { get; private set; }

        public event Action<CarryOnConfig>? OnConfigChanged;

        /// <summary>
        /// Raised after a file reload has been fully applied on the server (including
        /// re-running behavioral conditioning), so consumers can regenerate derived state
        /// against the newly applied config.
        /// </summary>
        public event Action<CarryOnConfig>? OnConfigReloadedFromFile;

        /// <summary>
        /// Raised after behavioral conditioning actually re-ran during a file reload
        /// (Mode B, dirty gate passed). Consumers rebuild and broadcast the carryability
        /// snapshot here - nothing fires when Mode A or a clean/non-behavior reload skipped
        /// the pass.
        /// </summary>
        public event Action<CarryOnConfig>? OnBehavioralReconditioned;

        /// <summary>
        /// Signature of the behavior surface the running world reflects: seeded at boot
        /// (either mode) after the first conditioning pass, and refreshed only after every
        /// successful Mode B recondition, since Mode A never re-runs conditioning. The Mode
        /// B dirty gate reconditions only when the reloaded config's signature differs; the
        /// Mode A pending-restart warning reads the same value to tell an edit that applies
        /// on reload from one that waits for the next server start. Null until the boot
        /// pass, treated as "dirty" by the reload gate.
        /// </summary>
        internal string? AppliedBehaviorSignature { get; set; }

        /// <summary>
        /// The session's boot-time operand mode: true = Mode B (live reconditioning +
        /// resync), false = Mode A (behavior edits wait for a restart). Seeded once by
        /// <c>CarrySystem</c> at <c>StartPre</c> and never reassigned.
        /// <para>
        /// This is a plain field on the service rather than a read of
        /// <c>Config.DebuggingOptions.ReconditionOnConfigChange</c> precisely so it cannot
        /// drift: the config object is replaced on every reload, and its flag is re-read
        /// from the user's file each time. Gating the reload on the config's own value meant
        /// a file edit could switch a running server's mode mid-session — including
        /// <i>into</i> live reconditioning, a capability the operator never enabled for that
        /// session. The flag is documented "(requires restart)", so the file value is a
        /// request for the <i>next</i> session, not for this one.
        /// </para>
        /// <para>
        /// <b>Do not "fix" a mismatch by writing back to the config.</b> Assigning
        /// <c>Config.DebuggingOptions.ReconditionOnConfigChange</c> to agree with this latch
        /// would persist the boot value over the user's edit through the non-reload
        /// writeback path (<c>StoreModConfig</c> serializes the config object) — the exact
        /// data-clobber shape this mod already fixed once. Disagreement is reported, not
        /// resolved.
        /// </para>
        /// </summary>
        internal bool ReconditionOnConfigChangeEnabled { get; set; }

        /// <summary>
        /// True while a config change that originated from the file itself is being applied.
        /// Reload-originated changes must not be written back to the file (the file is
        /// already the source of truth; persisting again would clobber the user's edits),
        /// but they still propagate to in-memory consumers such as the world config tree.
        /// </summary>
        internal bool IsReloadOriginated => persistSuspended;

        public CarryOnConfigService(CarryOnConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Replace(CarryOnConfig newConfig, bool fromFileReload = false)
        {
            ArgumentNullException.ThrowIfNull(newConfig);

            Config = newConfig;
            Interlocked.Increment(ref suppressWatcher);
            var priorPersistSuspended = persistSuspended;
            if (fromFileReload) persistSuspended = true;
            try
            {
                serverApi?.Logger.Debug("CarryOn: Config replaced, firing OnConfigChanged");
                OnConfigChanged?.Invoke(newConfig);
            }
            finally
            {
                persistSuspended = priorPersistSuspended;
                Interlocked.Decrement(ref suppressWatcher);
            }
        }

        public void NotifyChanged()
        {
            OnConfigChanged?.Invoke(Config);
        }

        public void Reload(ConfigReloadOrigin origin = ConfigReloadOrigin.Watcher, IServerPlayer? caller = null)
        {
            ReloadFromFile(origin, caller);
        }

        public void SetupFileWatcher(ICoreServerAPI api)
        {
            serverApi = api;

            if (Config.DebuggingOptions.DisableConfigWatcher)
            {
                api.Logger.Notification("CarryOn: Config watcher disabled via DebuggingOptions.DisableConfigWatcher");
                return;
            }

            try
            {
                var configPath = Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);
                if (!File.Exists(configPath))
                {
                    api.Logger.Warning("CarryOn: Config file not found at " + configPath);
                    return;
                }

                var configDir = Path.GetDirectoryName(configPath);
                if (configDir == null) return;

                // Trailing debounce: each watcher event re-arms this timer, so the reload
                // only runs after the file has been quiet for the full window. Partial
                // multi-write saves (truncate then rewrite) settle before the file is read.
                debounceTimer = new Timer(
                    _ => serverApi?.Event.EnqueueMainThreadTask(() => ReloadFromFile(), "carryon-config-reload"),
                    null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                configWatcher = new FileSystemWatcher(configDir, ConfigFile)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                configWatcher.Changed += OnConfigFileChanged;
                configWatcher.Created += OnConfigFileChanged;

                api.Logger.Notification("CarryOn: Watching " + configPath + " for config changes");
            }
            catch (Exception ex)
            {
                api.Logger?.Warning("CarryOn: Failed to set up config file watcher: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (configWatcher != null)
            {
                configWatcher.EnableRaisingEvents = false;
                configWatcher.Dispose();
                configWatcher = null;
            }
            debounceTimer?.Dispose();
            debounceTimer = null;
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            if (Interlocked.CompareExchange(ref suppressWatcher, 0, 0) != 0)
            {
                serverApi?.Logger.Debug("CarryOn: Config file change suppressed (originated from Replace)");
                return;
            }

            debounceTimer?.Change(DebounceMilliseconds, Timeout.Infinite);
            serverApi?.Logger.Debug("CarryOn: Config file change detected, reload scheduled after debounce");
        }

        private void ReloadFromFile(ConfigReloadOrigin origin = ConfigReloadOrigin.Watcher, IServerPlayer? caller = null)
        {
            if (serverApi == null) return;

            var notices = new List<ConfigReloadNotice>();
            try
            {
                // No main-thread re-check: the trailing debounce guarantees the file has been
                // quiet before a timer-fired reload, and the skeleton guard below rejects any
                // mid-write degraded read. A command-triggered reload (/carryon-reload) must
                // apply synchronously even when a watcher event just landed, so reloads never
                // defer without re-arming a retry.
                var configPath = Path.Combine(GamePaths.DataPath, "ModConfig", ConfigFile);

                string fileText;
                try
                {
                    fileText = File.ReadAllText(configPath);
                }
                catch (Exception ex)
                {
                    RejectReload(origin, "CarryOn: Config file unreadable, keeping current config (" + ex.Message + ")", notices);
                    return;
                }

                if (string.IsNullOrWhiteSpace(fileText))
                {
                    RejectReload(origin, "CarryOn: Config file is empty, keeping current config", notices);
                    return;
                }

                JObject fileJson;
                try
                {
                    fileJson = JObject.Parse(fileText);
                }
                catch (Exception ex)
                {
                    RejectReload(origin, "CarryOn: Config file is not valid JSON, keeping current config (" + ex.Message + ")", notices);
                    return;
                }

                // Deserialize from the exact text we validated above (not a second read) so
                // the bytes the guard accepted are the bytes that are applied.
                var reloaded = fileJson.ToObject<CarryOnConfig>();
                if (reloaded == null)
                {
                    RejectReload(origin, "CarryOn: ReloadFromFile - config deserialization returned null", notices);
                    return;
                }

                if (IsConfigFileSkeleton(fileJson))
                {
                    RejectReload(origin,
                        "CarryOn: Config file has no settable content (every section is empty), "
                        + "keeping current config. This is either a partial write caught mid-save "
                        + "- in which case the file watcher or /carryon-reload will pick it up "
                        + "again once the write finishes - or a deliberately minimal config, in "
                        + "which case it will keep being rejected until you write at least one "
                        + "real value into it. Your running config is unaffected either way.",
                        notices);
                    return;
                }

                WarnOnDroppedSections(fileJson, notices);

                serverApi.Logger.Debug("CarryOn: ReloadFromFile - applying config from disk");
                reloaded.UpgradeVersion();
                Replace(reloaded, fromFileReload: true);
                reloaded.ValidateBackpackTransformGroups(serverApi.Logger);

                // Re-apply behavioral conditioning so carryable / carryable-on-back edits take
                // effect immediately - but only in Mode B, and only when the behavior
                // surface is actually dirty. Mode B is the *boot-time* latch
                // (ReconditionOnConfigChangeEnabled), not the reloaded file's value: the
                // flag is documented "(requires restart)", so a file edit describes what
                // the next server start should do and cannot re-arm a running one. Mode A
                // (the default) therefore preserves the documented "requires restart"
                // semantics even after someone sets the flag in the file mid-session:
                // the config applies, carryability changes wait for the next server start.
                // The world config tree was already updated by the persist handler during
                // Replace, so conditional behaviors re-evaluate correctly whenever the pass
                // does run. The signature is refreshed only after a successful pass so a
                // thrown conditioning keeps the gate dirty for a retry.
                if (ReconditionOnConfigChangeEnabled)
                {
                    var signature = reloaded.ComputeBehaviorSurfaceSignature();
                    if (AppliedBehaviorSignature == null
                        || !string.Equals(AppliedBehaviorSignature, signature, StringComparison.Ordinal))
                    {
                        // Nothing to warn about: pass 0 of the pipeline restores the
                        // per-world baseline first, so every filter edit this pass can
                        // apply is undone by the next one. The interim
                        // one-way-edit warning that used to sit here was retired with the
                        // reversibility it described.
                        new BehavioralConditioning().Init(serverApi, Config);
                        AppliedBehaviorSignature = signature;
                        OnBehavioralReconditioned?.Invoke(Config);
                    }
                }

                // Warnings are diagnostics and must never misreport the reload: the config
                // has already been swapped above, so a throw in the warning path would
                // otherwise fall into the catch and report applied: false for a reload that
                // did apply, and skip OnConfigReloadedFromFile.
                TryRunReloadWarnings(reloaded, notices);

                OnConfigReloadedFromFile?.Invoke(Config);

                const string appliedMessage = "CarryOn: Config reloaded and applied";
                serverApi.Logger.Notification(appliedMessage);
                notices.Add(new ConfigReloadNotice(EnumChatType.Notification, appliedMessage));
                FinishReload(origin, applied: true, notices);
            }
            catch (Exception ex)
            {
                var message = "CarryOn: Failed to reload config from file: " + ex.Message;
                serverApi.Logger?.Warning(message);
                notices.Add(new ConfigReloadNotice(EnumChatType.CommandError, message));
                FinishReload(origin, applied: false, notices);
            }
        }

        /// <summary>
        /// Records a guard rejection: the message is logged exactly as before, recorded as a
        /// warning-class notice, and the reload is finished as not applied. Callers return
        /// immediately after, so the running config is untouched.
        /// </summary>
        private void RejectReload(ConfigReloadOrigin origin, string message, List<ConfigReloadNotice> notices)
        {
            serverApi?.Logger.Warning(message);
            notices.Add(new ConfigReloadNotice(EnumChatType.CommandError, message));
            FinishReload(origin, applied: false, notices);
        }

        /// <summary>
        /// Closes a reload attempt: publishes the outcome for the command path
        /// (<see cref="LastReloadResult"/>) and, for watcher-origin reloads, delivers the
        /// notices to player chat. Command reloads never broadcast - their notices travel
        /// back in the command result itself, and broadcasting as well would deliver the
        /// same notice to the caller twice. From the server console a command reload has no
        /// result channel and no chat recipient, so the log remains the record.
        /// </summary>
        private void FinishReload(ConfigReloadOrigin origin, bool applied, List<ConfigReloadNotice> notices)
        {
            LastReloadResult = new ConfigReloadResult(applied, notices);
            if (origin == ConfigReloadOrigin.Watcher)
            {
                DeliverNoticesToChat(notices);
            }
        }

        /// <summary>
        /// Delivers a watcher reload's notices to player chat: to online players holding the
        /// controlserver privilege, falling back to every online player on a non-dedicated
        /// host (a single-player world has no admin flag to check). On a dedicated server
        /// with no admin online nothing is sent - the log remains the record.
        /// <para>
        /// Warning-class notices are deduped per session (see
        /// <see cref="sentChatNotices"/>); informational notices are per-event feedback and
        /// always send. A text is recorded as sent only when it actually reached at least
        /// one player, so a reload that lands while nobody is online does not silence the
        /// next one.
        /// </para>
        /// </summary>
        private void DeliverNoticesToChat(List<ConfigReloadNotice> notices)
        {
            if (notices.Count == 0 || serverApi == null) return;

            var recipients = new List<IServerPlayer>();
            foreach (var online in serverApi.World.AllOnlinePlayers)
            {
                if (online is IServerPlayer player && online.HasPrivilege(Privilege.controlserver))
                {
                    recipients.Add(player);
                }
            }

            if (recipients.Count == 0 && !serverApi.Server.IsDedicated)
            {
                foreach (var online in serverApi.World.AllOnlinePlayers)
                {
                    if (online is IServerPlayer player) recipients.Add(player);
                }
            }

            if (recipients.Count == 0) return;

            foreach (var notice in notices)
            {
                if (notice.Type == EnumChatType.CommandError && !sentChatNotices.Add(notice.Message))
                {
                    continue;
                }

                foreach (var recipient in recipients)
                {
                    recipient.SendMessage(GlobalConstants.GeneralChatGroup, notice.Message, notice.Type);
                }
            }
        }

        /// <summary>
        /// Reports the top-level sections the incoming file leaves out, which are about to
        /// reset toward their defaults and are then propagated to the world config tree and
        /// every connected client.
        /// <para>
        /// The skeleton guard cannot catch this case, and that is by design: it only rejects
        /// a file with <i>no</i> settable content, because rejecting more would reject
        /// legitimate minimal configs. So a file like
        /// <c>{"DebuggingOptions":{"LoggingEnabled":false}}</c> passes the guard, is applied,
        /// and silently resets every other section - the operator sees "Config reloaded and
        /// applied" and no indication that the rest of their config just went to defaults. The
        /// name of the losing sections is the signal that was missing.
        /// </para>
        /// <para>
        /// Compares against <i>applied</i> config, not against the previous file: the question
        /// is what this apply changes, so a section the operator never had configured is not
        /// reported. Only sections that are currently populated and absent from the incoming
        /// file are named, which keeps the warning quiet for the common case of a full config
        /// save. <c>ConfigVersion</c> is exempt - it is not a section and its absence means
        /// nothing.
        /// </para>
        /// <para>
        /// Fires from <see cref="ReloadFromFile"/>, so it covers both entry points - the file
        /// watcher and <c>/carryon-reload</c>.
        /// </para>
        /// </summary>
        private void WarnOnDroppedSections(JObject fileJson, List<ConfigReloadNotice> notices)
        {
            var dropped = DroppedTopLevelSections(fileJson);
            if (dropped.Count == 0) return;

            var message =
                "CarryOn: Reloaded config is missing the "
                + (dropped.Count == 1 ? "section " : "sections ")
                + string.Join(", ", dropped)
                + " that the current config has values in. "
                + (dropped.Count == 1 ? "It will" : "They will")
                + " reset to defaults on the world config tree and every connected client, "
                + "because a reload replaces the whole config rather than merging into it. "
                + "If you meant to change only some sections, write the others out too - "
                + "CarryOn does not remember the previous values for this.";
            serverApi?.Logger.Warning(message);
            notices.Add(new ConfigReloadNotice(EnumChatType.CommandError, message));
        }

        /// <summary>
        /// The applied config's top-level sections that hold operator-set values the incoming
        /// file omits, i.e. the ones about to be thrown away by this apply.
        /// <para>
        /// Two filters, both there to keep the warning honest rather than loud. A section the
        /// incoming file does mention is never reported. A section whose applied value is
        /// identical to a fresh <see cref="CarryOnConfig"/>'s is not reported either: dropping
        /// it resets nothing, and naming every section of a legitimately minimal config would
        /// train the operator to ignore the one warning that matters. So the report is
        /// "settings you changed are about to be lost", not "sections the file lacks".
        /// </para>
        /// <para>
        /// Both sides are serialized rather than hand-listed, so a section added to
        /// <see cref="CarryOnConfig"/> later is covered without touching this method, and the
        /// comparison is exactly the one the config tree will perform on apply.
        /// <c>ConfigVersion</c> is exempt - it is a bookkeeping scalar, not a section, and its
        /// absence from a file means nothing.
        /// </para>
        /// </summary>
        private List<string> DroppedTopLevelSections(JObject fileJson)
        {
            var dropped = new List<string>();

            JObject applied;
            JObject defaults;
            try
            {
                applied = JObject.FromObject(Config);
                defaults = JObject.FromObject(new CarryOnConfig());
            }
            catch (Exception ex)
            {
                // The differential is a diagnostic, so never let it fail a reload.
                serverApi?.Logger.Debug("CarryOn: Could not compare config sections (" + ex.Message + ")");
                return dropped;
            }

            foreach (var property in defaults.Properties())
            {
                if (property.Name == nameof(CarryOnConfig.ConfigVersion)) continue;
                if (fileJson[property.Name] != null) continue;

                var appliedSection = applied[property.Name];
                if (appliedSection == null) continue;
                if (JToken.DeepEquals(appliedSection, property.Value)) continue;

                dropped.Add(property.Name);
            }

            return dropped;
        }

        /// <summary>
        /// Shared emptiness rule with <see cref="IsConfigFileSkeleton"/>: a section with no
        /// object entries, no array entries and no non-null scalar has nothing to lose.
        /// </summary>
        private static bool IsSectionEmpty(JToken token)
        {
            if (token is JObject obj) return obj.Count == 0;
            if (token is JArray arr) return arr.Count == 0;
            if (token is JValue val) return val.Value == null;
            return false;
        }

        /// <summary>
        /// Reports that the reloaded file's <c>ReconditionOnConfigChange</c> disagrees with
        /// this session's boot-time mode.
        /// <para>
        /// The gate above silently ignores the file's value, which is correct but leaves the
        /// operator with no signal: they set the flag, nothing happened, and the config file
        /// now disagrees with the running server. This names both values and says which one
        /// is in force. It fires from <see cref="ReloadFromFile"/>, so it covers
        /// <b>both</b> entry points - the file watcher and <c>/carryon-reload</c> - rather
        /// than only whichever one the operator happened to use.
        /// </para>
        /// <para>
        /// Warning only, deliberately: there is no force verb and nothing is re-armed. The
        /// flag is a restart-scoped setting, so the honest action is "restart to pick this
        /// up", and a reload-time override would reintroduce the mid-session mode switch
        /// this latch exists to prevent.
        /// </para>
        /// </summary>
        private void WarnOnModeMismatch(CarryOnConfig reloaded, List<ConfigReloadNotice> notices)
        {
            if (reloaded.DebuggingOptions.ReconditionOnConfigChange == ReconditionOnConfigChangeEnabled)
            {
                return;
            }

            var fileValue = reloaded.DebuggingOptions.ReconditionOnConfigChange;
            var sessionValue = ReconditionOnConfigChangeEnabled;
            var message =
                "CarryOn: DebuggingOptions.ReconditionOnConfigChange is "
                + (fileValue ? "true" : "false")
                + " in the config file but was "
                + (sessionValue ? "true" : "false")
                + " at server start. This flag is read once per server session, so the file "
                + "value takes effect on the next server start, not on this one - live "
                + "reconditioning is "
                + (sessionValue ? "enabled" : "NOT enabled")
                + " for this session. CarryOn did not modify your config file - the value "
                + "you set is preserved as-is and takes effect on the next server start.";
            serverApi?.Logger.Warning(message);
            notices.Add(new ConfigReloadNotice(EnumChatType.CommandError, message));
        }

        /// <summary>
        /// Runs the reload's diagnostic warnings (<see cref="WarnOnBehaviorSurfacePendingRestart"/>,
        /// <see cref="WarnOnModeMismatch"/>) after a reload has been applied, swallowing any
        /// throw. The config is already swapped at this point, so a warning failure must
        /// degrade to silence - same rule as the differential guard in
        /// <see cref="DroppedTopLevelSections"/> - rather than fall into the apply catch and
        /// report a reload that did apply as rejected, or skip
        /// <see cref="OnConfigReloadedFromFile"/>. Centralising the isolation here keeps the
        /// apply path's outcome independent of the diagnostic path.
        /// </summary>
        private void TryRunReloadWarnings(CarryOnConfig reloaded, List<ConfigReloadNotice> notices)
        {
            try
            {
                WarnOnBehaviorSurfacePendingRestart(reloaded, notices);
                WarnOnModeMismatch(reloaded, notices);
            }
            catch (Exception ex)
            {
                serverApi?.Logger?.Debug("CarryOn: Config reload warnings skipped (" + ex.Message + ")");
            }
        }

        /// <summary>
        /// Mode A counterpart of the Mode B recondition pass: reports that this reload
        /// changed the behavior surface while <c>ReconditionOnConfigChange</c> is off, so
        /// the change takes effect on the next server start rather than now. Without it
        /// the operator reads "Config reloaded and applied" and reasonably assumes the
        /// edit took effect.
        /// <para>
        /// The comparator is <see cref="AppliedBehaviorSignature"/> - the surface the
        /// running world reflects, not the previous file's surface. A per-reload
        /// comparator would false-warn on a revert: boot S0, edit to S1, revert to S0 -
        /// the file now matches the running world, nothing is pending. Under Mode A the
        /// running surface never changes (reconditioning never runs), so the boot-seeded,
        /// recondition-refreshed value is the correct comparator in both the change and
        /// the revert direction.
        /// </para>
        /// <para>
        /// Attribution mirrors <see cref="CarryOnConfig.ComputeBehaviorSurfaceSignature"/>
        /// exactly: the four behavior sections plus the two <c>CarryOptions</c> keys the
        /// signature includes, so the warning names precisely what a recondition would
        /// have re-applied. The two <c>CarryOptions</c> keys are hybrid - some of their
        /// effects are read live and still apply now - which the message says rather than
        /// silently omitting them.
        /// </para>
        /// </summary>
        private void WarnOnBehaviorSurfacePendingRestart(CarryOnConfig reloaded, List<ConfigReloadNotice> notices)
        {
            if (ReconditionOnConfigChangeEnabled || AppliedBehaviorSignature == null) return;

            var reloadedSurface = JObject.Parse(reloaded.ComputeBehaviorSurfaceSignature());
            var appliedSurface = JObject.Parse(AppliedBehaviorSignature);

            var changed = new List<string>();
            var changedCarryOptions = new List<string>();
            foreach (var property in reloadedSurface.Properties())
            {
                var appliedValue = appliedSurface[property.Name];
                if (appliedValue != null && JToken.DeepEquals(appliedValue, property.Value)) continue;

                if (IsCarryOptionsSurfaceKey(property.Name))
                {
                    changedCarryOptions.Add(property.Name);
                    changed.Add("CarryOptions." + property.Name);
                }
                else
                {
                    changed.Add(property.Name);
                }
            }

            if (changed.Count == 0) return;

            var message = "CarryOn: This reload changes " + string.Join(", ", changed)
                + ", which take effect on the next server start, not on this reload - "
                + "DebuggingOptions.ReconditionOnConfigChange is off for this session "
                + "(the flag is read once per server session).";
            if (changedCarryOptions.Count > 0)
            {
                message += " The changed CarryOptions key"
                    + (changedCarryOptions.Count == 1 ? " also has" : "s also have")
                    + " immediately-live effects (e.g. ground-storage pickup interaction) "
                    + "that apply now; only "
                    + (changedCarryOptions.Count == 1 ? "its" : "their")
                    + " carry-behavior effects wait for the restart.";
            }

            serverApi?.Logger.Warning(message);
            notices.Add(new ConfigReloadNotice(EnumChatType.CommandError, message));
        }

        /// <summary>
        /// The <see cref="CarryOnConfig.CarryOptions"/> keys included in the behavior
        /// surface signature, i.e. the signature contributors that live outside the four
        /// behavior sections.
        /// </summary>
        private static bool IsCarryOptionsSurfaceKey(string name) =>
            name == nameof(CarryOnConfig.CarryOptions.PickupGroundStorageBagWhileCarrying)
            || name == nameof(CarryOnConfig.CarryOptions.AllowHighCapacityStorageOnBack);

        /// <summary>
        /// Rejects a degraded/partial write: a file with no actual content - nothing but
        /// <see cref="CarryOnConfig.ConfigVersion"/> (or empty section shells) - is a file
        /// caught mid-save. Applying it would silently reset the live config toward
        /// defaults and, combined with a persist writeback, could clobber a user's edit
        /// - the data-loss case this rejection exists for. Any file that carries at least one
        /// non-empty top-level section is treated as a legitimate edit.
        /// </summary>
        private bool IsConfigFileSkeleton(JObject fileJson)
        {
            foreach (var property in fileJson.Properties())
            {
                if (property.Name == nameof(CarryOnConfig.ConfigVersion)) continue;
                if (!IsSectionEmpty(property.Value)) return false;
            }

            return true;
        }
    }
}
