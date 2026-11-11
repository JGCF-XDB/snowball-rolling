using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SnowballArena
{
    // The victory beat: the valley goes dark around the winner while a spotlight keeps the
    // winner itself at normal brightness and colour. Built at runtime so the scene keeps working
    // without extra wiring, and skipped in batch mode so headless match runs are unaffected.
    public sealed class VictoryStage : MonoBehaviour
    {
        private const float FadeSeconds = 1.1f;
        private static VictoryStage instance;

        private Volume volume;
        private Light spot;
        private Transform follow;
        private float weight;

        // Scene lighting captured on Begin so the darkening stays reversible.
        private bool lightingCaptured;
        private float ambientIntensity;
        private Color ambientSky, ambientEquator, ambientGround;
        private readonly List<Light> dimmedLights = new List<Light>();
        private readonly List<float> dimmedIntensities = new List<float>();

        public static void Begin(Transform target)
        {
            if (Application.isBatchMode || target == null) return;
            if (instance == null)
            {
                var host = new GameObject("Victory stage");
                instance = host.AddComponent<VictoryStage>();
                instance.Build();
            }
            instance.CaptureLighting();
            instance.follow = target;
            instance.gameObject.SetActive(true);
        }

        public static void End()
        {
            if (instance == null) return;
            instance.follow = null;
            instance.RestoreLighting();
            instance.gameObject.SetActive(false);
        }

        private void Build()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            // Deliberately no exposure or saturation push here. Post exposure is global, so
            // lowering it also dims whatever the spotlight is lighting, which turned the winner
            // into a black silhouette. The darkness comes from the scene lights instead.
            var vignette = profile.Add<Vignette>(false);
            vignette.color.Override(Color.black);
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.55f);
            // The project profile runs with Tonemapping = None, so any value above 1.0 clips
            // straight to white - which is why the spotlight kept blowing out no matter how the
            // intensity was tuned. Neutral rolls the highlight off and keeps the hue.
            var tonemap = profile.Add<Tonemapping>(false);
            tonemap.mode.Override(TonemappingMode.Neutral);

            var go = new GameObject("Victory grade");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 40f;
            volume.weight = 0f;
            volume.profile = profile;

            var lightObject = new GameObject("Victory spotlight");
            lightObject.transform.SetParent(transform, false);
            spot = lightObject.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.95f, 0.86f);
            spot.spotAngle = 34f;
            spot.innerSpotAngle = 18f;
            spot.range = 52f;
            spot.intensity = 0f;
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.8f;
        }

        private void CaptureLighting()
        {
            if (lightingCaptured) return;
            lightingCaptured = true;
            ambientIntensity = RenderSettings.ambientIntensity;
            ambientSky = RenderSettings.ambientSkyColor;
            ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor;
            dimmedLights.Clear();
            dimmedIntensities.Clear();
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light == spot || !light.enabled) continue;
                dimmedLights.Add(light);
                dimmedIntensities.Add(light.intensity);
            }
        }

        // Everything except the spotlight is scaled down together, so the arena falls away while
        // whatever the spot is pointing at keeps the illumination it had during the match.
        private void ApplyDarkening(float amount)
        {
            if (!lightingCaptured) return;
            // Keeping a third of the ambient light is the point: the spotlight only has to top up
            // what is missing, so the lit area lands near match brightness instead of blowing out.
            RenderSettings.ambientIntensity = Mathf.Lerp(ambientIntensity, ambientIntensity * 0.35f, amount);
            RenderSettings.ambientSkyColor = Color.Lerp(ambientSky, ambientSky * 0.55f, amount);
            RenderSettings.ambientEquatorColor = Color.Lerp(ambientEquator, ambientEquator * 0.55f, amount);
            RenderSettings.ambientGroundColor = Color.Lerp(ambientGround, ambientGround * 0.55f, amount);
            for (int i = 0; i < dimmedLights.Count; i++)
                if (dimmedLights[i] != null) dimmedLights[i].intensity = Mathf.Lerp(dimmedIntensities[i], dimmedIntensities[i] * 0.3f, amount);
        }

        private void RestoreLighting()
        {
            if (!lightingCaptured) return;
            lightingCaptured = false;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            for (int i = 0; i < dimmedLights.Count; i++)
                if (dimmedLights[i] != null) dimmedLights[i].intensity = dimmedIntensities[i];
        }

        private void LateUpdate()
        {
            float target = follow != null ? 1f : 0f;
            weight = Mathf.MoveTowards(weight, target, Time.unscaledDeltaTime / FadeSeconds);
            if (volume != null) volume.weight = weight;
            ApplyDarkening(weight);
            if (spot != null)
            {
                // Only tops up the missing ambient light. With Neutral tonemapping above, a little
                // over 1.0 rolls off instead of clipping, so this stays deliberately modest.
                spot.intensity = weight * 5000f;
                if (follow != null)
                {
                    Vector3 point = follow.position;
                    spot.transform.position = point + new Vector3(0f, 17f, -6f);
                    spot.transform.LookAt(point);
                }
            }
        }

        private void OnDestroy()
        {
            RestoreLighting();
            if (volume != null && volume.profile != null) Destroy(volume.profile);
            instance = null;
        }
    }
}
