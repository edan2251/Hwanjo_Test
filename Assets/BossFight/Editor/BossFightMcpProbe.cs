#if UNITY_EDITOR
using UnityEngine;
using MCPForUnity.Editor.Services;
namespace FourGuardians.BossFight.Editor
{
    // Editor-only: connects the already installed bridge to its existing local server.
    // Does not change scenes, gameplay, packages or editor preferences.
    public static class BossFightMcpProbe
    {
        public static async void ConnectExistingBridge()
        {
            bool connected=await MCPServiceLocator.Bridge.StartAsync();
            Debug.Log("[V6 MCP] Existing bridge connected="+connected);
        }
    }
}
#endif
