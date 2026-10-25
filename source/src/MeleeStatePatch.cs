using Gear;
using HarmonyLib;

namespace MastabaInspect;

[HarmonyPatch(typeof(MeleeWeaponFirstPerson), nameof(MeleeWeaponFirstPerson.ChangeState))]
internal static class MeleeStatePatch
{
    [HarmonyPrefix]
    private static void Prefix([HarmonyArgument(0)] eMeleeWeaponState state)
    {
        if (state == eMeleeWeaponState.Idle)
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
