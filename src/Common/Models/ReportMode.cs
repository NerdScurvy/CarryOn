namespace CarryOn.Common.Models
{
    /// <summary>Controls the verbosity level of debug reports.</summary>
    public enum ReportMode
    {
        /// <summary>Include all details in the report.</summary>
        Full = 0,
        /// <summary>Condense entries by side (client/server).</summary>
        CondensedSide,
        /// <summary>Condense entries by type.</summary>
        CondensedType,
        /// <summary>Condense all entries into a single summary.</summary>
        CondensedAll
    }
}
