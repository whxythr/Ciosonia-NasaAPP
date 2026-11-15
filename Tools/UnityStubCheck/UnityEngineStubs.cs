// Minimal compile-time stand-in for the parts of the UnityEngine API used by SpaceSim.Runtime.
// Purpose: type-check the Unity-layer C# outside the Editor (names, types, usings, overloads of OUR code).
// Signatures follow the real Unity API; there are no implementations. This does NOT replace compiling in Unity.
#pragma warning disable CS0067, CS0649, CS0414, CS1591
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public int GetInstanceID() => 0;
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static T Instantiate<T>(T original, Transform parent) where T : Object => original;
        public static T Instantiate<T>(T original, Transform parent, bool instantiateInWorldSpace) where T : Object => original;
        public static T Instantiate<T>(T original) where T : Object => original;
        public static implicit operator bool(Object o) => o != null;
        public static void DontDestroyOnLoad(Object target) { }
        public static T FindFirstObjectByType<T>() where T : Object => null;
        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object => new T[0];
    }

    public enum FindObjectsSortMode { None = 0, InstanceID = 1 }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
        public T GetComponent<T>() => default;
        public T GetComponentInChildren<T>() => default;
        public T GetComponentInParent<T>() => default;
        public T[] GetComponentsInChildren<T>() => new T[0];
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
    }

    public class Behaviour : Component { public bool enabled { get; set; } }
    public class MonoBehaviour : Behaviour
    {
        public bool isActiveAndEnabled => true;
        public Coroutine StartCoroutine(System.Collections.IEnumerator routine) => null;
    }
    public sealed class Coroutine { }
    public sealed class WaitForEndOfFrame { }
    public static class ScreenCapture { public static Texture2D CaptureScreenshotAsTexture() => null; }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject => null; }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public int layer { get; set; }
        public Transform transform => null;
        public bool activeSelf => true;
        public bool activeInHierarchy => true;
        public string tag { get; set; }
        public T AddComponent<T>() where T : Component => null;
        public T GetComponent<T>() => default;
        public T[] GetComponentsInChildren<T>() => new T[0];
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
        public T GetComponentInChildren<T>() => default;
        public void SetActive(bool value) { }
        public static GameObject CreatePrimitive(PrimitiveType type) => null;
        public static GameObject Find(string name) => null;
    }

    public class Transform : Component, System.Collections.IEnumerable
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion localRotation { get; set; }
        public Transform parent { get; set; }
        public void SetParent(Transform p, bool worldPositionStays) { }
        public System.Collections.IEnumerator GetEnumerator() => null;
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { }
        public Vector3 TransformPoint(Vector3 p) => p;
        public Vector3 InverseTransformPoint(Vector3 p) => p;
        public Vector3 InverseTransformDirection(Vector3 d) => d;
        public Vector3 TransformDirection(Vector3 d) => d;
        public void Rotate(Vector3 axis, float angle, Space relativeTo) { }
        public int childCount => 0;
        public Transform GetChild(int index) => null;
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a;
        public static Vector2 operator +(Vector2 a, Vector2 b) => a;
        public static Vector2 operator -(Vector2 a, Vector2 b) => a;
        public static Vector2 operator *(Vector2 a, float d) => a;
        public static float Distance(Vector2 a, Vector2 b) => 0f;
        public static implicit operator Vector2(Vector3 v) => default;
        public static bool operator ==(Vector2 a, Vector2 b) => true;
        public static bool operator !=(Vector2 a, Vector2 b) => false;
        public static implicit operator Vector3(Vector2 v) => default;
        public override bool Equals(object o) => false;
        public override int GetHashCode() => 0;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 zero => default;
        public static Vector3 one => default;
        public static Vector3 up => default;
        public static Vector3 right => default;
        public static Vector3 forward => default;
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;
        public Vector3 normalized => this;
        public static Vector3 Cross(Vector3 a, Vector3 b) => a;
        public static float Dot(Vector3 a, Vector3 b) => 0f;
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) => v;
        public static Vector3 Min(Vector3 a, Vector3 b) => a;
        public static Vector3 Max(Vector3 a, Vector3 b) => a;
        public static Vector3 operator +(Vector3 a, Vector3 b) => a;
        public static Vector3 operator -(Vector3 a, Vector3 b) => a;
        public static Vector3 operator -(Vector3 a) => a;
        public static Vector3 operator *(Vector3 a, float d) => a;
        public static Vector3 operator /(Vector3 a, float d) => a;
        public static float Distance(Vector3 a, Vector3 b) => 0f;
        public static Vector3 operator *(float d, Vector3 a) => a;
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static implicit operator Vector4(Vector3 v) => default;
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion FromToRotation(Vector3 from, Vector3 to) => default;
        public static Quaternion AngleAxis(float angle, Vector3 axis) => default;
        public static Quaternion Inverse(Quaternion rotation) => rotation;
        public static Quaternion identity => default;
        public static Quaternion LookRotation(Vector3 forward) => default;
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) => default;
        public static Quaternion Euler(Vector3 euler) => default;
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion operator *(Quaternion a, Quaternion b) => a;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public struct Color
    {
        public static Color Lerp(Color a, Color b, float t) => a;
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color black => default;
        public static Color white => default;
        public static Color clear => default;
        public static Color operator *(Color x, Color y) => x;
        public Color linear => this;
        public static Color operator +(Color x, Color y) => x;
        public static Color operator *(Color x, float d) => x;
        public static implicit operator Vector4(Color c) => default;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public float yMax => y + height;
        public float xMax => x + width;
        public Vector2 center => default;
        public bool Contains(Vector2 point) => false;
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { }
        public Vector3 min => default;
        public Vector3 max => default;
        public Vector3 center => default;
        public Vector3 size => default;
        public Vector3 extents => default;
        public void Encapsulate(Bounds b) { }
        public void Encapsulate(Vector3 p) { }
        public float SqrDistance(Vector3 p) => 0f;
    }

    public struct Ray
    {
        public Ray(Vector3 origin, Vector3 direction) { }
        public Vector3 origin => default;
        public Vector3 direction => default;
    }

    public sealed class MaterialPropertyBlock
    {
        public void Clear() { }
        public void SetColor(string name, Color value) { }
    }



    public struct Matrix4x4 { public static Matrix4x4 TRS(Vector3 p, Quaternion q, Vector3 s) => default; public static Matrix4x4 identity => default; public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => default; }

    public static class Mathf
    {
        public static int FloorToInt(float f) => 0;
        public static int CeilToInt(float f) => 0;
        public const float PI = 3.14159265f;
        public const float Deg2Rad = PI / 180f, Rad2Deg = 180f / PI;
        public static float Sqrt(float a) => a;
        public static float Acos(float a) => a;
        public static float Asin(float a) => a;
        public static float Repeat(float t, float length) => t;
        public static float Clamp(float v, float a, float b) => v;
        public static int Clamp(int v, int a, int b) => v;
        public static float Clamp01(float v) => v;
        public static float PerlinNoise(float x, float y) => 0f;
        public static float Max(float a, float b) => a;
        public static int Max(int a, int b) => a;
        public static float Min(float a, float b) => a;
        public static int Min(int a, int b) => a;
        public static float Tan(float a) => a;
        public static float Atan2(float y, float x) => y;
        public static float Abs(float a) => a;
        public static float Exp(float a) => a;
        public static float Lerp(float a, float b, float t) => a;
        public static float InverseLerp(float a, float b, float v) => v;
        public static float LerpAngle(float a, float b, float t) => a;
        public static float Cos(float a) => a;
        public static float Sin(float a) => a;
        public static float Pow(float a, float b) => a;
        public static float Log(float a) => a;
        public static float Log(float a, float p) => a;
        public static float SmoothStep(float from, float to, float t) => t;
        public static float Round(float f) => f;
        public static int RoundToInt(float f) => 0;
        public static bool Approximately(float a, float b) => true;
    }

    public sealed class Mesh : Object
    {
        public Vector3[] vertices { get; set; }
        public Vector3[] normals { get; set; }
        public Color[] colors { get; set; }
        public Vector2[] uv { get; set; }
        public Vector2[] uv2 { get; set; }
        public int[] triangles { get; set; }
        public int vertexCount => 0;
        public Bounds bounds { get; set; }
        public Rendering.IndexFormat indexFormat { get; set; }
        public void SetUVs(int channel, Vector2[] uvs) { }
        public void SetUVs(int channel, Vector3[] uvs) { }
        public void Clear() { }
        public void MarkDynamic() { }
        public void RecalculateBounds() { }
        public void RecalculateNormals() { }
    }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Material sharedMaterial { get; set; }
        public Material[] sharedMaterials { get; set; }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
        public void SetPropertyBlock(MaterialPropertyBlock block) { }
        public Bounds bounds => default;
    }

    public sealed class MeshRenderer : Renderer { }
    public sealed class SkinnedMeshRenderer : Renderer { public Bounds localBounds { get; set; } public Mesh sharedMesh { get; set; } }
    public sealed class MeshFilter : Component { public Mesh sharedMesh { get; set; } }
    public class Collider : Component { }

    public class Material : Object
    {
        public Material(Shader s) { }
        public void EnableKeyword(string keyword) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetVector(string n, Vector4 v) { }
        public void SetFloat(int id, float v) { }
        public void SetFloat(string n, float v) { }
        public void SetColor(string n, Color c) { }
        public void SetColor(int id, Color c) { }
        public void SetTexture(int id, Texture t) { }
        public void SetBuffer(int id, GraphicsBuffer b) { }
        public Shader shader => null;
        public Color GetColor(string n) => default;
        public bool HasProperty(string n) => false;
        public float GetFloat(string n) => 0f;
        public Texture GetTexture(string n) => null;
        public void SetTexture(string n, Texture t) { }
        public Vector2 GetTextureScale(string n) => default;
        public Vector2 GetTextureOffset(string n) => default;
        public void SetTextureScale(string n, Vector2 v) { }
        public void SetTextureOffset(string n, Vector2 v) { }
        public bool IsKeywordEnabled(string keyword) => false;
        public MaterialGlobalIlluminationFlags globalIlluminationFlags { get; set; }
        public bool enableInstancing { get; set; }
    }

    public sealed class Shader : Object
    {
        public static Shader Find(string n) => null;
        public bool isSupported => true;
        public static int PropertyToID(string n) => 0;
        public static void SetGlobalVector(int id, Vector4 v) { }
        public static void SetGlobalFloat(int id, float v) { }
        public static void SetGlobalTexture(int id, Texture t) { }
    }

    public sealed class GraphicsBuffer : IDisposable
    {
        public enum Target { Structured = 16 }
        public GraphicsBuffer(Target target, int count, int stride) { }
        public void SetData(Array data, int managedBufferStartIndex, int graphicsBufferStartIndex, int count) { }
        public void Dispose() { }
    }

    public static class Graphics
    {
        public static void Blit(Texture source, RenderTexture dest) { }
        public static void DrawMeshInstancedProcedural(Mesh mesh, int submeshIndex, Material material, Bounds bounds, int count,
            MaterialPropertyBlock properties = null, Rendering.ShadowCastingMode castShadows = Rendering.ShadowCastingMode.On,
            bool receiveShadows = true, int layer = 0) { }
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count,
            MaterialPropertyBlock properties = null, Rendering.ShadowCastingMode castShadows = Rendering.ShadowCastingMode.On,
            bool receiveShadows = true, int layer = 0) { }
    }

    public static class SystemInfo { public static bool supportsInstancing => true; public static int graphicsMemorySize => 0; }
    public static class QualitySettings { public static int vSyncCount { get; set; } }
    public enum TextClipping { Overflow, Clip }

    public enum LightType { Spot, Directional, Point }
    public enum LightShadows { None, Hard, Soft }

    public sealed class Light : Behaviour
    {
        public LightType type { get; set; }
        public LightShadows shadows { get; set; }
        public float shadowStrength { get; set; }
        public float shadowBias { get; set; }
        public float shadowNormalBias { get; set; }
        public float intensity { get; set; }
        public float range { get; set; }
        public float spotAngle { get; set; }
        public float innerSpotAngle { get; set; }
        public Color color { get; set; }
        public int cullingMask { get; set; }
    }

    public sealed class Material2 { }

    public static class RenderSettings
    {
        public static Light sun { get; set; }
        public static Material skybox { get; set; }
        public static Rendering.AmbientMode ambientMode { get; set; }
        public static Color ambientLight { get; set; }
        public static Rendering.SphericalHarmonicsL2 ambientProbe { get; set; }
    }

    public enum CameraClearFlags { Skybox = 1, SolidColor = 2, Depth = 3, Nothing = 4 }
    [Flags] public enum DepthTextureMode { None = 0, Depth = 1, DepthNormals = 2, MotionVectors = 4 }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
        public int cullingMask { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public bool allowHDR { get; set; }
        public DepthTextureMode depthTextureMode { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public float fieldOfView { get; set; }
        public RenderTexture targetTexture { get; set; }
        public void Render() { }
        public Vector3 WorldToScreenPoint(Vector3 p) => p;
        public Ray ScreenPointToRay(Vector3 p) => default;
    }

    public static class Debug
    {
        public static void Log(object m) { }
        public static void Log(object m, Object ctx) { }
        public static void LogWarning(object m) { }
        public static void LogWarning(object m, Object ctx) { }
        public static void LogError(object m) { }
        public static void LogError(object m, Object ctx) { }
        public static void LogException(Exception e) { }
    }

    public static class Time
    {
        public static float unscaledDeltaTime => 0f;
        public static float deltaTime => 0f;
        public static float unscaledTime => 0f;
        public static float time => 0f;
        public static float realtimeSinceStartup => 0f;
        public static int frameCount => 0;
    }

    public enum RuntimePlatform { WindowsPlayer, WebGLPlayer }
    public enum LogType { Error, Assert, Warning, Log, Exception }

    public static class Application
    {
        public static bool isPlaying => false;
        public static bool isEditor => false;
        public static string dataPath => "";
        public static int targetFrameRate { get; set; }
        public static RuntimePlatform platform => RuntimePlatform.WindowsPlayer;
        public static string persistentDataPath => "";
        public static string streamingAssetsPath => "";
        public static void OpenURL(string url) { }
        public static bool CanStreamedLevelBeLoaded(string levelName) => false;
        public static void Quit() { }
    }

    public static class JsonUtility
    {
        public static string ToJson(object o) => "";
        public static string ToJson(object o, bool pretty) => "";
        public static T FromJson<T>(string json) => default;
    }

    public static class Screen
    {
        public static int width => 0;
        public static int height => 0;
        public static Resolution[] resolutions => new Resolution[0];
        public static Resolution currentResolution => default;
        public static FullScreenMode fullScreenMode { get; set; }
        public static void SetResolution(int width, int height, FullScreenMode mode) { }
    }

    public enum TextureFormat { RGB24 = 3, RGBA32 = 4, RGBAHalf = 17 }
    [Flags] public enum HideFlags { None = 0, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public class Texture : Object
    {
        public TextureWrapMode wrapMode { get; set; }
        public TextureWrapMode wrapModeU { get; set; }
        public TextureWrapMode wrapModeV { get; set; }
        public FilterMode filterMode { get; set; }
        public int width => 0;
        public int height => 0;
    }

    public enum RenderTextureFormat { ARGB32 = 0, Depth = 1, ARGBHalf = 2, Default = 7 }

    public sealed class RenderTexture : Texture
    {
        public static RenderTexture active { get; set; }
        public static RenderTexture GetTemporary(int w, int h, int depth) => null;
        public static void ReleaseTemporary(RenderTexture rt) { }
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format) { }
        public int antiAliasing { get; set; }
        public bool Create() => true;
        public void Release() { }
        public bool IsCreated() => true;
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip, bool linear) { }
        public bool isReadable => false;
        public byte[] EncodeToPNG() => new byte[0];
        public void ReadPixels(Rect source, int destX, int destY) { }
        public bool LoadImage(byte[] data) => false;
        public Color32[] GetPixels32() => new Color32[0];
        public static Texture2D whiteTexture => null;
        public void SetPixel(int x, int y, Color c) { }
        public void SetPixels(Color[] colors) { }
        public void SetPixels32(Color32[] colors) { }
        public void SetPixelData<T>(T[] data, int mipLevel, int sourceDataStartIndex = 0) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
        public int anisoLevel { get; set; }
        public void Apply() { }
    }

    public sealed class Texture3D : Texture
    {
        public Texture3D(int w, int h, int d, Experimental.Rendering.GraphicsFormat f, Experimental.Rendering.TextureCreationFlags flags) { }
        public void SetPixelData<T>(T[] data, int mipLevel, int sourceDataStartIndex = 0) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
    }

    public sealed class Font : Object
    {
        public static Font CreateDynamicFontFromOSFont(string[] names, int size) => null;
        public static string[] GetOSInstalledFontNames() => new string[0];
    }

    public sealed class RectOffset { public RectOffset(int l, int r, int t, int b) { } }

    public sealed class GUIStyleState
    {
        public Texture2D background { get; set; }
        public Texture2D[] scaledBackgrounds { get; set; }
        public Color textColor { get; set; }
    }

    public sealed class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public TextClipping clipping { get; set; }
        public static GUIStyle none => null;
        public RectOffset padding { get; set; }
        public TextAnchor alignment { get; set; }
        public GUIStyleState normal { get; set; }
        public GUIStyleState hover { get; set; }
        public GUIStyleState active { get; set; }
        public GUIStyleState focused { get; set; }
        public GUIStyleState onNormal { get; set; }
        public GUIStyleState onHover { get; set; }
        public GUIStyleState onActive { get; set; }
        public GUIStyleState onFocused { get; set; }
        public RectOffset border { get; set; }
        public RectOffset margin { get; set; }
        public RectOffset overflow { get; set; }
        public float fixedWidth { get; set; }
        public float fixedHeight { get; set; }
        public Vector2 contentOffset { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public Font font { get; set; }
        public bool wordWrap { get; set; }
        public bool richText { get; set; }
        public Vector2 CalcSize(GUIContent content) => default;
        public float CalcHeight(GUIContent content, float width) => 0f;
    }

    public sealed class GUISkin : ScriptableObject
    {
        public Font font { get; set; }
        public GUIStyle box { get; set; }
        public GUIStyle label { get; set; }
        public GUIStyle button { get; set; }
        public GUIStyle textField { get; set; }
        public GUIStyle toggle { get; set; }
        public GUIStyle window { get; set; }
        public GUIStyle horizontalSlider { get; set; }
        public GUIStyle horizontalSliderThumb { get; set; }
        public GUIStyle horizontalScrollbar { get; set; }
        public GUIStyle horizontalScrollbarThumb { get; set; }
        public GUIStyle horizontalScrollbarLeftButton { get; set; }
        public GUIStyle horizontalScrollbarRightButton { get; set; }
        public GUIStyle verticalScrollbar { get; set; }
        public GUIStyle verticalScrollbarThumb { get; set; }
        public GUIStyle verticalScrollbarUpButton { get; set; }
        public GUIStyle verticalScrollbarDownButton { get; set; }
        public GUIStyle scrollView { get; set; }
        public GUISettings settings => null;
    }

    public sealed class GUIContent
    {
        public GUIContent() { }
        public GUIContent(string text) { }
        public static GUIContent none => null;
        public string text { get; set; }
    }
    public sealed class GUILayoutOption { }

    public static class GUI
    {
        public static void DrawTextureWithTexCoords(Rect position, Texture image, Rect texCoords) { }
        public delegate void WindowFunction(int id);
        public static Matrix4x4 matrix { get; set; }
        public static GUISkin skin { get; set; }
        public static Color color { get; set; }
        public static bool enabled { get; set; }
        public static int depth { get; set; }
        public static Color contentColor { get; set; }
        public static void SetNextControlName(string name) { }
        public static string GetNameOfFocusedControl() => "";
        public static void Label(Rect r, string text, GUIStyle s) { }
        public static void Box(Rect r, GUIContent c, GUIStyle s) { }
        public static float HorizontalSlider(Rect r, float value, float leftValue, float rightValue) => value;
        public static void DrawTexture(Rect r, Texture t) { }
        public static void DragWindow() { }
        public static bool Button(Rect r, string text, GUIStyle s) => false;
        public static bool Toggle(Rect r, bool value, string text, GUIStyle s) => value;
        public static string TextField(Rect r, string text, GUIStyle s) => text;
        public static void DrawTexture(Rect r, Texture t, ScaleMode mode, bool alphaBlend) { }
        public static void FocusControl(string name) { }
        public static Vector2 BeginScrollView(Rect position, Vector2 scroll, Rect view) => scroll;
        public static void EndScrollView() { }
    }

    public enum ScaleMode { StretchToFill, ScaleAndCrop, ScaleToFit }

    public static class GUILayout
    {
        public static void BeginArea(Rect r) { }
        public static void EndArea() { }
        public static void BeginVertical(params GUILayoutOption[] o) { }
        public static void BeginVertical(GUIStyle s, params GUILayoutOption[] o) { }
        public static void EndVertical() { }
        public static void BeginHorizontal(params GUILayoutOption[] o) { }
        public static void BeginHorizontal(GUIStyle s, params GUILayoutOption[] o) { }
        public static void EndHorizontal() { }
        public static void Label(string t, GUIStyle s, params GUILayoutOption[] o) { }
        public static void Space(float px) { }
        public static void FlexibleSpace() { }
        public static bool Button(string t, GUIStyle s, params GUILayoutOption[] o) => false;
        public static bool Toggle(bool v, string t, params GUILayoutOption[] o) => v;
        public static bool Toggle(bool v, string t, GUIStyle s, params GUILayoutOption[] o) => v;
        public static string TextField(string t, GUIStyle s, params GUILayoutOption[] o) => t;
        public static Rect Window(int id, Rect r, GUI.WindowFunction f, string title, params GUILayoutOption[] o) => r;
        public static GUILayoutOption Width(float w) => null;
        public static GUILayoutOption Height(float h) => null;
        public static float HorizontalSlider(float value, float leftValue, float rightValue, params GUILayoutOption[] o) => value;
        public static Vector2 BeginScrollView(Vector2 p, params GUILayoutOption[] o) => p;
        public static void EndScrollView() { }
    }

    public static class GUIUtility { public static int keyboardControl { get; set; } }

    public enum EventType { MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5, ScrollWheel = 6, Repaint = 7, Layout = 8, Ignore = 11, Used = 12 }

    public sealed class Event
    {
        public static Event current { get; set; }
        public EventType type { get; set; }
        public Vector2 mousePosition { get; set; }
        public int button { get; set; }
        public void Use() { }
        public KeyCode keyCode { get; set; }
    }

    // Real Unity values: code relies on the contiguous ranges A…Z, Alpha0…Alpha9, Keypad0…Keypad9 and F1…F12.
    public enum KeyCode
    {
        None = 0, Backspace = 8, Tab = 9, Return = 13, Escape = 27, Space = 32, Quote = 39, Comma = 44, Minus = 45, Period = 46, Slash = 47,
        Alpha0 = 48, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
        Semicolon = 59, Equals = 61, LeftBracket = 91, Backslash = 92, RightBracket = 93, BackQuote = 96,
        A = 97, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Delete = 127,
        Keypad0 = 256, Keypad1, Keypad2, Keypad3, Keypad4, Keypad5, Keypad6, Keypad7, Keypad8, Keypad9,
        KeypadPeriod = 266, KeypadDivide = 267, KeypadMultiply = 268, KeypadMinus = 269, KeypadPlus = 270, KeypadEnter = 271,
        UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276, Insert = 277, Home = 278, End = 279, PageUp = 280, PageDown = 281,
        F1 = 282, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
        RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306, RightAlt = 307, LeftAlt = 308,
    }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode k) => false;
        public static bool GetKey(KeyCode k) => false;
        public static bool GetMouseButton(int b) => false;
        public static bool GetMouseButtonDown(int b) => false;
        public static Vector3 mousePosition => default;
        public static Vector2 mouseScrollDelta => default;
    }

    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute { public MinAttribute(float m) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int o) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class ContextMenu : Attribute { public ContextMenu(string n) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute(int a, int b) { } }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
    }
}

