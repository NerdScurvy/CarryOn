namespace CarryOn.Client.Models
{
    public enum IconTextureMode
    {
        /// <summary>Standalone texture using best available GL method (default).</summary>
        Standalone = 0,

        /// <summary>Use direct atlas UV coordinates. Safe on all platforms.</summary>
        Atlas,

        /// <summary>Force standalone texture via GL.GetTexImage fallback for testing.</summary>
        StandaloneFallback,

        /// <summary>Disable icon labels entirely.</summary>
        Disabled
    }
}
