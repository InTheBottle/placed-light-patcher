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
        private static readonly ModKey TrueLight = ModKey.FromFileName("True Light.esm");
        private static readonly ModKey[] TrueLightAddons = [
            ModKey.FromNameAndExtension("True Light - Creation Club.esp"),
            ModKey.FromNameAndExtension("PL - Default.esp"),
            ModKey.FromNameAndExtension("PL - Dark.esp")
        ];

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "TrueLightPatcher.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            // Check if True Light.esm is present
            if (!state.LoadOrder.TryGetValue(TrueLight, out var trueLightListing) || trueLightListing.Mod == null)
            {
                Console.Error.WriteLine("'True Light.esm' cannot be found. Make sure you have installed True Light.");
                return;
            }

            // Check for conflicting lighting template plugins
            if (state.LoadOrder.ContainsKey(ModKey.FromNameAndExtension("TL - Default.esp")) &&
                state.LoadOrder.ContainsKey(ModKey.FromNameAndExtension("TL - Dark.esp")))
            {
                Console.Error.WriteLine("You are using both 'TL - Default.esp' and 'TL - Dark.esp', please choose only one Lighting Template plugin.");
                return;
            }

            // Setup list of True Light plugins
            var trueLightPlugins = new List<ISkyrimModGetter> { trueLightListing.Mod };

            // Add addons to the list of True Light plugins if found
            foreach (var modKey in TrueLightAddons)
            {
                if (state.LoadOrder.TryGetValue(modKey, out var addon) && addon.Mod != null)
                {
                    trueLightPlugins.Add(addon.Mod);
                }
            }

            var loadOrderLinkCache = state.LoadOrder.ToImmutableLinkCache();
            var trueLightLinkCache = trueLightPlugins.ToImmutableLinkCache();

            // Find all interior cells where True Light.esm is not already the winner
            var cellContexts = state.LoadOrder.PriorityOrder.Cell()
                .WinningContextOverrides(loadOrderLinkCache)
                .Where(i => i.ModKey != TrueLight)
                .Where(i => i.Record.Flags.HasFlag(Cell.Flag.IsInteriorCell))
                .Where(i => !i.Record.MajorFlags.HasFlag(Cell.MajorFlag.Persistent));

            var cellMask = new Cell.TranslationMask(false)
            {
                Lighting = true
            };

            uint patchedCellCount = 0;
            foreach (var winningCellContext in cellContexts)
            {
                if (!trueLightLinkCache.TryResolve<ICellGetter>(winningCellContext.Record.FormKey, out var trueLightCellRecord))
                    continue;

                if (trueLightCellRecord.Lighting == null)
                    continue;

                // If the winning cell record already has the same lighting values as True Light, skip it
                if (winningCellContext.Record.Equals(trueLightCellRecord, cellMask))
                    continue;

                winningCellContext.GetOrAddAsOverride(state.PatchMod).Lighting = trueLightCellRecord.Lighting.DeepCopy();
                patchedCellCount++;
            }

            uint patchedLightCount = 0;
            foreach (var winningLightRecord in state.LoadOrder.PriorityOrder.Light().WinningOverrides())
            {
                if (!trueLightLinkCache.TryResolve<ILightGetter>(winningLightRecord.FormKey, out var trueLightRecord))
                    continue;

                if (!loadOrderLinkCache.TryResolve<ILightGetter>(winningLightRecord.FormKey, out var originLightRecord, ResolveTarget.Origin))
                    continue;

                // Forward Light records if the winning record is using vanilla values
                if (winningLightRecord.Equals(originLightRecord) && !winningLightRecord.Equals(trueLightRecord))
                {
                    state.PatchMod.Lights.DuplicateInAsNewRecord(trueLightRecord);
                    patchedLightCount++;
                }
            }

            Console.WriteLine($"Patched {patchedCellCount} cells");
            Console.WriteLine($"Patched {patchedLightCount} lights");
        }
    }
}
