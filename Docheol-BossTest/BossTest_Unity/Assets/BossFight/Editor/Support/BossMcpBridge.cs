#if UNITY_EDITOR
using System;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

namespace BossFight.Editor.Support
{
    /// <summary>Explicit local inspection connection; never runs automatically during builds.</summary>
    public static class BossMcpBridge
    {
        private const string LocalServerUrl = "http://127.0.0.1:8080";
        private static bool connecting;

        [MenuItem("Tools/Boss Fight/Connect Local MCP for Inspection")]
        public static async void ConnectForInspection()
        {
            if (connecting || BuildPipeline.isBuildingPlayer)
            {
                Debug.LogWarning("[BossMcpBridge] Connection already pending or player build in progress.");
                return;
            }

            connecting = true;
            try
            {
                Environment.SetEnvironmentVariable("DISABLE_TELEMETRY", "true");
                Environment.SetEnvironmentVariable("UNITY_MCP_DISABLE_TELEMETRY", "true");
                Environment.SetEnvironmentVariable("MCP_DISABLE_TELEMETRY", "true");
                TelemetryHelper.DisableTelemetry();

                var configuration = EditorConfigurationCache.Instance;
                configuration.SetHttpBaseUrl(LocalServerUrl);
                configuration.SetHttpTransportScope("local");
                configuration.SetUseHttpTransport(true);
                EditorPrefs.SetBool("MCPForUnity.Security.AllowLanHttpBind", false);
                EditorPrefs.SetBool("MCPForUnity.Security.AllowInsecureRemoteHttp", false);

                bool started = await MCPServiceLocator.Bridge.StartAsync();
                if (!started)
                {
                    Debug.LogError("[BossMcpBridge] Could not connect. Start the local MCP server at " + LocalServerUrl + " first.");
                    return;
                }

                var verification = await MCPServiceLocator.Bridge.VerifyAsync();
                if (verification.Success)
                    Debug.Log("[BossMcpBridge] Local MCP connected; telemetry disabled. " + verification.Message);
                else
                    Debug.LogWarning("[BossMcpBridge] Bridge started but verification is pending: " + verification.Message);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                connecting = false;
            }
        }
    }
}
#endif