using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Solace.Editor
{
    /// <summary>
    /// First-open project setup. Ensures a URP render pipeline asset exists and is
    /// assigned in GraphicsSettings. Idempotent: no-ops once a pipeline is assigned.
    ///
    /// Uses only long-standing UnityEditor APIs:
    ///  - EditorApplication.delayCall
    ///  - AssetDatabase (IsValidFolder / CreateFolder / CreateAsset / AddObjectToAsset / SaveAssets)
    ///  - GraphicsSettings.renderPipelineAsset
    ///  - UniversalRenderPipelineAsset.Create() (public static factory in URP 17)
    /// </summary>
    [InitializeOnLoad]
    internal static class ProjectSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string PipelineAssetPath = SettingsFolder + "/SolaceURPPipeline.asset";

        static ProjectSetup()
        {
            // Defer until the editor has finished loading so AssetDatabase is fully usable.
            EditorApplication.delayCall += EnsureRenderPipeline;
        }

        private static void EnsureRenderPipeline()
        {
            if (GraphicsSettings.renderPipelineAsset != null)
            {
                Debug.Log("[Solace] ProjectSetup: render pipeline already assigned ("
                    + GraphicsSettings.renderPipelineAsset.name + "); nothing to do.");
                return;
            }

            try
            {
                if (!AssetDatabase.IsValidFolder(SettingsFolder))
                {
                    AssetDatabase.CreateFolder("Assets", "Settings");
                }

                // Public URP factory: creates the pipeline asset with a default
                // UniversalRendererData attached to its renderer list.
                var pipelineAsset = UniversalRenderPipelineAsset.Create();
                if (pipelineAsset == null)
                {
                    Debug.LogError("[Solace] ProjectSetup: failed to create UniversalRenderPipelineAsset.");
                    return;
                }

                AssetDatabase.CreateAsset(pipelineAsset, PipelineAssetPath);

                // Persist the renderer data created internally by Create() as a sub-asset
                // so the reference survives domain reloads and editor restarts.
                if (pipelineAsset.rendererDataList.Length > 0)
                {
                    var rendererData = pipelineAsset.rendererDataList[0];
                    if (rendererData != null)
                    {
                        AssetDatabase.AddObjectToAsset(rendererData, pipelineAsset);
                    }
                    else
                    {
                        Debug.LogWarning("[Solace] ProjectSetup: pipeline asset has no renderer data attached.");
                    }
                }

                GraphicsSettings.renderPipelineAsset = pipelineAsset;
                AssetDatabase.SaveAssets();

                Debug.Log("[Solace] ProjectSetup: created and assigned URP pipeline asset at "
                    + PipelineAssetPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Solace] ProjectSetup: unexpected error while creating URP asset: " + ex);
            }
        }
    }
}
