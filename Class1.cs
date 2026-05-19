using Il2Cpp;

using Il2CppInterop.Runtime.Injection;

using Il2CppNodeCanvas.Tasks.Actions;

using MelonLoader;

using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using UnityEngine;

using UnityEngine.Video;



[assembly: MelonInfo(typeof(UniversalWindowMod.WindowModMain), "TLD Universal Dynamic Windows", "2.4.0", "KarsonTR")]

[assembly: MelonGame("Hinterland", "TheLongDark")]



namespace UniversalWindowMod

{

    public class DynamicWindowController : MonoBehaviour

    {

        public DynamicWindowController(IntPtr ptr) : base(ptr) { }

        public float parallaxMultiplierX = 1f;
        public float parallaxMultiplierY = 1f;

        public float windowPhysicalWidth = 1f;
        public float windowPhysicalHeight = 1f;
        private Vector2 aspectCorrectedScale;
        private Vector2 baseCenterOffset;

        public int windowIndex;

        public string sceneRootPath;

        public MeshRenderer originalRenderer;



        private MeshRenderer myRenderer;

        private Material myMaterial;

        private Weather weather;

        private TimeOfDay timeOfDay;

        private Wind wind;



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

            wind = GameManager.GetWindComponent();



            vp1 = SetupVideoPlayer(out rt1);

            vp2 = SetupVideoPlayer(out rt2);



            // --- ESKİ KODUN YERİNE GELECEK YENİ KISIM ---
            CreateFadeScreen(); // Bunu önce çağırıyoruz ki fadeMaterial dolsun

            // Aspect Ratio (En/Boy Oranı) Hesaplaması
            float quadAspect = windowPhysicalWidth / windowPhysicalHeight;
            float videoAspect = 16f / 9f; // Videoların 16:9 oranında olduğunu varsayıyoruz

            float scaleX = 0.85f; // Senin kullandığın base scale
            float scaleY = 0.85f;

            // Eğer pencere videodan daha darsa (Görüntüyü sağdan ve soldan kırp)
            if (quadAspect < videoAspect)
            {
                scaleX = 0.85f * (quadAspect / videoAspect);
            }
            // Eğer pencere videodan daha genişse (Görüntüyü alttan ve üstten kırp)
            else
            {
                scaleY = 0.85f * (videoAspect / quadAspect);
            }

            aspectCorrectedScale = new Vector2(scaleX, scaleY);

            // Hem ana materyale hem de geçiş (fade) materyaline doğru scale'i uygula
            myMaterial.mainTextureScale = aspectCorrectedScale;
            if (fadeMaterial != null) fadeMaterial.mainTextureScale = aspectCorrectedScale;

            // Texture'ı tam merkeze oturtmak için dinamik offset hesaplaması
            baseCenterOffset = new Vector2((1f - scaleX) / 2f, (1f - scaleY) / 2f);
            // ---------------------------------------------



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



        private string GetWindSuffix()

        {

            if (wind == null) return "";



            float mph = wind.GetSpeedMPH();

            if (mph >= 25f) return "_VeryWindy";

            if (mph >= 12f) return "_Windy";



            return "";

        }



