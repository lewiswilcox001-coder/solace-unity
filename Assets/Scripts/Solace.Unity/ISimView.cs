// Solace.Unity — presentation/glue layer. Thin MonoBehaviours; all truth lives in Solace.Core.
using Solace.Core;

namespace Solace.Unity
{
    /// <summary>
    /// A view that mirrors simulation state onto the scene. GameBootstrap calls
    /// SyncFromState once per frame (play mode: from the view's own Update;
    /// verification: driven explicitly via GameBootstrap.SyncAllViews()).
    /// Implementations must not allocate in SyncFromState's hot path.
    /// </summary>
    public interface ISimView
    {
        void SyncFromState(GameState state);
    }
}
