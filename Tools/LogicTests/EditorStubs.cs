using System;
using UnityEngine;
namespace UnityEditor
{
    public class MenuItem : Attribute { public MenuItem(string s){} }
    public static class AssetDatabase {
        public static bool IsValidFolder(string p){return true;} public static string CreateFolder(string a,string b){return "";}
        public static void ImportAsset(string p){} public static T LoadAssetAtPath<T>(string p) where T : UnityEngine.Object {return null;}
        public static void SaveAssets(){} public static void Refresh(){} }
    public class AssetImporter { public static AssetImporter GetAtPath(string p){return null;} }
    public enum TextureImporterType { Sprite }
    public class TextureImporter : AssetImporter { public TextureImporterType textureType; public float spritePixelsPerUnit; public FilterMode filterMode; public void SaveAndReimport(){} }
    public static class PrefabUtility { public static GameObject SaveAsPrefabAsset(GameObject g,string p){return g;} }
    public static class EditorUtility { public static bool DisplayDialog(string a,string b,string c){return true;} }
    public class SerializedProperty { public UnityEngine.Object objectReferenceValue; }
    public class SerializedObject { public SerializedObject(UnityEngine.Object o){} public SerializedProperty FindProperty(string n){return null;} public bool ApplyModifiedPropertiesWithoutUndo(){return true;} }
    public class EditorBuildSettingsScene { public EditorBuildSettingsScene(string p,bool e){} }
    public static class EditorBuildSettings { public static EditorBuildSettingsScene[] scenes; }
}
namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene }
    public enum NewSceneMode { Single }
    public static class EditorSceneManager {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup s, NewSceneMode m){return new UnityEngine.SceneManagement.Scene();}
        public static bool SaveScene(UnityEngine.SceneManagement.Scene s,string p){return true;} }
}
