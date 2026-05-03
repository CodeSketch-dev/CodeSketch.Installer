using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

[InitializeOnLoad]
public static class AutoAddNewtonsoft
{
    static ListRequest s_listRequest;
    static bool s_checked = false;

    static AutoAddNewtonsoft()
    {
        // Delay the check so the editor has finished initializing
        EditorApplication.delayCall += CheckAndAdd;
    }

    static void CheckAndAdd()
    {
        if (s_checked) return;
        s_checked = true;

        try
        {
            s_listRequest = Client.List(true);
            EditorApplication.update += OnListProgress;
        }
        catch
        {
            // ignore
        }
    }

    static void OnListProgress()
    {
        if (s_listRequest == null || !s_listRequest.IsCompleted) return;
        EditorApplication.update -= OnListProgress;

        bool found = false;
        if (s_listRequest.Result != null)
        {
            foreach (var p in s_listRequest.Result)
            {
                if (p.name == "com.unity.nuget.newtonsoft-json")
                {
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            try
            {
                Client.Add("com.unity.nuget.newtonsoft-json");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Failed to auto-add com.unity.nuget.newtonsoft-json: " + ex.Message);
            }
        }

        s_listRequest = null;
    }
}
