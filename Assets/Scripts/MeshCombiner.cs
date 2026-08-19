using UnityEngine;
using System.Collections.Generic;

// Use this script on an empty GameObject,
// its children need to have the same material.
// Combiner executes once the button is pushed, it doesn't execute automatically in runtime
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MeshCombiner : MonoBehaviour
{
    [Tooltip("gameobject childs are going tobe deleted")]
    [SerializeField] private bool destroyOriginals = true;

    [Tooltip("use 32 bits indices (needed if total verices +~65.000)")]
    [SerializeField] private bool use32BitIndices = false;

    public bool DestroyOriginals => destroyOriginals;
    public bool Use32BitIndices => use32BitIndices;
}