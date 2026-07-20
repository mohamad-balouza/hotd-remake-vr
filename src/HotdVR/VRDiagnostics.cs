using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace HotdVR
{
    /// <summary>
    /// Startup diagnostics: dumps XR subsystem descriptors so log inspection can
    /// confirm whether the OpenVR XR plugin manifest was discovered by the engine.
    /// </summary>
    public class VRDiagnostics : MonoBehaviour
    {
        private IEnumerator Start()
        {
            // Give the engine a few frames so late-registered descriptors show up too.
            yield return null;
            yield return null;
            yield return null;
            DumpSubsystems("frame 3");
            yield return new WaitForSeconds(5f);
            DumpSubsystems("t+5s");
        }

        internal static void DumpSubsystems(string tag)
        {
            var displayDescriptors = new List<XRDisplaySubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(displayDescriptors);
            var inputDescriptors = new List<XRInputSubsystemDescriptor>();
            SubsystemManager.GetSubsystemDescriptors(inputDescriptors);

            Plugin.Log.LogInfo($"[Diag/{tag}] XRDisplaySubsystemDescriptors: {displayDescriptors.Count}");
            foreach (var d in displayDescriptors)
                Plugin.Log.LogInfo($"[Diag/{tag}]   display: id='{d.id}'");
            Plugin.Log.LogInfo($"[Diag/{tag}] XRInputSubsystemDescriptors: {inputDescriptors.Count}");
            foreach (var d in inputDescriptors)
                Plugin.Log.LogInfo($"[Diag/{tag}]   input: id='{d.id}'");

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            foreach (var d in displays)
                Plugin.Log.LogInfo($"[Diag/{tag}] display instance running={d.running}");
        }
    }
}
