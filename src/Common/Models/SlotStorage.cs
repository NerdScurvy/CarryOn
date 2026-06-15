using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.API.Common.Models;
using CarryOn.Common.Behaviors;
using CarryOn.Utility;
using Vintagestory.API.Datastructures;

namespace CarryOn.Common.Models
{
    public class SlotStorage
    {
        public readonly Dictionary<CarrySlot, SlotSettings> SlotSettingsDict = [];

        public void RemoveSlot(CarrySlot slot)
        {
            SlotSettingsDict.Remove(slot);
        }

        public SlotSettings? this[CarrySlot slot]
            => SlotSettingsDict.TryGetValue(slot, out var settings) ? settings : null;

        public int Count => SlotSettingsDict.Count;

        public void Initialize(JsonObject properties)
        {
            SlotSettingsDict.Clear();
            if (properties?.Exists != true)
            {
                if (!BlockBehaviorCarryable.DefaultAnimation.TryGetValue(CarrySlot.Hands, out string? anim)) anim = null;
                SlotSettingsDict.Add(CarrySlot.Hands, new SlotSettings { Animation = anim });
            }
            else
            {
                foreach (var slot in Enum.GetValues(typeof(CarrySlot)).Cast<CarrySlot>())
                {
                    var slotProperties = properties[slot.ToString()];
                    if (slotProperties?.Exists != true) continue;

                    if (!SlotSettingsDict.TryGetValue(slot, out var settings))
                    {
                        if (!BlockBehaviorCarryable.DefaultAnimation.TryGetValue(slot, out string? anim)) anim = null;
                        SlotSettingsDict.Add(slot, settings = new SlotSettings { Animation = anim });
                    }

                    settings.Animation = slotProperties["animation"].AsString(settings.Animation);
                    settings.AnimationSit = slotProperties["animationSit"].AsString(settings.AnimationSit);
                    settings.AnimationCrouch = slotProperties["animationCrouch"].AsString(settings.AnimationCrouch);

                    if (!BlockBehaviorCarryable.DefaultWalkSpeed.TryGetValue(slot, out var speed)) speed = 0.0F;
                    settings.WalkSpeedModifier = slotProperties["walkSpeedModifier"].AsFloat(speed);

                    settings.WalkSpeedModifierByType = JsonHelper.ParseFloatMap(slotProperties["walkSpeedModifierByBlockType"]);
                    settings.WalkSpeedModifierByGroup = JsonHelper.ParseFloatMap(slotProperties["walkSpeedModifierByGroup"]);

                    if (JsonHelper.TryGetFloat(slotProperties, "hungerModifier", out var hungerModifier))
                        settings.HungerModifier = hungerModifier;

                    if (JsonHelper.TryGetString(slotProperties, "enabledCondition", out var e)) settings.EnabledCondition = e;
                    if (JsonHelper.TryGetStringArray(slotProperties, "excludedTypes", out var x)) settings.ExcludedTypes = x;
                }
            }
        }
    }
}
