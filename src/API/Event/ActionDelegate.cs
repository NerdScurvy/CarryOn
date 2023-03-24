using Vintagestory.API.Common;

namespace CarryOn.API.Event
{
    public delegate void ActionDelegate(EnumEntityAction action, ref EnumHandling handled);
}