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
            if (properties?.Exists == true)
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

                    settings.Animation = slotProperties[CarryCodes.BehaviorJsonKeys.Animation].AsString(settings.Animation);
                    settings.AnimationSit = slotProperties[CarryCodes.BehaviorJsonKeys.AnimationSit].AsString(settings.AnimationSit);
                    settings.AnimationCrouch = slotProperties[CarryCodes.BehaviorJsonKeys.AnimationCrouch].AsString(settings.AnimationCrouch);

                    CarryCodes.Defaults.WalkSpeedModifier.TryGetValue(slot, out var speed);
                    settings.WalkSpeedModifier = slotProperties[CarryCodes.BehaviorJsonKeys.WalkSpeedModifier].AsFloat(speed);

                    settings.WalkSpeedModifierByType = JsonHelper.ParseFloatMap(slotProperties[CarryCodes.BehaviorJsonKeys.WalkSpeedModifierByBlockType]);
                    settings.WalkSpeedModifierByGroup = JsonHelper.ParseFloatMap(slotProperties[CarryCodes.BehaviorJsonKeys.WalkSpeedModifierByGroup]);

                    settings.HungerModifierByType = JsonHelper.ParseFloatMap(slotProperties[CarryCodes.BehaviorJsonKeys.HungerModifierByBlockType]);
                    settings.HungerModifierByGroup = JsonHelper.ParseFloatMap(slotProperties[CarryCodes.BehaviorJsonKeys.HungerModifierByGroup]);

                    if (JsonHelper.TryGetFloat(slotProperties, CarryCodes.BehaviorJsonKeys.HungerModifier, out var hungerModifier))
                        settings.HungerModifier = hungerModifier;

                    if (JsonHelper.TryGetString(slotProperties, CarryCodes.BehaviorJsonKeys.EnabledCondition, out var e)) settings.EnabledCondition = e;
                    if (JsonHelper.TryGetStringArray(slotProperties, CarryCodes.BehaviorJsonKeys.ExcludedTypes, out var x)) settings.ExcludedTypes = x;
                }
            }
        }
    }
}
