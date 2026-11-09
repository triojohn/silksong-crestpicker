using System;

namespace CrestPicker.Pure;

/// <summary>What a node of a cloned vanilla crest card should be after the cosmetic pass.</summary>
internal enum CloneNode
{
    /// <summary>Node stays as the vanilla card left it.</summary>
    Keep,

    /// <summary>Node is switched off.</summary>
    Disable,
}

/// <summary>Which nodes of a cloned crest card are switched off, free of Unity types.</summary>
internal static class CloneFilter
{
    /// <summary>Effect holder of a vanilla crest card, invisible on an unselected card.</summary>
    private const string SubmitEffectsName = "Crest Submit Effects";

    /// <summary>
    /// Decides the fate of one node of a cloned card. The silhouette subtree and the slot
    /// widgets always stay; the vanilla effects go off, because their glow and white flash
    /// draw over the silhouette and only the vanilla fade kept them invisible.
    /// </summary>
    /// <param name="nodeName">Name of the node.</param>
    /// <param name="isSilhouette">True when the node is the silhouette or sits inside it.</param>
    /// <param name="inSlot">True when the node is a slot widget or sits inside one.</param>
    /// <returns><see cref="CloneNode.Disable"/> for an effect, <see cref="CloneNode.Keep"/> otherwise.</returns>
    internal static CloneNode Evaluate(string nodeName, bool isSilhouette, bool inSlot)
    {
        if (isSilhouette || inSlot)
        {
            return CloneNode.Keep;
        }

        if (string.Equals(nodeName, SubmitEffectsName, StringComparison.OrdinalIgnoreCase) ||
            nodeName.IndexOf("Burst Effect", StringComparison.OrdinalIgnoreCase) >= 0 ||
            nodeName.IndexOf("White Flash", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return CloneNode.Disable;
        }

        return CloneNode.Keep;
    }
}
