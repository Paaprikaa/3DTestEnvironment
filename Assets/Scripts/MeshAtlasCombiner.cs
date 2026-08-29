using UnityEngine;

// Children objects have to be same material and LOD level exactly 4
public class MeshAtlasCombiner : MonoBehaviour
{
    [Header("Atlas")]
    [Tooltip("Must be power of 2")]
    public int atlasSize = 2048;

    [Tooltip("pixels between diff images combined in the new atlas")]
    public int paddingPixels = 4;

    [Header("Atlas properties on shader")]
    public string baseColorProperty = "_BaseColor";
    public string normalMapProperty = "_NormalMap";
    public string maskMapProperty = "_MaskMap";

    [Header("Output")]
    public string outputFolder = "Assets/CombinedMeshes";

    [Header("Behavior")]
    [Tooltip("destroy childrens")]
    public bool destroyOriginals = true;

    [Tooltip("use 32 index bits (needed if vertices exceed ~65.000)")]
    public bool use32BitIndices = false;
}