using Vintagestory.API.MathTools;

namespace CarryOn.Utility
{
    public static class ColorHelper
    {
        // Try parse hex "#RRGGBB" or "RRGGBB" or "#RGB" => Vec4f (0..1), returns true on success
        public static bool TryParseHex(string hex, float alpha, out Vec4f result)
        {
            // default fallback color - use an obvious error color (magenta) to highlight invalid configs
            result = new Vec4f(1f, 0f, 1f, alpha); // #FF00FF
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var h = hex.Trim().TrimStart('#');
            if (h.Length == 3)
            {
                // expand shorthand
                h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            }
            if (h.Length != 6) return false;
            try
            {
                int r = int.Parse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                int g = int.Parse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                int b = int.Parse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                result = new Vec4f(r / 255f, g / 255f, b / 255f, alpha);
                return true;
            }
            catch { return false; }
        }

        // Normalize hex string into the canonical upper-case #RRGGBB form.
        // Accepts formats: "#RRGGBB", "RRGGBB", "#RGB" and "RGB". Returns false if invalid.
        public static bool TryNormalizeHex(string hex, out string? normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            var h = hex.Trim().TrimStart('#');
            if (h.Length == 3)
            {
                h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            }
            if (h.Length != 6) return false;
            // validate hex digits
            for (int i = 0; i < 6; i++)
            {
                char c = h[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            normalized = "#" + h.ToUpperInvariant();
            return true;
        }
    }
}