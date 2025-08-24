using System.Collections.Generic;
using System.Linq;
using CarryOn.Utility;
using Vintagestory.API.Common;
namespace CarryOn.Client
{
    /// <summary> Utility class which is meant to syncronize animations for
    ///           the local player to actually reflect what is being carried. </summary>
    public class AnimationFixer
    {
        private HashSet<string> previous = new();

        public void Update(EntityPlayer player)
        {
            var current = new HashSet<string>(player.GetCarried()
                .Select(carried => carried.GetCarryableBehavior()?.Slots[carried.Slot]?.Animation)
                .Where(animation => animation != null));

            var added = current.Except(this.previous);
            var removed = this.previous.Except(current);

            foreach (var anim in added) player.StartAnimation(anim);
            foreach (var anim in removed) player.StopAnimation(anim);

            this.previous = current;
        }
    }
}
