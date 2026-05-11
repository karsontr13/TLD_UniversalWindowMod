using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MelonLoader;
using UnityEngine;
using UnityEngine.Video;
using Il2Cpp;
using Il2CppInterop.Runtime.Injection;

[assembly: MelonInfo(typeof(UniversalWindowMod.WindowModMain), "TLD Universal Dynamic Windows", "2.4.0", "KarsonTR")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace UniversalWindowMod
{
    public class DynamicWindowController : MonoBehaviour
    {
        public DynamicWindowController(IntPtr ptr) : base(ptr) { }

        public int windowIndex;
        public string sceneRootPath;
        public MeshRenderer originalRenderer;

        private MeshRenderer myRenderer;
        private Material myMaterial;
        private Weather weather;
        private TimeOfDay timeOfDay;
        private Wind wind; // Added wind component for dynamic weather handling

        private VideoPlayer vp1;
        private VideoPlayer vp2;
        private RenderTexture rt1;
        private RenderTexture rt2;
        private bool usingVp1 = true;

        private VideoPlayer ActiveVp => usingVp1 ? vp1 : vp2;
        private VideoPlayer NextVp => usingVp1 ? vp2 : vp1;
        private RenderTexture ActiveRt => usingVp1 ? rt1 : rt2;
        private RenderTexture NextRt => usingVp1 ? rt2 : rt1;

        private string playingWeather = "";
        private string targetWeather = "";

        private bool playingIsNightVariant = false;
        private bool targetIsNightVariant = false;

        // Track the full path of the currently playing video to prevent unnecessary transitions
        private string currentPlayingVideoPath = "";

        private float weatherCheckTimer = 0f;

        private GameObject fadeScreen;
        private MeshRenderer fadeRenderer;
        private Material fadeMaterial;

        private float transitionDuration = 15.0f;
        private float fadeTimer = 0f;
        private bool isTransitioning = false;

        private bool isWaitingToPrepare = false;
        private float prepareDelayTimer = 0f;
        private string pendingVideoPath = "";
        private bool isInitialLoad = true;

        private float lastGameHour = -1f;

        void Start()
        {
            myRenderer = GetComponent<MeshRenderer>();
            myMaterial = myRenderer.material;
            weather = GameManager.GetWeatherComponent();
            timeOfDay = GameManager.GetTimeOfDayComponent();
            wind = GameManager.GetWindComponent(); // Retrieve the wind component from the game manager

            vp1 = SetupVideoPlayer(out rt1);
            vp2 = SetupVideoPlayer(out rt2);

            myMaterial.mainTextureScale = new Vector2(0.85f, 0.85f);

            CreateFadeScreen();

            if (weather != null)
            {
                playingWeather = GetCleanWeather();
                targetWeather = playingWeather;

                bool isNightTime = timeOfDay != null && timeOfDay.IsNight();
                string currentWindSuffix = GetWindSuffix();

                string loadPath = ResolveVideoPath(playingWeather, isNightTime, currentWindSuffix, out playingIsNightVariant);
                currentPlayingVideoPath = loadPath;

                if (!string.IsNullOrEmpty(loadPath))
                {
                    pendingVideoPath = loadPath;
                    isWaitingToPrepare = true;
                    isInitialLoad = true;
                    prepareDelayTimer = windowIndex * 1.0f;
                }
                else
                {
                    PlayStandardWeather(playingWeather, loadPath, playingIsNightVariant);
                }
            }

            if (timeOfDay != null) lastGameHour = timeOfDay.GetHour() + (timeOfDay.GetMinutes() / 60f);
        }

        // Determine the filename suffix based on wind intensity
        private string GetWindSuffix()
        {
            if (wind == null) return "";

            // Get wind speed in MPH
            // Note: Depending on the TLD version, this might be GetWindSpeedMPH() instead of GetSpeedMPH().
            float mph = wind.GetSpeedMPH();

            if (mph >= 25f) return "_VeryWindy"; // Severe storm/High wind
            if (mph >= 12f) return "_Windy";     // Normal wind

            return ""; // Calm weather
        }

        // UPDATED: Added naming tolerance for the ClearAurora weather stage
        private string ResolveVideoPath(string wStage, bool isNightTime, string windSuffix, out bool isNightVariant)
        {
            isNightVariant = false;
            List<string> suffixesToTry = new List<string>();

            // IF WEATHER IS AURORA: The recording system might have added the '_Night' suffix or left it plain. Scan all possibilities.
            if (wStage == "ClearAurora")
            {
                if (!string.IsNullOrEmpty(windSuffix)) suffixesToTry.Add($"_Night{windSuffix}");
                suffixesToTry.Add("_Night");
                if (!string.IsNullOrEmpty(windSuffix)) suffixesToTry.Add($"{windSuffix}");
                suffixesToTry.Add(""); // Standard/Fallback
            }
            // NIGHT CHECK FOR OTHER WEATHER CONDITIONS
            else if (isNightTime)
            {
                if (!string.IsNullOrEmpty(windSuffix)) suffixesToTry.Add($"_Night{windSuffix}");
                suffixesToTry.Add("_Night");
            }
            // DAY CHECK
            else
            {
                if (!string.IsNullOrEmpty(windSuffix)) suffixesToTry.Add($"{windSuffix}");
                suffixesToTry.Add(""); // Standard/Fallback
            }

            // Check the list sequentially. If a windy video is missing, it automatically falls back to normal.
            foreach (string suffix in suffixesToTry)
            {
                string webmPath = Path.GetFullPath(Path.Combine(sceneRootPath, wStage, $"Window_{windowIndex}{suffix}.webm"));
                if (File.Exists(webmPath)) { isNightVariant = suffix.Contains("Night"); return webmPath; }

                string mp4Path = Path.GetFullPath(Path.Combine(sceneRootPath, wStage, $"Window_{windowIndex}{suffix}.mp4"));
                if (File.Exists(mp4Path)) { isNightVariant = suffix.Contains("Night"); return mp4Path; }
            }

            return string.Empty;
        }

        private void CreateFadeScreen()
        {
            fadeScreen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fadeScreen.name = $"FadeScreen_{windowIndex}";
            fadeScreen.transform.SetParent(this.transform, false);
            fadeScreen.transform.localPosition = new Vector3(0, 0, -0.0005f);
            fadeScreen.transform.localRotation = Quaternion.identity;
            fadeScreen.transform.localScale = Vector3.one;

            Destroy(fadeScreen.GetComponent("MeshCollider"));

            fadeRenderer = fadeScreen.GetComponent<MeshRenderer>();
            Shader transparentShader = Shader.Find("Unlit/Transparent");
            if (transparentShader == null) transparentShader = Shader.Find("UI/Default");

            fadeMaterial = new Material(transparentShader);
            fadeMaterial.mainTextureScale = myMaterial.mainTextureScale;

            Color startColor = Color.white;
            startColor.a = 0f;
            fadeMaterial.color = startColor;

            fadeRenderer.material = fadeMaterial;
            fadeRenderer.enabled = false;
        }

        private VideoPlayer SetupVideoPlayer(out RenderTexture rt)
        {
            VideoPlayer vp = gameObject.AddComponent<VideoPlayer>();
            vp.playOnAwake = false;
            vp.isLooping = true;
            vp.renderMode = VideoRenderMode.RenderTexture;
            vp.audioOutputMode = VideoAudioOutputMode.None;
            vp.aspectRatio = VideoAspectRatio.Stretch;
            vp.waitForFirstFrame = false;

            rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.filterMode = FilterMode.Bilinear;
            rt.Create();

            vp.targetTexture = rt;
            return vp;
        }

        void Update()
        {
            if (timeOfDay == null) return;

            float currentNumericHour = timeOfDay.GetHour() + (timeOfDay.GetMinutes() / 60f);
            float hourDelta = Mathf.Abs(currentNumericHour - lastGameHour);
            if (hourDelta > 0.15f && hourDelta < 23.85f)
            {
                ForceImmediateUpdate();
            }
            lastGameHour = currentNumericHour;

            bool isNightTime = timeOfDay.IsNight();

            weatherCheckTimer += Time.deltaTime;
            if (weatherCheckTimer >= 2.0f && !isTransitioning && !isWaitingToPrepare)
            {
                weatherCheckTimer = 0f;

                if (originalRenderer != null && originalRenderer.enabled)
                    originalRenderer.enabled = false;

                if (weather != null)
                {
                    string gameWeather = GetCleanWeather();
                    string currentWindSuffix = GetWindSuffix();

                    // Determine what the target video SHOULD be
                    string targetVideoPath = ResolveVideoPath(gameWeather, isNightTime, currentWindSuffix, out bool targetWillBeNight);

                    // Trigger a transition if the target video is different from the currently playing one 
                    // (e.g., if wind changed and a specific video exists)
                    if (targetVideoPath != currentPlayingVideoPath || gameWeather != playingWeather)
                    {
                        TriggerWeatherChange(gameWeather, targetVideoPath, targetWillBeNight);
                    }
                }
            }

            if (isWaitingToPrepare)
            {
                if (prepareDelayTimer > 0)
                {
                    prepareDelayTimer -= Time.deltaTime;
                }
                else
                {
                    isWaitingToPrepare = false;
                    StartPreparingNextVideo();
                }
            }

            if (!isWaitingToPrepare && !isTransitioning && ActiveVp != null)
            {
                if (ActiveVp.isPrepared && !ActiveVp.isPlaying && !string.IsNullOrEmpty(ActiveVp.url))
                    ActiveVp.Play();
            }

            Color playingTint = GetTintForWeather(playingWeather, timeOfDay, playingIsNightVariant);
            myMaterial.color = playingTint;

            if (isTransitioning && !isWaitingToPrepare)
            {
                fadeTimer += Time.deltaTime;
                float alphaProgress = Mathf.Clamp01(fadeTimer / transitionDuration);

                Color targetTint = GetTintForWeather(targetWeather, timeOfDay, targetIsNightVariant);
                targetTint.a = alphaProgress;
                fadeMaterial.color = targetTint;

                if (alphaProgress >= 1f) CompleteTransition();
            }

            UpdateParallax();
        }

        private Color GetTintForWeather(string wStage, TimeOfDay tod, bool isNightVariantLoaded)
        {
            if (wStage == "ClearAurora") return new Color(1.2f, 1.2f, 1.3f, 1f);
            if (isNightVariantLoaded) return Color.white;

            return CalculateTintColor(tod, wStage);
        }

        private void StartPreparingNextVideo()
        {
            if (isInitialLoad)
            {
                isInitialLoad = false;
                ActiveVp.url = pendingVideoPath;
                ActiveVp.isLooping = true;
                ActiveVp.prepareCompleted += new Action<VideoPlayer>(OnInitVideoPrepared);
                ActiveVp.Prepare();
            }
            else
            {
                isTransitioning = true;
                fadeTimer = 0f;
                NextVp.url = pendingVideoPath;
                NextVp.isLooping = true;
                NextVp.prepareCompleted += new Action<VideoPlayer>(OnVideoPrepared);
                NextVp.Prepare();
            }
        }

        private void ForceImmediateUpdate()
        {
            string newWeather = GetCleanWeather();
            bool isNightTime = timeOfDay != null && timeOfDay.IsNight();
            string currentWind = GetWindSuffix();

            string newPath = ResolveVideoPath(newWeather, isNightTime, currentWind, out bool expectedNightVariant);

            // If the currently playing video and the target video are identical, do nothing to save resources
            if (playingWeather == newWeather && currentPlayingVideoPath == newPath && !isTransitioning && !isWaitingToPrepare)
                return;

            isTransitioning = false;
            isWaitingToPrepare = false;
            prepareDelayTimer = 0f;
            fadeTimer = 0f;
            fadeRenderer.enabled = false;

            vp1.Stop();
            vp2.Stop();
            vp1.url = "";
            vp2.url = "";

            playingWeather = newWeather;
            targetWeather = newWeather;
            playingIsNightVariant = expectedNightVariant;
            targetIsNightVariant = expectedNightVariant;

            PlayStandardWeather(newWeather, newPath, expectedNightVariant);
        }

        // UPDATED: Now takes the targeted video path directly as a parameter
        private void TriggerWeatherChange(string newWeather, string targetVideoPath, bool targetNightVariant)
        {
            // FIX: If a new trigger occurs while a soft transition is ongoing, wait for the current 
            // transition to complete instead of updating abruptly. This prevents jarring screen cuts.
            if (isTransitioning || isWaitingToPrepare)
            {
                return;
            }

            targetWeather = newWeather;
            targetIsNightVariant = targetNightVariant;

            if (!string.IsNullOrEmpty(targetVideoPath))
            {
                pendingVideoPath = targetVideoPath;
                isWaitingToPrepare = true;
                prepareDelayTimer = windowIndex * 2.5f;
            }
            else
            {
                PlayStandardWeather(newWeather, targetVideoPath, targetNightVariant);
            }
        }

        private void OnInitVideoPrepared(VideoPlayer source)
        {
            source.prepareCompleted -= new Action<VideoPlayer>(OnInitVideoPrepared);
            myMaterial.mainTexture = ActiveRt;
            source.Play();
        }

        private void OnVideoPrepared(VideoPlayer source)
        {
            if (!isTransitioning) return;
            source.prepareCompleted -= new Action<VideoPlayer>(OnVideoPrepared);

            fadeRenderer.enabled = true;
            fadeMaterial.mainTexture = NextRt;
            source.Play();
        }

        private void CompleteTransition()
        {
            isTransitioning = false;
            fadeRenderer.enabled = false;
            usingVp1 = !usingVp1;
            myMaterial.mainTexture = ActiveRt;

            playingWeather = targetWeather;
            playingIsNightVariant = targetIsNightVariant;
            currentPlayingVideoPath = NextVp.url; // New video is playing, save its path!

            NextVp.Stop();
            NextVp.url = string.Empty;
        }

        // UPDATED: Now takes the targeted video path directly as a parameter
        private void PlayStandardWeather(string targetWeatherStage, string absoluteVideoPath, bool isNightVariant)
        {
            currentPlayingVideoPath = absoluteVideoPath; // Save the currently playing path

            if (string.IsNullOrEmpty(absoluteVideoPath))
            {
                ActiveVp.Stop();
                ActiveVp.url = string.Empty;
                string fallbackPath = Path.Combine(sceneRootPath, targetWeatherStage, $"Window_{windowIndex}.png");
                string absoluteFallbackPath = Path.GetFullPath(fallbackPath);

                if (!File.Exists(absoluteFallbackPath))
                {
                    fallbackPath = Path.Combine(sceneRootPath, "Clear", $"Window_{windowIndex}.png");
                    absoluteFallbackPath = Path.GetFullPath(fallbackPath);
                }

                if (File.Exists(absoluteFallbackPath))
                    myMaterial.mainTexture = LoadStaticTexture(absoluteFallbackPath);
            }
            else
            {
                ActiveVp.url = absoluteVideoPath;
                ActiveVp.Play();
                myMaterial.mainTexture = ActiveRt;
            }
        }

        private string GetCleanWeather()
        {
            string w = weather.GetWeatherStage().ToString();
            if (w == "ClearAurora" || w.Contains("Aurora")) return "ClearAurora";
            return w.Contains("Clear") ? "Clear" : w;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus && ActiveVp != null && !string.IsNullOrEmpty(ActiveVp.url)) ActiveVp.Play();
            if (hasFocus && isTransitioning && NextVp != null) NextVp.Play();
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus && ActiveVp != null && !string.IsNullOrEmpty(ActiveVp.url)) ActiveVp.Play();
            if (!pauseStatus && isTransitioning && NextVp != null) NextVp.Play();
        }

        void OnDestroy()
        {
            if (vp1 != null) { vp1.prepareCompleted -= new Action<VideoPlayer>(OnVideoPrepared); vp1.prepareCompleted -= new Action<VideoPlayer>(OnInitVideoPrepared); }
            if (vp2 != null) { vp2.prepareCompleted -= new Action<VideoPlayer>(OnVideoPrepared); vp2.prepareCompleted -= new Action<VideoPlayer>(OnInitVideoPrepared); }

            if (rt1 != null) { rt1.Release(); Destroy(rt1); }
            if (rt2 != null) { rt2.Release(); Destroy(rt2); }
            if (fadeMaterial != null) Destroy(fadeMaterial);
        }

        private void UpdateParallax()
        {
            var cam = GameManager.GetMainCamera();
            if (cam == null) return;
            Vector3 localPos = transform.InverseTransformPoint(cam.transform.position);
            Vector2 offset = new Vector2((-localPos.x * 0.07f) + 0.075f, (-localPos.y * 0.07f) + 0.075f);
            myMaterial.mainTextureOffset = offset;
            if (isTransitioning && fadeMaterial != null) fadeMaterial.mainTextureOffset = offset;
        }

        private Texture2D LoadStaticTexture(string path)
        {
            byte[] fileData = File.ReadAllBytes(path);
            var il2cppBytes = (Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>)fileData;
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            ImageConversion.LoadImage(tex, il2cppBytes);
            tex.Apply(false, true);
            return tex;
        }

        private Color CalculateTintColor(TimeOfDay tod, string weatherStage)
        {
            float hour = tod.GetHour() + (tod.GetMinutes() / 60f);

            Color dayColor = Color.white;
            Color nightColor = new Color(0.08f, 0.12f, 0.20f, 1f);
            Color dawnColor = new Color(0.85f, 0.50f, 0.30f, 1f);
            Color duskColor = new Color(0.80f, 0.40f, 0.20f, 1f);

            string lowerWeather = weatherStage.ToLower();

            if (lowerWeather.Contains("fog") || lowerWeather.Contains("blizzard") || lowerWeather.Contains("heavysnow"))
            {
                dayColor = new Color(0.75f, 0.75f, 0.80f, 1f);
                dawnColor = new Color(0.40f, 0.45f, 0.50f, 1f);
                duskColor = new Color(0.35f, 0.40f, 0.45f, 1f);
            }
            else if (lowerWeather.Contains("cloudy") || lowerWeather.Contains("overcast"))
            {
                dayColor = new Color(0.9f, 0.9f, 0.9f, 1f);
                dawnColor = Color.Lerp(dawnColor, Color.gray, 0.6f);
                duskColor = Color.Lerp(duskColor, Color.gray, 0.6f);
            }

            if (hour >= 21.5f || hour < 5.0f) return nightColor;

            if (hour >= 5.0f && hour < 7.5f)
            {
                float t = (hour - 5.0f) / 2.5f;
                return Color.Lerp(nightColor, dawnColor, t);
            }

            if (hour >= 7.5f && hour < 10.0f)
            {
                float t = (hour - 7.5f) / 2.5f;
                return Color.Lerp(dawnColor, dayColor, t);
            }

            if (hour >= 10.0f && hour < 15.5f) return dayColor;

            if (hour >= 15.5f && hour < 18.5f)
            {
                float t = (hour - 15.5f) / 3.0f;
                return Color.Lerp(dayColor, duskColor, t);
            }

            if (hour >= 18.5f && hour < 21.5f)
            {
                float t = (hour - 18.5f) / 3.0f;
                return Color.Lerp(duskColor, nightColor, t);
            }

            return dayColor;
        }
    }

    public class WindowModMain : MelonMod
    {
        private string baseTexturesFolderPath;

        private Dictionary<string, Vector3> customSceneRotations = new Dictionary<string, Vector3>()
        {
            { "CampOffice", new Vector3(0, 180, 180) }
        };

        private Dictionary<string, Vector3> specificWindowRotations = new Dictionary<string, Vector3>()
        {
            { "SafeHouseA_5", new Vector3(90, 0, 0) }
        };

        private Dictionary<string, Vector3> specificWindowOffsets = new Dictionary<string, Vector3>();

        public override void OnInitializeMelon()
        {
            ClassInjector.RegisterTypeInIl2Cpp<DynamicWindowController>();
            baseTexturesFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "WindowTextures");

            if (!Directory.Exists(baseTexturesFolderPath))
            {
                Directory.CreateDirectory(baseTexturesFolderPath);
            }
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // 1. Get the original scene name (e.g., SafeHouseA)
            string baseSceneName = sceneName.Replace("_SANDBOX", "").Replace("_DLC01", "").Replace("_WILDLIFE", "");
            string uniqueSceneId = baseSceneName;

            // 2. Detect GUID (e.g., SafeHouseA_a8f9b...)
            if (GameManager.m_SceneTransitionData != null && !string.IsNullOrEmpty(GameManager.m_SceneTransitionData.m_SceneSaveFilenameCurrent))
            {
                uniqueSceneId = GameManager.m_SceneTransitionData.m_SceneSaveFilenameCurrent;
            }

            // 3. Check the GUID folder first; if there's no custom footage, fall back to the Base folder
            string currentSceneFolder = Path.Combine(baseTexturesFolderPath, uniqueSceneId);
            if (!Directory.Exists(currentSceneFolder))
            {
                currentSceneFolder = Path.Combine(baseTexturesFolderPath, baseSceneName);
            }

            if (Directory.Exists(currentSceneFolder))
            {
                // Send both names to prevent positioning issues
                ReplaceWindowsInScene(currentSceneFolder, baseSceneName, uniqueSceneId);
            }
        }

        private void ReplaceWindowsInScene(string sceneFolderPath, string baseSceneName, string uniqueSceneId)
        {
            var allRenderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            List<GameObject> windows = new List<GameObject>();

            foreach (var renderer in allRenderers)
            {
                if (renderer.material != null && renderer.material.name.Contains("GLB_GlowExt"))
                {
                    windows.Add(renderer.gameObject);
                }
            }

            var sortedWindows = windows.OrderBy(w => w.transform.position.y)
                                       .ThenBy(w => w.transform.position.x)
                                       .ThenBy(w => w.transform.position.z)
                                       .ToList();

            int index = 0;
            foreach (var window in sortedWindows)
            {
                bool alreadyModded = false;
                for (int i = 0; i < window.transform.childCount; i++)
                {
                    if (window.transform.GetChild(i).name.StartsWith("CustomWindowScreen"))
                    {
                        alreadyModded = true;
                        break;
                    }
                }

                if (alreadyModded) { index++; continue; }

                // Added uniqueSceneId to the function call
                ModifySingleWindow(window, index, sceneFolderPath, baseSceneName, uniqueSceneId);
                index++;
            }
        }

        private void EradicateAuroraGlows(GameObject windowObject)
        {
            var scripts = windowObject.GetComponents<MonoBehaviour>();
            foreach (var script in scripts)
            {
                if (script.GetIl2CppType().Name.ToLower().Contains("aurora"))
                {
                    UnityEngine.Object.Destroy(script);
                }
            }

            Transform parent = windowObject.transform.parent;
            if (parent != null)
            {
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    Transform sibling = parent.GetChild(i);
                    if (sibling.gameObject != windowObject && !sibling.name.StartsWith("CustomWindowScreen"))
                    {
                        string sName = sibling.name.ToLower();
                        if (sName.Contains("aurora") || sName.Contains("fx") || sName.Contains("glow"))
                        {
                            MeshRenderer mr = sibling.GetComponent<MeshRenderer>();
                            if (mr == null || (mr.material != null && !mr.material.name.Contains("GLB_GlowExt")))
                            {
                                UnityEngine.Object.Destroy(sibling.gameObject);
                            }
                        }
                    }
                }
            }
        }

        private void ModifySingleWindow(GameObject windowObject, int index, string sceneFolderPath, string baseSceneName, string uniqueSceneId)
        {
            MeshRenderer renderer = windowObject.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            EradicateAuroraGlows(windowObject);
            renderer.enabled = false;

            GameObject myCustomScreen = GameObject.CreatePrimitive(PrimitiveType.Quad);
            myCustomScreen.name = $"CustomWindowScreen_{index}";
            myCustomScreen.transform.SetParent(windowObject.transform, false);

            myCustomScreen.transform.position = renderer.bounds.center;
            myCustomScreen.transform.rotation = windowObject.transform.rotation;

            // Create override keys for both GUID and Base names
            string overrideKeyGuid = $"{uniqueSceneId}_{index}";
            string overrideKeyBase = $"{baseSceneName}_{index}";

            // ROTATION CONTROL (To ensure custom settings, like those for Trapper's Cabin, are preserved)
            if (specificWindowRotations.ContainsKey(overrideKeyGuid))
                myCustomScreen.transform.Rotate(specificWindowRotations[overrideKeyGuid], Space.Self);
            else if (specificWindowRotations.ContainsKey(overrideKeyBase))
                myCustomScreen.transform.Rotate(specificWindowRotations[overrideKeyBase], Space.Self);
            else if (customSceneRotations.ContainsKey(uniqueSceneId))
                myCustomScreen.transform.Rotate(customSceneRotations[uniqueSceneId], Space.Self);
            else if (customSceneRotations.ContainsKey(baseSceneName))
                myCustomScreen.transform.Rotate(customSceneRotations[baseSceneName], Space.Self);
            else
                myCustomScreen.transform.Rotate(0, 180, 0, Space.Self);

            if (baseSceneName != "CampOffice")
            {
                if (myCustomScreen.transform.up.y < 0) myCustomScreen.transform.Rotate(0, 0, 180, Space.Self);
            }

            // OFFSET (POSITION) CONTROL
            if (specificWindowOffsets.ContainsKey(overrideKeyGuid))
                myCustomScreen.transform.Translate(specificWindowOffsets[overrideKeyGuid], Space.Self);
            else if (specificWindowOffsets.ContainsKey(overrideKeyBase))
                myCustomScreen.transform.Translate(specificWindowOffsets[overrideKeyBase], Space.Self);

            // Scaling operations (remains the same as before)
            float finalX = 1.0f;
            float finalY = 1.0f;

            if (baseSceneName == "CampOffice")
            {
                finalX = 1.4f; finalY = 1.5f;
                if (index == 0) { finalX = -1.4f; finalY = -1.5f; }
                else if (index == 2 || index == 5 || index == 7) { finalX = -1.4f; }
            }
            else
            {
                MeshFilter meshFilter = windowObject.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.mesh != null)
                {
                    Vector3 localSize = meshFilter.mesh.bounds.size;
                    Vector3 lossyScale = windowObject.transform.lossyScale;
                    float sX = Math.Abs(localSize.x * lossyScale.x);
                    float sY = Math.Abs(localSize.y * lossyScale.y);
                    float sZ = Math.Abs(localSize.z * lossyScale.z);

                    float w = sX; float h = sY;
                    if (sY < 0.05f) { h = sZ; w = sX; }
                    else if (sX < 0.05f) { w = sZ; h = sY; }

                    finalX = w * 1.05f; finalY = h * 1.05f;
                    if (finalX < 0.1f) finalX = 1.0f;
                    if (finalY < 0.1f) finalY = 1.0f;
                }
            }

            myCustomScreen.transform.localScale = new Vector3(finalX, finalY, 1f);

            Shader shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");

            Material mat = new Material(shader);
            myCustomScreen.GetComponent<MeshRenderer>().material = mat;

            var controller = myCustomScreen.AddComponent<DynamicWindowController>();
            controller.windowIndex = index;
            controller.sceneRootPath = sceneFolderPath;
            controller.originalRenderer = renderer;
        }
    }
}
