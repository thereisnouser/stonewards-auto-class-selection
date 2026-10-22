using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace StonewardsAutoClassSelection
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class AutoClassSelectionPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.thereisnouser.stonewards.autoclassselection";
        public const string PluginName = "Stonewards Auto Class Selection";
        public const string PluginVersion = "0.1.4";

        private const BindingFlags InstanceMethods =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _openDelaySeconds;
        private ConfigEntry<bool> _allowDuringActiveRun;


        private static AutoClassSelectionPlugin _plugin;
        private Harmony _harmony;
        private bool _hubReady;
        private bool _hubHookInstalled;
        private float _nextRetryAt;
        private int _failedAttempts;
        private object _currentConnection;
        private ulong _currentSteamLobbyId;
        private FieldInfo _steamLobbyInstanceField;
        private FieldInfo _steamLobbyIdField;
        private PropertyInfo _sceneTypeProperty;
        private bool _loggedMissingSceneType;
        private float _connectedAt;
        private float _nextCheckAt;
        private bool _handledConnection;
        private bool _loggedMissingMethod;
        private string _lastWaitReason;
        private float _nextWaitLogAt;

        private void Awake()
        {
            _enabled = Config.Bind(
                "General", "Enabled", true,
                "Automatically show the game's native class-selection window when joining a session.");
            _openDelaySeconds = Config.Bind(
                "General", "OpenDelaySeconds", 1.0f,
                new ConfigDescription(
                    "Seconds to wait after connecting before opening class selection (0 to 10).",
                    new AcceptableValueRange<float>(0f, 10f)));
            _allowDuringActiveRun = Config.Bind(
                "General", "AllowSelectionDuringActiveRun", false,
                "EXPERIMENTAL: attempt to open the native class menu when entering an ongoing run. " +
                "Changing class mid-run can reset inventory and upgrades; enable only knowingly.");

            var lobbyType = typeof(StoneWardsNetworkManager).Assembly.GetType("SteamLobby");
            const BindingFlags anyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags anyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (lobbyType != null)
            {
                _steamLobbyInstanceField = lobbyType.GetField("Instance", anyStatic);
                _steamLobbyIdField = lobbyType.GetField("currentLobbyID", anyInstance);
            }
            _sceneTypeProperty = typeof(GameManager).GetProperty("SceneType", anyInstance);
            if (_steamLobbyInstanceField == null || _steamLobbyIdField == null)
                Logger.LogWarning("Steam lobby metadata is unavailable; using connection identity as fallback.");

            _plugin = this;
            _harmony = new Harmony(PluginGuid);
            var hubType = AccessTools.TypeByName("HubManager");
            var hubReady = hubType == null ? null :
                AccessTools.Method(hubType, "OnLoadingScreenCompleted");
            if (hubReady != null)
            {
                try
                {
                    _harmony.Patch(hubReady, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(AutoClassSelectionPlugin),
                            nameof(AfterHubLoadingCompleted))));
                    _hubHookInstalled = true;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning("HubManager hook failed: " + ex);
                }
            }
            Logger.LogInfo(PluginName + " v" + PluginVersion +
                " loaded (hubHook=" + _hubHookInstalled + ").");
        }

        // The game invokes this callback when the hub's loading animation ends.
        private static void AfterHubLoadingCompleted()
        {
            if (_plugin == null || !_plugin._enabled.Value)
                return;
            _plugin._hubReady = true;
            // The callback signals that UI loading has finished, but it is
            // not guaranteed to run on every switch between Steam lobbies.
            if (!_plugin._handledConnection)
            {
                _plugin._nextRetryAt = Time.unscaledTime;
                _plugin.Logger.LogInfo("Hub loading completed; native class selection is ready to be checked.");
            }
            else
            {
                _plugin.Logger.LogDebug("Hub completion for a lobby whose selection was already handled.");
            }
        }

        private void Update()
        {
            // Four checks per second are sufficient, including on slower loading screens.
            if (Time.unscaledTime < _nextCheckAt)
                return;
            _nextCheckAt = Time.unscaledTime + 0.25f;

            object connection = NetworkClient.connection;
            if (!NetworkClient.active || !NetworkClient.isConnected || connection == null)
            {
                ResetSession();
                return;
            }

            // A different Steam lobby must reopen selection even if the same
            // Mirror connection object is reused or a brief disconnect is missed.
            ulong lobbyId = ReadSteamLobbyId();
            bool connectionChanged = !ReferenceEquals(connection, _currentConnection);
            bool lobbyChanged = lobbyId != 0UL &&
                                _currentSteamLobbyId != 0UL &&
                                lobbyId != _currentSteamLobbyId;
            if (connectionChanged || lobbyChanged)
            {
                _currentConnection = connection;
                _currentSteamLobbyId = lobbyId;
                _connectedAt = Time.unscaledTime;
                _handledConnection = false;
                _hubReady = false;
                _failedAttempts = 0;
                _nextRetryAt = Time.unscaledTime;
                _loggedMissingMethod = false;
                _lastWaitReason = null;
                _nextWaitLogAt = Time.unscaledTime;
                Logger.LogInfo("New lobby detected (Steam ID: " + lobbyId +
                    ", connectionChanged=" + connectionChanged +
                    ", lobbyChanged=" + lobbyChanged +
                    "). Scheduling native class selection independently of the hub callback.");
            }
            else if (lobbyId != 0UL)
            {
                // A late 0 -> real lobby ID transition is not another join.
                _currentSteamLobbyId = lobbyId;
            }

            if (!_enabled.Value || _handledConnection)
                return;

            // IMPORTANT: do not require the hub callback. Stonewards can
            // switch Steam lobbies within an already loaded hub scene,
            // without invoking HubManager.OnLoadingScreenCompleted again.
            if (Time.unscaledTime < _nextRetryAt)
                return;

            if (Time.unscaledTime - _connectedAt < _openDelaySeconds.Value)
                return;

            // The game's own loading flow opens class selection before it
            // necessarily has a NetworkClient.localPlayer. Requiring a spawned
            // player here can block the menu forever in a fresh lobby.
            // Guard by scene instead so the menu never opens mid-run.
            // In Stonewards 0.2.0+, joining an active session can place a
            // client straight in gameplay (GameSceneType = 1) with no hub.
            // The game intentionally does not open class selection there.
            // Preserve that behavior by default: class changes during a run
            // could reset a restored player's items or upgrades.
            int sceneType = GetSceneTypeValue();
            if (sceneType == 1 && !_allowDuringActiveRun.Value)
            {
                LogWait("joined an active run (SceneType=1); native selection disabled for safety. " +
                    "Set AllowSelectionDuringActiveRun=true to try the experimental mode");
                _handledConnection = true;
                return;
            }
            if (sceneType != 0 && sceneType != 1)
            {
                LogWait("unknown or unavailable scene type: " + sceneType);
                return;
            }

            var screen = CharacterSelectionScreen.Instance;
            if (screen == null)
            {
                LogWait("CharacterSelectionScreen.Instance is null in SceneType=" + sceneType +
                    "; this scene may not contain the native class selection UI");
                return;
            }

            // Some game versions can already open the class menu themselves.
            if (screen.IsOpen)
            {
                _handledConnection = true;
                Logger.LogInfo("Class selection already open in this lobby; nothing to do.");
                return;
            }

            try
            {
                // Invoke exactly the base method Stonewards calls in GameManager:
                // UnlockableMenu.Show(bool), not a guessed derived Show overload.
                var method = AccessTools.Method(typeof(UnlockableMenu),
                    "Show", new[] { typeof(bool) });
                if (method == null)
                {
                    // Avoid log spam if a future Stonewards update changes its UI API.
                    if (!_loggedMissingMethod)
                    {
                        Logger.LogWarning(
                            "No supported Show/ BaseShow method found on CharacterSelectionScreen. " +
                            "The mod will not fabricate a replacement menu.");
                        _loggedMissingMethod = true;
                    }
                    _handledConnection = true;
                    return;
                }

                object[] args = method.GetParameters().Length == 0
                    ? new object[0]
                    : new object[] { true };

                // Calls Stonewards' own menu method; no custom interface or network messages.
                method.Invoke(screen, args);
                if (screen.IsOpen)
                {
                    _handledConnection = true;
                    Logger.LogInfo("Native class selection opened (SceneType=" +
                        GetSceneTypeValue() + ", hubCallback=" + _hubReady + ").");
                }
                else
                {
                    Logger.LogWarning("Show(true) returned but the class window is still closed.");
                    RetryOpening();
                }
            }
            catch (TargetInvocationException ex)
            {
                Logger.LogWarning("Native class menu threw: " +
                                  (ex.InnerException ?? ex));
                RetryOpening();
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not open class selection: " + ex);
                RetryOpening();
            }
        }

        private void LogWait(string reason)
        {
            // A single diagnostic per reason and occasional repeats make
            // missing game lifecycle events observable without filling logs.
            if (reason == _lastWaitReason && Time.unscaledTime < _nextWaitLogAt)
                return;
            _lastWaitReason = reason;
            _nextWaitLogAt = Time.unscaledTime + 5f;
            Logger.LogInfo("Waiting to open class selection: " + reason +
                ", lobby=" + _currentSteamLobbyId +
                ", hubCallback=" + _hubReady + ".");
        }

        private void RetryOpening()
        {
            _failedAttempts++;
            if (_failedAttempts >= 3)
            {
                _handledConnection = true;
                Logger.LogError("Class menu failed to open after three attempts.");
            }
            else
            {
                _nextRetryAt = Time.unscaledTime + 1f;
            }
        }

        private ulong ReadSteamLobbyId()
        {
            if (_steamLobbyInstanceField == null || _steamLobbyIdField == null)
                return 0UL;

            try
            {
                object lobby = _steamLobbyInstanceField.GetValue(null);
                if (lobby == null) return 0UL;
                object value = _steamLobbyIdField.GetValue(lobby);
                return value == null ? 0UL : Convert.ToUInt64(value);
            }
            catch
            {
                // Reflection failure must never interfere with Steam or Mirror.
                return 0UL;
            }
        }

        private int GetSceneTypeValue()
        {
            var manager = GameManager.Instance;
            if (manager == null) return -1;
            try
            {
                if (_sceneTypeProperty != null)
                    return Convert.ToInt32(_sceneTypeProperty.GetValue(manager, null));

                if (!_loggedMissingSceneType)
                {
                    Logger.LogWarning("GameManager.SceneType is unavailable.");
                    _loggedMissingSceneType = true;
                }
            }
            catch (Exception ex)
            {
                if (!_loggedMissingSceneType)
                {
                    Logger.LogWarning("Could not read GameManager.SceneType: " + ex.Message);
                    _loggedMissingSceneType = true;
                }
            }
            return -1;
        }

        private static MethodInfo FindNativeShowMethod(Type type)
        {
            // Prefer the game's public/overridden screen lifecycle method.
            var method = FindMethod(type, "Show", typeof(bool));
            if (method != null) return method;

            method = FindMethod(type, "Show");
            if (method != null) return method;

            // Other Stonewards native menus use the BaseMenu.BaseShow lifecycle.
            return FindMethod(type, "BaseShow", typeof(bool));
        }

        private static MethodInfo FindMethod(Type type, string name, params Type[] parameterTypes)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                var methods = current.GetMethods(InstanceMethods);
                foreach (var method in methods)
                {
                    if (!string.Equals(method.Name, name, StringComparison.Ordinal))
                        continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length != parameterTypes.Length)
                        continue;

                    bool matches = true;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (parameters[i].ParameterType != parameterTypes[i])
                        {
                            matches = false;
                            break;
                        }
                    }
                    if (matches) return method;
                }
            }
            return null;
        }

        private void ResetSession()
        {
            _currentConnection = null;
            _currentSteamLobbyId = 0UL;
            _handledConnection = false;
            _hubReady = false;
            _failedAttempts = 0;
            _loggedMissingMethod = false;
            _lastWaitReason = null;
            _nextWaitLogAt = 0f;
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            if (ReferenceEquals(_plugin, this))
                _plugin = null;
        }
    }
}
