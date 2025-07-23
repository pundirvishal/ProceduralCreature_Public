using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// This class defines the custom inspector for our shader.
public class LitShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        // First, draw the default properties for the shader
        base.OnGUI(materialEditor, properties);

        Material targetMat = materialEditor.target as Material;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sorting Options", EditorStyles.boldLabel);

        // Get the current sorting layer and order from the renderer
        // Note: This requires an object with the material to be selected in the scene
        Renderer renderer = GetRenderer(materialEditor);
        if (renderer == null)
        {
            EditorGUILayout.HelpBox("Select an object in the scene with this material to see Sorting Options.", MessageType.Info);
            return;
        }

        // Allow the user to change the sorting layer
        string[] sortingLayerNames = GetSortingLayerNames();
        int currentSortingLayerID = renderer.sortingLayerID;
        int currentLayerIndex = -1;
        for (int i = 0; i < sortingLayerNames.Length; i++)
        {
            if (SortingLayer.NameToID(sortingLayerNames[i]) == currentSortingLayerID)
            {
                currentLayerIndex = i;
                break;
            }
        }

        EditorGUI.BeginChangeCheck();
        int newLayerIndex = EditorGUILayout.Popup("Sorting Layer", currentLayerIndex, sortingLayerNames);
        if (EditorGUI.EndChangeCheck())
        {
            renderer.sortingLayerName = sortingLayerNames[newLayerIndex];
        }

        // Allow the user to change the order in layer
        EditorGUI.BeginChangeCheck();
        int newOrderInLayer = EditorGUILayout.IntField("Order in Layer", renderer.sortingOrder);
        if (EditorGUI.EndChangeCheck())
        {
            renderer.sortingOrder = newOrderInLayer;
        }
    }

    private Renderer GetRenderer(MaterialEditor materialEditor)
    {
        foreach (Object obj in materialEditor.targets)
        {
            Material mat = obj as Material;
            if (mat != null)
            {
                // Find a renderer that uses this material in the current selection
                var renderers = Selection.GetFiltered<Renderer>(SelectionMode.Editable | SelectionMode.Deep);
                foreach (var r in renderers)
                {
                    if (System.Array.IndexOf(r.sharedMaterials, mat) > -1)
                    {
                        return r;
                    }
                }
            }
        }
        return null;
    }

    private string[] GetSortingLayerNames()
    {
        var layers = SortingLayer.layers;
        string[] names = new string[layers.Length];
        for (int i = 0; i < layers.Length; i++)
        {
            names[i] = layers[i].name;
        }
        return names;
    }
}