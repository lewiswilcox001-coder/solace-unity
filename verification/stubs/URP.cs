// STUB — compile-check only (verification/stubs/). Never compiled by Unity
// itself (this directory is outside Assets/), and never shipped.
//
// Why this exists: Assets/Editor/ProjectSetup.cs (pre-existing) references
// the URP package API (UnityEngine.Rendering.Universal) to create/assign the
// render pipeline asset on first open. The URP package is source-only in the
// editor install; compiling the entire Universal RP package just to typecheck
// one editor setup script is disproportionate, so this minimal stub stands in
// for the two URP types ProjectSetup touches. It mirrors the real API shape:
//   - UniversalRenderPipelineAsset.Create() (public static factory in URP 17)
//   - UniversalRenderPipelineAsset.rendererDataList (ScriptableRendererData[])
// If ProjectSetup starts using more URP API, extend this stub — or compile
// the real com.unity.render-pipelines.universal package instead.
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering.Universal
{
    public class ScriptableRendererData : ScriptableObject { }

    public class UniversalRenderPipelineAsset : RenderPipelineAsset
    {
        public static UniversalRenderPipelineAsset Create() { return null; }

        protected override RenderPipeline CreatePipeline() { return null; }

        public ScriptableRendererData[] rendererDataList
        {
            get { return new ScriptableRendererData[0]; }
        }
    }
}
