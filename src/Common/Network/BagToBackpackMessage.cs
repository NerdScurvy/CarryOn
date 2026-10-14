using ProtoBuf;

namespace CarryOn.Common.Network
{
    /// <summary>
    /// Sent from client to server when a player stows a carried bag (picked up from ground
    /// storage) into the first empty slot of their vanilla backpack inventory using the
    /// pick-up + swap-back keys. The bag is currently carried in the hands slot; it never
    /// touches <see cref="CarryOn.API.Common.Models.CarrySlot.Back"/>.
    /// </summary>
    [ProtoContract(ImplicitFields = ImplicitFields.AllFields)]
    public record BagToBackpackMessage;
}