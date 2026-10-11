using System.ComponentModel;
using CarryOn.API.Common.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace CarryOn.Common.Models
{
    /// <summary>Configuration for generating carryable block reports.</summary>
    public class CarryableReportConfig
    {
        [DisplayName("Enabled")]
        [Description("Enable generating a carryable block report on world load")]
        [DefaultValue(false)]
        [TreeValue("Enabled")] public bool Enabled { get; set; } = false;

        [DisplayName("Output to Log")]
        [Description("Write the report to the server log")]
        [DefaultValue(true)]
        [TreeValue("OutputToLog")] public bool OutputToLog { get; set; } = true;

        [DisplayName("File Format")]
        [Description("Output format for the report: None (no file), Markdown, or Html")]
        [DefaultValue(ReportFileFormat.Markdown)]
        [TreeValue("FileFormat")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ReportFileFormat FileFormat { get; set; } = ReportFileFormat.Markdown;

        [DisplayName("Block Filters")]
        [Description("Optional wildcard patterns to filter which blocks appear (e.g. \"mymod:*\", \"*chest*\"). Empty = show all.")]
        [DefaultValue(new string[0])]
        [TreeValue("BlockFilters")] public string[] BlockFilters { get; set; } = [];

        [DisplayName("Report Mode")]
        [Description("Controls report condensation: Full (every row), CondensedSide (collapse n/e/s/w), CondensedType (collapse sides + group identical types), CondensedAll (all condensations including wildcards)")]
        [DefaultValue(ReportMode.Full)]
        [TreeValue("ReportMode")]
        [JsonConverter(typeof(StringEnumConverter))]
        public ReportMode ReportMode { get; set; } = ReportMode.Full;

    }

    /// <summary>Debug and developer options.</summary>
    public class DebuggingOptionsConfig
    {
        [DisplayName("Logging Enabled")]
        [Description("Enable debug logging")]
        [DefaultValue(false)]
        [TreeValue("LoggingEnabled")] public bool LoggingEnabled { get; set; } = false;

        [DisplayName("Disable Harmony Patch")]
        [Description("Disable Harmony runtime patching - changes require a server restart")]
        [DefaultValue(false)]
        [TreeValue("DisableHarmonyPatch")] public bool DisableHarmonyPatch { get; set; } = false;

        [DisplayName("Enable Pack Adjustment Tool")]
        [Description("Enable the pack adjustment debug tool (requires restart)")]
        [DefaultValue(false)]
        [TreeValue("EnablePackAdjustmentTool")] public bool EnablePackAdjustmentTool { get; set; } = false;

        [DisplayName("Disable Config Watcher")]
        [Description("Disable the file system watcher that hot-reloads the config on change (requires restart)")]
        [DefaultValue(false)]
        [TreeValue("DisableConfigWatcher")] public bool DisableConfigWatcher { get; set; } = false;

        [DisplayName("Recondition Carryables on Config Change")]
        [Description("Re-apply behavioral conditioning and resync carryable state to clients when the config changes (requires restart); when off, Carryables/CarryablesOnBack/Interactables/... changes only take effect on the next server start. * when enabled at server start, the sections marked (requires restart*) apply on /carryon-reload or when the config watcher sees a change, instead of requiring a restart")]
        [DefaultValue(false)]
        [TreeValue("ReconditionOnConfigChange")] public bool ReconditionOnConfigChange { get; set; } = false;

        [DisplayName("Recondition Full Restore")]
        [Description("Treat every conditioning-baseline entry as dirty on each behavioral recondition, restoring the full boot surface instead of only what a previous run touched - a live-debugging fallback and convergence cross-check; leaves the normal dirty-gated restore when off")]
        [DefaultValue(false)]
        [TreeValue("ReconditionFullRestore")] public bool ReconditionFullRestore { get; set; } = false;

        [DisplayName("Carryable Report")]
        [Description("Generate a report of all carryable blocks with their speed and hunger modifiers")]
        public CarryableReportConfig CarryableReport { get; set; } = new CarryableReportConfig();
    }
}
