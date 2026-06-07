using Vintagestory.API.Client;
using CarryOn.Common.Logic;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Utility
{
    internal static class CarryErrorHelper
    {
        internal static void ShowError(ICoreClientAPI api, string errorCode)
        {
            api.TriggerIngameError(ModId, errorCode, LocalizationHelper.GetLang(errorCode));
        }

        internal static void ShowError(ICoreClientAPI api, string errorCode, string langKey)
        {
            api.TriggerIngameError(ModId, errorCode, LocalizationHelper.GetLang(langKey));
        }

        internal static void ShowErrorWithFallback(ICoreClientAPI api, string failureCode, string defaultCode)
        {
            if (failureCode != FailureCode.Ignore)
            {
                api.TriggerIngameError(ModId, failureCode, LocalizationHelper.GetLang(defaultCode + "-" + failureCode));
            }
            else
            {
                api.TriggerIngameError(ModId, defaultCode, LocalizationHelper.GetLang(defaultCode));
            }
        }

        internal static void ShowErrorIfMessage(ICoreClientAPI api, string? failureCode, string? onScreenErrorMessage)
        {
            if (onScreenErrorMessage != null)
            {
                api.TriggerIngameError(ModId, failureCode, onScreenErrorMessage);
            }
        }
    }
}