using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.Net;
using Game.SceneFlow;
using ILM.Localization;
using ILM.Systems.Emission;
using ILM.Systems.Prefabs;
using ILM.Systems.Tool;
using ILM.Systems.Topology;
using ILM.Systems.UI;
#if DEBUG
using ILM.Diagnostics;
#endif

namespace ILM
{
    /// <summary>
    /// Entry point. Replaces the vanilla <see cref="SecondaryLaneSystem"/> with
    /// <see cref="CustomSecondaryLaneSystem"/> and registers the prefab clone systems, the node
    /// marking tool and the systems that turn user-drawn lines and fills into game entities.
    /// </summary>
    public class Mod : IMod
    {
        public static ILog log = CreateLog();

        private static ILog CreateLog()
        {
            var logger = LogManager.GetLogger($"{nameof(ILM)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
            // By default UnityLogger reopens and closes the file on every write. When an open
            // fails (antivirus or cloud sync holding the file), Open() swallows the error, the
            // writer stays null and the write throws a NullReferenceException outside its
            // IOException catch, so it escapes into whichever system was logging. Keeping the
            // stream open means one open per session.
            logger.keepStreamOpen = true;
            return logger;
        }

        public static ILMSetting Settings { get; private set; }
        // ILMUISystem needs the live instance to resolve the mod's ExecutableAsset path:
        // ModManager indexes assets by mod instance, so a new Mod() would not be found.
        public static Mod Instance { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            // The defaults instance must be created before the live one: every ModSetting ctor
            // registers itself in the static ModSetting.instances[id] map, and the game resolves
            // the id to whichever instance was constructed last.
            var settingDefaults = new ILMSetting(this);
            Settings = new ILMSetting(this);
            // Must run before GetAction() resolves anything, otherwise the ToggleMarkingTool
            // ProxyAction silently never fires. Traffic registers key bindings in the same order.
            Settings.RegisterKeyBindings();
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(ILM), Settings, settingDefaults);
            // A decode failure silently falls back to SetDefaults(), so log what the load actually
            // produced; user reports then show the real state.
            log.Info($"settings loaded: edge={Settings.EdgeLineEnabled}/{Settings.EdgeLineStyle}, parking={Settings.ParkingMarkingsEnabled}/{Settings.ParkingLineStyle}/{Settings.ParkingEndStyle}, pins='{Settings.PinnedLineStylesCsv}'/'{Settings.PinnedAreaStylesCsv}'");
            // Set the level explicitly either way: the logger's default level is not ours to rely
            // on, and ~60 Debug lines sit in per-tick code paths.
            if (Settings.VerboseLogging)
            {
                log.effectivenessLevel = Level.Debug;
                log.Info("verbose logging on");
            }
            else
            {
                log.effectivenessLevel = Level.Info;
            }

            // Vanilla-surface fill styles; see VanillaSurfaceLateClone for why they are registered
            // after loading.
            VanillaSurfaceLateClone.Register(updateSystem.World);

#if DEBUG
            // Developer prefab surveys. Debug builds only, and even there off unless the hidden
            // DiagnosticDumps setting is on: they write tens of thousands of lines per boot.
            if (Settings.DiagnosticDumps)
            {
                // Read-only structural dump, useful when something changes between game patches.
                updateSystem.UpdateAt<RoadPrefabDumpSystem>(SystemUpdatePhase.PrefabUpdate);
                // One-shot probes for Shader.Find and the vanilla SurfacePrefab inventory.
                // Disables itself after its second pass.
                updateSystem.UpdateAt<AreasPrototypeSystem>(SystemUpdatePhase.PrefabUpdate);
            }
            log.Info($"diagnostic dumps: {(Settings.DiagnosticDumps ? "ON" : "off")}");
            // ParkingPairDumpSystem (parking endpoint debugging) is not registered by default;
            // add it here in GameSimulation when needed.
#endif

            // Marking prefab clones. Both run in PrefabUpdate so that PrefabSystem.UpdatePrefab
            // triggers NetInitializeSystem in the same frame and the SecondaryNetLane buffers are
            // baked before road geometry reads them.
            updateSystem.UpdateAt<EdgeLineCloneSystem>(SystemUpdatePhase.PrefabUpdate);
            updateSystem.UpdateAt<ParkingLineCloneSystem>(SystemUpdatePhase.PrefabUpdate);

            // Only the secondary lane pass (markings) is replaced; LaneSystem and traffic lanes are
            // untouched.
            var vanilla = updateSystem.World.GetOrCreateSystemManaged<SecondaryLaneSystem>();
            bool wasEnabled = vanilla.Enabled;
            vanilla.Enabled = false;
            log.Info($"vanilla SecondaryLaneSystem disabled (was Enabled={wasEnabled})");

            // Must run in Modification4B, where vanilla SecondaryLaneSystem runs and
            // AllowBarrier<ModificationBarrier4B> applies (Game.Common.SystemOrder). In Modification4,
            // SafeCommandBufferSystem.CreateCommandBuffer throws "Trying to create
            // EntityCommandBuffer when it's not allowed!".
            updateSystem.UpdateAt<CustomSecondaryLaneSystem>(SystemUpdatePhase.Modification4B);
            log.Info($"CustomSecondaryLaneSystem registered at Modification4B");

            // There is deliberately no system that reapplies settings at runtime: refreshing the
            // clone prefabs (UpdatePrefab) in a live world leaves existing sublanes with stale
            // PrefabRefs, and the next secondary lane rebuild crashes natively inside a Burst job.
            // Settings apply on the next save load through the clone systems' PrefabUpdate pass.

            // ToolBaseSystem adds itself to ToolSystem.tools in OnCreate; registering is enough.
            updateSystem.UpdateAt<MarkingNodeToolSystem>(SystemUpdatePhase.ToolUpdate);
            updateSystem.UpdateAt<MarkingToolHotkeySystem>(SystemUpdatePhase.Modification1);
            // Idle unless the marking tool is active; writes to OverlayRenderSystem.Buffer.
            updateSystem.UpdateAt<MarkingOverlaySystem>(SystemUpdatePhase.Rendering);

            // User lines and fills are emitted as regular vanilla entities (SecondaryLane sublanes
            // and Game.Areas.Area) so the game's own renderer draws them. A custom mesh path
            // cannot work: the vanilla decal shaders need DOTS instance properties
            // (colossal_CurveMatrix) that only the BatchRendererGroup pipeline supplies.
            //
            // Modification1 runs before LaneSystem (4), SecondaryLaneSystem (4B) and
            // SecondaryLaneReferencesSystem (5), which adds the emitted sublanes to the node's
            // SubLane buffer. Ordering inside Modification1 comes from [UpdateBefore]/[UpdateAfter]
            // on the classes: migration, line topology, line emission; line topology, area
            // topology, area emission.
            updateSystem.UpdateAt<MarkingPairMigrationSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<MarkingTopologySystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<MarkingSegmentEmissionSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<MarkingCrosswalkEmissionSystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<MarkingAreaTopologySystem>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<MarkingAreaEmissionSystem>(SystemUpdatePhase.Modification1);
            // Runs after vanilla Game.Areas.GeometrySystem (Modification2B) and replaces its
            // shrink-and-budget ear clipping, which can leave fills invisible, with a full
            // triangulation of the real outline.
            updateSystem.UpdateAt<MarkingAreaTriangulationSystem>(SystemUpdatePhase.Modification2B);
            // After GeometrySystem (Modification4): rebuilds markings whose road moved (Move It).
            updateSystem.UpdateAt<MarkingGeometryWatchSystem>(SystemUpdatePhase.Modification5);

            updateSystem.UpdateAt<ILMUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
        }
    }
}