namespace UnityEngine.Experimental.Rendering
{
    public enum GraphicsFormat { R8G8B8A8_UNorm = 8 }
    [Flags] public enum TextureCreationFlags { None = 0, MipChain = 1 }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public enum IndexFormat { UInt16, UInt32 }
    public enum AmbientMode { Skybox, Trilight, Flat = 3, Custom = 4 }
    public struct SphericalHarmonicsL2 { public void Clear() { } public void AddAmbientLight(Color color) { } }
    public class RenderPipelineAsset : ScriptableObject { }
    public abstract class RenderPipeline
    {
        public class StandardRequest
        {
            public UnityEngine.RenderTexture destination;
            public int mipLevel;
            public int slice;
        }
        public static bool SupportsRenderRequest<RequestData>(UnityEngine.Camera camera, RequestData data) => false;
        public static void SubmitRenderRequest<RequestData>(UnityEngine.Camera camera, RequestData data) { }
    }
    public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline => null; }
}

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class UnityTestAttribute : Attribute { }
    public static class LogAssert { public static void Expect(UnityEngine.LogType type, System.Text.RegularExpressions.Regex message) { } }
}

namespace UnityEngine
{
    public static class GUILayoutUtility { public static Rect GetRect(float width, float height, params GUILayoutOption[] o) => default; public static Rect GetLastRect() => default; }
}

