using HarmonyLib;

namespace MastabaInspect;

[HarmonyPatch(typeof(FirstPersonItemHolder), nameof(FirstPersonItemHolder.LateUpdate))]
internal static class HolderLateUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        InspectDriver driver = InspectDriver.GetLocal();
        if (driver)
        {
            driver.AfterHolderLateUpdate();
        }
    }
}