        private string ResolveVideoPath(string wStage, bool isNightTime, string windSuffix, out bool isNightVariant)
        {
            isNightVariant = false;
            List<string> suffixesToTry = new List<string>();

            if (wStage == "ClearAurora")
            {
                if (!string.IsNullOrEmpty(windSuffix)) suffixesToTry.Add($"_Night{windSuffix}");
                suffixesToTry.Add("_Night");
                if (!string.IsNullOrEmpty(windSuffix)) suffixesToTry.Add($"{windSuffix}");
                suffixesToTry.Add("");
            }
            else if (isNightTime)
            {
                if (!string.IsNullOrEmpty(windSuffix)) suffixesToTry.Add($"_Night{windSuffix}");
                suffixesToTry.Add("_Night");
            }
            else
            {
                if (!string.IsNullOrEmpty(windSuffix)) suffixesToTry.Add($"{windSuffix}");
                suffixesToTry.Add("");
            }

            foreach (string suffix in suffixesToTry)
            {
                string webmPath = Path.GetFullPath(Path.Combine(sceneRootPath, wStage, $"Window_{windowIndex}{suffix}.webm"));
                if (File.Exists(webmPath)) { isNightVariant = suffix.Contains("Night"); return webmPath; }

                string mp4Path = Path.GetFullPath(Path.Combine(sceneRootPath, wStage, $"Window_{windowIndex}{suffix}.mp4"));
                if (File.Exists(mp4Path)) { isNightVariant = suffix.Contains("Night"); return mp4Path; }
            }

            // --- YENİ: VİDEO BULUNAMAZSA KONSOLA YAZDIR ---
            MelonLoader.MelonLogger.Warning($"[WindowMod] VİDEO BULUNAMADI! Aranan dosya: {Path.Combine(sceneRootPath, wStage, $"Window_{windowIndex}.webm")}");
            // ----------------------------------------------

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



            Shader transparentShader = Shader.Find("Legacy Shaders/Transparent");

            if (transparentShader == null) transparentShader = Shader.Find("UI/Default");



            fadeMaterial = new Material(transparentShader);

            fadeMaterial.mainTextureScale = myMaterial.mainTextureScale;



            fadeMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 0f));



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



