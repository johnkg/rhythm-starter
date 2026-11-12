using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class SerializeField : Attribute {}
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s){} }
    public class HeaderAttribute : Attribute { public HeaderAttribute(string s){} }
    public class RequireComponent : Attribute { public RequireComponent(Type t){} }
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int i){} }
    public class Object {
        public static T Instantiate<T>(T original, Transform parent) where T : Object { return original; }
        public static void Destroy(Object o) {}
        public static void DestroyImmediate(Object o) {}
    }
    public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} }
    public struct Color { public float r,g,b,a; public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;}
        public static Color white = new Color(1,1,1,1); public static Color yellow = new Color(1,1,0,1); }
    public struct Rect { public Rect(float a,float b,float c,float d){} }
    public class Sprite : Object { public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu){return null;} }
    public struct Vector2 { public float x,y; public Vector2(float a,float b){x=a;y=b;} }
    public struct Color32 { public Color32(byte r,byte g,byte b,byte a){} }
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Point }
    public enum CameraClearFlags { SolidColor }
    public class Texture2D : Object { public int width=4, height=4; public static Texture2D whiteTexture; public Texture2D(int w,int h,TextureFormat f,bool m){} public void SetPixels32(Color32[] c){} public byte[] EncodeToPNG(){return null;} }
    public class AudioListener : Component {}
    public class Camera : Component { public static Camera main; public bool orthographic; public float orthographicSize; public Color backgroundColor; public CameraClearFlags clearFlags; public Vector3 WorldToScreenPoint(Vector3 v){return v;} }
    public class Transform : Component {
        public Vector3 localPosition, localScale, position;
        public void SetParent(Transform p, bool worldPositionStays) {}
    }
    public class Component : Object {
        public Transform transform { get { return null; } }
        GameObject goCache; public GameObject gameObject { get { return goCache ?? (goCache = new GameObject("")); } }
        public T GetComponent<T>() { return default(T); }
    }
    public class Behaviour : Component { public bool enabled; }
    public class YieldInstruction {}
    public class WaitForSecondsRealtime : YieldInstruction { public WaitForSecondsRealtime(float s){} }
    public class AsyncOperation : YieldInstruction {}
    public class ResourceRequest : AsyncOperation { public Object asset; }
    public class Coroutine {}
    public class MonoBehaviour : Behaviour { public Coroutine StartCoroutine(System.Collections.IEnumerator e){return null;} }
    public class GameObject : Object { public bool activeSelf = true; public void SetActive(bool a){ activeSelf = a; } public string tag; public T GetComponent<T>() { return default(T); } Transform tr = new Transform(); public GameObject(string n){} public Transform transform { get { return tr; } } public T AddComponent<T>() where T : Component { return (T)Activator.CreateInstance(typeof(T)); } }
    public class SpriteRenderer : Component { public Sprite sprite; public Color color; public int sortingOrder; }
    public class AudioClip : Object { public float length; public int frequency; public static AudioClip Create(string n,int a,int b,int c,bool d){return new AudioClip();} public bool SetData(float[] d,int o){return true;} }
    public class AudioSource : Component { public AudioClip clip; public bool loop, playOnAwake; public int timeSamples; public float time, volume; public float spatialBlend; public static int PlayCount; public static AudioClip LastClip; public void Play(){ PlayCount++; PlayCalls++; LastClip = clip; isPlaying = true; } public int PlayCalls, PauseCalls, UnPauseCalls; public double ScheduledAt = -1; public bool isPlaying; public void PlayScheduled(double t){ ScheduledAt = t; } public void Stop(){} public void Pause(){ PauseCalls++; } public void UnPause(){ UnPauseCalls++; } }
    public static class AudioSettings { public static double dspTime; public static int outputSampleRate; }
    public class TextAsset : Object { public string text; }
    public static class Resources { public static T Load<T>(string p) where T : Object { return null; } public static ResourceRequest LoadAsync<T>(string p) where T : Object { return null; } public static void UnloadAsset(Object o){} }
    public static class JsonUtility { public static T FromJson<T>(string s){return default(T);} public static string ToJson(object o,bool pretty){return "";} }
    public static class Debug { public static void Log(object o){} public static void LogError(object o){} public static void LogWarning(object o){} }
    public static class Application { public static string persistentDataPath; public static void Quit(){} }
    public static class PlayerPrefs {
        static Dictionary<string,object> d = new Dictionary<string,object>();
        public static float GetFloat(string k,float def){ return d.ContainsKey(k)?(float)d[k]:def; } public static void SetFloat(string k,float v){ d[k]=v; }
        public static int GetInt(string k,int def){ return d.ContainsKey(k)?(int)d[k]:def; } public static void SetInt(string k,int v){ d[k]=v; }
        public static string GetString(string k,string def){ return d.ContainsKey(k)?(string)d[k]:def; } public static void SetString(string k,string v){ d[k]=v; }
        public static void Save(){} }
    public static class Time { public static double realtimeSinceStartupAsDouble; public static float unscaledTime; public static float unscaledDeltaTime; public static int frameCount; }
    public static class Screen { public static int width, height; }
    public static class Mathf {
        public const float PI = 3.14159f;
        public static float Max(float a,float b){return a>b?a:b;} public static int Max(int a,int b){return a>b?a:b;}
        public static float Clamp(float v,float a,float b){return v<a?a:(v>b?b:v);} public static int Clamp(int v,int a,int b){return v<a?a:(v>b?b:v);} public static int CeilToInt(float f){return (int)Math.Ceiling(f);}
        public static float Abs(float a){return a<0?-a:a;} public static float Round(float a){return (float)Math.Round(a);} public static float Clamp01(float a){return a<0?0:(a>1?1:a);} public static float Min(float a,float b){return a<b?a:b;}
        public static int RoundToInt(float a){return (int)a;} public static float Sin(float a){return (float)Math.Sin(a);} public static float Exp(float a){return (float)Math.Exp(a);} public static float Sqrt(float a){return (float)Math.Sqrt(a);}
    }
    public enum FontStyle { Normal, Bold }
    public enum TextAnchor { MiddleCenter }
    public class GUIContent { public static GUIContent none; }
    public class GUIStyle { public int fontSize; public FontStyle fontStyle; public TextAnchor alignment; public GUIStyle(){} public GUIStyle(GUIStyle o){} }
    public class GUISkin { public GUIStyle label; }
    public static class GUI { public static GUISkin skin; public static Color color; public static int depth;
        public static void Label(Rect r,string s){} public static void Label(Rect r,string s,GUIStyle st){}
        public static void Box(Rect r,string s){} public static void Box(Rect r,GUIContent c){} }
    public static class GUILayout { public static void BeginArea(Rect r){} public static void EndArea(){} public static void Label(string s,GUIStyle st){} public static void Space(float f){} }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager { public static void LoadScene(string n){} public static Scene GetActiveScene(){return new Scene();} }
}
namespace UnityEngine.InputSystem
{
    public enum InputActionType { Button }
    public struct CallbackContext { public double time; }
    public class InputAction { public InputAction(string n, InputActionType t){} public void AddBinding(string p){}
        public event Action<CallbackContext> performed; public event Action<CallbackContext> canceled;
        public void Enable(){} public void Disable(){} public void Dispose(){} }
    public class ButtonControl { public bool wasPressedThisFrame; }
    public class KeyControl : ButtonControl { public string name; }
    public class Keyboard { public static Keyboard current; public ButtonControl rKey; public KeyControl escapeKey, enterKey; public KeyControl[] allKeys; }
}
