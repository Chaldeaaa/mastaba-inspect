using HarmonyLib;

namespace MastabaInspect;

[HarmonyPatch(typeof(InputMapper), nameof(InputMapper.OnFocusStateChanged))]
internal static class FocusPatch
{
    [HarmonyPrefix]
    private static void Prefix([HarmonyArgument(0)] eFocusState focus)
    {
        if (focus == eFocusState.FPS)
        {
            return;
        }

        InspectDriver driver = InspectDriver.GetLocal();
        if (driver)
        {
            driver.Stop();
        }
    }
}