                    string targetVideoPath = ResolveVideoPath(gameWeather, isNightTime, currentWindSuffix, out bool targetWillBeNight);



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
                if (ActiveVp.isPrepared)
                {
                    if (!ActiveVp.isPlaying && !string.IsNullOrEmpty(ActiveVp.url))
                    {
                        ActiveVp.Play();
                    }

                    // --- YENİ: Il2Cpp event'i yuttuysa, videoyu Update icinde zorla materyale ata! ---
                    if (myMaterial.mainTexture != ActiveRt)
                    {
                        myMaterial.mainTexture = ActiveRt;
                    }
                    // ----------------------------------------------------------------------------------
                }
            }



            if (playingWeather == "ClearAurora")

            {

                myMaterial.EnableKeyword("_EMISSION");

                myMaterial.SetColor("_EmissionColor", new Color(0.2f, 0.8f, 0.4f) * 1.5f);

            }

            else

            {

                myMaterial.DisableKeyword("_EMISSION");

            }



            // --- YENİ DİNAMİK RENK HESAPLAMA SİSTEMİ ---

            Color gameAmbient = RenderSettings.ambientLight;

            Color gameFog = RenderSettings.fogColor;



            Color dynamicSkyTint = Color.Lerp(gameAmbient, gameFog, 0.7f);

            float currentLuminance = (dynamicSkyTint.r + dynamicSkyTint.g + dynamicSkyTint.b) / 3f;



            float dayLightFactor = Mathf.Clamp01(currentLuminance * 2.0f);

            Color finalSkyTint = Color.Lerp(dynamicSkyTint, Color.white, dayLightFactor * 0.2f);



            // Zifiri Karanlık Koruması (Çok Koyu Gece Mavisi)

            Color safeDarkTint = new Color(0.08f, 0.09f, 0.12f);

            float tintLuma = (finalSkyTint.r + finalSkyTint.g + finalSkyTint.b) / 3f;



            if (tintLuma < 0.25f)

            {

                float lerpF = Mathf.InverseLerp(0.25f, 0.0f, tintLuma);

                finalSkyTint = Color.Lerp(finalSkyTint, safeDarkTint, lerpF);

            }



            // Saat 7'ye Kadar Zorunlu Sabah Karanlığı

            if (currentNumericHour >= 5.0f && currentNumericHour < 7.0f)

            {

                float morningLift = Mathf.InverseLerp(6.75f, 7.0f, currentNumericHour);

                finalSkyTint = Color.Lerp(safeDarkTint, finalSkyTint, morningLift);

            }



            // --- KIZILLIK (GOLDEN HOUR) DÜZELTMESİ ---

            bool canShowGoldenHour = true;

            if (playingWeather == "Blizzard" || playingWeather == "HeavySnow" || playingWeather == "Fog")

            {

                canShowGoldenHour = false;

            }



            if (canShowGoldenHour)

            {

                // Sabah Şafağı (07:00 - 08:00)

                if (currentNumericHour >= 7.0f && currentNumericHour <= 8.0f)

                {

                    float t = (currentNumericHour - 7.0f) / 1.0f;

                    Color dawnRed = new Color(1.2f, 0.4f, 0.2f);

                    float timeIntensity = Mathf.Sin(t * Mathf.PI);

                    finalSkyTint = Color.Lerp(finalSkyTint, dawnRed, timeIntensity * 0.65f);

                }

                // Akşam Gün Batımı (18:00 - 20:00)

                else if (currentNumericHour >= 18.0f && currentNumericHour <= 20.0f)

                {

                    float t = (currentNumericHour - 18.0f) / 2.0f;

                    Color duskOrange = new Color(1.0f, 0.35f, 0.15f);

                    float timeIntensity = Mathf.Sin(t * Mathf.PI);

                    finalSkyTint = Color.Lerp(finalSkyTint, duskOrange, timeIntensity * 0.65f);

                }

            }



            if (playingWeather == "ClearAurora")

            {

                finalSkyTint *= 1.5f;

            }



            // --- AMBIENT LIGHTS DÜZELTMESİ ---

            float maxExposureLimit = WindowModMain.isAmbientLightsInstalled ? 3.0f : 1.0f;

            float baseExposure = Mathf.Clamp(currentLuminance * 1.5f, 0.1f, maxExposureLimit);



            if (WindowModMain.isAmbientLightsInstalled)

            {

                float boostFactor = Mathf.Lerp(1.0f, 2.5f, dayLightFactor);

                baseExposure *= boostFactor;



                if (dayLightFactor > 0.5f)

                {

                    float whiteBoost = Mathf.InverseLerp(0.5f, 1.0f, dayLightFactor) * 0.4f;

                    finalSkyTint = Color.Lerp(finalSkyTint, Color.white, whiteBoost);

                }

            }

            // ---------------------------------



            Color currentTint = finalSkyTint;

            float currentExposure = Mathf.Max(baseExposure, 0.80f);

            Color targetTint = finalSkyTint;

            float targetExposure = Mathf.Max(baseExposure, 0.80f);



            if (playingIsNightVariant)

            {

                currentTint = Color.Lerp(Color.white, finalSkyTint, 0.4f);

                currentExposure = Mathf.Max(baseExposure, 0.85f);

            }



            if (targetIsNightVariant)

            {

                targetTint = Color.Lerp(Color.white, finalSkyTint, 0.4f);

                targetExposure = Mathf.Max(baseExposure, 0.85f);

            }



            // 5. GEÇİŞ VE UYGULAMA

            if (isTransitioning && !isWaitingToPrepare)

            {

                fadeTimer += Time.deltaTime;

                float alphaProgress = Mathf.Clamp01(fadeTimer / transitionDuration);



                float blendedExposure = Mathf.Lerp(currentExposure, targetExposure, alphaProgress);



                Color fadeOldTint = new Color(currentTint.r * blendedExposure, currentTint.g * blendedExposure, currentTint.b * blendedExposure, 1f);

                myMaterial.SetColor("_Color", fadeOldTint);



                Color fadeNewTint = new Color(targetTint.r * blendedExposure, targetTint.g * blendedExposure, targetTint.b * blendedExposure, alphaProgress);

                fadeMaterial.SetColor("_Color", fadeNewTint);



                if (alphaProgress >= 1f) CompleteTransition();

            }

            else

            {

                // NORMAL OYNATIM

                Color finalSolidTint = new Color(currentTint.r * currentExposure, currentTint.g * currentExposure, currentTint.b * currentExposure, 1.0f);

                myMaterial.SetColor("_Color", finalSolidTint);

            }



            UpdateParallax();

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



        private void TriggerWeatherChange(string newWeather, string targetVideoPath, bool targetNightVariant)

        {

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

            currentPlayingVideoPath = NextVp.url;



            NextVp.Stop();

            NextVp.url = string.Empty;

        }



        private void PlayStandardWeather(string targetWeatherStage, string absoluteVideoPath, bool isNightVariant)

        {

            currentPlayingVideoPath = absoluteVideoPath;



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

            // 1. Kameranın pencereye olan yerel pozisyonunu al
            Vector3 localPos = transform.InverseTransformPoint(cam.transform.position);

            // 2. YENİ: Vektörü "normalize" ediyoruz. 
            // Bu sayede uzaklık devreden çıkıyor, SADECE kameranın pencereye bakış AÇISINI (yönünü) alıyoruz.
            Vector3 viewDir = localPos.normalized;

            // 3. Parallax Şiddeti (Eğer kayma gözüne hala çok gelirse bu değeri 0.10f veya 0.05f yapabilirsin)
            float parallaxIntensity = 0.15f;

            // Ham offset hesabı (Açı * Şiddet * Yön Çarpanı + Merkeze Hizalama)
            Vector2 targetOffset = new Vector2(
                (-viewDir.x * parallaxIntensity * parallaxMultiplierX) + baseCenterOffset.x,
                (-viewDir.y * parallaxIntensity * parallaxMultiplierY) + baseCenterOffset.y
            );

            // 4. KORUMA SINIRI (Clamp): Doku sınırlarının dışına çıkıp görüntünün sünmesini/bozulmasını engeller.
            // Bu sayede sadece kırptığımız (gizlediğimiz) alan kadar parallax hareketi yapmasına izin veririz.
            targetOffset.x = Mathf.Clamp(targetOffset.x, 0f, Mathf.Max(0f, 1f - aspectCorrectedScale.x));
            targetOffset.y = Mathf.Clamp(targetOffset.y, 0f, Mathf.Max(0f, 1f - aspectCorrectedScale.y));

            myMaterial.mainTextureOffset = targetOffset;
            if (isTransitioning && fadeMaterial != null) fadeMaterial.mainTextureOffset = targetOffset;
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

    }



    public class WindowModMain : MelonMod
    {
        private string baseTexturesFolderPath;
        private string configFilePath;

        public static bool isAmbientLightsInstalled = false;

        private Dictionary<string, Vector3> customSceneRotations = new Dictionary<string, Vector3>();
        private Dictionary<string, Vector3> specificWindowRotations = new Dictionary<string, Vector3>();
        private Dictionary<string, Vector3> specificWindowOffsets = new Dictionary<string, Vector3>();
        private Dictionary<string, Vector3> specificWindowScales = new Dictionary<string, Vector3>();
        private Dictionary<string, Vector3> specificWindowLocalPositions = new Dictionary<string, Vector3>();
        private Dictionary<string, Vector2> specificParallaxMultipliers = new Dictionary<string, Vector2>();
        //private Dictionary<string, Vector3> specificWindowWorldPositions = new Dictionary<string, Vector3>();

        // YENİ: Aynalanacak pencerelerin listesi
        private HashSet<string> mirroredWindows = new HashSet<string>();

        private Dictionary<string, int> groupedPrefabs = new Dictionary<string, int>();
        private HashSet<string> ignoredObjects = new HashSet<string>();

        private static string cachedCoordinateHash = "DefaultPos";

        private string GetUniqueFolderID(string interiorScene)
        {
            var transition = GameManager.m_SceneTransitionData;

            if (transition != null)
            {
                Vector3 extPos = transition.m_PosBeforeInteriorLoad;
                if (extPos != Vector3.zero)
                {
                    cachedCoordinateHash = $"X{Mathf.RoundToInt(extPos.x)}_Y{Mathf.RoundToInt(extPos.y)}_Z{Mathf.RoundToInt(extPos.z)}";
                }
            }

            return $"{interiorScene}_{cachedCoordinateHash}".Replace(" ", "");
        }

        public override void OnInitializeMelon()
        {
            ClassInjector.RegisterTypeInIl2Cpp<DynamicWindowController>();
            baseTexturesFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods", "WindowTextures");
            configFilePath = Path.Combine(baseTexturesFolderPath, "WindowConfig.txt");

            if (!Directory.Exists(baseTexturesFolderPath)) Directory.CreateDirectory(baseTexturesFolderPath);

            // DİKKAT: LoadConfiguration(); buradan kaldırıldı. Artık sahne yüklendiğinde anlık olarak çalışacak.

            foreach (var mod in MelonMod.RegisteredMelons)
            {
                string safeModName = mod.Info.Name.ToLower().Replace(" ", "").Replace("-", "");
                if (safeModName.Contains("ambientlights"))
                {
                    isAmbientLightsInstalled = true;
                    MelonLogger.Msg("[UniversalWindowMod] Ambient Lights Modu Tespit Edildi! Pencerelere ekstra parlaklik uygulanacak.");
                    break;
                }
            }
        }

        private void LoadConfiguration()
        {
            if (!File.Exists(configFilePath)) CreateDefaultConfig();
            try
            {
                string[] lines = File.ReadAllLines(configFilePath);
                int currentSection = 0;

                customSceneRotations.Clear();
                specificWindowRotations.Clear();
                specificWindowOffsets.Clear();
                mirroredWindows.Clear();
                specificWindowScales.Clear();
                specificWindowLocalPositions.Clear();
                specificParallaxMultipliers.Clear();

                foreach (string line in lines)
                {
                    string l = line.Trim();
                    if (string.IsNullOrEmpty(l) || l.StartsWith("//")) continue;

                    // Başlıkları Belirleme
                    if (l == "[CustomSceneRotations]") { currentSection = 1; continue; }
                    if (l == "[SpecificWindowRotations]") { currentSection = 2; continue; }
                    if (l == "[SpecificWindowOffsets]") { currentSection = 3; continue; }
                    if (l == "[MirroredWindows]") { currentSection = 4; continue; }
                    if (l == "[SpecificWindowScales]") { currentSection = 5; continue; }
                    if (l == "[SpecificWindowLocalPositions]") { currentSection = 6; continue; }
                    if (l == "[SpecificParallaxMultipliers]") { currentSection = 8; continue; }
                    if (l == "[GroupedPrefabs]") { currentSection = 9; continue; }
                    if (l == "[IgnoredObjects]") { currentSection = 10; continue; }

                    // Sadece isim alan listeler (Mirrored ve Ignored)
                    if (currentSection == 4 || currentSection == 10)
                    {
                        string key = l.Contains("=") ? l.Split('=')[0].Trim() : l.Trim();

                        if (currentSection == 4) mirroredWindows.Add(key);
                        else if (currentSection == 10) ignoredObjects.Add(key.ToLower()); // Küçük harf koruması

                        continue;
                    }

                    // Vektör ve Sayı alan listeler
                    if (l.Contains("="))
                    {
                        string[] parts = l.Split('=');
                        string key = parts[0].Trim();
                        string[] vals = parts[1].Split(',');

                        // --- YENİ: GRUPLANMIŞ PENCERE SAYISI ATAMASI ---
                        if (currentSection == 9)
                        {
                            groupedPrefabs[key.ToLower()] = int.Parse(vals[0].Trim());
                        }
                        // ----------------------------------------------

                        else if (vals.Length >= 2 && currentSection == 8)
                        {
                            specificParallaxMultipliers[key] = new Vector2(
                                float.Parse(vals[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                                float.Parse(vals[1].Trim(), System.Globalization.CultureInfo.InvariantCulture)
                            );
                        }
                        else if (vals.Length >= 3)
                        {
                            Vector3 vec = new Vector3(
                                float.Parse(vals[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                                float.Parse(vals[1].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                                float.Parse(vals[2].Trim(), System.Globalization.CultureInfo.InvariantCulture)
                            );

                            if (currentSection == 1) customSceneRotations[key] = vec;
                            else if (currentSection == 2) specificWindowRotations[key] = vec;
                            else if (currentSection == 3) specificWindowOffsets[key] = vec;
                            else if (currentSection == 5) specificWindowScales[key] = vec;
                            else if (currentSection == 6) specificWindowLocalPositions[key] = vec;
                        }
                    }
                }
                    MelonLogger.Msg("[UniversalWindowMod] Ayarlar basariyla yucellendi (Canli Yukleme).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Ayarlar okunurken hata olustu: {ex.Message}");
            }
        }

        private void CreateDefaultConfig()
        {
            string[] defaultLines = {
                "// WARNING: Use DOT instead of COMMA for decimals (e.g., 90.5)",
                "// Format: ObjectName = X, Y, Z",
                "",
                "[CustomSceneRotations]",
                "CampOffice = 0, 180, 180",
                "",
                "[SpecificWindowRotations]",
                "SafeHouseA_5 = 90, 0, 0",
                "",
                "[SpecificWindowOffsets]",
                "",
                "// ---------------------------------------------------------",
                "// [SpecificWindowScales] (Ozel Pencere Boyutlari)",
                "// Format: ObjectName = Genislik (X), Yukseklik (Y), Z (Her zaman 1 kalsin)",
                "// Ornek: QuonsetGasStation_3 = 1.2, 2.5, 1",
                "// ---------------------------------------------------------",
                "[SpecificWindowScales]",
                "",
                "// ---------------------------------------------------------",
                "// [MirroredWindows] (Aynalama / Sag-Sol Ters Cevirme)",
                "// Goruntudeki agaclarin, manzaranin sag/sol yonunu tersine cevirir.",
                "// Sadece sahne adini veya pencere ID'sini yazmaniz yeterlidir.",
                "// Ornek: LakeCabinB_X1469_Y21_Z-45_1",
                "// ---------------------------------------------------------",
                "[MirroredWindows]"
            };

            File.WriteAllLines(configFilePath, defaultLines);
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // YENİ: Oyuncu her kapıdan girdiğinde TXT dosyası baştan okunur!
            // Böylece oyunu kapatmana gerek kalmaz, sadece kapıdan çık-gir yapman yeterlidir.
            LoadConfiguration();

            string interiorScene = sceneName.Replace("_SANDBOX", "").Replace("_DLC01", "").Replace("_WILDLIFE", "");

            if (interiorScene == "Empty")
            {
                GetUniqueFolderID(interiorScene);
                return;
            }

            string uniqueID = GetUniqueFolderID(interiorScene);

            MelonLogger.Msg("--------------------------------------------------");
            MelonLogger.Msg($"[WindowMod] Sahne: {interiorScene}");
            MelonLogger.Msg($"[WindowMod] Eger bu eve ozel video istiyorsan klasor adini su yap: {uniqueID}");
            MelonLogger.Msg("--------------------------------------------------");

            string finalPath = "";

            if (Directory.Exists(Path.Combine(baseTexturesFolderPath, uniqueID)))
            {
                finalPath = Path.Combine(baseTexturesFolderPath, uniqueID);
            }
            else if (Directory.Exists(Path.Combine(baseTexturesFolderPath, interiorScene)))
            {
                finalPath = Path.Combine(baseTexturesFolderPath, interiorScene);
            }

            if (!string.IsNullOrEmpty(finalPath))
            {
                MelonLogger.Msg($"[WindowMod] Yuklenen Klasor: {Path.GetFileName(finalPath)}");
                ReplaceWindowsInScene(finalPath, interiorScene, uniqueID);
            }
            else
            {
                MelonLogger.Warning($"[WindowMod] '{interiorScene}' icin hicbir doku klasoru bulunamadi.");
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
                    string objName = renderer.gameObject.name.ToLower();

                    // 1. TXT'den gelen Kara Liste (IgnoredObjects) Kontrolü
                    // Eğer Unity Explorer'da devasa bir "sahte ışık" bulup adını TXT'ye yazarsan, anında yok edilir.
                    bool isIgnored = false;
                    foreach (var ignored in ignoredObjects)
                    {
                        if (objName.Contains(ignored)) { isIgnored = true; break; }
                    }
                    if (isIgnored) continue;

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
                string wName = window.name.ToLower();
                bool alreadyModded = false;

                for (int i = 0; i < window.transform.childCount; i++)
                {
                    if (window.transform.GetChild(i).name.StartsWith("CustomWindowScreen"))
                    {
                        alreadyModded = true;
                        break;
                    }
                }

                // 2. Bu obje tekil mi yoksa birleşik prefab mı? Kaç pencere çıkaracağız?
                int groupCount = 1;

                // Eski evler için hardcoded miraslar
                if (wName.Contains("parlourroomwindowglow")) groupCount = 3;
                else if (wName.Contains("garagewindowsglow")) groupCount = 8;
                else
                {
                    // TXT'den dinamik grup kontrolü (Sihir burada!)
                    foreach (var kvp in groupedPrefabs)
                    {
                        if (wName.Contains(kvp.Key))
                        {
                            groupCount = kvp.Value;
                            break;
                        }
                    }
                }

                if (alreadyModded)
                {
                    index += groupCount;
                    continue;
                }

                // 3. Pencereleri Yarat
                for (int i = 0; i < groupCount; i++)
                {
                    // FarmHouse dışındaki tüm "birleşik" prefablarda devasa boyutu engellemek için 1.5f standart koruması uygula
                    bool forceStandardSize = (groupCount > 1 && !wName.Contains("parlourroomwindowglow"));

                    ModifySingleWindow(window, index, sceneFolderPath, baseSceneName, uniqueSceneId, forceStandardSize);
                    index++;
                }
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
                        else if (sName.Contains("glass"))
                        {
                            MeshRenderer mr = sibling.GetComponent<MeshRenderer>();
                            if (mr != null && mr.material != null)
                            {
                                if (mr.material.HasProperty("_Color"))
                                {
                                    Color glassColor = mr.material.GetColor("_Color");
                                    glassColor.a *= 0.45f;
                                    mr.material.SetColor("_Color", glassColor);
                                }
                                else if (mr.material.HasProperty("_TintColor"))
                                {
                                    Color glassColor = mr.material.GetColor("_TintColor");
                                    glassColor.a *= 0.45f;
                                    mr.material.SetColor("_TintColor", glassColor);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void ModifySingleWindow(GameObject windowObject, int index, string sceneFolderPath, string baseSceneName, string uniqueSceneId, bool isGroupedPrefab = false)
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

            string overrideKeyGuid = $"{uniqueSceneId}_{index}";
            string overrideKeyBase = $"{baseSceneName}_{index}";

            bool shouldMirror = mirroredWindows.Contains(overrideKeyGuid) ||
                                mirroredWindows.Contains(overrideKeyBase) ||
                                mirroredWindows.Contains(uniqueSceneId) ||
                                mirroredWindows.Contains(baseSceneName);

            if (shouldMirror)
            {
                Mesh mesh = myCustomScreen.GetComponent<MeshFilter>().mesh;
                Vector2[] uvs = mesh.uv;
                for (int j = 0; j < uvs.Length; j++)
                {
                    uvs[j] = new Vector2(1f - uvs[j].x, uvs[j].y);
                }
                mesh.uv = uvs;
            }

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

            myCustomScreen.transform.Translate(0, 0, -0.015f, Space.Self);

            if (specificWindowOffsets.ContainsKey(overrideKeyGuid))
                myCustomScreen.transform.Translate(specificWindowOffsets[overrideKeyGuid], Space.Self);
            else if (specificWindowOffsets.ContainsKey(overrideKeyBase))
                myCustomScreen.transform.Translate(specificWindowOffsets[overrideKeyBase], Space.Self);

            // --- YENİ: NOKTA ATIŞI LOKAL KONUM (LOCAL POSITION) ---
            if (specificWindowLocalPositions.ContainsKey(overrideKeyGuid))
                myCustomScreen.transform.localPosition = specificWindowLocalPositions[overrideKeyGuid];
            else if (specificWindowLocalPositions.ContainsKey(overrideKeyBase))
                myCustomScreen.transform.localPosition = specificWindowLocalPositions[overrideKeyBase];
            // ------------------------------------------------------

            float finalX = 1.0f;
            float finalY = 1.0f;

            if (baseSceneName == "CampOffice")
            {
                finalX = 1.55f; finalY = 1.85f;
                if (index == 0) { finalX = -1.55f; finalY = -1.85f; }
                else if (index == 2 || index == 5 || index == 7) { finalX = -1.55f; }
            }
            else if (isGroupedPrefab)
            {
                // YENİ KISIM: Eğer obje FarmHouse veya Quonset gibi birleşik bir prefab ise, 
                // bounds.size almasını engelliyoruz, yoksa devasa olurlar! 
                // Bunun yerine makul, standart bir boyut veriyoruz.
                finalX = 1.5f;
                finalY = 1.5f;
            }
            else
            {
                MeshFilter meshFilter = windowObject.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.mesh != null)
                {
                    Vector3 localSize = meshFilter.mesh.bounds.size;
                    Vector3 lossyScale = windowObject.transform.lossyScale;
                    float sX = Mathf.Abs(localSize.x * lossyScale.x);
                    float sY = Mathf.Abs(localSize.y * lossyScale.y);
                    float sZ = Mathf.Abs(localSize.z * lossyScale.z);

                    float w = sX; float h = sY;
                    if (sY < 0.05f) { h = sZ; w = sX; }
                    else if (sX < 0.05f) { w = sZ; h = sY; }

                    finalX = w * 1.15f;
                    finalY = h * 1.35f;

                    if (finalX < 0.1f) finalX = 1.0f;
                    if (finalY < 0.1f) finalY = 1.0f;
                }
            }

            // ... mevcut kodda finalX ve finalY hesaplama blokları bittikten sonra,
            // myCustomScreen.transform.localScale = ... satırından HEMEN ÖNCE bunu ekle:

            // --- YENİ: TXT DOSYASINDA ÖZEL BİR BOYUT VARSA ONU UYGULA ---
            if (specificWindowScales.ContainsKey(overrideKeyGuid))
            {
                finalX = specificWindowScales[overrideKeyGuid].x;
                finalY = specificWindowScales[overrideKeyGuid].y;
            }
            else if (specificWindowScales.ContainsKey(overrideKeyBase))
            {
                finalX = specificWindowScales[overrideKeyBase].x;
                finalY = specificWindowScales[overrideKeyBase].y;
            }
            // -----------------------------------------------------------

            myCustomScreen.transform.localScale = new Vector3(finalX, finalY, 1f);

            Shader cleanShader = Shader.Find("Legacy Shaders/Transparent");
            if (cleanShader == null) cleanShader = Shader.Find("UI/Default");

            Material mat = new Material(cleanShader);
            myCustomScreen.GetComponent<MeshRenderer>().material = mat;

            var controller = myCustomScreen.AddComponent<DynamicWindowController>();
            controller.windowIndex = index;
            controller.sceneRootPath = sceneFolderPath;
            controller.originalRenderer = renderer;

            controller.windowPhysicalWidth = finalX;
            controller.windowPhysicalHeight = finalY;

            // --- YENİ: OTOMATİK AYNALAMA DÜZELTMESİ ---
            // Eğer pencere aynalanmışsa (mirrored), parallax da yatayda ters çalışmalıdır!
            if (shouldMirror)
            {
                controller.parallaxMultiplierX = -1f;
            }

            // --- YENİ: MANUEL TXT YÖN DEĞİŞTİRME ---
            // Eğer TXT dosyasında bu pencere için özel bir parallax yönü belirtilmişse, her şeyi ezer
            if (specificParallaxMultipliers.ContainsKey(overrideKeyGuid))
            {
                controller.parallaxMultiplierX = specificParallaxMultipliers[overrideKeyGuid].x;
                controller.parallaxMultiplierY = specificParallaxMultipliers[overrideKeyGuid].y;
            }
            else if (specificParallaxMultipliers.ContainsKey(overrideKeyBase))
            {
                controller.parallaxMultiplierX = specificParallaxMultipliers[overrideKeyBase].x;
                controller.parallaxMultiplierY = specificParallaxMultipliers[overrideKeyBase].y;
            }
        }
    }
}