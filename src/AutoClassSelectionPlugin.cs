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
        public const string PluginVersion = "0.1.0";

        private const BindingFlags InstanceMethods =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _openDelaySeconds;

        private object _currentConnection;
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

            // A replacement connection object means a fresh join, even if
            // disconnect/reconnect happened between two Update ticks.
            if (!ReferenceEquals(connection, _currentConnection))
            {
                _currentConnection = connection;
                _connectedAt = Time.unscaledTime;
                _handledConnection = false;
                _loggedMissingMethod = false;
            }

            if (!_enabled.Value || _handledConnection)
                return;

            if (Time.unscaledTime - _connectedAt < _openDelaySeconds.Value)
                return;

            // Don't show the menu during the connection/loading phase.
            if (NetworkClient.localPlayer == null)
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
            _handledConnection = false;
            _loggedMissingMethod = false;
        }
    }
}
