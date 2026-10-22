using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;

namespace StonewardsAutoClassSelection
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class AutoClassSelectionPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.thereisnouser.stonewards.autoclassselection";
        public const string PluginName = "Stonewards Auto Class Selection";
        public const string PluginVersion = "0.1.1";

        private const BindingFlags InstanceMethods =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _openDelaySeconds;

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

            Logger.LogInfo(PluginName + " v" + PluginVersion + " loaded.");
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
                _loggedMissingMethod = false;
                Logger.LogInfo("Entered new lobby (Steam ID: " + lobbyId + "); waiting for native menu.");
            }
            else if (lobbyId != 0UL)
            {
                // A late 0 -> real lobby ID transition is not another join.
                _currentSteamLobbyId = lobbyId;
            }

            if (!_enabled.Value || _handledConnection)
                return;

            if (Time.unscaledTime - _connectedAt < _openDelaySeconds.Value)
                return;

            // Wait for the local player and the actual lobby. Never open
            // class selection while the session is already in a running level.
            if (NetworkClient.localPlayer == null || !IsInLobbyScene())
                return;

            var screen = CharacterSelectionScreen.Instance;
            if (screen == null)
                return;

            // Some game versions can already open the class menu themselves.
            if (screen.IsOpen)
            {
                _handledConnection = true;
                Logger.LogDebug("Class selection was already open for this connection.");
                return;
            }

            try
            {
                var method = FindNativeShowMethod(screen.GetType());
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
                _handledConnection = true;
                Logger.LogInfo("Opened native class selection after entering a session via " +
                    method.DeclaringType.Name + "." + method.Name + ".");
            }
            catch (TargetInvocationException ex)
            {
                _handledConnection = true; // Fail closed, rather than retrying every frame.
                Logger.LogError("The game's class-selection method threw: " +
                                (ex.InnerException ?? ex));
            }
            catch (Exception ex)
            {
                _handledConnection = true;
                Logger.LogError("Could not open class selection: " + ex);
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

        private bool IsInLobbyScene()
        {
            var manager = GameManager.Instance;
            if (manager == null) return false;

            try
            {
                if (_sceneTypeProperty != null)
                {
                    // Inspected build: 0 = hub, 1 = active level.
                    return Convert.ToInt32(_sceneTypeProperty.GetValue(manager, null)) == 0;
                }

                if (!_loggedMissingSceneType)
                {
                    Logger.LogWarning("GameManager.SceneType unavailable; cannot safely verify lobby.");
                    _loggedMissingSceneType = true;
                }
            }
            catch (Exception ex)
            {
                if (!_loggedMissingSceneType)
                {
                    Logger.LogWarning("Unable to read current scene: " + ex.Message);
                    _loggedMissingSceneType = true;
                }
            }
            return false;
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
            _loggedMissingMethod = false;
        }
    }
}
