using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;

namespace TimedItemDestruction;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
public class TimedItemDestruction : BaseUnityPlugin
{
	private const string ModName = "Timed Item Destruction";
	private const string ModVersion = "1.0.3";
	private const string ModGUID = "org.bepinex.plugins.timeditemdestruction";

	private static readonly ConfigSync configSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<Toggle> destructBaseItems = null!;
	private static ConfigEntry<Toggle> destructTarItems = null!;
	private static ConfigEntry<int> destructItemTimerWild = null!;
	private static ConfigEntry<int> destructItemTimerBase = null!;

	private enum Toggle
	{
		On = 1,
		Off = 0,
	}

	public void Awake()
	{
		serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.Off, "If on, the configuration is locked and can be changed by server admins only.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		destructItemTimerWild = config("2 - Item Destruction", "Time in seconds until items are deleted in the wild", (int)ItemDrop.c_AutoDestroyTimeout, "Specifies the amount of seconds that have to pass, before items on the floor in the wild are removed.");
		destructTarItems = config("2 - Item Destruction", "Delete items in tar", Toggle.Off, "Deletes items stuck in tar for the specified time as well.");
		destructBaseItems = config("2 - Item Destruction", "Delete items in bases", Toggle.Off, "Deletes items on the floor for the specified time in player bases as well.");
		destructItemTimerBase = config("2 - Item Destruction", "Time in seconds until items are deleted in bases", (int)ItemDrop.c_AutoDestroyTimeout, "Specifies the amount of seconds that have to pass, before items on the floor in player bases are removed.");

		Assembly assembly = Assembly.GetExecutingAssembly();
		Harmony harmony = new(ModGUID);
		harmony.PatchAll(assembly);
	}

	[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.TimedDestruction))]
	private class Patch_ItemDrop_TimedDestruction
	{
		private static double baseDestructionTime() => destructItemTimerWild.Value;
		private static bool checkInBase(bool inBase, ItemDrop item) => inBase && (destructBaseItems.Value == Toggle.Off || item.GetTimeSinceSpawned() < destructItemTimerBase.Value);
		private static bool checkInTar(bool inTar) => inTar && destructTarItems.Value == Toggle.Off;

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo inBase = AccessTools.DeclaredMethod(typeof(ItemDrop), nameof(ItemDrop.IsInsideBase));
			MethodInfo inTar = AccessTools.DeclaredMethod(typeof(ItemDrop), nameof(ItemDrop.InTar));

			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Ldc_R8 && instruction.OperandIs(ItemDrop.c_AutoDestroyTimeout))
				{
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(Patch_ItemDrop_TimedDestruction), nameof(baseDestructionTime)));
				}
				else
				{
					yield return instruction;
				}

				if (instruction.opcode == OpCodes.Call && instruction.OperandIs(inBase))
				{
					yield return new CodeInstruction(OpCodes.Ldarg_0);
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(Patch_ItemDrop_TimedDestruction), nameof(checkInBase)));
				}

				if (instruction.opcode == OpCodes.Call && instruction.OperandIs(inTar))
				{
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(Patch_ItemDrop_TimedDestruction), nameof(checkInTar)));
				}
			}
		}
	}
}
