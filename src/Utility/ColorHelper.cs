using System;
using Vintagestory.API.MathTools;

namespace CarryOn.Utility
{
    public static class ColorHelper
    {
        private static string? NormalizeHexInput(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return null;
            var h = hex.Trim().TrimStart('#');
            if (h.Length == 3)
                h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            return h.Length == 6 ? h : null;
        }

        public static bool TryParseHex(string hex, float alpha, out Vec4f result)
        {
            result = new Vec4f(1f, 0f, 1f, alpha);
            var h = NormalizeHexInput(hex);
            if (h == null) return false;

            int r = int.Parse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            int g = int.Parse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            int b = int.Parse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            result = new Vec4f(r / 255f, g / 255f, b / 255f, alpha);
            return true;
        }

        public static bool TryNormalizeHex(string hex, out string? normalized)
        {
            normalized = null;
            var h = NormalizeHexInput(hex);
            if (h == null) return false;

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