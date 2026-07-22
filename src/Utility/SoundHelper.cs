using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Utility
{
    internal static class SoundHelper
    {
        internal static void PlaySound(ICoreAPI api, Block block, BlockPos pos, EntityPlayer? entityPlayer = null, bool dualCall = true)
        {
            const float SOUND_RANGE = 16.0f;
            const float SOUND_VOLUME = 1.0f;

            var sound = block.Sounds?.Place.Location ?? new AssetLocation(CarryCodes.SoundPaths.DefaultPlace);
            if (sound == null) return;

            var world = api.World;
            var player = dualCall && (entityPlayer != null) && (world.Side == EnumAppSide.Server)
                ? entityPlayer?.Player : null;

            world.PlaySoundAt(sound,
                pos.X + 0.5, pos.Y + 0.25, pos.Z + 0.5, player,
                range: SOUND_RANGE, volume: SOUND_VOLUME);
        }
    }
}
