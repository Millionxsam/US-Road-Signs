using System;
using System.IO;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.UI.InGame;
using USRoadSigns.Digital;
using USRoadSigns.Localization;
using USRoadSigns.Route;
using USRoadSigns.Runtime;

namespace USRoadSigns
{
    /// <summary>
    /// Mod entry point. Keeps initialization light: loads settings and the sign catalog, then defers prefab
    /// creation until the game has loaded its own prefabs (the toolbar groups we attach to must exist).
    /// </summary>
    public sealed class Mod : IMod
    {
        public const string ModId = "USRoadSigns";

        internal static readonly ILog Log = LogManager.GetLogger(ModId).SetShowsErrorsInUI(false);

        internal static Mod Instance { get; private set; }

        internal ModSettings Settings { get; private set; }

        private SignLibraryLoader m_Loader;
        private LocaleEN m_Locale;
        private string m_LocaleId;
        private bool m_WaitingForWorld;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            Log.Info("OnLoad " + typeof(Mod).Assembly.GetName().Version);

            if (!GameManager.instance.modManager.TryGetExecutableAsset(this, out ExecutableAsset asset))
            {
                Log.Error("Could not locate the mod assembly on disk; US Road Signs will not load.");
                return;
            }

            string modFolder = Path.GetDirectoryName(asset.path);
            Log.Info("Mod folder: " + modFolder);

            Settings = new ModSettings(this);
            AssetDatabase.global.LoadSettings(ModId, Settings, new ModSettings(this));
            Settings.RegisterInOptionsUI();

            try
            {
                m_Loader = new SignLibraryLoader(modFolder, Settings, updateSystem.World);
                m_Loader.Prepare();
            }
            catch (Exception e)
            {
                Log.Error(e, "Failed to prepare the sign library");
                m_Loader = null;
            }

            m_LocaleId = GameManager.instance.localizationManager.fallbackLocaleId;
            m_Locale = new LocaleEN(Settings, m_Loader?.Catalog);
            GameManager.instance.localizationManager.AddSource(m_LocaleId, m_Locale);

            if (m_Loader == null)
                return;

            RegisterDigitalSignSystems(updateSystem);
            RegisterRouteSignSystems(updateSystem);

            GameManager.State state = GameManager.instance.state;
            if (state == GameManager.State.WorldReady || state == GameManager.State.Loading)
            {
                m_Loader.RegisterPrefabs();
            }
            else
            {
                m_WaitingForWorld = true;
                GameManager.instance.onWorldReady += OnWorldReady;
            }
        }

        /// <summary>Message renderer plus the "Message Sign" section of the selected info panel.</summary>
        private static void RegisterDigitalSignSystems(UpdateSystem updateSystem)
        {
            try
            {
                updateSystem.UpdateAt<DigitalSignRenderSystem>(SystemUpdatePhase.Rendering);
                DigitalSignSection section = updateSystem.World.GetOrCreateSystemManaged<DigitalSignSection>();
                updateSystem.World.GetOrCreateSystemManaged<SelectedInfoUISystem>().AddMiddleSection(section);
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not set up the digital sign systems; message signs will show blank displays.");
            }
        }

        /// <summary>Plate and artwork renderer plus the "Route sign" section of the selected info panel.</summary>
        private static void RegisterRouteSignSystems(UpdateSystem updateSystem)
        {
            try
            {
                updateSystem.UpdateAt<RouteSignRenderSystem>(SystemUpdatePhase.Rendering);
                RouteSignSection section = updateSystem.World.GetOrCreateSystemManaged<RouteSignSection>();
                updateSystem.World.GetOrCreateSystemManaged<SelectedInfoUISystem>().AddMiddleSection(section);
            }
            catch (Exception e)
            {
                Log.Error(e, "Could not set up the route sign systems; route marker plates will not be drawn.");
            }
        }

        private void OnWorldReady()
        {
            if (m_WaitingForWorld)
            {
                m_WaitingForWorld = false;
                GameManager.instance.onWorldReady -= OnWorldReady;
            }
            m_Loader?.RegisterPrefabs();
        }

        public void OnDispose()
        {
            Log.Info("OnDispose");
            if (m_WaitingForWorld && GameManager.instance != null)
                GameManager.instance.onWorldReady -= OnWorldReady;
            m_WaitingForWorld = false;

            if (m_Locale != null && GameManager.instance?.localizationManager != null)
                GameManager.instance.localizationManager.RemoveSource(m_LocaleId, m_Locale);

            m_Loader?.Dispose();
            m_Loader = null;

            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
            Instance = null;
        }
    }
}
