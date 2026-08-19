using UnityEngine;

namespace MiniECS.Rendering.Authoring
{
    /// <summary>
    /// Lightweight cache component added to each child of a <see cref="VatSubScene"/>
    /// by the editor bake. Holds the pre-baked VatAnimationData so a built player
    /// can convert the child to a VAT entity without re-baking.
    /// </summary>
    [DisallowMultipleComponent]
    public class VatSubSceneChild : MonoBehaviour
    {
        public VatAnimationData overrideVatData;
    }
}
