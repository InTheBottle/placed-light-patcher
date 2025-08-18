using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda;
using Noggog;
using Mutagen.Bethesda.Plugins;

namespace TrueLightPatcher
{
    public class Program
    {
        static ModKey TrueLight { get; } = ModKey.FromFileName("True Light.esm");
        static ModKey[] TrueLightAddons { get; } = [
            ModKey.FromNameAndExtension("True Light - Creation Club.esp"),
            ModKey.FromNameAndExtension("True Light - USSEP Patch.esp"),
            ModKey.FromNameAndExtension("TL Bulbs ISL.esp"),
            ModKey.FromNameAndExtension("TL - WSU Patch.esp"),
            ModKey.FromNameAndExtension("TL - Default.esp"),
            ModKey.FromNameAndExtension("TL - Bright.esp"),
            ModKey.FromNameAndExtension("TL - Even Brighter.esp"),
            ModKey.FromNameAndExtension("TL - Fixed Vanilla.esp"),
            ModKey.FromNameAndExtension("TL - Nightmare.esp")
        ];

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "TrueLightPatcher.esp")
                .Run(args);
        }

        private static ICellGetter? GetDefaultLightingCell(ILinkCache TrueLightLinkCache)
        {
            var defaultCell = TrueLightLinkCache.PriorityOrder.Cell()
                .FirstOrDefault(c => c.Flags.HasFlag(Cell.Flag.IsInteriorCell) && c.Lighting != null);
            if (defaultCell != null)
            {
                Console.WriteLine($"Default lighting cell: {defaultCell.EditorID} (FormKey: {defaultCell.FormKey})");
                Console.WriteLine($" - Ambient: {defaultCell.Lighting.Ambient}");
                Console.WriteLine($" - Fog Near: {defaultCell.Lighting.FogNear}, Far: {defaultCell.Lighting.FogFar}");
            }
            else
            {
                Console.WriteLine("Warning: No valid default lighting cell found in TrueLight plugins.");
            }
            return defaultCell;
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (state.LoadOrder.TryGetValue(TrueLight) is not { Mod: not null } TrueLightMod)
            {
                Console.Error.WriteLine("'True Light.esm' cannot be found. Make sure you have installed True Light.");
                return;
            }

            var conflictingTemplates = new[] { "TL - Default.esp", "TL - Bright.esp", "TL - Even Brighter.esp", "TL - Fixed Vanilla.esp", "TL - Nightmare.esp" };
            var activeTemplates = conflictingTemplates.Where(t => state.LoadOrder.ContainsKey(ModKey.FromNameAndExtension(t))).ToList();
            if (activeTemplates.Count > 1)
            {
                Console.Error.WriteLine($"You are using multiple lighting template plugins: {string.Join(", ", activeTemplates)}. Please choose only one.");
                return;
            }

            var TrueLightPlugins = new List<ISkyrimModGetter> { TrueLightMod.Mod };
            foreach (var modKey in TrueLightAddons)
            {
                if (state.LoadOrder.TryGetValue(modKey) is { Mod: not null } addon)
                {
                    TrueLightPlugins.Add(addon.Mod);
                }
            }

            var loadOrderLinkCache = state.LoadOrder.ToImmutableLinkCache();
            var TrueLightLinkCache = TrueLightPlugins.ToImmutableLinkCache();

            var cellContexts = state.LoadOrder.PriorityOrder.Cell()
                .WinningContextOverrides(loadOrderLinkCache)
                .Where(i => i.Record.Flags.HasFlag(Cell.Flag.IsInteriorCell));

            var cellMask = new Cell.TranslationMask(false)
            {
                Lighting = true
            };

            uint patchedCellCount = 0;
            foreach (var winningCellContext in cellContexts)
            {
                var cell = winningCellContext.Record;
                ICellGetter? TrueLightCellRecord;

                if (!TrueLightLinkCache.TryResolve<ICellGetter>(cell.FormKey, out TrueLightCellRecord) || TrueLightCellRecord.Lighting == null)
                {
                    TrueLightCellRecord = GetDefaultLightingCell(TrueLightLinkCache);
                    if (TrueLightCellRecord == null || TrueLightCellRecord.Lighting == null)
                    {
                        continue; 
                    }
                }

                if (winningCellContext.Record.Equals(TrueLightCellRecord, cellMask))
                {
                    continue;
                }

                winningCellContext.GetOrAddAsOverride(state.PatchMod).Lighting = TrueLightCellRecord.Lighting.DeepCopy();
                patchedCellCount++;
            }

            uint patchedLightCount = 0;
            foreach (var winningLightRecord in state.LoadOrder.PriorityOrder.Light().WinningOverrides())
            {
                if (!TrueLightLinkCache.TryResolve<ILightGetter>(winningLightRecord.FormKey, out var TrueLightRecord))
                {
                    continue;
                }

                if (!loadOrderLinkCache.TryResolve<ILightGetter>(winningLightRecord.FormKey, out var originLightRecord, ResolveTarget.Origin))
                {
                    continue;
                }

                if (winningLightRecord.Equals(originLightRecord) && !winningLightRecord.Equals(TrueLightRecord))
                {
                    state.PatchMod.Lights.DuplicateInAsNewRecord(TrueLightRecord);
                    patchedLightCount++;
                }
            }

            Console.WriteLine($"Patched {patchedCellCount} cells");
            Console.WriteLine($"Patched {patchedLightCount} lights");
        }
    }
}
