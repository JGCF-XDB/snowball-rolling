using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace SnowballArena
{
    public enum MatchPhase { Menu, Countdown, Running, Results }
    public enum EliminationCause { None, Fall, Cannon }
    public sealed class MatchParticipant
    {
        public SnowballBody body;
        public SnowballMotor motor;
        public bool alive = true;
        public string displayName;
        public bool isPlayer;
        public EliminationCause eliminationCause;
    }

    public sealed class MatchController : MonoBehaviour
    {
        public GameObject snowballPrefab;
        public ArenaSurface surface;
        public Camera arenaCamera;
        public int botCount = 5;
        public float countdownSeconds = 3f;
        public MatchPhase Phase { get; private set; } = MatchPhase.Menu;
        public List<MatchParticipant> Participants { get; } = new List<MatchParticipant>();
        public int Remaining { get; private set; }
        public string Result { get; private set; }
        public string Notice { get; private set; }
        public float Countdown { get; private set; }
        public CannonDirector Cannon { get; private set; }
        private readonly List<Material> ownedMaterials = new List<Material>();
        private int lastCountdownSecond = -1;
        private GUIStyle title, heading, label, small, button;
        private Font font;
        private void Awake()
        {
            Cannon = GetComponent<CannonDirector>();
            if (Cannon == null) Cannon = gameObject.AddComponent<CannonDirector>();
            Cannon.match = this;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) ReturnToMenu();
                if (Phase == MatchPhase.Menu)
                {
                    if (keyboard.leftArrowKey.wasPressedThisFrame) { botCount = Mathf.Max(0, botCount - 1); Click(); }
                    if (keyboard.rightArrowKey.wasPressedThisFrame) { botCount = Mathf.Min(15, botCount + 1); Click(); }
                    if (keyboard.enterKey.wasPressedThisFrame) StartMatch(botCount);
                }
                else if (keyboard.rKey.wasPressedThisFrame && (Phase == MatchPhase.Results || Participants.Count > 0 && !Participants[0].alive)) StartMatch(botCount);
            }
            if (Phase == MatchPhase.Countdown)
            {
                Countdown -= Time.deltaTime;
                int second = Mathf.CeilToInt(Mathf.Max(0f, Countdown));
                if (second != lastCountdownSecond) { lastCountdownSecond = second; Tick(); }
                if (Countdown <= 0) BeginRunning();
            }
        }

        private static void Click() { if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.UiClick(); }
        private static void Tick() { if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.UiTick(); }

        public bool StartMatch(int bots)
        {
            bots = Mathf.Clamp(bots, 0, 15);
            var settings = snowballPrefab.GetComponent<SnowballBody>().settings;
            if (!SpawnService.TryGetPositions(surface, bots + 1, settings.initialRadius, out var spawns))
            { Notice = "平台空间不足，请减少机器人数。"; return false; }
            ClearParticipants(); botCount = bots; Notice = null; Result = null;
            for (int i = 0; i <= bots; i++)
            {
                var instance = Instantiate(snowballPrefab, spawns[i], Quaternion.identity);
                instance.name = i == 0 ? "Player" : "Bot " + i;
                var body = instance.GetComponent<SnowballBody>();
                var motor = instance.GetComponent<SnowballMotor>();
                var participant = new MatchParticipant { body = body, motor = motor, isPlayer = i == 0, displayName = i == 0 ? "你" : "雪球 " + i };
                Participants.Add(participant);
                var input = instance.GetComponent<PlayerInputSource>();
                if (i == 0) input.cameraFrame = arenaCamera.transform;
                else
                {
                    input.enabled = false;
                    var brain = instance.AddComponent<BotBrain>();
                    brain.match = this; brain.self = participant; brain.personality = i * 1.731f;
                    var renderer = instance.GetComponentInChildren<MeshRenderer>();
                    float hue = 0.47f + (i * 0.618034f % 1f) * 0.19f;
                    var snow = new Material(renderer.sharedMaterials[0]);
                    var packedSnow = new Material(renderer.sharedMaterials[1]);
                    snow.SetColor("_BaseColor", Color.HSVToRGB(hue, 0.28f, 0.94f));
                    packedSnow.SetColor("_BaseColor", Color.HSVToRGB(hue, 0.56f, 0.76f));
                    ownedMaterials.Add(snow); ownedMaterials.Add(packedSnow);
                    renderer.sharedMaterials = new[] { snow, packedSnow };
                }
                motor.controlEnabled = false;
                instance.GetComponent<SnowballGrowth>().enabled = false;
            }
            Remaining = Participants.Count; Countdown = countdownSeconds; lastCountdownSecond = -1; Phase = MatchPhase.Countdown;
            if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.UiConfirm();
            return true;
        }

        public void BeginRunning()
        {
            if (Phase != MatchPhase.Countdown) return;
            Phase = MatchPhase.Running;
            foreach (var participant in Participants)
            {
                participant.motor.controlEnabled = true;
                participant.body.GetComponent<SnowballGrowth>().enabled = true;
                participant.body.GetComponent<SnowballGrowth>().ResetTracking();
            }
        }

        private void FixedUpdate()
        {
            if (Phase == MatchPhase.Running) Cannon.Advance(Time.fixedDeltaTime);
            ResolveEliminations();
        }
        public void Eliminate(MatchParticipant participant, EliminationCause cause)
        {
            if (Phase != MatchPhase.Running || participant == null || !participant.alive) return;
            participant.alive = false; participant.motor.controlEnabled = false;
            participant.eliminationCause = cause;
            participant.body.GetComponent<SnowballGrowth>().enabled = false;
            participant.body.gameObject.SetActive(false);
            if (participant.isPlayer) Notice = cause == EliminationCause.Cannon ? "你被炮弹击中" : "你掉出了平台";
        }
        public void ResolveEliminations()
        {
            if (Phase != MatchPhase.Running) return;
            Remaining = 0; MatchParticipant survivor = null;
            // Collect this physics step's eliminations before resolving the winner.
            foreach (var participant in Participants)
            {
                if (participant.alive && participant.body.RigidBody.position.y < surface.definition.fallHeight)
                {
                    Eliminate(participant, EliminationCause.Fall);
                }
                if (participant.alive) { Remaining++; survivor = participant; }
            }
            bool survivorOnPlatform = survivor != null && ArenaQuery.TrySupport(gameObject.scene.GetPhysicsScene(),
                survivor.body.RigidBody.position, survivor.body.Radius, survivor.motor.supportLayers,
                survivor.body.settings.supportProbeGap, survivor.body.settings.minimumSupportNormal, out _);
            if (Remaining == 0 || botCount > 0 && Remaining == 1 && survivorOnPlatform)
            {
                Result = Remaining == 0 ? (botCount == 0 ? "练习结束" : "平局") : survivor.isPlayer ? "你赢了！" : survivor.displayName + " 获胜";
                Phase = MatchPhase.Results;
                Cannon.Stop();
                if (ArenaFeedback.Instance != null) ArenaFeedback.Instance.Result(Remaining == 1 && survivor != null && survivor.isPlayer);
                foreach (var participant in Participants)
                {
                    participant.motor.controlEnabled = false;
                    participant.body.GetComponent<SnowballGrowth>().enabled = false;
                    // Freeze only after the match ends so the displayed winner stays on screen.
                    if (participant.alive) participant.body.RigidBody.isKinematic = true;
                }
            }
        }

        public void ReturnToMenu() { ClearParticipants(); Phase = MatchPhase.Menu; Notice = null; Click(); }
        private void ClearParticipants()
        {
            if (Cannon != null) Cannon.ResetRound();
            foreach (var participant in Participants)
                if (participant.body != null) { participant.body.gameObject.SetActive(false); Destroy(participant.body.gameObject); }
            Participants.Clear(); Remaining = 0;
            foreach (var material in ownedMaterials) Destroy(material);
            ownedMaterials.Clear();
        }

        private void OnGUI()
        {
            if (title == null)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Segoe UI" }, 24);
                title = Style(36, FontStyle.Bold, Color.white); heading = Style(28, FontStyle.Bold, Color.white);
                label = Style(19, FontStyle.Normal, new Color(0.87f, 0.95f, 1)); small = Style(15, FontStyle.Normal, new Color(0.60f, 0.80f, 0.87f));
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 20 };
            }
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            float w = Screen.width / scale, h = Screen.height / scale;
            Panel(new Rect(24, 22, 270, 95));
            GUI.Label(new Rect(44, 32, 240, 44), "雪山 · 滚雪球", heading);
            GUI.Label(new Rect(44, 79, 240, 25), "滚动成长 · 碰撞求生", small);
            if (Phase == MatchPhase.Menu)
            {
                var r = new Rect(w / 2 - 230, h / 2 - 182, 460, 364); Panel(r);
                GUI.Label(new Rect(r.x + 32, r.y + 22, 400, 52), "留在雪山之巅", title);
                GUI.Label(new Rect(r.x + 32, r.y + 84, 400, 30), "最后存活者获胜 · 20秒后炮击", label);
                GUI.Label(new Rect(r.x + 32, r.y + 130, 250, 30), "机器人数量", label);
                if (GUI.Button(new Rect(r.x + 32, r.y + 176, 60, 44), "−", button)) { botCount = Mathf.Max(0, botCount - 1); Click(); }
                GUI.Label(new Rect(r.x + 116, r.y + 178, 210, 44), botCount == 0 ? "0  ·  自由练习" : botCount + "  位对手", heading);
                if (GUI.Button(new Rect(r.x + 368, r.y + 176, 60, 44), "+", button)) { botCount = Mathf.Min(15, botCount + 1); Click(); }
                if (GUI.Button(new Rect(r.x + 32, r.y + 245, 396, 48), "开始游戏  ·  Enter", button)) StartMatch(botCount);
                GUI.Label(new Rect(r.x + 32, r.y + 313, 410, 30), Notice ?? "← → 调整人数    WASD 控制白色雪球", small);
            }
            else
            {
                string hazardText = Phase == MatchPhase.Results ? $"对局 {Cannon.Elapsed:F0}s · 对局结束" : Cannon.ActiveCannonCount > 0 ?
                    $"{Cannon.Elapsed:F0}s · 炮台 {Cannon.ActiveCannonCount} · 预警 {Cannon.WarningCount} / 飞行 {Cannon.ProjectileCount}" :
                    $"对局 {Cannon.Elapsed:F0}s · 炮台 {Mathf.Max(0, Cannon.startAfterSeconds - Cannon.Elapsed):F0}s 后出现";
                GUI.Label(new Rect(w / 2 - 180, 26, 360, 32), hazardText, label);
                Panel(new Rect(w - 284, 22, 260, 123));
                GUI.Label(new Rect(w - 266, 35, 235, 44), "场上剩余  " + Remaining, heading);
                var player = Participants[0];
                GUI.Label(new Rect(w - 266, 78, 235, 27), $"半径 {player.body.Radius:F2} m  ·  {player.body.RigidBody.mass:F0} kg", small);
                GUI.Label(new Rect(w - 266, 109, 235, 27), player.alive ? "白色雪球是你" : Notice == "你被炮弹击中" ? "炮弹命中，观战中" : "你已出局，观战中", small);
                Panel(new Rect(24, h - 76, 550, 52));
                GUI.Label(new Rect(44, h - 63, 520, 30), "WASD 移动   ·   Esc 返回菜单   ·   出局后 R 重开", small);
                if (Phase != MatchPhase.Results && GUI.Button(new Rect(w - 230, h - 72, 206, 42), "返回菜单  ·  Esc", button)) ReturnToMenu();
                if (Phase == MatchPhase.Countdown)
                    GUI.Label(new Rect(w / 2 - 40, h / 2 - 40, 100, 80), Mathf.CeilToInt(Countdown).ToString(), title);
                if (Phase == MatchPhase.Results)
                {
                    var r = new Rect(w / 2 - 200, h / 2 - 110, 400, 220); Panel(r);
                    GUI.Label(new Rect(r.x + 28, r.y + 25, 344, 50), Result, title);
                    if (GUI.Button(new Rect(r.x + 28, r.y + 92, 344, 44), "再来一局  ·  R", button)) StartMatch(botCount);
                    if (GUI.Button(new Rect(r.x + 28, r.y + 150, 344, 44), "返回菜单  ·  Esc", button)) ReturnToMenu();
                }
            }
            GUI.matrix = previous;
        }

        private GUIStyle Style(int size, FontStyle weight, Color color)
        {
            var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight };
            style.normal.textColor = color; return style;
        }
        private static void Panel(Rect r)
        {
            Color previous = GUI.color; GUI.color = new Color(0.025f, 0.08f, 0.12f, 0.94f); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.88f, 0.96f); GUI.DrawTexture(new Rect(r.x, r.y, 3, r.height), Texture2D.whiteTexture); GUI.color = previous;
        }
        private void OnDestroy() { if (font != null) Destroy(font); foreach (var material in ownedMaterials) if (material != null) Destroy(material); }
    }
}