// ---- Added for the surface robot (RobotBodyModel / RobotVisorOverlay) ----
namespace UnityEngine
{
    public class RuntimeAnimatorController : Object { }
    public enum AnimatorControllerParameterType { Float = 1, Int = 3, Bool = 4, Trigger = 9 }
    public enum AnimatorCullingMode { AlwaysAnimate = 0, CullUpdateTransforms = 1, CullCompletely = 2 }
    public sealed class AnimatorControllerParameter
    {
        public string name { get; set; }
        public int nameHash => 0;
        public AnimatorControllerParameterType type { get; set; }
    }
    public sealed class Animator : Behaviour
    {
        public bool applyRootMotion { get; set; }
        public AnimatorCullingMode cullingMode { get; set; }
        public RuntimeAnimatorController runtimeAnimatorController { get; set; }
        public AnimatorControllerParameter[] parameters => new AnimatorControllerParameter[0];
        public float speed { get; set; }
        public bool isActiveAndEnabled => true;
        public static int StringToHash(string name) => 0;
        public void SetFloat(int id, float value) { }
        public void SetBool(int id, bool value) { }
        public bool HasState(int layerIndex, int stateID) => false;
        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration, int layer) { }
    }
    public sealed class AnimationState { public string name { get; set; } public float speed { get; set; } }
    public sealed class Animation : Behaviour, System.Collections.IEnumerable
    {
        public AnimationState this[string name] => null;
        public void CrossFade(string animation, float fadeLength) { }
        public System.Collections.IEnumerator GetEnumerator() { yield break; }
    }
    public class Joint : Component { }
    public sealed class CharacterController : Collider { }
    public sealed class Rigidbody : Component { }
    public sealed class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public float volume { get; set; }
        public float time { get; set; }
        public bool loop { get; set; }
        public bool playOnAwake { get; set; }
        public bool ignoreListenerPause { get; set; }
        public float spatialBlend { get; set; }
        public int priority { get; set; }
        public bool isPlaying => false;
        public void Play() { }
        public void Stop() { }
        public void Pause() { }
        public void UnPause() { }
    }
    public sealed class AudioClip : Object { public float length => 0f; }
    public sealed class AudioListener : Behaviour { public static bool pause { get; set; } }
    public enum AudioType { UNKNOWN = 0, MPEG = 13, OGGVORBIS = 14, WAV = 20 }
    public sealed class ParticleSystemRenderer : Renderer { }
    public sealed class TrailRenderer : Renderer { }
    public sealed class LineRenderer : Renderer { }
    [Flags] public enum MaterialGlobalIlluminationFlags { None = 0, RealtimeEmissive = 1, BakedEmissive = 2, EmissiveIsBlack = 4 }
    public enum Space { World = 0, Self = 1 }
}
namespace UnityEngine
{
    public static class Random
    {
        public static float value => 0f;
        public static float Range(float min, float max) => min;
        public static int Range(int minInclusive, int maxExclusive) => minInclusive;
    }
}

namespace UnityEngine
{
    public class YieldInstruction { }
    public class AsyncOperation : YieldInstruction
    {
        public bool isDone => true;
        public float progress => 1f;
        public bool allowSceneActivation { get; set; }
    }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler : IDisposable { public void Dispose() { } }
    public sealed class DownloadHandlerAudioClip : DownloadHandler
    {
        public bool compressed { get; set; }
        public bool streamAudio { get; set; }
        public AudioClip audioClip => null;
        public static AudioClip GetContent(UnityWebRequest www) => null;
    }
    public sealed class UnityWebRequestAsyncOperation : AsyncOperation { public UnityWebRequest webRequest => null; }
    public class UnityWebRequest : IDisposable
    {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }
        public Result result => Result.Success;
        public string error => null;
        public DownloadHandler downloadHandler { get; set; }
        public UnityWebRequestAsyncOperation SendWebRequest() => null;
        public void Dispose() { }
    }
    public static class UnityWebRequestMultimedia
    {
        public static UnityWebRequest GetAudioClip(string uri, AudioType audioType) => null;
    }
}
