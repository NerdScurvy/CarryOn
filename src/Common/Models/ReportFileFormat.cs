namespace CarryOn.Common.Models
{
    /// <summary>Specifies the output format for debug reports.</summary>
    public enum ReportFileFormat
    {
        /// <summary>Do not write a report file.</summary>
        None = 0,
        /// <summary>Write the report as a Markdown file.</summary>
        Markdown,
        /// <summary>Write the report as an HTML file.</summary>
        Html
    }
}
