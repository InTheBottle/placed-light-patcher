using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda;
using Noggog;
using Mutagen.Bethesda.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TrueLightPatcher
{
    public class Program
    {
        // Core mod configuration
        private static readonly ModKey TrueLight = ModKey.FromFileName("True Light.esm");
        
        // Addon patches
        private static readonly ModKey[] TrueLightAddons = 
        {
            ModKey.FromNameAndExtension("True Light - Creation Club.esp"),
            ModKey.FromNameAndExtension("True Light - USSEP Patch.esp"),
            ModKey.FromNameAndExtension("TL Bulbs ISL.esp"),
            ModKey.FromNameAndExtension("TL - WSU Patch.esp")
        };

        // Lighting template presets (mutually exclusive)
        private static readonly ModKey[] LightingTemplates = 
        {
            ModKey.FromNameAndExtension("TL - Default.esp"),
            ModKey.FromNameAndExtension("TL - Bright.esp"),
            ModKey.FromNameAndExtension("TL - Even Brighter.esp"),
            ModKey.FromNameAndExtension("TL - Fixed Vanilla.esp"),
            ModKey.FromNameAndExtension("TL - Nightmare.esp")
        };

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "TrueLightPatcher.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            try
            {
                Console.WriteLine("=== True Light Patcher Started ===");

                // Validate True Light installation
                if (!ValidateTrueLightInstallation(state, out var trueLightMod) || trueLightMod.Mod == null)
                {
                    return;
                }

                // Check for conflicting lighting templates
                if (!ValidateLightingTemplates(state))
                {
                    return;
                }

                // Build list of active True Light plugins
                var trueLightPlugins = BuildTrueLightPluginList(state, trueLightMod.Mod);
                Console.WriteLine($"Found {trueLightPlugins.Count} True Light plugin(s) active");

                // Create link caches
                var loadOrderLinkCache = state.LoadOrder.ToImmutableLinkCache();
                var trueLightLinkCache = trueLightPlugins.ToImmutableLinkCache();

                // Get default lighting template for cells without specific overrides
                var defaultLightingCell = GetDefaultLightingCell(trueLightLinkCache);

                // Process cells
                var cellStats = ProcessCells(state, loadOrderLinkCache, trueLightLinkCache, defaultLightingCell);

                // Process lights
                var lightStats = ProcessLights(state, loadOrderLinkCache, trueLightLinkCache);

                // Report results
                PrintResults(cellStats, lightStats);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error during patching: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
            }
        }

        private static bool ValidateTrueLightInstallation(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            out Mutagen.Bethesda.Plugins.Order.IModListing<ISkyrimModGetter> trueLightMod)
        {
            trueLightMod = null!;

            if (!state.LoadOrder.TryGetValue(TrueLight, out var modListing) || modListing.Mod == null)
            {
                Console.Error.WriteLine("ERROR: 'True Light.esm' not found in load order!");
                Console.Error.WriteLine("Please ensure True Light is installed and enabled.");
                return false;
            }

            trueLightMod = modListing;
            Console.WriteLine($"✓ Found True Light.esm");
            return true;
        }

        private static bool ValidateLightingTemplates(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var activeTemplates = LightingTemplates
                .Where(template => state.LoadOrder.ContainsKey(template))
                .Select(template => template.FileName)
                .ToList();
            
            if (activeTemplates.Count > 1)
            {
                Console.Error.WriteLine("ERROR: Multiple lighting template plugins detected!");
                Console.Error.WriteLine($"Active templates: {string.Join(", ", activeTemplates)}");
                Console.Error.WriteLine("Please enable only ONE lighting template plugin.");
                return false;
            }
            
            if (activeTemplates.Count == 1)
            {
                Console.WriteLine($"✓ Using lighting template: {activeTemplates[0]}");
            }
            else
            {
                Console.WriteLine("ℹ No lighting template selected (using True Light defaults)");
            }
            
            return true;
        }

        private static List<ISkyrimModGetter> BuildTrueLightPluginList(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ISkyrimModGetter trueLightMod)
        {
            var plugins = new List<ISkyrimModGetter> { trueLightMod };

            // Add active addons and templates
            var allPossibleAddons = TrueLightAddons.Concat(LightingTemplates);

            foreach (var addonKey in allPossibleAddons)
            {
                if (state.LoadOrder.TryGetValue(addonKey, out var addon) && addon.Mod != null)
                {
                    plugins.Add(addon.Mod);
                    Console.WriteLine($"  - Added: {addonKey.FileName}");
                }
            }

            return plugins;
        }

        private static ICellGetter? GetDefaultLightingCell(ILinkCache trueLightLinkCache)
        {
            // Find the first interior cell with lighting data from True Light plugins
            var defaultCell = trueLightLinkCache.PriorityOrder
                .WinningOverrides<ICellGetter>()
                .FirstOrDefault(cell => 
                    cell.Flags.HasFlag(Cell.Flag.IsInteriorCell) && 
                    cell.Lighting != null);
            
            if (defaultCell != null)
            {
                Console.WriteLine($"\nDefault lighting template: {defaultCell.EditorID ?? "Unnamed"} [{defaultCell.FormKey}]");
                
                if (defaultCell.Lighting != null)
                {
                    var lighting = defaultCell.Lighting;
                    Console.WriteLine($"  Ambient: {lighting.AmbientColor}");
                    Console.WriteLine($"  Fog: Near={lighting.FogNear:F2}, Far={lighting.FogFar:F2}");
                    Console.WriteLine($"  Directional: Fade={lighting.DirectionalFade:F2}, Rotation={lighting.DirectionalRotationXY}°");
                }
            }
            else
            {
                Console.WriteLine("⚠ Warning: No default lighting template found in True Light plugins");
            }
            
            return defaultCell;
        }

        private static CellPatchStats ProcessCells(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ILinkCache loadOrderLinkCache,
            ILinkCache trueLightLinkCache,
            ICellGetter? defaultLightingCell)
        {
            var stats = new CellPatchStats();
            
            Console.WriteLine("\nProcessing interior cells...");
            
            // Create comparison mask for lighting only
            var cellMask = new Cell.TranslationMask(defaultOn: false)
            {
                Lighting = true
            };
            
            // Get all interior cells from the load order (including mod-added cells)
            var cellContexts = state.LoadOrder.PriorityOrder.Cell()
                .WinningContextOverrides(loadOrderLinkCache)
                .Where(context => context.Record.Flags.HasFlag(Cell.Flag.IsInteriorCell));
            
            foreach (var cellContext in cellContexts)
            {
                stats.Total++;
                
                var cell = cellContext.Record;
                
                // Skip cells without lighting data
                if (cell.Lighting == null)
                {
                    stats.Skipped++;
                    continue;
                }
                
                // Determine if this is a mod-added cell (not from vanilla masters)
                bool isModAdded = !IsVanillaFormKey(cell.FormKey);
                
                // Try to find True Light override for this specific cell
                ICellGetter? trueLightCell = null;
                if (!isModAdded && trueLightLinkCache.TryResolve<ICellGetter>(cell.FormKey, out var resolvedCell))
                {
                    trueLightCell = resolvedCell;
                }
                
                // Use default template if no specific override exists (or if mod-added)
                if (trueLightCell?.Lighting == null)
                {
                    trueLightCell = defaultLightingCell;
                    stats.UsingDefault++;
                    
                    if (isModAdded)
                    {
                        stats.ModAddedCount++;
                    }
                }
                
                // Skip if no lighting data available
                if (trueLightCell?.Lighting == null)
                {
                    stats.Skipped++;
                    continue;
                }
                
                // Skip if lighting is already identical
                if (cell.Equals(trueLightCell, cellMask))
                {
                    stats.AlreadyPatched++;
                    continue;
                }
                
                // Apply True Light lighting
                var patchedCell = cellContext.GetOrAddAsOverride(state.PatchMod);
                patchedCell.Lighting = trueLightCell.Lighting.DeepCopy();
                stats.Patched++;
                
                // Log verbose details if needed
                if (stats.Patched <= 5) // Show first 5 for debugging
                {
                    var cellType = isModAdded ? " (Mod-Added)" : "";
                    Console.WriteLine($"  ✓ Patched: {cell.EditorID ?? "Unnamed"} [{cell.FormKey}]{cellType}");
                }
            }
            
            return stats;
        }
        
        private static bool IsVanillaFormKey(FormKey formKey)
        {
            // Check if FormKey is from vanilla Skyrim or official DLCs
            var vanillaMasters = new HashSet<ModKey>
            {
                ModKey.FromNameAndExtension("Skyrim.esm"),
                ModKey.FromNameAndExtension("Update.esm"),
                ModKey.FromNameAndExtension("Dawnguard.esm"),
                ModKey.FromNameAndExtension("HearthFires.esm"),
                ModKey.FromNameAndExtension("Dragonborn.esm"),
                // Include Creation Club content as "vanilla" for this purpose
                ModKey.FromNameAndExtension("ccBGSSSE001-Fish.esm"),
                ModKey.FromNameAndExtension("ccBGSSSE025-AdvDSGS.esm"),
                ModKey.FromNameAndExtension("ccBGSSSE037-Curios.esl"),
                ModKey.FromNameAndExtension("ccQDRSSE001-SurvivalMode.esl")
                // Add other CC content as needed
            };
            
            return vanillaMasters.Contains(formKey.ModKey);
        }

        private static LightPatchStats ProcessLights(
            IPatcherState<ISkyrimMod, ISkyrimModGetter> state,
            ILinkCache loadOrderLinkCache,
            ILinkCache trueLightLinkCache)
        {
            var stats = new LightPatchStats();
            
            Console.WriteLine("\nProcessing light records...");
            
            // Process all lights in the load order
            var lightRecords = state.LoadOrder.PriorityOrder.Light().WinningOverrides();
            
            foreach (var winningLight in lightRecords)
            {
                stats.Total++;
                
                // Check if True Light has an override for this light
                if (!trueLightLinkCache.TryResolve<ILightGetter>(winningLight.FormKey, out var trueLightVersion))
                {
                    stats.NotInTrueLight++;
                    continue;
                }
                
                // Get the original vanilla version
                if (!loadOrderLinkCache.TryResolve<ILightGetter>(winningLight.FormKey, out var vanillaLight, ResolveTarget.Origin))
                {
                    stats.Skipped++;
                    continue;
                }
                
                // Check if the current winning record is still vanilla (unmodified)
                if (!winningLight.Equals(vanillaLight))
                {
                    stats.AlreadyModified++;
                    continue;
                }
                
                // Check if True Light version is different from vanilla
                if (winningLight.Equals(trueLightVersion))
                {
                    stats.AlreadyPatched++;
                    continue;
                }
                
                // Forward the True Light version
                var patchedLight = state.PatchMod.Lights.DuplicateInAsNewRecord(trueLightVersion);
                stats.Patched++;
                
                // Log verbose details if needed
                if (stats.Patched <= 5) // Show first 5 for debugging
                {
                    Console.WriteLine($"  ✓ Patched: {trueLightVersion.EditorID ?? "Unnamed"} [{trueLightVersion.FormKey}]");
                }
            }
            
            return stats;
        }

        private static void PrintResults(CellPatchStats cellStats, LightPatchStats lightStats)
        {
            Console.WriteLine("\n=== Patching Complete ===");
            
            Console.WriteLine("\nCell Statistics:");
            Console.WriteLine($"  Total Processed: {cellStats.Total}");
            Console.WriteLine($"  Patched: {cellStats.Patched}");
            Console.WriteLine($"  Already Correct: {cellStats.AlreadyPatched}");
            Console.WriteLine($"  Using Default Template: {cellStats.UsingDefault}");
            Console.WriteLine($"  Mod-Added Cells: {cellStats.ModAddedCount}");
            Console.WriteLine($"  Skipped (No Lighting): {cellStats.Skipped}");
            
            Console.WriteLine("\nLight Statistics:");
            Console.WriteLine($"  Total Processed: {lightStats.Total}");
            Console.WriteLine($"  Patched: {lightStats.Patched}");
            Console.WriteLine($"  Already Correct: {lightStats.AlreadyPatched}");
            Console.WriteLine($"  Already Modified by Other Mods: {lightStats.AlreadyModified}");
            Console.WriteLine($"  Not in True Light: {lightStats.NotInTrueLight}");
            Console.WriteLine($"  Skipped: {lightStats.Skipped}");
            
            Console.WriteLine($"\n✓ Total Changes: {cellStats.Patched + lightStats.Patched}");
        }

        // Statistics tracking classes
        private class CellPatchStats
        {
            public int Total { get; set; }
            public int Patched { get; set; }
            public int AlreadyPatched { get; set; }
            public int UsingDefault { get; set; }
            public int ModAddedCount { get; set; }
            public int Skipped { get; set; }
        }

        private class LightPatchStats
        {
            public int Total { get; set; }
            public int Patched { get; set; }
            public int AlreadyPatched { get; set; }
            public int AlreadyModified { get; set; }
            public int NotInTrueLight { get; set; }
            public int Skipped { get; set; }
        }
    }
}