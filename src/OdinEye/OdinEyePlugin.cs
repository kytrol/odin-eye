namespace OdinEye
{
    using BepInEx;
    using BepInEx.Configuration;
    using Coroutines;
    using Events;
    using HarmonyLib;
    using Http;
    using Logging;
    using Middlewares;
    using System.Reflection;

    [BepInPlugin("org.bepinex.plugins.odineye", "odineye", "1.0.0.0")]
    public class OdinEyePlugin : BaseUnityPlugin
    {
        private ConfigEntry<string> httpServerAddress;
        public static OdinEyePlugin Instance { get; private set; }
        public ILogger Logger { get; private set; }
        public EventHandler EventHandler { get; private set; }
        public HttpWebServer HttpWebServer { get; private set; }
        public GameStatsSnapshotCoroutine StatsSnapshotCoroutine { get; private set; }

        private void Awake()
        {
            Instance = this;
            Logger = new Logger(base.Logger);
            Logger.LogInfo("OdinEye starting!");

            LoadConfiguration();

            try
            {
                SafeJsonSerializer.Warm();
            }
            catch (System.Exception ex)
            {
                Logger.LogError($"Failed to pre-build OdinEye's JSON formatters: {ex}");
            }

            try
            {
                HttpWebServer = new HttpWebServer(httpServerAddress.Value, Logger);
            }
            catch (System.Exception ex)
            {
                Logger.LogError(
                    $"Failed to start OdinEye's Http/WebSocket server at '{httpServerAddress.Value}': {ex}. " +
                    "Check the [Hosting] HttpServerAddress setting in the org.bepinex.plugins.odineye.cfg config file. " +
                    "OdinEye's REST API and WebSocket event stream will be unavailable until this is fixed.");
                return;
            }

            try
            {
                SetupEventPipeline();
            }
            catch (System.Exception ex)
            {
                Logger.LogError(
                    $"Failed to initialize OdinEye's event pipeline: {ex}. " +
                    "OdinEye's WebSocket event stream will be unavailable until this is fixed.");
                return;
            }

            try
            {
                SetupHarmonyPatches();
            }
            catch (System.Exception ex)
            {
                Logger.LogError(
                    $"Failed to apply OdinEye's Harmony patches: {ex}. " +
                    "In-game event hooks (chat, world/player events, etc.) will not fire, but the REST API and WebSocket server remain available.");
                return;
            }

            Logger.LogInfo("OdinEye running!");
        }

        private void LoadConfiguration() =>
            httpServerAddress = Config.Bind("Hosting", "HttpServerAddress", "http://localhost:2469/", "The network address where the Http Server will be hosted");
        
        private void SetupHarmonyPatches()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var harmony = new Harmony("org.bepinex.plugins.odineye");
            harmony.PatchAll(assembly);
        }
        
        private void SetupEventPipeline()
        {
            StatsSnapshotCoroutine = new GameStatsSnapshotCoroutine(HttpWebServer);
            EventHandler = new EventHandler();
            EventHandler.Configure(options => options
                .AddMiddleware(new ExceptionHandlerMiddleware(Logger))
                .AddMiddleware(new LoggingMiddleware(Logger))
                .AddMiddleware(new EventDispatcherMiddleware(HttpWebServer)));
        }
    }
}